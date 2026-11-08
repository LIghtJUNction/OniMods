# CycleTrim 整帧采样与液体验收记录

日期：2026-11-08—2026-11-09（Asia/Taipei）

## 当前验收状态

**500 FPS 和液体物理目标均未完成验收。**用户已回传 e24b534b 在隔离 NVIDIA 图形环境中的新结果：暂停时 Game.Update 节奏约 15.6–17.6 次/秒，运行时约 8.5–9.3 次/秒。它们是更新回调频率，不能等同于用户桌面的实际呈现 FPS。500 FPS 对应平均完整帧间隔不超过 2 ms。

用户最新回传再次确认：32 格宽深岩浆池在 1000 次更新后，两侧柱质量约为 4844.86 kg 与 6003.26 kg，差约 1158.40 kg，仍按失败处理。此前实机副本池面超过两格的高度差也仍按失败处理。云端尚未取得该原始 fixture、未提交液体差异或原始游戏日志。

本轮新增的 16 项合成液压回归已经通过，并完成包含 Linux 原生库的 onim 候选构建；这些证据和用户原场景分开记账。完整原生门仍有 11 项硬件 GPU 测试失败。新增原生候选没有部署、启动 Steam 或取得游戏进程加载记录。

前一轮完成了仿真时间截断和整帧计时口径的源码修复，全部 10 个优化宿主回归族通过，并通过 onim 的托管参考构建。用户现已回报该代码在原主机构建、部署以及加载 DLL/原生库哈希核对成功；该回传补充了实机证据，但没有满足液体收敛与 500 FPS 门槛。

## e24b534b 实机回传

来源为用户在本次对话提供的测试摘要。用户指定原始日志为 `/tmp/oni-perf-e24b534b-20261108/player.log`；该路径未挂载到当前云端，以下不声称经过原始日志逐行复核。测试报告的 HEAD 为 e24b534b，完整未提交差异和哈希字符串尚未提供，不能据此重建原主机的完整源码树或二进制。

| 项目 | 用户回传 | 解释与限制 |
| --- | --- | --- |
| 图形条件 | 隔离 NVIDIA、1920×1080、远缩放 | 虚拟显示的呈现路径不同于用户桌面 |
| 暂停 | Game.Update 约 15.6–17.6 次/秒 | 频率倒数约 56.82–64.10 ms；不是逐帧分位数 |
| 三倍速运行 | 稳定窗口约 8.5–9.3 次/秒 | 频率倒数约 107.53–117.65 ms；不是实际呈现 FPS |
| 仿真推进 | 10 墙钟秒推进约 15.8 游戏秒 | 比率约 1.58 游戏秒/墙钟秒；尚未取得实际 Time.timeScale，不能仅用速度档位推断名义吞吐 |
| 宿主回归 | 帧计时、性能探针、取货逻辑通过 | 用户在原主机的回传结果 |
| 构建和加载 | CycleTrim 构建部署成功；进程中的 DLL、原生库与构建产物哈希一致 | 具体哈希字符串、启动日志尚未回传 |
| 原宽深岩浆池 | 1000 次更新后两侧 4844.86 / 6003.26 kg | 失败；没有放宽阈值或忽略该用例 |

暂停状态仍有较长回调间隔，说明解释整帧瓶颈时必须包含暂停期间执行的工作和等待，不能只研究推进中的液体求解。现有摘要没有 Game.Update/LateUpdate/Unattributed 分项、线程采样或 GPU 时间，尚不能判断主要时间消耗在 CPU 工作、图形呈现还是同步等待。运行与暂停采样也不能简单相减后当作液体耗时。

更早约 25–33 FPS、旧 Total 约 27–36 ms 的记录保留为历史线索；计时边界与显示路径没有对齐，因此不据此宣称 e24b534b 提升或降低了实际呈现性能。

### 下一次线程采样

现有只读脚本 [profile_cycletrim_process.py](../scripts/profile_cycletrim_process.py) 已补充每线程等待位置、期末状态以及主动／被动上下文切换次数差。汇总保留状态与等待位置的离散观察次数，并以 `contextSwitchIntervals` 和 `waitChannelUnavailableSamples` 记录覆盖情况。权限不足时保留 CPU 采样；不会把缺失读数补成零次切换。Linux 的 `wchan` 表示读取时内核中的阻塞位置；该读数与状态不是原子快照，不是阻塞持续时间或 GPU 时间，解释依据见 [Linux /proc 文档](https://docs.kernel.org/filesystems/proc.html)。

设置 `oni_game_pid` 为已通过 Steam 启动、已核验加载库的游戏 PID 后，可分别在暂停观察和有独立超时／自动暂停的短步进期间采样。脚本本身不启动或控制游戏，现有输出文件会被拒绝覆盖：

```bash
python3 scripts/profile_cycletrim_process.py --pid "$oni_game_pid" \
  --seconds 10 --interval 0.25 --output /tmp/oni-paused-thread-sample.json
```

运行状态另选新的输出文件，保留相同存档、分辨率、镜头和模组组合。线程采样用于选择后续应分析的 CPU 调用栈或等待路径；正式验收另取无此采样器的完整帧窗口，避免混入观察者成本。

`python3 scripts/verify_cycletrim_process_profile.py` 的 8 项 Linux 回归通过，包括真实忙循环／休眠进程、状态与计数汇总、可选文件权限不足、PID/TID 身份变化、进程提前退出和证据文件保护。这建立采样工具的行为证据，没有产生新的游戏帧率结果。

## 后续源码复核：暂停路径与隐藏动画

已固定并核对 `Kupie/ONI_Decomp@8d4ace10c2b6a84cee394653a91b990a5122e636`（声明 build 744825）的参考源码。它说明调用语义；不能证明用户当前游戏在各调用中花费多少时间。

**暂停仍执行原生交接。** [Game.cs](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/Game.cs) 中 `Game.Update` 无条件调用 `SimEveryTick`；不足一个子步时仍执行 `UnsafeSim200ms(0f)`，重建 active-region 列表、发送 `NewGameFrame` 并调用 `StepTheSim`。`LateUpdate` 也没有暂停时的总跳过门，仍会触及导管、可视化、状态图标及音频。因此应先查看既有诊断的 `SimEveryTick(inclusive)` 与 `StepTheSim(inclusive)`，再结合线程 CPU／等待采样定位。不能把暂停区间全部归为渲染。

原版 `SimEveryTick` 自身存在 0.2 秒的输入上限；它与前一轮删除的 CycleTrim 额外 2/60 秒限幅是两处不同代码。此次没有修改原版限幅。仅凭当前摘要，不能确认它对 1.58 游戏秒／墙钟秒的贡献。

**隐藏动画恢复原版更新路径。** [KBatchedAnimController.cs](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/KBatchedAnimController.cs) 在可见性门之前处理 transform／dirty；隐藏的 `OffscreenUpdate` 可以推进时间，`forceRebuild` 可以越过隐藏门。原补丁直接返回 false 会吞掉这些行为。修复复用已有 Hook 和累计时间消费函数：隐藏时交还原版，并只消费一次此前可见期间累计的时间；零 dt 仍交给原版，不消费累计时间。

加强合成 receiver 后、修改生产前缀前，离屏更新回归退出 1：应收到 0.03 秒，实际为 0。修复后同一断言及全部 9 个动画场景通过。receiver 仅表示调度与隐藏门，不模拟完整 Unity 动画系统。旧的“移动但隐藏”测试现在同时检查原版调用次数和原版隐藏门，不再错误假设每次到达原版都会推进动画。

最终三文件候选已通过完整 Release 宿主入口的 10 个回归族，以及 `onim build -m CycleTrim --release` 的托管参考构建（0 errors，28 个既有 Jlib 警告，`IncludeOniSim=false`）。本次候选实现 DLL 的 SHA-256 为 `889a97e6ebbe549923fa3e4a7f9e6db05e773478bf89f196cb7df325d182f0c8`，与包内实现 DLL 一致；三份源码在构建与回归前后保持相同指纹。这次修复没有实机视觉或 FPS 收益证据，也没有新增原生库部署。实现与回归见 [ZoomRenderAccelerationPatch.cs](../mods/CycleTrim/Patches/ZoomRenderAccelerationPatch.cs)、[ZoomAnimationRegression.cs](../tests/CycleTrim.Optimizations.Tests/ZoomAnimationRegression.cs)。

### 进一步修正：撤除整个动画方法的降频

以上隐藏路径修复提交为 [fc3ec836](https://github.com/LIghtJUNction/OniMods/commit/fc3ec83658c524c9f7064e565585084efc87b3ee)。继续沿调用链复核后，发现剩余的可见静止分支仍不能安全跳过整个 `UpdateAnim`：

- [Global.cs](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/Global.cs) 分别在 Update 和 LateUpdate 中调用动画事件管理器与动画更新器。
- [AnimEventManager.cs](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/AnimEventManager.cs) 可以独立完成当前动画并触发下一段；[KAnimControllerBase.cs](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/KAnimControllerBase.cs) 随后重置新动画时间。按控制器累计的旧 dt 没有随动画更换而清除，下一次调度会补到新段。
- 原版还负责当次 transform 更新、强制重建、同步及 stopped／paused 控制器的退订。跳过整个方法会连这些操作一起延后；原版已通过 `SuspendUpdates` 和更新列表退订减少停止动画的调度。

宿主反例在旧前缀下得到新动画时间 0.560 秒，而按原调用顺序应为 0.550 秒；另有可见重建、transform 和退订延迟的独立反例。当前修复删除该动画 Hook、stride 门及累计时间弱表，让每次调用保留自己的 dt。两个 overlay 调度类保持原行为。回归通过生产嵌套 patch 类的发现／应用后实际调用 receiver，覆盖 13 个场景，不仅检查某个 Hook 是否被删除。最终 fixture 删除一项不属于原版的强制重建模拟后，重新运行完整 Release 宿主入口：全部 10 个回归族及这 13 个动画场景通过，三份源码前后哈希一致。随后 onim 托管参考构建退出 0，0 警告／0 错误，实现 DLL 与包内副本的 SHA-256 均为 `c8bbeae9ccf2fdb0acdec5678f210da9845e093aa8d791418d71def1bc72d807`；该包不含原生库。改前反例、最终宿主日志、构建日志及源码指纹已保存在[动画验证收据](cycletrim-validation-20261109/animation-receipt.json)。

这一发现还限定了性能计时的解释：Global 的动画与事件回调不属于 Game.Update／Game.LateUpdate，仍计入 `Unattributed`。因此该桶明确包含主线程工作，不能直接解释成 GPU 时间。普通动画更新列表只在 `Time.deltaTime > 0` 时运行；Always 列表使用 unscaled time，不能把暂停状态下所有动画都当作逐帧零 dt 调用。

## 源码与测试范围

本轮首先读取了 AGENTS.md、RTK.md、UPSTREAM.toml 和 docs/autonomous-iteration.md，并检查当前差异。可访问的云端检出不含交接中的未提交液体改动、原始 hydraulic_tests、测试存档、perf.data 或游戏运行日志。原 Steam／RTX 4060 主机没有接入此执行环境。

已审计的初始源码为 [3c745da8](https://github.com/LIghtJUNction/OniMods/commit/3c745da8067724f1cde46dc151fe4136f95cefa6)，它是交接所述 dd97815e 的后代。后续 main 的 [f07d9bc8](https://github.com/LIghtJUNction/OniMods/commit/f07d9bc887b6b698d7d7623aae0cdfabba6fad9f) 由另一轮工作提交了原生 ID 的 Release 校验修复；本轮不把该修复计作自己的帧计时改动。

在 3c745da8 上实际执行了以下筛选命令：

```bash
cargo test --release --locked --offline \
  --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml \
  hydraulic_tests -- --nocapture
```

结果为 `0 passed; 0 failed; 100 filtered out`。这表示没有选中交接用例，不能记为液体回归通过。不得用早先的完整测试数量替代当前缺失的失败场景。

## 前一轮：e24b534b 帧计时修正

**帧率补丁保留原始仿真时间。** 删除 Game.simDt 的反射写入和 ref dt 截断；SimEveryTick 补丁只承担诊断，关闭诊断时不安装。宿主回归同时检查诊断开、关两种模式下传入的 0.25 秒与 0.25 秒积压保持不变。取消截断后，低帧率时需要执行的仿真工作可能增加，实际帧率与仿真进度须重新成对测量。

**完整区间按相邻 Game.Update 入口的单调时钟计量。** 首次入口建立基线，下次入口结算前一完整区间，纳入此前漏掉的 Update→LateUpdate 空档。统计达到 1 秒或 2048 个样本时完整结窗，FPS、均值、p50/p95/p99 和最大值使用同一组样本，下一窗口沿用上一次入口边界。

**各项耗时保留其测量边界。** 日志明确标为 Game.Update cadence；Unattributed 包含其他回调、引擎工作、等待、呈现相关工作以及探针自身开销。Update/LateUpdate 的覆盖时间按嵌套并集扣除；SimEveryTick、StepTheSim 仅作为 inclusive 子指标。GCCollectionsDelta 记录各代回收次数差，没有将次数解释成 GC 耗时。

**捕获状态随调用和生命周期隔离。** 使用原有 Hook 目标，每次调用保存独立 state，finalizer 关闭异常和重入作用域并保留原异常。新 Game、游戏与菜单 OnSpawn、帧号不连续或非单调时间戳重新建立基线；旧窗口和旧 generation 的计时不会写进新窗口。诊断默认关闭，不分配捕获数组。

实现：[FrameRateUnlockPatch](../mods/CycleTrim/Patches/FrameRateUnlockPatch.cs)、[FrameTimingWindow](../mods/CycleTrim/Core/FrameTimingWindow.cs)。回归：[FrameRateDiagnosticsRegression](../tests/CycleTrim.Optimizations.Tests/FrameRateDiagnosticsRegression.cs)。

## e24b534b 验证记录

| 验证 | 实际结果 | 证据范围 |
| --- | --- | --- |
| 原实现时间输入反例 | 加强断言后退出 1 | 3c745da8 的原生产补丁改变了 0.25 秒输入／积压 |
| 原实现计时空档反例 | 独立计量空档约 20.091 ms，旧 Total 约 0.13 ms | 宿主夹具中的计时边界缺口，未测游戏 FPS |
| 候选定向 frame-rate 回归 | 退出 0 | 确定性时间戳、真实 Harmony 宿主、时间输入保持 |
| 全部优化回归 | Release，10 个回归族通过 | 同一测试项目中的其他生产补丁及共享夹具回归 |
| onim 托管参考构建 | 成功，0 errors；28 个 Jlib 既有警告 | net48、固定参考程序集、IncludeOniSim=false |
| 独立源码复审 | 未发现阻断问题 | 五个代码／测试候选文件的全文及完整差异 |
| GitHub Mod quality | completed / success | 精确代码提交 e24b534b；[运行 42488038103](https://github.com/LIghtJUNction/OniMods/actions/runs/42488038103) |
| GitHub ONI reference compatibility | completed / success | 精确代码提交 e24b534b；[运行 42488038076](https://github.com/LIghtJUNction/OniMods/actions/runs/42488038076) |
| Steam 图形与液体副本（云端直接执行） | 未执行 | 用户原主机的新回传见上节；云端未读原始日志 |

上述两条 GitHub 工作流均对应代码提交 [e24b534b5420fe17670813327649203bf08e468f](https://github.com/LIghtJUNction/OniMods/commit/e24b534b5420fe17670813327649203bf08e468f)，分别于 2026-11-08 22:10:21、22:12:50（Asia/Taipei）完成。它们补充了该提交的宿主回归与参考兼容性证据，没有执行原始宽池 fixture 或 Steam 游戏验收，也不构成全部原生测试通过的声明。

宿主测试使用 .NET SDK 10.0.401。定向与完整命令如下：

```bash
dotnet run --project tests/CycleTrim.Optimizations.Tests/CycleTrim.Optimizations.Tests.csproj \
  -- --regression-frame-rate
dotnet run --project tests/CycleTrim.Optimizations.Tests/CycleTrim.Optimizations.Tests.csproj \
  -c Release --no-restore
```

构建使用 onim 0.1.13。当前执行环境的并行依赖恢复返回失败，使用下列串行调度命令完成构建；未修改生产源代码、参考版本或警告校验规则来通过构建：

```bash
BuildInParallel=false RestoreBuildInParallel=false \
  ./target/release/onim build -m CycleTrim --release
```

本地构建配置为 `IncludeOniSim=false`、`TreatWarningsAsErrors=true`，引用仓库清单验证的 22 个 DLL。清单声明 ONI 737790，辅助 Unity 模块为 2021.3.33。这是托管参考构建，不是当前游戏版本的运行验收；本次包未包含原生库。

该轮参考编译得到的 `CycleTrim.dll` 为 191488 字节，SHA-256 为 `f4d060b7b21845a677ab42f95b782458b2344d6b7247bfc26f2e74f33b725abf`，与包内 `implementation/CycleTrim.dll` 相同；本次编译采用的帧计时代码与 e24b534b 一致。该云端参考构建未部署到 Steam 游戏目录；用户新回传的原主机构建属于另一次构建，没有提供其哈希字符串，不能默认它等于上述指纹。

## 已确认的帧计时问题

### 仿真时间被帧率补丁截断

[基线 FrameRateUnlockPatch](https://github.com/LIghtJUNction/OniMods/blob/3c745da8067724f1cde46dc151fe4136f95cefa6/mods/CycleTrim/Patches/FrameRateUnlockPatch.cs) 的 SimEveryTick Prefix 同时将 Game.simDt 与传入 dt 截断到 2/60 秒。该行为在性能诊断关闭时也执行。

把现有宿主回归的要求改为“传入的 0.25 秒及积压的 0.25 秒保持不变”后，旧生产实现实际失败。该反例确认了补丁改变时间输入；它没有测量游戏内速度或收益。后续性能比较必须记录游戏仿真进度与墙钟时间，不能靠丢弃积压提高帧率。

### Total 漏计 Update 与 LateUpdate 之间的时间

基线 Total 由 Game.Update、Game.LateUpdate，以及 LateUpdate 结束到下一次 Update 开始的间隔相加。Update 结束到 LateUpdate 开始之间的其他脚本和引擎工作没有进入总和。Engine/Render 只是该剩余区间的旧标签，未提供渲染线程或 GPU 的独立计时。

在生产补丁链接的宿主夹具中，向 Update 与 LateUpdate 之间插入等待并独立计时：该间隔平均约 20.091 ms，而旧日志 Total 约为 0.13 ms。夹具提供的 unscaledDeltaTime 为 0.25 秒，旧日志因而显示 4.0 FPS。以上是计时边界反例，数值不表示 ONI 实际帧率。

## 新增纯液体压力修复与回归

当前云端没有用户原来的 32 格岩浆 fixture。为避免覆盖原主机的未提交文件，新用例放在 [hydraulic_regression_tests.rs](../mods/CycleTrim/OniSim/native/src/hydraulic_regression_tests.rs)，函数仍含 `hydraulic_tests`，原筛选命令可以同时选中它们。新场景使用明确的合成参数：单格容量 1840 kg、每面流量上限 100 kg、最小水平／竖向流量 0.1 kg、比热 1、初温 1800 K；它们不是从当前游戏捕获的 Magma 记录。

### 改动和物理含义

[fluid_pressure.rs](../mods/CycleTrim/OniSim/native/src/fluid_pressure.rs) 对同一种纯液体的相邻湿格建立连通面，以包含高度的同一水头计算水平和竖向交换。迭代阶段只求压力，最终才成对扣加质量，避免每轮迭代重复花费流量预算；已经处理的内部面不再走旧显式规则。气体、异种液体、混合格、实体和属性屏障不会被接入这张同相压力图。

新模型的满格压缩关系是 `下格质量 = 上格质量 + 0.01 × 单格容量`，旧竖向近似为 `下格质量 = 1.01 × 上格质量`。这是实际的状态方程改变，不能称为原版逐位等价。两者在深柱、高压和仍走旧规则的界面上存在差异，真实材质及实机校准仍是验收项。

实现同时修复了四类已复现问题：左右侧向请求使用同一个源状态；纯液体内部不再因绝对最小流量留下永久压差；压力位移从真实 `write.mass` 取库存，并携带当前温度和病菌；排空时将整数病菌余数带到实际出流面。显示用的 `flow` 在整批子步中累计，不能再次从更新后的库存中扣除。混合格保留原有最小流量门，没有借此隐式改变其尚未验证的输送规则。

### 实际结果

在未改动的已提交原生实现上，新五场景初始结果为 **2 通过、3 失败**：32 格池柱质量极差 449.660858 kg，U 形连通器左右均柱质量约 11742.64／4590.17 kg，单步左右输送为 25／18.75 kg。这与用户原场景的 1158.40 kg 失败是两份独立证据。

最终候选的筛选回归为 **16 通过、0 失败、0 忽略**。宽池验收阈值始终为柱质量极差小于 92 kg，逐列镜像阈值始终为 0.05 kg，没有因失败调整。

| 场景 | 最终实测结果 | 范围 |
| --- | --- | --- |
| 32 格宽、多层池，1000 次更新 | 正向极差 0.000732 kg，镜像极差 0.000244 kg | 调用生产 CPU liquid_phase |
| 同一池拆成两个相邻区域 | 正向极差 0.005249 kg，镜像极差 0.004639 kg | 按区域刷新 read/write，保留批次累计 flow |
| U 形连通器 | 左右均柱质量约 8140.156921／8140.157064 kg | 墙体未穿透，差约 0.000142 kg |
| 封闭真空 | 三格均达到至少 99% 单格容量 | 保留质量／热量守恒断言 |
| 其他回归 | 通过 | 左右对称、异温热量、病菌、气体／不可混液库存、属性与混合格屏障、不同世界的 scratch 复用、近干通道及排空 |

病菌修复有独立负对照：净零质量／温度通流曾把总病菌从 700000 增至 760696；双出口排空曾把 1 个病菌减为 0。最终回归覆盖 1 和 7 个病菌的单／双出口排空。气体和不可混液集成用例使用不同的比热，检查各元素库存和总显热；同一布局在每步清 flow 与整批累计 flow 时得到相同质量和温度。

完整原生测试实际返回 **exit 101：103 通过、11 失败、4 个既有忽略**。其中 10 项 GPU 仿真测试明确返回 `no hardware Vulkan adapter available`；GPU pathfinder 初始化测试返回 -1 而要求 0。当前云端没有可用硬件 Vulkan 适配器，完整原生门未通过；没有删断言、增加忽略或把软件环境测试当作 NVIDIA 运行验收。

```bash
cargo test --locked --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml \
  hydraulic_tests -- --nocapture --test-threads=1
cargo test --locked --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml
```

### 压力阶段成本与仍未满足的数值条件

成本测试在当前云端 Intel Xeon Platinum 8573C 上执行，最多 4096 个湿格、扫描矩形 9100 格。每种几何采用 reference→candidate→candidate→reference 顺序，每轮新建同一状态，1 次预热、16 个计时样本，单线程单独筛选。reference 是本轮首版隐式求解的迭代坐标算法，**不是原 main 的液体代码**；两者共享建图、残差门、物质提交和实际状态变化通知。候选预计算每节点常量，并在未饱和分段直接求解坐标。未保留实际变慢的 SOR 实验。

| 几何 | reference 两轮中位数 | candidate 两轮中位数 | candidate 两轮 p95 |
| --- | --- | --- | --- |
| 初始静水平衡 | 9.540／8.042 ms | 2.553／2.943 ms | 4.639／6.373 ms |
| 有初始水头差 | 12.489／12.523 ms | 2.868／2.868 ms | 4.682／3.262 ms |

以上是带测试统计的压力阶段 CPU 成本，不是游戏整帧、完整液体阶段或 FPS。固定物理子步的调用频率也不同于渲染帧率，不能用这些毫秒的倒数推算实际游戏 FPS。

两种算法的迭代与残差基本一致：静水平衡样本需要 24–48 轮；有水头差的候选样本 **16/16 都达到 64 轮上限**，最终提交状态相对求解水头的最大质量残差约 **2.277360 kg**。因此不能宣称每一步普遍达到数值收敛。近干格还有显式库存限制：本步不能花费尚未收到的物料；三格反例中这一限制导致约 **12.420003 kg** 的提交残差。该通道在 1000 步后通过质量、热量、病菌及液面检查，但这不消除瞬态分裂近似。

```bash
cargo test --release --locked --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml \
  hydraulic_tests_new_pressure_cost_samples_preserve_inventory \
  -- --nocapture --test-threads=1
```

负对照、最终 16 项回归、完整失败门、ABBA 原始采样及六份源码指纹均保存在[液压验证收据](cycletrim-validation-20261109/hydraulic-receipt.json)及同目录日志。保留失败日志作为证据，没有用单独筛选的通过数量替代完整门。

### 包含原生库的 onim 构建

最终冻结源码通过 `onim build -m CycleTrim --release` 构建，`IncludeOniSim=true`、`OniSimNativeBundle` 留空，仅生成当前 Linux x64 原生库。退出码 0，墙时约 60.38 秒，0 errors；日志分别有 28 条 Jlib C# 告警和 36 条 Rust 未使用项告警。托管层使用固定的编译参考镜像，不代表已验证当前游戏二进制兼容性。

| 对象 | SHA-256 |
| --- | --- |
| 原生候选源码 digest | `21b4c57ba8427eadf9036564f265d722c25a184a5f24d4eb2098a5312ce4ee77` |
| Linux `liboni_sim_core.so` | `94b41200af71d2e34b1838f75a76ea251d13bf08384110acf7447fadb5cba795` |
| `implementation/CycleTrim.dll` | `67643663ec64005020ef3f66a3ee2981e358e93bca01a9ec3953105923168476` |
| 完整 Linux ZIP | `34cca03bb279c47b9bc7631b0c42c8e845004c1f385be4edd3876b2cadb4df3e` |

构建前后及最终核验时，95 个原生 Rust 源文件、manifest、GPU 输入和动画源码均保持相同哈希。Cargo 实际 `.so`、bundle、bin、dist 与 ZIP 条目逐字节一致；managed 实现也与 ZIP 条目一致。包中没有分发游戏编译参考 DLL。

构建时原生改动尚未提交，因此 `build.json.commit` 的 `84207e7b` 是基准 HEAD；绑定候选实际原生代码的是上述 source digest。`gameRuntimeTested=false`。详细检查保存在[完整 Linux 构建收据](cycletrim-validation-20261109/full-linux-receipt.json)，不能将包内一致性核验表述为游戏进程已经加载此库。

## 液体源码基线审计与剩余范围

以下发现保留已提交基线的源码依据。前两项已由上述纯液体候选和新回归推进；GPU 液体路径、互溶输送与原主机差异仍需分别验证。交接中的原始未提交改动不可见，因此不能据此宣布原 1158 kg 残差的根因或验收结果已确认。

| 发现 | 源码依据 | 后续验证 |
| --- | --- | --- |
| CPU 水平输送先左后右，并在第一次传输后减少 remaining | [fluid.rs](https://github.com/LIghtJUNction/OniMods/blob/3c745da8067724f1cde46dc151fe4136f95cefa6/mods/CycleTrim/OniSim/native/src/fluid.rs) | 原宽池及其镜像；逐面、逐步记录质量与能量 |
| 水平流量为质量差的 0.25 倍，并受 min_horizontal_flow 截断 | 同上 | 用真实岩浆参数检查非零压差是否进入停流区间；同时保留连通器断言 |
| 生产调度将 gpu_liquid_flow 设为 false，压力 GPU 调用选择 phase 1 | [simulation_step.rs](https://github.com/LIghtJUNction/OniMods/blob/3c745da8067724f1cde46dc151fe4136f95cefa6/mods/CycleTrim/OniSim/native/src/simulation_step.rs) | 记录实际执行的液体路径；GPU 启用日志本身不说明液体压力路径已执行 |
| GPU flux 接收 head 与 upper_y，却没有在通量中使用；向上流要求接收格已经是液体 | [gas_pressure_faces.wgsl](https://github.com/LIghtJUNction/OniMods/blob/3c745da8067724f1cde46dc151fe4136f95cefa6/mods/CycleTrim/OniSim/gpu/src/gas_pressure_faces.wgsl) | 若启用此候选路径，验证受压真空、连通性和不同相边界 |
| transfer_liquid 的互溶分支增加组分质量后直接返回，没有同步加入源液体携带的热量与病菌 | [fluid.rs](https://github.com/LIghtJUNction/OniMods/blob/3c745da8067724f1cde46dc151fe4136f95cefa6/mods/CycleTrim/OniSim/native/src/fluid.rs)、[world.rs](https://github.com/LIghtJUNction/OniMods/blob/3c745da8067724f1cde46dc151fe4136f95cefa6/mods/CycleTrim/OniSim/native/src/world.rs) | 用异温互溶液体检查组分质量、热量及病菌转移 |

最小水平流量若为 q，局部规则可让小于 4q 的非零相邻质量差停流；宽池上的多个小差值可能累积成明显坡度。这是从代码公式得到的候选机制，仍需在原失败布局、参数和实际路径上验证。

## 仍需完成的运行验收

1. 取得原主机现有的完整未提交差异、原始宽池 fixture 和 player.log，保留既存 perf.data；合并时保留原 fixture，不用新增合成用例替代它。
2. 将本轮 16 项合成回归与真实材质、原失败布局并列复测，补齐硬件 GPU 门；继续检查深柱、混合物和达到迭代上限的瞬态条件。
3. 在原主机用 onim 构建与部署，通过 Steam 启动测试存档副本，核验实际加载的 DLL、原生库与日志。
4. 按暂停观察、短步进、再暂停验证液体状态；原实机验收失败保留到出现新的通过证据。
5. 在相同存档、分辨率、镜头、缩放、暂停／运行状态和模组组合下记录完整帧间隔的均值、p50/p95/p99、仿真进度、错误与视觉正确性；进一步拆分主线程、渲染线程、GPU、同步等待和 GC。
6. 隔离 Xvfb／RTX 对比必须标注其呈现路径；最终 500 FPS 验收仍需用户实际桌面条件。无头更新率与局部微基准不用于替代该目标。

## 证据复核

| 检查项 | 当前结论 |
| --- | --- |
| 问题是否可定位 | 时间截断、计时漏项、动画跨段补时和新液压缺陷均有源码或执行反例 |
| 表述是否清楚 | 用户交接数据、本轮宿主结果、源码推断分别标注 |
| 实验是否支持性能提升 | 尚无本轮实机 FPS 数据，不报告性能提升 |
| 验收是否完整 | 16 项新合成液压及托管回归通过；完整原生门、原 fixture、实机副本和图形性能分解均未闭环 |
| 修复是否满足语义约束 | 宿主回归已确认原始仿真时间输入保持；后续动画修复恢复逐次原版调用，实机行为仍待验证 |
