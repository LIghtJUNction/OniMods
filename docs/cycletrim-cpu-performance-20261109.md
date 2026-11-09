# CycleTrim：减少 CPU 重复工作并拆开仿真阶段耗时

日期：2026-11-09（Asia/Taipei）。基线：CycleTrim 0.4.10，`07e18765`。

本轮完成 **CycleTrim 0.4.11** 的代码改进与 Linux x64 安装包：减少 GPU 输入准备的 CPU 重复工作，加快日照图生成，并修复小区域转入 CPU 时缺少速度、焓的问题。固定宿主对照中，736×304 区域的气体输入准备耗时下降 **30.94%–56.11%**；同尺寸混合网格的日照更新耗时下降 **39.47%**。输入准备 14 项、日照 18 项性能门槛全部通过。新增分段日志区分仿真、网格发布、纹理和组分发布。这些数字对应独立函数，不能相加或换算实际存档帧率。

## CPU 仍在做什么

生产路径仍以 CPU 内存中的世界网格为数据源。多数 GPU 算子需要先由 CPU 整理区域输入，再提交计算、等待结果、读回并写回网格；随后，CPU 继续生成供游戏读取的纹理和组分记录。GPU 内核加速只覆盖这条流程中的部分计算。源码依据：[GPU 后端](../mods/CycleTrim/OniSim/native/src/gpu_sim.rs)、[批次执行与发布](../mods/CycleTrim/OniSim/native/src/frames_engine.rs)。

液体流动求解、部分组件更新和逐格后处理也仍在 CPU 上执行；当前生产代码的 `gpu_liquid_flow` 为 `false`。因此，液体多、组分多和频繁发布的场景各有不同的 CPU 工作量，需要按同一存档的阶段用时判断优先级。GPU 常驻世界结构尚未接入完整生产时间步，本轮没有把它写成已完成的加速能力。依据：[仿真步骤](../mods/CycleTrim/OniSim/native/src/simulation_step.rs)。

## 已实现的改动

### 小区域按实际执行后端准备数据

GPU 可用时，小于 256 格的区域仍可能进入 CPU 路径。此前焓和速度的准备条件只判断“有没有 GPU”，导致这些区域缺少 CPU 计算所需的数据。现在统一按该区域是否实际使用 GPU 决定准备工作；2×2、8×8 和 15×17 的宿主回归覆盖了这条路径。

提交：[b570414](https://github.com/LIghtJUNction/OniMods/commit/b570414d8c84fe6690dd346a7ac39c7cfa5d670e)。这是计算正确性修复，没有给它附加未经测量的速度比例。

### 日照图连续读取相邻格子

原日照更新沿一整列跨行读取内存。新实现每次处理最多 64 列，逐行访问这些相邻格子，并保留每一列自身的累计顺序。元素的质量换算系数在一次更新中按需复用；整段使用同一覆盖材质时，直接处理该段。暂存位于有界栈空间，没有新增逐帧堆分配。浮点异常未屏蔽时，沿用原来的列顺序。

提交：[d8ba373](https://github.com/LIghtJUNction/OniMods/commit/d8ba373e16058309bdfe05a7cf5c11c3cbbef241)。[生产实现](../mods/CycleTrim/OniSim/native/src/presentation_sun.rs)和[回归及基准](../mods/CycleTrim/OniSim/native/src/presentation_sun_tests.rs)一起交付。

下表来自本次[完整测量记录](cycletrim-cpu-performance-20261109/measurements.json)中的日照数据。单位为一次完整日照图更新的毫秒，中位数越小越好。

| 区域布局 | 尺寸 | 0.4.10，ms ↓ | 本轮，ms ↓ | 耗时下降 |
|---|---:|---:|---:|---:|
| 气体 | 96×96 | 0.0298 | 0.0213 | 28.41% |
| 气体 | 256×384 | 0.3749 | 0.2590 | 30.92% |
| 气体 | 736×304 | 0.9443 | 0.5705 | 39.58% |
| 混合 | 96×96 | 0.0651 | 0.0493 | 24.31% |
| 混合 | 256×384 | 1.1092 | 0.6683 | 39.75% |
| 混合 | 736×304 | 2.7474 | 1.6630 | 39.47% |

实验使用 Linux x86-64、Rust 1.99.1 Release，固定 CPU 0。每侧预热 8 次，再执行 17 个交替 ABBA／BAAB 块，每侧保留 34 条样本。计时覆盖完整生产函数和栈暂存初始化，夹具创建与输出比较在计时外。六个密集场景要求至少减少 10% 耗时；真空、遮挡、覆盖材质和分区场景要求退步不超过 3%。18 项门槛全部通过，另报告六个 17×19 小区域观察场景，全部原始样本保留。

五项正确性测试通过，覆盖输出字节、不同区域和材质、重复更新及浮点环境。736×304 混合场景在这个函数中节省约 **1.08 ms**；整帧收益还取决于其他阶段的耗时与实际调用频率。

### 减少 GPU 输入准备中的分配、清零和重复搜索

原路径为每个区域重新创建一批向量，并反复在线性数组里寻找已出现的气体。现在由每个 GPU 实例保留暂存容量，使用元素编号直接查表，按连续行打包稀疏输入；空质量块可以跳过种类检查。GPU 自行生成速度的路径不再准备空有尺寸、实际不用的主机速度数组。每次区域更新仍完整刷新所有输入，容量复用不会复用上一时间步的物理状态。

提交：[4940b9e](https://github.com/LIghtJUNction/OniMods/commit/4940b9e6a74b3b92fe667b4638c8331e24760594)。依据：[暂存实现](../mods/CycleTrim/OniSim/native/src/multicomponent_staging.rs)、[生产调用](../mods/CycleTrim/OniSim/native/src/multicomponent_flux.rs)、[冻结基线与回归](../mods/CycleTrim/OniSim/native/src/multicomponent_staging_tests.rs)。

| 区域尺寸 | 活跃气体种类 | 基线，ms ↓ | 本轮，ms ↓ | 耗时下降 |
|---|---:|---:|---:|---:|
| 96×96 | 2 | 0.2110 | 0.1078 | 48.90% |
| 96×96 | 4 | 0.2371 | 0.1806 | 23.80% |
| 96×96 | 16 | 0.2950 | 0.1870 | 36.61% |
| 256×384 | 2 | 2.4497 | 1.0911 | 55.46% |
| 256×384 | 4 | 2.5784 | 1.8685 | 27.53% |
| 256×384 | 16 | 3.3071 | 2.0079 | 39.29% |
| 736×304 | 2 | 5.5999 | 2.4577 | 56.11% |
| 736×304 | 4 | 5.8102 | 4.0125 | 30.94% |
| 736×304 | 16 | 7.3972 | 4.4807 | 39.43% |

这里的基线是 `b570414`，即 0.4.10 加上前述小区域正确性修复，使性能对照使用正确的 CPU 输入。对照覆盖输入准备及成功时移交、释放固体掩码；不含外层暂存对象取出／归还、GPU 提交、GPU 读回、网格投影或整帧。基线提取段已与该提交的 Git 对象核对；GPU 入口选择由测试参数控制，无需硬件设备即可测量 CPU 整理工作。

每侧预热 5 次，执行 17 个交替 ABBA／BAAB 块，每侧 34 个样本，14 个场景共 **952 个样本全部保留**。736×304 的 2／4／16 种气体分别要求至少减少 5% 耗时；其余六个活跃场景和五个控制场景要求退步不超过 3%，结果 **14／14 通过**。控制包括小区域 CPU 回退、单气体、全墙、空区域和关闭 GPU 后的 CPU 准备路径。350 组输入对照覆盖尺寸、气体种类、布局、边界浮点值与后端切换，相关九项正确性测试通过。

复用容量会保留内存。736×304 的新实例在双气体密集路径后保留约 **5.12 MiB**，四／十六气体稀疏路径后约 **8.96 MiB**；同一实例依次运行这三种布局后，向量容量合计约 **14.08 MiB**。以上统计在成功移交固体掩码之后，不计入调用者持有的掩码。这些是 `capacity × 元素大小` 的求和，不能当成进程 RSS；实例销毁后释放，较大区域或其他布局可增加保留量。成功结果的固体掩码仍移交给调用者，所以完整调用并非零分配。

### GPU 自行生成速度时省去无用的主机速度检查

GPU 生成速度的上传入口现在保留物质、质量、温度和网格约束校验，跳过未使用的主机速度字段。CPU 路径以及接收外部速度的入口继续执行速度校验，设备端限制每步输运幅度的 CFL 检查继续保留。该改动减少了重复扫描；六项相关宿主校验测试通过。本轮没有为这项改动单独发布 GPU 耗时数字。

依据：[多组分输入校验及回归](../mods/CycleTrim/OniSim/gpu/src/multicomponent.rs)。

## 新日志如何定位下一处耗时

启用 `CYCLETRIM_GPU_PROFILE=1` 后，每 64 个批次采样一次完整阶段用时，第一批也会采样。Linux 用户可在 Steam 启动选项中加入 `CYCLETRIM_GPU_PROFILE=1 %command%`；采集结束后移除该设置。

| 日志字段 | 包含的工作 |
|---|---|
| `simulation_wall` | 命令处理、仿真推进及期间的同步等待 |
| `grid_publish_wall` | 事件结算与网格发布 |
| `textures_wall` | 原生日照、液体和流量纹理生成 |
| `species_publish_wall` | 面向托管层的逐格组分记录发布 |
| `finalize_wall` | 发布收尾、帧信息和可见性交换 |
| `liquid_flow_and_sync_wall` | 区域日志中的温度事件合并、CPU 液体求解与相关网格同步 |

批次记录每 64 批采样，区域记录沿用每 61 个区域调用采样；两者频率不同。上述字段记录墙钟时间，包含等待，单位为 ms，不能直接当成 CPU 忙碌时间。`gpu_active` 表示 GPU 后端是否存在；具体区域仍可能因尺寸阈值选择 CPU。比较相同存档、镜头和速度下的记录，可以分别判断是否应继续优化仿真输入整理、液体求解或发布工作。依据：[批次计时](../mods/CycleTrim/OniSim/native/src/frames_engine.rs)、[区域计时](../mods/CycleTrim/OniSim/native/src/simulation_step.rs)。

## 构建、测试与交付

代码已分阶段推送到 `main`：小区域修复 `b570414`、日照优化 `d8ba373`、气体暂存及计时 `4940b9e`。本次二进制对应完整代码提交 `4940b9e6a74b3b92fe667b4638c8331e24760594`，报告后续提交不会改变该源码摘要。

| 验证层次 | 结果 | 范围 |
|---|---|---|
| 原生完整测试 | 116 通过、11 失败、8 忽略 | 10 项缺少硬件 Vulkan 适配器；1 项 GPU pathfinder 初始化返回 -1。整套测试未全绿。 |
| 日照正确性与性能 | 5 项正确性通过；18／18 性能门槛通过 | 输出字节、浮点环境、冻结旧实现与完整 24 场景样本。 |
| 气体准备正确性与性能 | 9 项正确性通过；14／14 性能门槛通过 | 含 350 组输入对照，保留全部 952 个计时样本。 |
| GPU 输入与既有守卫 | 6 项宿主测试通过 | 材质校验、CPU/CFL、焓输入和 WGSL 验证，不等价于设备执行验收。 |
| 诊断开启后的现有消息／发布测试 | 18 通过、2 忽略 | 实际输出批次阶段字段和 `gpu_active=false`。 |
| `onim build -m CycleTrim --release` | 成功，构建摘要 0 错误、28 警告 | 固定引用程序集构建；警告来自既有 Jlib 文档注释。 |
| 安装包加载与依赖 | 通过 | 包内 Linux 原生库导出／初始化／关闭；bootstrap 依赖门；外部 Jlib；未夹带游戏 DLL。 |
| Release 二进制守卫 | 16／16 通过 | 检查本次构建的托管实现。 |
| 版本及内容一致性 | 通过 | CycleTrim 0.4.11.0、Jlib 0.3.2.0；最低 Jlib 0.3.2；ZIP 校验、原生格式与源码／二进制摘要。 |

上述宿主构建使用 .NET 10.0.401 SDK 与 Rust 1.99.1，不执行官方游戏程序集。原生加载测试显式关闭 GPU，其结果只证明包在本机宿主中的加载与 CPU 初始化路径。

截至 **2026-11-09 08:34 UTC**，最终代码的 [Mod quality](https://github.com/LIghtJUNction/OniMods/actions/runs/42585889904) 与 [ONI reference compatibility](https://github.com/LIghtJUNction/OniMods/actions/runs/42585889804) 均通过。[Native platforms](https://github.com/LIghtJUNction/OniMods/actions/runs/42585889683)、[Transport](https://github.com/LIghtJUNction/OniMods/actions/runs/42585889863)、[Diamond](https://github.com/LIghtJUNction/OniMods/actions/runs/42585889576) 整项仍失败：Linux／Windows 缺硬件 Vulkan 设备，macOS 当前目标未启用 Vulkan 后端；pathfinder 在初始化处失败。两个 Lean 证明 job 均成功。本轮没有跳过或修改这些运行测试；三平台 bundle 因前置测试失败而未生成。

本次安装包仅含 **Linux x64** 原生库。Windows／macOS 没有混入旧版本库；本轮没有更新 Steam Workshop。原生构建记录继续保持 `gameRuntimeTested=false`。

交付位置：[Google Drive 文件夹](https://drive.google.com/drive/folders/1Y62vLJ34FaY0bm8-uocGVEhmXviP6CNp)。其中包含 `CycleTrim-0.4.11-linux-x64.zip`、`Jlib-0.3.2.zip`、本报告和 `CycleTrim-0.4.11-cpu-evidence.zip`（原始测量、失败候选记录及验证日志）。最低 Jlib 版本仍是 0.3.2；已安装该版本时不需要升级依赖。

| 安装包 | SHA-256 |
|---|---|
| CycleTrim-0.4.11-linux-x64.zip | `33d23cdbf6ded2d8eaed649e50aee3d80b6d8157a133cf3518e1caeb7f70227b` |
| Jlib-0.3.2.zip | `d4f1a880997ae18d267cc33605a5d4bacb5d3abbced9f4c18ee69acacc8b550b` |

完整的 [measurements.json](cycletrim-cpu-performance-20261109/measurements.json) 包含全部原始样本、各场景统计、测量边界、源码指纹、内存容量和逐项 CI 结果。对应生产回归与基准已进入 GitHub 源码；证据 ZIP 保留构建及运行日志，便于复查。

## 复现实验

在代码提交 `4940b9e` 上，使用 Rust 1.99.1 Release，依次执行下列命令。两个基准分开运行，避免互相争用 CPU；`taskset` 将测试固定在 CPU 0，其他机器应选择实际允许使用的核心并记录硬件信息。硬件与系统负载变化会改变绝对耗时和比例。

```bash
cargo +1.99.1 test --release --locked --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml --lib presentation::sun::tests:: -- --test-threads=1
cargo +1.99.1 test --release --locked --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml --lib multicomponent_flux:: -- --test-threads=1
taskset -c 0 cargo +1.99.1 test --release --locked --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml --lib presentation::sun::tests::benchmark_sun_indexing -- --exact --ignored --nocapture --test-threads=1
taskset -c 0 cargo +1.99.1 test --release --locked --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml --lib multicomponent_flux::staging::tests::benchmark_gas_staging -- --exact --ignored --nocapture --test-threads=1
```

固定旧实现已保存在测试源码中，基准的预热、交替顺序和门槛也在源码中。完整原生测试仍用 `cargo +1.99.1 test --release --locked --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml --lib -- --test-threads=1`；本轮记录保留其 GPU 初始化失败，不用局部通过结果替代整套状态。

## 验证范围与证据核对

本轮没有可用的硬件 GPU 和 ONI 实机测试会话，尚未测量 Unity／Mono、真实存档、GPU 内核或整局 FPS。宿主正确性、函数基准和实际游戏验收分别记录。液体颜色向量化候选在受控对照中出现无液体场景退步，已排除；完整日志和样本保留，交付源码继续使用已接受的液体实现。

按 Research-Paper-Writing-Skills 的五个维度核对主要主张：

| 核对项 | 依据与结论 |
|---|---|
| 贡献 | 修复实际后端选择错误，减少气体整理分配／扫描和日照跨行访问；源码与测试可定位。 |
| 表达 | 函数耗时统一用 ms，说明基线、采样顺序和计时边界；没有换算游戏帧率。 |
| 实验强度 | 输入准备 14／14、日照 18／18 固定门槛通过；保留全部样本，失败候选未交付。 |
| 验证完整性 | 宿主输出与浮点回归已执行；真实 GPU、Unity／Mono 和存档仍待实际会话验收。 |
| 方法有效性 | 保留各列运算顺序、浮点异常分支及后端输入校验；分段日志用于确认真实场景的下一处耗时。 |
