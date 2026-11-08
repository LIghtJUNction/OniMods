# CycleTrim 源码审查与改进记录

日期：2026-11-08（Asia/Taipei）

本轮完成了差事成本、动画时间补偿、诊断开关、无效设置和 Release 仿真 ID 校验五项修正，并修复了构建与测试中发现的阻塞。托管与原生构建通过，相关宿主回归通过；原生全量测试仍有 11 项 GPU 初始化相关失败，当前环境未找到硬件 Vulkan 适配器。本轮没有进行 ONI 游戏内存档测试或 FPS 测量。

## 审查范围与版本

- 仓库：[LIghtJUNction/OniMods](https://github.com/LIghtJUNction/OniMods)。审查从 `9f7ebd59f871bfbdfbe82141cbddc6c70cd91e5b` 开始，随后接入主分支已有的新提交。
- 范围：CycleTrim 的托管补丁、Fetch 候选路径、缓存、远景动画、诊断，以及新加入的 GPU 差事接口与构建链。
- 工作使用独立 checkout；另一工作目录的未提交文件未参与本轮修改。Jlib 与 OniHarness 使用主仓库固定的子模块提交，未修改子仓库。
- 已分批提交：[参考程序集修复 `dd97815`](https://github.com/LIghtJUNction/OniMods/commit/dd97815ea5a7ddb710cdf3c3daf9d42bef66a660)、[核心修复与宿主测试 `8cb3ada`](https://github.com/LIghtJUNction/OniMods/commit/8cb3ada7b2a12903107c208cc5c84360dd374047)、[原生提交接口与验证修复 `3c745da`](https://github.com/LIghtJUNction/OniMods/commit/3c745da8067724f1cde46dc151fe4136f95cefa6)。Release ID 校验与本记录更新随随后一次提交保存，完整提交以本文件历史为准。

## 已修复的问题

| 优先级 | 问题与影响 | 修正结果 | 主要证据 |
|---|---|---|---|
| P1 | GPU 差事补丁把已经求得的导航成本覆盖为几何距离，可能改变任务排序；计算还增加整图传播与同步回读 | 删除自动覆写补丁及其唯一托管桥接，保留游戏已经求得的成本；独立原生 API 标注为几何距离实验 | 基线调用链、两组最小网格反例、Release DLL 类型缺席检查 |
| P1 | 元素、病菌和背墙 ID 检查仅在 debug 模式执行，Release 接受无效状态 | 恢复现有批量校验，保持每个 step 一次预检 | 原失败用例在 Release 复现；修复后 PreparedStep 5 项通过，含无活动标记的非法写入及合法空值 |
| P2 | 远景动画使用“本帧 dt × stride”，在不等长帧、首次调用或缩放变化时增加或丢失时间 | 用控制器弱键累计真实 dt，执行时消费一次；转近景或开始移动时消费待处理时间，静止对象被剔除时清空 | 同一真实 Harmony 宿主测试在旧实现失败，修复后 7 组场景通过 |
| P2 | 关闭性能诊断后仍执行逐帧计时、GC 采样和周期日志 | 纯计时补丁按现有开关安装，Stopwatch 按需创建；混合职责补丁仅对诊断部分加门控 | 真实 Harmony 测试覆盖关闭、开启及其他 Prefix 跳过原方法 |
| P2 | `TargetFrameRate` 设置被保存后无人读取，实际始终设置 `-1` | 删除该无效设置和无用配置入口；保留已有不限帧行为 | 设置调用链检查与帧策略回归 |

### 1. 保留已有的差事导航成本

旧 `GpuChoreSchedulerPatch` 在 `ChooseChore` 前接收已经通过预条件的 `succeededContexts`，读取其中的 `ctx.cost`，再次调用 GPU，然后覆盖该成本。它没有替代之前的预条件或导航工作。

底层 jump flooding 在多个格子之间传播起点种子，只检查传播落点；最终成本使用曼哈顿距离。墙体中间的阻挡、绕路、梯子、权限、导航能力和工作站位均未被这个计算表达。

两个最小反例按实际 WGSL 初始化、传播和求值规则建立 CPU 模型，并以四邻接、单位边权的 BFS 作对照：

| 输入 | 几何估计 | 网格路径结果 | 意义 |
|---|---:|---:|---|
| 4×1，`nav=[1,0,1,1]`，起点 0，目标 2 | 2 | 不可达 | 种子可以越过封墙 |
| 4×3，墙位于 `(1,0)`、`(1,1)`；起点 `(0,0)`，目标 A `(2,0)` | 2 | 6 | 忽略绕路 |
| 同一地图，目标 B `(3,2)` | 5 | 5 | 将估计写回后，A/B 的距离排序被反转 |

表内数值属于这个最小网格模型，没有把它们当作 ONI 实际导航成本，也没有运行 GPU 来声称性能收益。删除自动路径后，同帧网格更新被误判为已上传、不同 NavGrid 复用同一版本缓存的问题也随该托管路径一并消除。

相关代码：[基线自动补丁](https://github.com/LIghtJUNction/OniMods/blob/9f7ebd59f871bfbdfbe82141cbddc6c70cd91e5b/mods/CycleTrim/Patches/GpuChoreSchedulerPatch.cs)、[原生实验说明](../mods/CycleTrim/OniSim/gpu/src/gpu_pathfinder.rs)、[编译产物校验](../scripts/verify_cycletrim_release_binary.py)。

### 2. 动画按真实经过时间推进

四个调用的 dt 分别为 `0.010、0.020、0.030、0.040` 秒时，实际总时间是 `0.100` 秒。旧实现跳过前三次后，把最后一次的 `0.040` 乘以 4，向动画传入 `0.160` 秒。同一回归在修改前实际报出 `expected 0.1, got 0.16`，修改后通过。

累计状态使用 `ConditionalWeakTable`，值只保存时间，不反向持有控制器。移动实体仍逐次执行。测试还覆盖第一次调用、缩放改变、移动改变、可见性改变、控制器隔离和零时间调用，验证待处理时间被恰好消费一次。

代码：[ZoomRenderAccelerationPatch](../mods/CycleTrim/Patches/ZoomRenderAccelerationPatch.cs)；测试：[ZoomAnimationRegression](../tests/CycleTrim.Optimizations.Tests/ZoomAnimationRegression.cs)。

### 3. 诊断开关控制实际开销

`Game.Update`、`StepTheSim` 的纯计时补丁在诊断关闭时不安装；其余补丁仍执行已有帧率与仿真职责。计时器只在启用诊断后创建，周期统计和 GC 采样也由同一开关控制。

`SimEveryTick` 的 Prefix 同时处理仿真时间，因此没有把整个补丁关闭。它使用每次调用的 `__state` 记录是否启动计时，使其他 Harmony Prefix 跳过本次处理后，Postfix 不会访问未创建的计时器。旧代码在同一测试中失败于“关闭诊断仍输出周期报告”；修复后关闭、开启和外部 Prefix 三种路径均通过。

代码：[FrameRateUnlockPatch](../mods/CycleTrim/Patches/FrameRateUnlockPatch.cs)；测试：[FrameRateDiagnosticsRegression](../tests/CycleTrim.Optimizations.Tests/FrameRateDiagnosticsRegression.cs)。

### 4. Release 仿真步骤验证 ID

GitHub 的完整原生回归发现 `prepared_next_step_revalidates_mutated_state` 失败：下一步接受了被改成 `u16::MAX` 的元素 ID。根因是 `cfg!(debug_assertions)` 在 Release 跳过了元素、病菌和背墙元素检查。

CPU 指令、状态加载与 GPU 回读均有直接写入这些数组的路径，现有 activity/dirty 标记不完整追踪 ID 写入。本轮恢复现有 SSE2/标量批量校验，每步仍只有一次 preflight；没有引入额外重复验证阶段。新增测试覆盖读/写元素、读/写病菌、背墙元素五类非法写入，并确认合法的空值哨兵继续被接受、拒绝无效状态时不推进随机状态或 tick。

代码：[simulation_step.rs](../mods/CycleTrim/OniSim/native/src/simulation_step.rs)；测试：[messages_tests.rs](../mods/CycleTrim/OniSim/native/src/messages_tests.rs)。

## 构建与验证维护

**参考程序集补齐。** 原主分支的 Jlib pack 缺少 `UnityEngine.AssetBundleModule`、`UnityEngine.AudioModule` 和 `UnityEngine.UnityWebRequestAudioModule`。三项均从清单原有、固定版本与 SHA-256 的 Unity 包提取，并新增各文件的大小与散列验证；未更换原包或放宽来源校验。[清单](../ci/oni-reference-assemblies.json)

**原生跨模块访问修复。** CI 实际报出 Rust `E0616`：`gpu_sim.rs` 直接访问另一 crate 中私有的 `GpuContext.queue`。修复让现有 `GpuContext::submit` 返回提交编号，热计算通过该公开方法提交并等待对应结果，队列字段保持原有封装。[公开提交接口](../mods/CycleTrim/OniSim/gpu/src/gpu_context.rs)

**测试随当前实现更新。** PerformanceProbe 宿主项目链接当前 Jlib 的真实 HoverCard 实现，补充游戏边界夹具，并验证卡片元素名称、质量、回退、同帧去重和绘制坐标恢复。过期的性能报告源码契约改为检查当前同步发布流程：必须先记录 scheduler 所有权、回调传入该 scheduler，并禁止修改正在遍历的调度队列；原有 generation 和旧回调隔离检查保留。丢失所有权与重新入队两种临时 mutation 均被正确拒绝。

## 验证结果与重现命令

运行环境：Linux，.NET SDK `10.0.401`，Rust `1.99.1`，ILSpy `11.0.0.9375`。

| 验证 | 结果 | 范围 |
|---|---|---|
| Jlib `netstandard2.1` pack | 通过 | 固定参考程序集下的共享库构建 |
| CycleTrim `net48` Release | 通过，0 errors | 实际生产 C# 项目；Jlib 既有允许警告仍保留 |
| OniSim 原生 Release | 通过，0 errors | `--locked`，包含 GPU crate 的真实 Rust 编译；既有 dead-code 等警告仍保留 |
| PreparedStep Release 回归 | 5 passed、0 failed、97 filtered out | 包括旧实现失败的下一步状态重验和新增 ID 边界用例 |
| 原生全量 Release 测试 | 87 passed、11 failed、4 ignored、0 filtered out | 剩余 10 项明确报告无硬件 Vulkan 适配器，另 1 项为同一 GPU 初始化路径的 FFI 返回 `-1`；完整套件尚未通过 |
| CycleTrim Optimizations | 10 个回归族通过 | 链接生产代码，含新动画和诊断场景 |
| Fetch | 5,899 条断言、1,462 个三路径差分用例通过；兼容性用例通过 | 真实 Harmony 与合成游戏接口 |
| PerformanceProbe | 非计时回归通过 | 包含 options 88、fetch layout 42、多组分 44 条断言及计数/并发/生命周期检查 |
| NavGridProbe | 通过 | 导航工作量探针宿主回归 |
| 源码与 Python 检查 | 65 项均已验证通过 | 初次批量通过 64 项；唯一失败项修复后定向复测，并通过两项 mutation 反例；其余结果复用 |
| Release 二进制检查 | 16 项通过 | 对实际生成 DLL 检查，包括已删除的两个 GPU 托管类型 |
| 差异空白检查 | 通过 | `git diff --check` |

主要命令：

```bash
dotnet pack mods/Jlib/Jlib/Jlib.csproj -c Release \
  -p:TargetFrameworks=netstandard2.1 \
  -p:OniManagedPath="$ONI_MANAGED_PATH" -p:ImportDirectoryBuildProps=false

dotnet build mods/CycleTrim/CycleTrim.csproj -c Release \
  -m:1 -nodeReuse:false -p:BuildInParallel=false \
  -p:OniManagedPath="$ONI_MANAGED_PATH" -p:ImportDirectoryBuildProps=false \
  -p:IncludeOniSim=false -p:TreatWarningsAsErrors=true

cargo +1.99.1 build --release --locked \
  --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml
cargo +1.99.1 test --release --locked \
  --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml prepared_

dotnet run --project tests/CycleTrim.Optimizations.Tests -c Release
dotnet run --project tests/CycleTrim.Fetch.Tests -c Release
dotnet run --project tests/CycleTrim.PerformanceProbe.Tests -c Release -- --skip-synthetic-performance
dotnet run --project tests/CycleTrim.NavGridProbe.Tests -c Release
python3 scripts/check_mods.py --static-only
python3 scripts/verify_cycletrim_release_binary.py
git diff --check
```

`ONI_MANAGED_PATH` 指向按仓库清单下载并验证的参考程序集目录。下载和编译使用固定引用；清单当前区分官方跟踪版本 `744825` 与声明版本 `737790` 的编译镜像，辅助 Unity 包只用于编译。托管构建使用 `IncludeOniSim=false`，原生模块单独构建；自动生成的托管验证包没有作为完整安装包交付。

## 后续验证边界

- GitHub 在 `3c745da` 上的 [Mod quality](https://github.com/LIghtJUNction/OniMods/actions/runs/42486325133) 已通过。该提交的原生全量任务暴露了随后修复的 Release ID 校验问题，以及 GPU 适配器环境问题：[transport](https://github.com/LIghtJUNction/OniMods/actions/runs/42486325022)、[diamond](https://github.com/LIghtJUNction/OniMods/actions/runs/42486325085)。后续提交的 CI 应按对应提交重新查看；本记录不将前一提交的结果描述为后一提交的 CI 通过。
- GPU 数值回归需提供当前代码接受的硬件 Vulkan 适配器。已保持全部测试与硬件选择要求，未更改 ignore 标记。
- 游戏内验收尚待实际 ONI 环境：同一存档中的任务选择、动画切换、不同模组组合、存档读写与实际帧率。
- 保留的原生 jump-flood API 仍是独立实验。审查还发现其起始步长对部分 `2^k+1` 尺寸的覆盖问题，以及超过 2,048 个候选时的静默截断；它们不再通过已删除的自动托管路径影响差事排序。后续应为该实验定义明确接口与独立 GPU 一致性测试。
- 没有报告 FPS 提升比例。代码删除了额外的差事 GPU 计算，诊断关闭时也去除了对应计时与日志开销；游戏中的收益需要同场景测量。

## 复核

| 维度 | 自检问题 | 结论 |
|---|---|---|
| 改进价值 | 是否改变可观测的错误行为？ | 成本覆盖、0.10→0.16 时间偏差、关闭诊断仍输出日志均有代码或执行证据 |
| 表达清楚 | 是否说明原因、改动和作用？ | 每项按输入、错误结果、修正行为列出 |
| 实验证据 | 测试能区分旧实现与修复吗？ | 动画和诊断均有旧实现失败、修复后通过的实证 |
| 覆盖完整 | 是否交代未执行的验证？ | 明确未进行游戏运行、GPU数值验收和FPS测量 |
| 设计合理 | 是否增加新的假开关或绕过校验？ | 复用原诊断开关，删除无效设置，保留编译、来源和二进制检查 |

托管补丁与验证修正均经过独立只读复核，未发现本轮改动的阻断问题。
