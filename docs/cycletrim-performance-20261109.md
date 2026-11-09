# CycleTrim 0.4.9 性能更新

日期：2026-11-09（Asia/Taipei）。

本次减少了纹理发布前的元素校验开销、空闲批次的整图复制，以及资源面板悬停的临时集合分配。三项改动都已进入 `main`，版本为 **0.4.9**。下表来自实际生产函数或 Harmony 补丁的合成工作负载；测量范围是相应 CPU 路径，尚未进行 ONI 存档中的呈现 FPS 测量。

## 结果与范围

同一工作负载下的改前、改后结果。每个计时样本先计算多次调用的平均耗时，再对这些样本取中位数；分配量按一次悬停进入加退出计量。

| 测量对象 | 场景 | 改前 | 改后 | 减少 |
|---|---|---:|---:|---:|
| 完整纹理校验函数 | 736×304 网格 | 243.019 μs | 183.661 μs | 24.4% |
| 工作线程空批次 `Engine::run_batch` | 736×304，双缓冲预热后 | 1.167 ms | 0.276 ms | 76.3% |
| 工作线程零时间帧批次 `Engine::run_batch` | 736×304，双缓冲预热后 | 1.140 ms | 0.371 ms | 67.4% |
| 资源悬停分配 | 10,000 个拾取物 | 935,752 B | 192 B | 99.98% |

完整原始样本、分位数、运行顺序和运行时信息见 [measurements.json](cycletrim-performance-20261109/measurements.json)。共享运行环境有明显长尾；所有样本均保留，没有用最快一次或删除离群值来计算表中收益。

### 1. 纹理发布前批量检查元素编号

每个格子的元素编号都必须落在元素表范围内。`PresentationBuffers::validate` 原来逐个判断，现在直接使用项目已有的 `valid_u16::<false>`：x86-64 路径每次检查八个 16 位编号，其他架构沿用其标量路径。它仍扫描完整的带边界网格，拒绝同样的无效值，并保留原来的错误状态、错误文字和校验顺序。

该改动仅替换整数范围检查，没有改变纹理生成或物理计算。有效路径没有引入新容器或堆分配；这里的分配结论来自代码结构检查，没有对整个原生进程挂接分配计数器。

实现：[presentation.rs](../mods/CycleTrim/OniSim/native/src/presentation.rs)。测试：[presentation_validation_tests.rs](../mods/CycleTrim/OniSim/native/src/presentation_validation_tests.rs)。

### 2. 空闲时复用两份已经更新的网格

世界没有变化时，原路径仍逐批复制元素、质量、温度、辐射、病菌、背墙等数组。本次沿用已有的空闲识别和失效规则，在两份交替缓冲都完成更新后，保留其中的相同数据。按字段宽度计算，736×304 网格每批少写约 7.90 MiB 的重复字段；这是数据量计算，实际耗时按基准单独测量。

累计流量仍逐次复制。它每十五次发布会清零，必须保持“先发布当前值，再清零世界数组”的原顺序。带修改命令或推进时间的批次、空批次与零时间帧之间的切换，以及同步世界写入和存档规范化，继续使复用失效。帧数、事件、纹理地址、退休帧槽和可见性交换照常更新。

实现：[frames_engine.rs](../mods/CycleTrim/OniSim/native/src/frames_engine.rs)、[world.rs](../mods/CycleTrim/OniSim/native/src/world.rs)。回归与基准：[messages_tests.rs](../mods/CycleTrim/OniSim/native/src/messages_tests.rs)。

各尺寸的完整工作线程批次耗时，单位 μs。每个实现、每个场景共 84 个样本。

| 模式 | 网格 | 改前中位数 | 改后中位数 | 改前样本均值 p95 | 改后样本均值 p95 |
|---|---|---:|---:|---:|---:|
| 空批次 | 96×96 | 48.069 | 5.500 | 82.310 | 10.867 |
| 空批次 | 256×384 | 538.035 | 108.835 | 863.489 | 236.592 |
| 空批次 | 736×304 | 1167.350 | 276.139 | 1428.554 | 483.186 |
| 零时间帧 | 96×96 | 34.036 | 5.781 | 62.664 | 19.318 |
| 零时间帧 | 256×384 | 551.478 | 99.002 | 1428.055 | 243.742 |
| 零时间帧 | 736×304 | 1140.089 | 371.339 | 1443.382 | 1007.815 |

测量覆盖 `Engine::run_batch`。游戏线程的公开 Grid 复制、Unity 回调、GPU 等待和画面呈现不在这个计时区间中。完整活跃物理步骤仍走原来的完整发布路径。

### 3. 悬停高亮复用临时集合

每次资源悬停都需要访问拾取物，并避免把同一个控制器重复高亮。原实现每次创建 HashSet 和 List，物品越多，扩容及回收的临时内存越多。现在使用现有线程局部对象池复用这两个集合；完成或异常退出时，`finally` 清空对象引用后归还。嵌套悬停租用独立上下文，回退原版和部分失败的回滚顺序保持不变。

| 拾取物数 | 改前每周期分配 | 改后每周期分配 |
|---|---:|---:|
| 0 | 304 B | 152 B |
| 16 | 1,336 B | 192 B |
| 1,024 | 90,000 B | 192 B |
| 10,000 | 935,752 B | 192 B |

预热后的集合分配不再随拾取物数量增长；反射装箱和接口枚举等固定分配仍然存在。对象池每线程最多保留一个上下文及其集合容量，集合中的游戏对象引用已清空。这里的收益是减少反复分配与回收，容量复用仍占用一定常驻内存。

实现：[PickupHighlightOptimizationPatch.cs](../mods/CycleTrim/Patches/PickupHighlightOptimizationPatch.cs)。行为回归和基准位于 [CycleTrim.Optimizations.Tests](../tests/CycleTrim.Optimizations.Tests)。

## 测量方法

- 基线从 `caf5599e809984eefc3388009df87ed224638436` 开始。纹理校验基准包含这个提交的完整旧 validator，与实际新生产函数同进程交替测量。空闲批次对照使用两份编译后的生产实现，两者都已启用原有 derived-data 复用；比较的是本次新增的 Grid 复制减量。
- 原生基准使用 Rust 1.99.1 Release、x86-64、CPU 0。纹理校验先预热三轮，再执行十七轮交替 ABBA／BAAB，每实现 34 个样本。
- 空闲批次使用相同测试源编译两份可执行文件。八次进程顺序为 `A B B A B A A B`；每个场景预热八批，每次进程保留二十一个样本，合计每实现每场景 84 个样本。96×96、256×384、736×304 分别重复 400、81、35 批形成一个平均耗时样本。表中 p95 是样本均值的分位数，不是单个批次或游戏帧的尾延迟；纹理校验也先对多次调用求平均。`CYCLETRIM_ONISIM_GPU=0` 仅用于这个 CPU 批次基准。
- 悬停基准使用 .NET 10.0.12 x64、关闭分层编译，执行真实 Harmony Prefix 与合成游戏对象。每个规模预热 128 个周期，采样 21 次，每次 128 个周期。分配通过 `GC.GetAllocatedBytesForCurrentThread` 测量；日志输出位于计量区间外。所有同规模样本的分配值一致。未用该组计时日志宣称 Unity/Mono 加速比例。

## 正确性与构建验证

| 检查 | 结果及覆盖 |
|---|---|
| 托管优化回归 | 11 个独立回归族通过，包括真实 Harmony 悬停、去重、嵌套、销毁对象、异常恢复和弱引用生命周期 |
| 悬停负对照 | 同一最新测试链接原生产实现，按预期失败于分配随物品数增长；候选通过 |
| Presentation 回归 | 8 项通过，2 个手动基准忽略；含无效编号、边界、尾部和错误优先级 |
| 整数校验回归 | 3 项通过，覆盖完整 u16/u8 范围、边界计数和未对齐切片 |
| 空闲发布回归 | 256 批逐次比较完整 Grid image、radiation/flow 原始位模式、两槽地址、事件、可见性、修改命令、同步失效及存档规范化；改前和改后均通过 |
| 原生全量 Release | 105 通过、11 失败、6 个手动基准忽略；CPU 回归通过，GPU 验收尚未完成 |
| 生产 `net48` 参考编译 | 通过，0 errors；28 条既有 Jlib 编译警告 |
| 完整 Linux 构建 | `onim build -m CycleTrim --release` 通过，包含原生库；MSBuild 0 errors/0 warnings，Rust 保留 36 条既有未使用项警告 |
| 原生加载器 | 平台选择、ABI 导出、初始化、关闭与已加载模块检查通过 |
| Release 二进制检查 | 16 项通过 |
| 打包一致性 | ZIP 内的三个自有 DLL 和 Linux 原生库与构建输出逐字节一致，版本为 0.4.9，没有打入游戏参考 DLL |

原生全量的十一项失败与此前记录的硬件 GPU 限制一致：十项明确返回 `no hardware Vulkan adapter available`，一项 pathfinder FFI 初始化返回 `-1`。没有修改这些断言或把它们标为忽略。存档规范化的新增宿主用例通过 mutation guard 调用序列化逻辑，未声称覆盖完整 `SIM_BeginSave` FFI 或游戏存档流程。

帧批次回归还明确验证启动屏障：先接收并持有合法的初始空发布，在屏障期间验证非法批次拒绝并提交两帧，释放后严格要求下一批发布两帧。这消除了测试对线程启动先后顺序的依赖。

### GitHub Actions

以下结果均对应实际构建的代码提交 `8b85f498b833f4db53e7d1e70d08e2d00e290297`，全部已结束。运行编号及逐平台记录见 [ci.json](cycletrim-performance-20261109/ci.json)。

| 工作流 | 结果 | 解释 |
|---|---|---|
| [Mod quality](https://github.com/LIghtJUNction/OniMods/actions/runs/42539832331) | 通过 | 模组质量检查完成 |
| [ONI reference compatibility](https://github.com/LIghtJUNction/OniMods/actions/runs/42539832272) | 通过 | 参考编译及 API 表面检查完成；不等于游戏进程验收 |
| [OniSim native platforms](https://github.com/LIghtJUNction/OniMods/actions/runs/42539832279) | 失败 | Linux 105 通过、Windows/macOS 各 101 通过；三平台均有 11 项 GPU 初始化失败、6 个手动基准忽略；新增回归通过 |
| [OniSim transport proof and regression](https://github.com/LIghtJUNction/OniMods/actions/runs/42539832372) | 失败 | 证明及历史组件通过，当前原生套件受相同的 11 项 GPU 初始化失败阻挡 |
| [OniSim diamond proof and regression](https://github.com/LIghtJUNction/OniMods/actions/runs/42539832354) | 失败 | 证明、压力投影参考及内存探针协议通过；原生组件同上，独立 GPU 运算套件为 20 通过、25 失败、6 忽略 |

已读取失败作业日志。Linux/Windows 报告缺少硬件 Vulkan 适配器；macOS 报告未启用 Vulkan backend。原生套件中的 pathfinder FFI 用例以初始化返回 `-1` 失败。以上失败保持可见，没有降低 CI 门槛，也没有将其计入本次性能收益的正确性证据。

主要复现入口：

```bash
dotnet run --project tests/CycleTrim.Optimizations.Tests -c Release \
  -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true

DOTNET_TieredCompilation=0 dotnet run \
  --project tests/CycleTrim.Optimizations.Tests -c Release \
  -p:ImportDirectoryBuildProps=false -- --benchmark-pickup-highlight

cargo +1.99.1 test --release --locked \
  --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml

cargo +1.99.1 test --release --locked \
  --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml \
  presentation::validation_tests::benchmark_validation -- \
  --exact --ignored --nocapture --test-threads=1

CYCLETRIM_ONISIM_GPU=0 cargo +1.99.1 test --release --locked \
  --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml \
  messages_tests::benchmark_warmed_idle_publication -- \
  --exact --ignored --nocapture --test-threads=1

onim build -m CycleTrim --release
```

`onim build` 需要正确配置 ONI 编译引用和项目固定的 Jlib 子模块。本次使用清单固定的参考程序集：声明目标 737790，仓库另跟踪官方源契约 744825；参考编译与实际游戏版本兼容性分开记录。

## 构建包

交付文件为 `CycleTrim-0.4.9-linux-x64.zip`，3,429,909 字节，对应代码提交 `8b85f49`。包中包含托管实现与 `linux-x64/liboni_sim_core.so`，Jlib 仍作为独立运行时模组安装。当前包通过本机加载器检查，未部署至 Steam 游戏进程。

构建命令、退出码、原生元数据与包内一致性结果见 [delivery.json](cycletrim-performance-20261109/delivery.json)。报告与原始测量另保存于 [Google Drive / OniMods / CycleTrim-performance-20261109](https://drive.google.com/drive/folders/108NUgpPIhZrvzAs8DXZRojKVMSoxUnJu)。

## 提交与复核

- [d266081：纹理校验](https://github.com/LIghtJUNction/OniMods/commit/d26608162c400514f7aad6520a48cf05816c24a1)
- [daeee00：悬停集合复用](https://github.com/LIghtJUNction/OniMods/commit/daeee008b80561780756b97c413ef6c6d09fe9ce)
- [8b85f49：空闲网格复用与 0.4.9](https://github.com/LIghtJUNction/OniMods/commit/8b85f498b833f4db53e7d1e70d08e2d00e290297)

| 复核维度 | 结论 |
|---|---|
| 改进价值 | 三项均减少实际生产路径中的重复工作，有独立分配或耗时证据 |
| 表达清楚 | 表格列出函数边界、工作负载、单位和统计口径 |
| 实验证据 | 保留同工作负载对照、完整样本与悬停负对照 |
| 覆盖完整 | CPU/宿主验证已完成；硬件 GPU、Unity/Mono 与实际存档 FPS 明确未验收 |
| 设计合理 | 复用现有整数校验、失效规则与对象池；三个生产改动均经独立只读复核 |
