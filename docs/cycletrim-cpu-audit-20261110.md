# CycleTrim CPU 调用链检查（2026-11-10）

检查源码版本：OniMods `16795327`。本轮只检查源码、进程与已有采样，未修改模拟算法、未启动游戏。检查时没有 ONI 进程，因此下述运行数据明确属于此前采样，不能当作当前版本的全帧性能验收。

## 结论与证据边界

此前把 143ms 模拟批次耗时作为优先改 GPU 的依据不充分。该计时覆盖 CPU 指令、数据准备、驱动提交和 GPU 等待，并非 GPU 执行时间，也不是主线程等待时长。

CPU 路径存在明确的重复工作：搬运候选全量扫描、全局失效导致的重复刷新、任务候选收集与排序、每帧寻路队列扫描、全图数据复制、逐格组分解码。应优先测量和减少这些工作，但目前还不能断言其中某一个占当前帧时间的多数。

此前 `/tmp/cycletrim-e2e-20261110-render-batch/player.log` 的 FrameChain 序号 168–170 位于有界运行测试内，三个窗口分别包含 22、19、22 帧，均有对象模拟调用，均没有 GC 集合增长，droppedScopes 为 0：

| 项目 | 各窗口平均耗时范围（ms/帧） |
| --- | ---: |
| 完整 Game.Update 间隔 | 46.43–54.90 |
| Sim.PrepareGameData，同步获取模拟结果 | 0.33–0.72 |
| JobManager.Run，自身计时，含工作与等待 | 5.46–5.72 |
| 对象模拟 AdvanceOneSimSubTick，自身计时 | 3.02–4.99 |
| 动画 UpdateDirty + Render，自身计时相加 | 3.84–5.10 |
| 尚未归因的帧间时间 | 19.57–30.92 |

这排除了“该片段主要被同步获取原生结果拖慢”的解释，但未归因部分仍然很大，不能把它归入 CPU 或 GPU 任意一方。恢复运行时的序号 166–167 另有大尖峰，不能隐藏；167 有 GC 集合增长，但不足以证明尖峰由 GC 导致。

该次测试 action.json 记录运行 8 秒，推进约 5.375 模拟秒。它与后来约 6.235 秒推进约 6 模拟秒的采样不是同一个版本/测试窗口。不得混合得出当前模拟速度或 FPS。

最新保留的短样本中，6.235 秒内主线程累计 CPU 时间 3.00 秒、onisim-physics 2.49 秒、UnityGfxDeviceW 1.61 秒。这说明 CPU 有实际工作，但不能用总 CPU 使用率判断主线程关键路径是否饱和；等待和系统调度不计入这些 CPU 时间。

## 1. 小人分配任务：重复扫描比单次排序更值得先看

真实调用链：

`BrainScheduler.RenderEveryTick -> Brain.UpdateBrain -> Sensors.OnBrainPreUpdate -> PickupableSensor.Update -> UpdateFetches + UpdatePickups -> ChoreConsumer.FindNextChore`

源码入口：

- `decompiled/BrainScheduler.cs:90`：每次按预算更新脑；复制人默认 initialProbeCount 是 1，动物是 5。不是每帧全体复制人重新分配任务。
- `decompiled/PickupableSensor.cs:14`：每次更新先重新构造目的地候选，再重新构造拾取候选。
- `decompiled/FetchManager.cs:390`：遍历所有物品类型，建立 offset 检查作业、候选作业，分别调用两次同步 `GlobalJobManager.Run`，合并后再排序。
- `decompiled/FetchManager.cs:290`：offset 作业即使缓存有效，也遍历物品检查位置。`OffsetTracker.GetOffsets` 已有位置缓存，因此问题不是所有物品都重新计算 offset，而是反复遍历和调度检查。
- `mods/CycleTrim/Patches/FetchPickupCandidatePatch.cs:152`：现有优化把“排序全部候选后去重”变成字典分组，但仍然扫描全部物品、检查当前工人的资格和导航成本。其余外层遍历未被消除。
- `decompiled/GlobalChoreProvider.cs:201`：扫描该世界搬运目的地，查询导航成本、排序和去重；接着处理清扫候选。
- `decompiled/ChoreProvider.cs:167`：收集该世界任务；超过 48 项时按 16 项分块执行同步工作批次。
- `decompiled/ChoreConsumer.cs:303`：每次清空快照、收集、排序，再从末端选择可用任务。
- `decompiled/FetchManager.cs:487`：每个找物品的任务还可能线性查找候选列表，最坏情况下形成“任务数 × 候选数”的检查。

优先改法：物品静态数据、offset 有效性与工人资格/路径数据分离；按世界与物品类型维护脏列表和索引，跨小人复用不依赖工人的部分。路径成本、预约资格、权限与优先级仍须按工人校验，不能共享最终分配结果。对 FetchTarget 建立标签候选索引，保持原比较规则和失败后候选回退。

## 2. 现有忙碌小人节流容易因无关事件失效

- `Core/InvalidationVersions.cs` 的 FetchVersion、ChoreVersion 是全局计数。
- `Patches/InvalidationGenerationPatches.cs` 在物品增删、储存/标签更新、预约/取消预约、每秒 FetchManager 更新以及任务增删时推进计数。
- `Patches/BusyDuplicantChoreThrottlePatch.cs:120` 把上述全局计数和小人所在格子放进刷新戳；`Core/RefreshStamp.cs:56` 做完整比较。

结果：无关位置的物品预约、其他世界的任务变化，也会让一个小人的刷新戳改变；小人走到下一格同样会改变。不能假定“配置跳过四次”就真的省掉五分之四工作。实际命中率尚未测量。

优先改法：增加按原因统计的命中/失效计数，然后把物品版本缩小到世界和类型、任务版本缩小到世界/相关任务组。小人自己的路径与能力变化继续使其缓存失效；紧急需求与明确优先任务保持立即刷新。不得为了命中率屏蔽真实变化。

## 3. 寻路：主线程维护队列，工作线程求解

- `decompiled/AsyncPathProber.cs:261`：TickFrame 持有 Manager 锁，消费完成结果、遍历全部注册导航器、排序、回收旧队列，再建立最多 4 个请求。即使只发 4 项，也要处理全体导航器的排序。
- `Patches/AsyncPathProbeOptimizationPatch.cs:281`：现有去重仅支持精确类型 `CreaturePathFinderAbilities`，复制人不在覆盖范围内。
- `decompiled/NavGrid.cs:494`：脏格扩展成邻域，再重建导航图。它已有位图去重，不是每次无条件遍历全图。

优先改法：记录导航器数、重排次数、重复请求率与锁等待；用公平的就绪队列替代反复全排序。复制人缓存必须包含门权限、装备、导航能力、占用与相关网格变化，不能直接套动物的去重键。脏导航扩展是否热点，应由现有 NavGridWorkloadProbe 的触达和工作量数据判断。

## 4. OniSim 的 GPU 前后有大量 CPU 工作

- `native/src/simulation_step.rs:27`：每步验证 read/write 元素和疾病索引等数组；其中注释明确指出现有 activity flags 未覆盖所有写入，因此不能未经补全写入追踪就删除验证。
- `native/src/simulation_step.rs:210`：全世界 read/write 快照同步，随后刷新活动气体映射。区域内还有按阶段的多数组复制。
- `native/src/multicomponent_staging.rs:178`：扫描组分集合，清零并填充稠密数组；当前 sparse=false，不能把未走到的稀疏上传优化算作已生效。
- `native/src/multicomponent_flux.rs:633`：完成上述 CPU 工作后才检查休眠。返回结果时逐格写回、排序组分、更新额外组分表与显示投影。
- `native/src/multicomponent_publication.rs:136`：每个非复用批次重新遍历全图，重建组分发布头和记录。
- `native/src/state.rs:355`、`grid.rs:125`：主线程发布时再次复制 16 个全图数组；当前不按脏块跳过。

优先改法：缓存组分清单、在打包前判定脏块、按脏块更新发布内容；跨双缓冲维护各自版本，保证稳定指针和读者生命周期。验证改为变更点验证/脏区验证之前，必须先覆盖全部写入入口。不要直接减少合法世界的检查来获取表面提速。

## 5. 显示和多组分桥接也在 CPU 上

- `Core/OniSimMulticomponentProvider.cs:96`：单格查询跨 FFI 获取读租约，逐字段 Marshal 解码，再释放。
- `native/src/lib.rs:375`：读租约持有全局 State Mutex。多个并行读者在该入口串行，主线程发布也需要同一锁。
- `Core/OniSimMulticomponentProvider.cs:187`：批量读取已将原生数据复制到托管缓冲后释放租约，避免渲染期间一直持锁；不要误报为“尚未批量读取”。
- Jlib 的颜色更新仍扫描需绘制区域并消费组分快照，批量化没有自动消除未变化帧的重复工作。

优先改法：按发布版本/脏块缓存可显示结果，逐步把多读者访问与可变仿真状态锁分离。必须保留旧快照直到读者释放；不能缓存即将复用的原生指针，也不能在无版本协议时直接绕过 Mutex。

## 6. CPU 等 GPU 与 GPU 等 CPU：当前数据不能混为一谈

现有路径是 CPU 扫描/打包 -> 上传/编码 -> GPU 压力 -> CPU 等速度结果 -> 再编码 -> GPU 输运 -> CPU 等最终结果 -> CPU 解码/发布。CPU 准备期间，同一管线的下一项 GPU 工作尚未提交；但 GPU 可能正执行游戏渲染或其他任务，源码不能证明硬件空闲比例。

`gpu/src/multicomponent.rs:1053` 的 poll Wait 墙钟耗时包含排队、设备执行、驱动及主机被调度的延迟，不是纯着色器时间。`native/src/worker.rs:252` 是游戏等物理工作线程的另一处屏障。增加工作线程或仅减少着色器计算，都不一定缩短关键路径。

## 下一次验证与实施顺序

1. 保持算法不变，先补齐计时分类：JobManager 按物品 offset、拾取候选、任务筛选、纹理和覆盖层分组；记录工人数量、遍历物品数、候选数、缓存失效原因。现有 PerfProbe 把 UpdatePickups 标为 main，但实际它可在作业线程执行，其累积墙钟时间不能直接加进主线程预算。
2. 同时记录主线程/物理线程 CPU 时间、任务等待、全局读锁等待、GPU 提交次数、传输字节与设备时间戳。测量采样本身开销；不要逐格打日志。
3. 同存档同镜头分别短测暂停和 1 倍速。通过 OniMcp 暂停/读取/运行/再暂停，记录模拟进度；不在测试时并行编译或跑 GPU 宿主测试。
4. 第一组优化针对 CPU：搬运静态索引、局部失效与发布/显示脏块。用命中率、访问格数和帧时间共同验证；先做具有明确调用量证据的一项。
5. 若仍有大量等待，再做 GPU 同步合并；若 CPU 求解本身仍重，再考虑较大的物理算法改造。没有 GPU 时间戳证据，不以“GPU 算力不足”作为重构理由。

尚未完成：当前版本的实机完整 CPU 调用栈、GPU 时间戳和上述对照采样。本次是源码审计与旧证据重分析，不是新的 FPS 优化验收。
