# CycleTrim

![CycleTrim](preview.gif)

**前置 Mod：** 先安装并启用 [Jlib](https://github.com/LIghtJUNction/Jlib) **0.3.5 或更新版本**，再重启游戏。CycleTrim 自 0.4.10 起使用新的批量读取接口，启动器会在加载实现前检查依赖版本。消费者包不再内嵌一份 Jlib。

极具颠覆性的高风险实验性性能优化 Mod，仍在早期开发。方向是重构底层算法，按实测耗时选择 CPU/GPU，减少重复计算与同步等待。顺序是先加速，再兼容。**只在可丢弃的测试存档上使用。**

当前版本（0.4.22）修复微量气体耗尽后的质量残留，并复用液体与气体暂存缓冲区，减少重复分配。OniSim 在原生时间步内混合使用驻留 CPU 算子与 GPU 批处理；多组分发布提供共存记录，旧 Grid 仍显示主要元素。兼容性与存读档仍属实验范围，详见[实现说明](OniSim/docs/multicomponent-publication.md)和[更新记录](CHANGELOG.md)。需要 Jlib 0.3.8 或更新版本；整机帧率收益须以受控实测确认。

下面列出的热路径裁剪是眼下构建里已经有的内容，不是最终形态，也不承诺与原版行为对齐。

本模组来自 [OniMods](https://github.com/LIghtJUNction/OniMods)。onim 是这个仓库里的工具，把缺氧 Mod 的开发、测试和创意工坊发布连成一条直线：`onim dev` 放进游戏里试，`onim build` 打包，`onim publish` 发到 Steam。

## 快速索引（本页为总览）

- [全游戏更新链路与诊断（0.4.12）](#全游戏更新链路与诊断0412)
- [本轮完整实验与验收边界](../../docs/cycletrim-whole-chain-20261109.md)
- [下个版本：实验替换一格一物](docs/next-cell-representation.md)
- [GPU 进度（2026-11-08）](#gpu-进度2026-11-08)
- [多组分假设审计](docs/multicomponent-assumptions-audit.md)
- [算法演进与数学模型 (Stage 2/2.1)](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md#算法演进从一格一物排挤到-gpu-泊松压力投影-stage-221)
- [核心优化](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md#核心功能简要)
- [架构与优化原理](../../docs/architecture-and-principles.md)
- [安装与运行](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md#使用方式)
- [验证与兼容性](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md#兼容与说明)
- [性能实测摘要](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md#性能实测摘要)
- [详细说明与日志](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md#详细说明与日志)

## 全游戏更新链路与诊断（0.4.12）

一次游戏更新包含原生仿真、结果发布、建筑和对象状态机、寻路队列、属性纹理、动画以及 UI、相机和声音。GPU 内核变快之后，主线程仍可能在这些交接处计算或等待。0.4.12 把诊断扩展到 22 个阶段，并测量相邻 `Game.Update` 入口之间的完整间隔；独立 Unity 回调也在审查范围内。[调用链与源码依据](../../docs/cycletrim-whole-chain-20261109.md#2-一次更新经过哪些边界)

在 Steam 启动参数中加入 `--cycletrim-perf-probe`，即可同时开启托管阶段与原生批次诊断。`Player.log` 中的 `[CycleTrim][FrameChain]` JSON 记录完整区间、中位数、P95/P99、各阶段包含子调用的耗时和扣除子调用的自身耗时，以及尚未归因的时间。日志解析命令：

```bash
python3 scripts/analyze_cycletrim_frame_chain.py /absolute/path/Player.log --output frame-chain.json
```

父阶段和子阶段的耗时不能直接相加；`PrepareGameData` 和 `JobManager.Run` 都可能包含工作与等待，未归因时间也不能直接当作 GPU 时间。这里的更新入口频率不是显示器实际呈现的 FPS。诊断默认关闭，关闭时不安装专用计时补丁。[指标定义与验证范围](../../docs/cycletrim-whole-chain-20261109.md#4-22-个阶段的计时含义)

本轮评估了交替空转发布复用、原生内容身份驱动的纹理上传复用，以及减少 worker 对象头复制的候选。四轮完整原生对照均未通过既定性能门槛；四轮上传消融中只有第二轮通过，不能与其他版本拼接验收。因此 0.4.12 撤回本轮性能候选，保留 0.4.11 原有生产优化、22 阶段诊断与手动整链路测试。完整报告保留每轮失败、全部场景和尾部耗时；本轮没有取得整局帧率提升或新的 ONI、硬件 GPU 验收。[完整实验记录与交付范围](../../docs/cycletrim-whole-chain-20261109.md#7-保留决定与交付)

## 算法演进：从一格一物排挤到 GPU 泊松压力投影 (Stage 2/2.1)

作为 CycleTrim 底层物理重构的核心方向，模拟管线正在从原版的**离散状态排挤**演进为**GPU 驻留连续流体泊松投影**。

### 1. 原版旧算法（串行网格遍历 + 排挤分支）
- **串行供受体质量迁移**（受 `ElementRecord.flow` 控制）：
  $$\Delta m = \text{clamp}\left(k \cdot (m_a - m_b), \, -0.125 \cdot m_b, \, 0.125 \cdot m_a\right)$$
- **排挤仲裁（Displacement）**：当新物质试图进入已被占据的格子时，执行 `displace_gas` / `liquid_displacement`，沿邻格进行深度与空间分支搜索。
- **伴随状态维护**：单次迁移量硬截断在供体质量的 $12.5\%$，同步搬运离散温度与病菌计数，改变目标格材料身份并处理 Vacuum / Sink。
- **硬件局限**：严苛的时序依赖和分支跳转导致现代 CPU 宽向量指令（AVX）难以对齐，并在 GPU 上引发严重的线程束分化（Warp Divergence）与缓存颠簸。

### 2. 新版 GPU 连续流体泊松投影算法（Stage 2/2.1 WGSL 内核）
新算法基于变密度有限体积连续流体模型，并实现为全流程 GPU 驻留算子：

- **步骤 1：多组分守恒界面通量输运（Multicomponent Transport Kernel）**
  $$M_c(t + \Delta t) = M_c(t) - \Delta t \cdot \nabla \cdot F_c$$
  每个格子执行完全相同的向量乘加（MAC），彻底消除排挤搜索与抢占仲裁。
- **步骤 2：有限体积中间速度场散度（Divergence Kernel）**
  $$\nabla \cdot u^* = \frac{u(y, x+1) - u(y, x)}{\Delta x} + \frac{v(y+1, x) - v(y, x)}{\Delta y}, \quad \text{RHS} = \frac{\nabla \cdot u^*}{\Delta t}$$
- **步骤 3：变密度泊松方程与红黑高斯-赛德尔求解（RBGS Pressure Kernel）**
  $$\nabla \cdot \left(\beta \nabla p\right) = \frac{\nabla \cdot u^*}{\Delta t}, \quad \text{其中界面渗透率 } \beta_f = \frac{1}{\rho_f} = \frac{2}{\rho_1 + \rho_2}$$
  使用带松弛因子的红黑棋盘格（Red-Black Gauss-Seidel with SOR）多 Pass 并行迭代：
  $$p_i^{(k+1)} = p_i^{(k)} + \omega \left( \frac{\sum_{j \in \mathcal{N}(i)} \beta_{ij} p_j - \text{RHS}_i}{\sum_{j \in \mathcal{N}(i)} \beta_{ij}} - p_i^{(k)} \right)$$
  分为 Red Pass $((x+y) \bmod 2 = 0)$ 与 Black Pass $((x+y) \bmod 2 = 1)$ 分步调度，管线屏障保证内存可见性。
- **步骤 4：无散速度场投影修正（Velocity Projection Kernel）**
  $$u_f^{n+1} = u_f^* - \Delta t \cdot \beta_u \cdot \frac{p_{right} - p_{left}}{\Delta x}$$
  $$v_f^{n+1} = v_f^* - \Delta t \cdot \beta_v \cdot \frac{p_{bottom} - p_{top}}{\Delta y}$$
- **边界条件**：固体壁面与封闭外边界严格强制法向速度 $u_n = 0$，Neumann 齐次边界 $\frac{\partial p}{\partial n} = 0 \iff \beta = 0$。

### 3. GPU 闭环流水线愿景
$$\boxed{ \text{Transport} \longrightarrow \text{Divergence} \longrightarrow \text{Poisson} \longrightarrow \text{Projection} \longrightarrow \text{Transport} }$$
目标是让完整物理循环驻留显存，CPU 处理状态快照、接口与上层游戏发布；当前实际执行边界见下节，尚未实现这一整环驻留目标。

> **阶段说明**：Stage 2 的 CPU 数值参考（残差 $10^{-7}$、散度收敛、零速度不动点等 6 项测试）与 Stage 2.1 的 WGSL 核心算子（7 项 GPU Operator 测试及 Benchmark）均已就绪。这些压力投影算子保留为独立实验与参考。0.4.15 的生产气体路径使用密度驱动的有界传输和热计算，已移除不可压缩投影及中间速度回读。

## GPU 进度（2026-11-08）

OniSim 启动时用 wgpu 30（Vulkan 后端）创建 `GpuSimulation`，默认开启；`CYCLETRIM_ONISIM_GPU=0` 可在启动前关闭，初始化失败时打印原因并回到 CPU 路径。`CYCLETRIM_GPU_PROFILE=1` 或 Steam 启动参数 `--cycletrim-gpu-profile` 打开抽样耗时日志。

**已提交（main）并接入时间步：**

- 全图 GPU 热扩散（变系数有限体积）与组分显热焓换算；温度在步间驻留显存，组分存在掩码压缩上传，按变化块上传。
- 符合条件的气体区域：多组分浓度扩散、密度梯度速度、有界传输与热计算在一次 GPU 提交内接力。
- 符合条件的液体区域：GPU 多组分稀疏扩散，与 CPU 气压遍历重叠执行；扩散快照只覆盖活动区域。
- CPU 侧：区域资格扫描和打包、相变结果应用、事件与建筑组件仍需处理；没有 GPU 或区域不符合条件时，相关阶段执行 CPU 路径。空闲零时长帧可复用已经生成的发布结果。

**2026-11-09 整链源码审查：** 气泡、温度、相变选择、符合条件的气体压力/传输、疾病与辐射存在 GPU 调用路径；上传、等待、读回及 CPU 应用仍穿插在固定步内。液体多组分扩散有 GPU 路径，但生产 `gpu_liquid_flow` 固定为 `false`，液体压力、流动和有序排挤仍由 CPU 执行。仓库中存在液体流动等 WGSL 算子和 `resident_cells` 缓冲，不代表整条路径已经接管生产更新。0.4.15 已移除生产气体压力投影，并将环境辐射改为 CPU 直接遍历；上述记录保留其历史时点。逐阶段依据见[原生固定步审查](../../docs/cycletrim-whole-chain-20261109.md#原生固定步也保留-cpu-阶段和同步)。

**仍未完成：** 每个时间步仍有 CPU 上传、GPU 等待与读回，不是全流程显存驻留；事件应用、建筑交互和结果发布仍在 CPU；气压/分压/液体体积占比发布值、三种以上组分、完整存读档未贯通；整局帧率提升未完整验证。最近一份 `Player.log`（11-08 00:26）记录 GPU 在 RTX 4060 Laptop 上激活，之后的提交尚无新的实机日志。细节见 [OniSim/gpu/README.md](OniSim/gpu/README.md) 与 [OniSim/docs/progress.md](OniSim/docs/progress.md)。

## 核心功能（简要）

- 优化拾取候选的成本复用逻辑。
- 仅对忙碌复制人的高开销任务进行间歇刷新，空闲复制人不受影响。
- 节流静止小动物的重复导航探测。
- 存档时复用组件类型的固定标记和名称，减少重复反射查询。
- 电力视图每次刷新时复用同一电路的需求功率查询。
- 输送轨道构建中，重复搜索较多的长路径可使用临时索引。
- 动画覆盖数据逐位相同时，避免重复增加版本号和复制纹理数据。
- 可选降低气液管道显示网格的重建频率；模拟更新、每帧绘制和音效保留原路径。
- 大地图远景缩放时按缩放级别对静止物体动画、材料视图与管道视图更新分帧降频（复制人保留更高刷新率）；纹理插值与延时摄影相机优化由 Jlib 提供。
- 强制关闭 VSync 并解除帧率上限；被其他代码恢复时重新应用并记录一次日志。

## 使用方式

- 订阅后按常规启用即可。
- 推荐配合现有存档直接运行，无需额外配置。

### 模组设置

在游戏的**模组 → CycleTrim → 设置**中调整。设置界面由独立 Jlib 提供；更改后按提示重启游戏。

- **OniSim 仿真核心**：启动即强制映射 Rust SimDLL，不再提供官方 SimDLL 运行时回退。紧急关闭用 `CYCLETRIM_ONISIM_ENABLE=0`。
- **物流候选、忙碌复制人刷新、静止小动物寻路、异步寻路去重、小动物思考刷新**：五项优化可分别关闭，便于排查其他模组的兼容问题；默认保持开启。关闭某项后不安装该组优化补丁。
- **性能诊断日志**：默认关闭。开启后把耗时和内存统计写入 `Player.log`，统计本身有额外开销。
- **存档类型信息缓存**：默认开启，减少存档时重复反射，并在同一次存档内复用相邻的类型查询；可单独关闭，修改后需要重启。
- **电力视图查询复用**：默认关闭，可选开启。它只在本次视图更新内复用；电路重复少时可能增加开销，未知电力组件或冲突补丁保留原查询。修改后需要重启。
- **输送轨道路径查找**：默认关闭。在一次路径构建内，扫描量足够大时才建立索引；短路径、早期命中和少量查询保留直接搜索。路径被修改时重新搜索，修改设置后需要重启。
- **动画重复更新优化**：默认关闭。按浮点位模式比较覆盖数据，完全相同时保留版本号，让显示端复用已经复制的数据；发生过写入异常的实例继续保留原更新行为。修改设置后需要重启。
- **状态机查询复用**：默认关闭。列表未变化时复用状态机查找结果；列表变更后重新查找，短列表和工作线程保留原路径。修改后需要重启。
- **管道动画刷新**：默认 Full。Half / Quarter 在输入稳定时每 2 / 4 个渲染帧重建显示网格，中间帧继续绘制；模拟、视野、高亮等变化立即刷新，达到 100 毫秒时在下次绘制刷新。内容覆盖层及音效请求保留完整更新。修改后需要重启；FastTrack 或不兼容渲染补丁存在时让出这组优化。小工作量可能因缓存检查而变慢，详见[实现、完整测量与验收范围](../../docs/cycletrim-visual-refresh.md)。

这些优化来自对 oni-moono 机制的研究，使用独立实现。存档与电力视图的验证见[首轮说明](../../docs/cycletrim-moono-optimizations.md)，轨道查找与动画更新的设计、测试及收益范围见[后续优化报告](../../docs/cycletrim-moono-followup.md)。各项合成基准分别度量具体工作量；实机性能需用同一存档和设置交替测量。

短轨道、提前返回和动画比较等退化路径的后续改进，见[开销回收与复测报告](../../docs/cycletrim-regression-recovery.md)。其中保留了旧版与新版的交错样本，以及未采纳方案的记录。

如果列表里只有旧的 `CycleTrimHarness` 测试副本，需退出游戏后更新 CycleTrim 并启用 Jlib，并确认只启用一份相同 `staticID` 的安装。源码中存在设置页，不代表当前游戏加载的旧 DLL 也有这个入口。

## 兼容与说明

- 与 ONI DLC 与主线兼容。
- 以稳定性优先，避免影响原版关键玩法。
- CycleTrim 不再接管 `SmartReservoir` 的 automation 信号发送；储液罐/气库及复用该组件的第三方建筑保留原版重复通知语义。

### OniSim 原生仿真内核

OniSim 是 CycleTrim 的原生仿真核心，源码位于 [`OniSim/`](OniSim/)。Windows x64、macOS x64、Linux x64 构建默认把本机原生库打入 Mod 包，启动时强制映射接管官方 SimDLL。映射无法热撤销。`CYCLETRIM_ONISIM_ENABLE=0`、`false` 或 `off` 仍可作为启动前的紧急关闭开关。

疾病发射范围使用 Lean 证明的逐行菱形扫描，发射范围与疾病生长后处理已接入最多 8 线程的常驻线程池；小任务保留串行。未设置时默认 `CYCLETRIM_ONISIM_WORKERS=8`，启动前设置 `CYCLETRIM_ONISIM_WORKERS=1` 可关闭内核并行。GPU 算子相关开关见上文 [GPU 进度](#gpu-进度2026-11-08)。源码内的验收记录见 [`OniSim/docs/parallel-validation.md`](OniSim/docs/parallel-validation.md)。

历史二进制的隔离实测曾通过加载、4 工作线程短步运行、另存、重载和继续运行，见 [`OniSim/docs/runtime-validation.md`](OniSim/docs/runtime-validation.md)；这些回执只适用于记录中的精确二进制，不代表当前源码已通过游戏验收。仅用于可丢弃的测试存档；完整原版对照、长时运行、DLC 和其他 Mod 兼容性仍未验收，不能视为完整替代品。Linux 保留已检查的 `Assembly-CSharp` 和 Mono 整文件指纹检查；Windows/macOS 要求 ONI 744825 正式版、相同的原生接口与消息结构布局。其他版本或布局会拒绝映射并在日志中说明原因。

Windows x64、macOS x64、Linux x64 下构建默认包含 OniSim；可用 `IncludeOniSim=false` 生成不带核心的包。运行时默认强制接管；`CYCLETRIM_ONISIM_ENABLE=0` 可在启动前紧急关闭。`CYCLETRIM_ONISIM_NATIVE_PATH` 可用绝对路径覆盖包内原生库；未设置时使用 Mod 目录下 `OniSim/<platform>/` 中的 `.dll`、`.dylib` 或 `.so`。macOS 需要 x64 游戏进程（Apple Silicon 上使用 Rosetta）。三平台 CI 构建与宿主加载器测试不等于全平台 ONI 实机验收。

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
│   ├── native/         # Rust SimDLL（时间步、多组分通量、发布）
│   └── gpu/            # wgpu/WGSL GPU 算子与基准
├── Core/               # 与游戏补丁无关的核心逻辑（如 BrainRateCap）
├── Patches/            # Harmony 补丁
├── docs/               # Steam 描述、设计与审计文档、截图
└── CycleTrim.csproj
```

与仓库其它 mod 约定一致：根目录只保留入口与工程/元数据文件；补丁进 `Patches/`，可复用逻辑进 `Core/`。

## 详细说明与日志

- 变更记录： [CHANGELOG.md](CHANGELOG.md)
- 运行时目标验证脚本： [verify_cycletrim_target_contract.py](../../scripts/verify_cycletrim_target_contract.py)
