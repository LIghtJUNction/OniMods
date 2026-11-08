# CycleTrim 整帧采样与液体验收记录

日期：2026-11-08（Asia/Taipei）

## 当前验收状态

**500 FPS 和液体物理目标均未完成验收。**本轮尚未获得新的 Steam 图形运行数据。用户交接中的图形记录约为 25–33 FPS、记录中的总帧时约 27–36 ms；这些是既有证据，不能写成本轮测量。500 FPS 对应平均完整帧间隔不超过 2 ms。

用户交接中的 32 格宽深岩浆池在 1000 次更新后，两侧柱质量约为 4844.86 kg 与 6003.26 kg，差约 1158 kg，仍按失败处理。此前实机副本池面超过两格的高度差也仍按失败处理。本轮没有运行该原始 fixture 或游戏副本。

本轮完成了仿真时间截断和整帧计时口径的源码修复，全部 10 个优化宿主回归族通过，并通过 onim 的托管参考构建。实机液体与图形性能仍缺原游戏主机和未提交工作区，不能用本轮宿主结果替代。

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

## 本轮完成的修正

**帧率补丁保留原始仿真时间。** 删除 Game.simDt 的反射写入和 ref dt 截断；SimEveryTick 补丁只承担诊断，关闭诊断时不安装。宿主回归同时检查诊断开、关两种模式下传入的 0.25 秒与 0.25 秒积压保持不变。取消截断后，低帧率时需要执行的仿真工作可能增加，实际帧率与仿真进度须重新成对测量。

**完整区间按相邻 Game.Update 入口的单调时钟计量。** 首次入口建立基线，下次入口结算前一完整区间，纳入此前漏掉的 Update→LateUpdate 空档。统计达到 1 秒或 2048 个样本时完整结窗，FPS、均值、p50/p95/p99 和最大值使用同一组样本，下一窗口沿用上一次入口边界。

**各项耗时保留其测量边界。** 日志明确标为 Game.Update cadence；Unattributed 包含其他回调、引擎工作、等待、呈现相关工作以及探针自身开销。Update/LateUpdate 的覆盖时间按嵌套并集扣除；SimEveryTick、StepTheSim 仅作为 inclusive 子指标。GCCollectionsDelta 记录各代回收次数差，没有将次数解释成 GC 耗时。

**捕获状态随调用和生命周期隔离。** 使用原有 Hook 目标，每次调用保存独立 state，finalizer 关闭异常和重入作用域并保留原异常。新 Game、游戏与菜单 OnSpawn、帧号不连续或非单调时间戳重新建立基线；旧窗口和旧 generation 的计时不会写进新窗口。诊断默认关闭，不分配捕获数组。

实现：[FrameRateUnlockPatch](../mods/CycleTrim/Patches/FrameRateUnlockPatch.cs)、[FrameTimingWindow](../mods/CycleTrim/Core/FrameTimingWindow.cs)。回归：[FrameRateDiagnosticsRegression](../tests/CycleTrim.Optimizations.Tests/FrameRateDiagnosticsRegression.cs)。

## 本轮验证结果

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
| Steam 图形与液体副本 | 未执行 | 原游戏主机与交接中的未提交液体实现不可访问 |

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

编译得到的 `CycleTrim.dll` 为 191488 字节，SHA-256 为 `f4d060b7b21845a677ab42f95b782458b2344d6b7247bfc26f2e74f33b725abf`，与包内 `implementation/CycleTrim.dll` 相同；本次编译采用的帧计时代码与 e24b534b 一致。尚未核验游戏进程加载此 DLL，未部署到 Steam 游戏目录。

## 已确认的帧计时问题

### 仿真时间被帧率补丁截断

[基线 FrameRateUnlockPatch](https://github.com/LIghtJUNction/OniMods/blob/3c745da8067724f1cde46dc151fe4136f95cefa6/mods/CycleTrim/Patches/FrameRateUnlockPatch.cs) 的 SimEveryTick Prefix 同时将 Game.simDt 与传入 dt 截断到 2/60 秒。该行为在性能诊断关闭时也执行。

把现有宿主回归的要求改为“传入的 0.25 秒及积压的 0.25 秒保持不变”后，旧生产实现实际失败。该反例确认了补丁改变时间输入；它没有测量游戏内速度或收益。后续性能比较必须记录游戏仿真进度与墙钟时间，不能靠丢弃积压提高帧率。

### Total 漏计 Update 与 LateUpdate 之间的时间

基线 Total 由 Game.Update、Game.LateUpdate，以及 LateUpdate 结束到下一次 Update 开始的间隔相加。Update 结束到 LateUpdate 开始之间的其他脚本和引擎工作没有进入总和。Engine/Render 只是该剩余区间的旧标签，未提供渲染线程或 GPU 的独立计时。

在生产补丁链接的宿主夹具中，向 Update 与 LateUpdate 之间插入等待并独立计时：该间隔平均约 20.091 ms，而旧日志 Total 约为 0.13 ms。夹具提供的 unscaledDeltaTime 为 0.25 秒，旧日志因而显示 4.0 FPS。以上是计时边界反例，数值不表示 ONI 实际帧率。

## 液体源码的待验证机制

以下发现适用于可见的已提交基线。交接中的未提交改动不可见，因此这些发现不构成原 1158 kg 残差的已确认根因。

| 发现 | 源码依据 | 后续验证 |
| --- | --- | --- |
| CPU 水平输送先左后右，并在第一次传输后减少 remaining | [fluid.rs](https://github.com/LIghtJUNction/OniMods/blob/3c745da8067724f1cde46dc151fe4136f95cefa6/mods/CycleTrim/OniSim/native/src/fluid.rs) | 原宽池及其镜像；逐面、逐步记录质量与能量 |
| 水平流量为质量差的 0.25 倍，并受 min_horizontal_flow 截断 | 同上 | 用真实岩浆参数检查非零压差是否进入停流区间；同时保留连通器断言 |
| 生产调度将 gpu_liquid_flow 设为 false，压力 GPU 调用选择 phase 1 | [simulation_step.rs](https://github.com/LIghtJUNction/OniMods/blob/3c745da8067724f1cde46dc151fe4136f95cefa6/mods/CycleTrim/OniSim/native/src/simulation_step.rs) | 记录实际执行的液体路径；GPU 启用日志本身不说明液体压力路径已执行 |
| GPU flux 接收 head 与 upper_y，却没有在通量中使用；向上流要求接收格已经是液体 | [gas_pressure_faces.wgsl](https://github.com/LIghtJUNction/OniMods/blob/3c745da8067724f1cde46dc151fe4136f95cefa6/mods/CycleTrim/OniSim/gpu/src/gas_pressure_faces.wgsl) | 若启用此候选路径，验证受压真空、连通性和不同相边界 |
| transfer_liquid 的互溶分支增加组分质量后直接返回，没有同步加入源液体携带的热量与病菌 | [fluid.rs](https://github.com/LIghtJUNction/OniMods/blob/3c745da8067724f1cde46dc151fe4136f95cefa6/mods/CycleTrim/OniSim/native/src/fluid.rs)、[world.rs](https://github.com/LIghtJUNction/OniMods/blob/3c745da8067724f1cde46dc151fe4136f95cefa6/mods/CycleTrim/OniSim/native/src/world.rs) | 用异温互溶液体检查组分质量、热量及病菌转移 |

最小水平流量若为 q，局部规则可让小于 4q 的非零相邻质量差停流；宽池上的多个小差值可能累积成明显坡度。这是从代码公式得到的候选机制，仍需在原失败布局、参数和实际路径上验证。

## 仍需完成的运行验收

1. 取得原 dd97815e 工作区的完整未提交差异和原始宽池 fixture，保留既存 perf.data；先理解现有修复再修改液体算法。
2. 同时通过宽深池、镜像、连通器、受压封闭真空、不同流体边界，以及质量与热量守恒回归，记录大池收敛步数和计算成本。
3. 在原主机用 onim 构建与部署，通过 Steam 启动测试存档副本，核验实际加载的 DLL、原生库与日志。
4. 按暂停观察、短步进、再暂停验证液体状态；原实机验收失败保留到出现新的通过证据。
5. 在相同存档、分辨率、镜头、缩放、暂停／运行状态和模组组合下记录完整帧间隔的均值、p50/p95/p99、仿真进度、错误与视觉正确性；进一步拆分主线程、渲染线程、GPU、同步等待和 GC。
6. 隔离 Xvfb／RTX 对比必须标注其呈现路径；最终 500 FPS 验收仍需用户实际桌面条件。无头更新率与局部微基准不用于替代该目标。

## 证据复核

| 检查项 | 当前结论 |
| --- | --- |
| 问题是否可定位 | 时间截断与计时漏项有生产源码及宿主反例 |
| 表述是否清楚 | 用户交接数据、本轮宿主结果、源码推断分别标注 |
| 实验是否支持性能提升 | 尚无本轮实机 FPS 数据，不报告性能提升 |
| 验收是否完整 | 原液体 fixture、实机副本、图形性能分解均待完成 |
| 修复是否满足语义约束 | 宿主回归已确认原始仿真时间输入保持；液体断言与原生实现未修改，实机行为待验证 |
