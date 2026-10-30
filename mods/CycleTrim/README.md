# CycleTrim

ONI 性能优化 Mod。该 Mod 针对缺氧的几个高频模拟路径做降频优化，目标是在保持行为兼容的前提下降低开销、提升稳定性。

## 快速索引（本页为总览）

- [核心优化](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md#核心功能简要)
- [架构与优化原理](../../docs/architecture-and-principles.md)
- [安装与运行](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md#使用方式)
- [验证与兼容性](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md#兼容与说明)
- [性能实测摘要](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md#性能实测摘要)
- [详细说明与日志](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md#详细说明与日志)

## 核心功能（简要）

- 优化拾取候选的成本复用逻辑。
- 仅对忙碌复制人的高开销任务进行间歇刷新，空闲复制人不受影响。
- 节流静止小动物的重复导航探测。
- 存档时复用组件类型的固定标记和名称，减少重复反射查询。
- 电力视图每次刷新时复用同一电路的需求功率查询。
- 输送轨道构建中，重复搜索较多的长路径可使用临时索引。
- 动画覆盖数据逐位相同时，避免重复增加版本号和复制纹理数据。

## 使用方式

- 订阅后按常规启用即可。
- 推荐配合现有存档直接运行，无需额外配置。

### 模组设置

在游戏的**模组 → CycleTrim → 设置**中调整。PLib 已内置，不需要另装配置工具；更改后按提示重启游戏。

- **OniSim 仿真核心**：沿用原有默认值；关闭后在下次启动保留原版 SimDLL。
- **物流候选、忙碌复制人刷新、静止小动物寻路、异步寻路去重、小动物思考刷新**：五项优化可分别关闭，便于排查其他模组的兼容问题；默认保持开启。关闭某项后不安装该组优化补丁。
- **性能诊断日志**：默认关闭。开启后把耗时和内存统计写入 `Player.log`，统计本身有额外开销。
- **存档类型信息缓存**：默认开启，减少存档时重复反射；可单独关闭，修改后需要重启。
- **电力视图查询复用**：默认关闭，可选开启。它只在本次视图更新内复用；电路重复少时可能增加开销，未知电力组件或冲突补丁保留原查询。修改后需要重启。
- **输送轨道路径查找**：默认关闭。在一次路径构建内，扫描量足够大时才建立索引；短路径、早期命中和少量查询保留直接搜索。路径被修改时重新搜索，修改设置后需要重启。
- **动画重复更新优化**：默认关闭。按浮点位模式比较覆盖数据，完全相同时保留版本号，让显示端复用已经复制的数据；发生过写入异常的实例继续保留原更新行为。修改设置后需要重启。

这些优化来自对 oni-moono 机制的研究，使用独立实现。存档与电力视图的验证见[首轮说明](../../docs/cycletrim-moono-optimizations.md)，轨道查找与动画更新的设计、测试及收益范围见[后续优化报告](../../docs/cycletrim-moono-followup.md)。各项合成基准分别度量具体工作量；实机性能需用同一存档和设置交替测量。

如果列表里只有旧的 `CycleTrimHarness` 测试副本，需退出游戏后更新到包含 PLib 的 CycleTrim 包，并确认只启用一份相同 `staticID` 的安装。源码中存在设置页，不代表当前游戏加载的旧 DLL 也有这个入口。

## 兼容与说明

- 与 ONI DLC 与主线兼容。
- 以稳定性优先，避免影响原版关键玩法。
- CycleTrim 不再接管 `SmartReservoir` 的 automation 信号发送；储液罐/气库及复用该组件的第三方建筑保留原版重复通知语义。

### OniSim 原生仿真内核

OniSim 是 CycleTrim 的原生仿真核心，源码位于 [`OniSim/`](OniSim/)。Windows x64、macOS x64、Linux x64 构建默认把本机原生库打入 Mod 包；Steam 发布包通过三平台 CI 合并全部原生库，并在启动阶段尝试把 `Assembly-CSharp` 的 `SimDLL` 导入映射到 OniSim。PLib 的 CycleTrim Mod 设置中“Use OniSim simulation core”默认开启；关闭后重启 ONI，启动时就会保留原版 SimDLL。映射无法热撤销。`CYCLETRIM_ONISIM_ENABLE=0`、`false` 或 `off` 仍可作为启动前的强制关闭开关。

疾病发射范围使用 Lean 证明的逐行菱形扫描，发射范围与疾病生长后处理已接入最多 8 线程的常驻线程池；小任务保留串行。启动前设置 `CYCLETRIM_ONISIM_WORKERS=1` 可关闭内核并行。源码内的验收记录见 [`OniSim/PARALLEL-VALIDATION.md`](OniSim/PARALLEL-VALIDATION.md)。

隔离实测已通过加载、4 工作线程短步运行、另存、重载和继续运行，见 [`OniSim/RUNTIME-VALIDATION.md`](OniSim/RUNTIME-VALIDATION.md)。仅用于可丢弃的测试存档；完整原版对照、长时运行、DLC 和其他 Mod 兼容性仍未验收，不能视为完整替代品。Linux 保留已检查的 `Assembly-CSharp` 和 Mono 整文件指纹检查；Windows/macOS 要求 ONI 744825 正式版、相同的原生接口与消息结构布局。其他版本或布局会拒绝映射并在日志中说明原因。

Windows x64、macOS x64、Linux x64 下构建默认包含 OniSim；可用 `IncludeOniSim=false` 生成不带核心的包。正常情况下通过游戏 Mod 设置页启用或关闭，并按 PLib 提示重启 ONI。`CYCLETRIM_ONISIM_ENABLE=0` 可在启动前强制关闭，`1` 可显式允许加载。`CYCLETRIM_ONISIM_NATIVE_PATH` 可用绝对路径覆盖包内 `.so`；未设置时使用 Mod 目录下 `OniSim/<platform>/` 中的 `.dll`、`.dylib` 或 `.so`。macOS 需要 x64 游戏进程（Apple Silicon 上使用 Rosetta）。三平台 CI 构建与宿主加载器测试不等于全平台 ONI 实机验收。

## 性能实测摘要

以下为固定场景增量测试（非全局通用），硬件与存档不同会有差异：

| 场景 | 指标 | 优化前 -> 优化后 | 改善 |
|---|---|---|---|
| 忙碌复制人节流 | FPS 中位数（CPU 热点场景） | `70.6 → 85.1` | `+20.6%` |
| 忙碌复制人节流 | Brain 总耗时 | `5.087s → 2.449s` | `-51.9%` |
| 静止小动物导航节流 | FPS 中位数 | `81.23 → 109.28` | `+34.5%` |
| 静止小动物导航节流 | Navigator 热点总耗时 | `826.852ms → 47.970ms` | `-94.2%` |
| UpdatePickups 缓存 | 命中率中位数 | `24.37%` | `命中率 24.37%` |
| UpdatePickups 缓存 | 平均耗时 | `41.279us → 35.708us` | `-13.5%` |
| UpdatePickups 缓存 | 每秒命中数（30s 窗口估算） | `~6.29/次` | `命中有效复用` |

### Brain 调度的函数级合成基准（已接入，仅普通 Creature）

Brain 限流已接入生产 Harmony 补丁，但只作用于原版普通 `CreatureBrainGroup`。复制人（Dupe）、自定义 Brain 组继续走原版调度；priority Brain 保留原版队列选择和即时执行路径，不受普通 Creature 限额影响。

> **这是独立的合成函数基准，不是游戏内 FPS 测试，也不能直接换算为整局性能提升。**

- 环境：独立 `.NET 10` Release 基准，不启动 ONI。
- 场景：模拟 `240` 渲染 FPS、持续 `30` 秒，每次普通 Brain 调用执行 `512` 次确定性 CPU 工作。
- 方法：`2` 次预热，`7` 组 baseline/candidate 交替配对测量，取中位数；每组校验调用数与 checksum 一致。
- 调用口径：总调用从 `43,200` 降至 `19,200`（`-55.56%`）；其中普通 Creature 调用从 `36,000` 降至 `12,000`（`-66.67%`），Dupe 的 `7,200` 次调用保持原版。
- 代表性本机结果：耗时从 `255.580ms` 降至 `113.289ms`（`-55.67%`，`2.26x`）。
- 边界：不包含 priority 工作量、真实 Sensors/PathProber 成本或游戏内 Harmony 调度开销，不能代表实际 FPS。
- 安全差异：当前游戏 DLL 使用动态 `i != brains.Count` 边界。CycleTrim 同样每轮重读 Count，让帧内新增 Brain 可纳入扫描；但在 Update 中列表收缩到已扫描位置之后时会有界终止，避免原版条件可能无法收敛。

### AsyncPathProber 去重与背压

- 仅支持精确的原版 `CreaturePathFinderAbilities`；Minion、Robot 与自定义 abilities 因动态权限/装备状态无法完整证明，全部回退原版。
- 缓存只在 `Navigator.TakeResult` 正常应用后成立；排队清空、注销、过期未应用及异常不会写入缓存。相同快照最多连续跳过 8 次，随后强制刷新。
- 0.3.3 修复工作线程出队后、回调前主线程已进入下一 tick 的交错：工作线程保留该任务原有的排队标记，只有主线程准备替换任务时才推进标记。跨 tick 及注销后的迟到回调有直接调用生产补丁的宿主回归覆盖。
- 启动时会验证 Creature 的 sentinel 回收方法仍为空实现；若游戏更新改变该契约，整组 Async 优化会自动禁用而不是冒险运行。
- 队列背压按 worker 与 in-flight 动态计算，范围 `1..4`；1 worker 空闲时 quota=2、忙碌时 quota=1。
- 10,000 请求合成矩阵（**不是游戏内 FPS**）：目标命中率 `0/25/50/75/90%` 时，实际 PathProbe 执行为 `10000/7701/5501/3301/2000`，避免 clone 为 `0/2299/4499/6699/8000`；理论 probe 工作下降 `0/22.99/44.99/66.99/80.00%`。8 次有界回退使实际降幅低于名义命中率。

上述数字来自已退役的合成基准项目，保留为历史测量记录。当前宿主回归位于 `tests/`；历史数字不代表当前游戏性能。

完整场景明细见：[dist/CycleTrim/README.md](../../dist/CycleTrim/README.md)（打包产物中的实测段落）。

## 源码目录

```text
mods/CycleTrim/
├── ModInfo.cs          # KMod 入口
├── OniSim/             # 原生仿真核心、C ABI 与加载器
├── Core/               # 与游戏补丁无关的核心逻辑（如 BrainRateCap）
├── Patches/            # Harmony 补丁
├── docs/               # Steam 描述等
└── CycleTrim.csproj
```

与仓库其它 mod 约定一致：根目录只保留入口与工程/元数据文件；补丁进 `Patches/`，可复用逻辑进 `Core/`。

## 详细说明与日志

- 变更记录： [CHANGELOG.md](CHANGELOG.md)
- 运行时目标验证脚本： [verify_cycletrim_target_contract.py](../../scripts/verify_cycletrim_target_contract.py)
