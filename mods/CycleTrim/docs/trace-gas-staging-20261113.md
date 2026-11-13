# Trace-gas correctness and reusable staging — 2026-11-13

## Correctness

Gas projection formerly snapped changes smaller than `max(1e-7 kg, previous * 1e-5)`
back to the previous mass. For trace gas this could resurrect a computed zero or
suppress newly transported gas. Both serial and parallel projection now use only
the relative tolerance. Exact zero display slots become vacuum, or promote a
surviving secondary species. Positive trace mass is preserved; no arbitrary mass
purge was introduced. A rendered `0.0 µg` alone still does not prove exact zero.

The new trace-gas regression fails with the original threshold (exit 101) and
passes with the fix. Tests cover depletion, emergence and continued trace-gas
transport in both projection paths, plus zero-slot promotion.

## CPU gas work

The pending gas-transport optimization is included: reuse each cell's neighbor
velocities across species, and skip solid-cell work. The test-only frozen
transport oracle checks exact outputs, including nine-channel workloads. Earlier
same-process paired microbenchmarks measured about 5–14% lower local chain time
across dense/sparse and diffusion cases. This is not a whole-game FPS result.

## Allocation work

One per-device liquid workspace reuses packed input, masks, solid flags and CPU
projection storage. Finished work returns its workspace even when readback fails.
Gas/ liquid diffusion masks circulate between staging and the caller instead of
being dropped after every region. Capacity survives steps, not physics values;
obstacle flags and inputs are refreshed. The existing asynchronous liquid-submit /
gas-compute / liquid-finish order is unchanged.

The new reuse regression verifies stable warmed input/mask/projection storage and
alternating obstacle states. This removes repeated large-array allocations; it is
not evidence that total ONI memory fell by the same amount.

## Verification

- Native library: 218 passed, 16 ignored; includes CPU/GPU parity and liquid overlap regressions.
- Jlib: 2290 host assertions passed; shared native-loader API compiled and loaded in ONI.
- Loader host checks passed; no claim that the optional native-library smoke was run.
- Nested TOON camera-view regression: 19 Harness runtime tests passed.
- CycleTrim and OniMcp built/deployed through `onim dev`.
- Steam graphical test copy loaded with 15 duplicants; actual mapped native SHA256:
  `f239c29cc12635c5cb5a00045faa536c19ca0ae49e5e72a4255bf4138fbc6bf6`.

The first instrumented active series measured 13.16 median camera callbacks/s,
but used zoom 24.41 rather than the older zoom-8 baseline and observed about
820 MiB swap-in / 598 MiB swap-out. It does not establish an optimization gain.
Paused instrumentation also showed low cadence with no collections in those
windows: measured Game.Update/LateUpdate covered only a small part of the frame.
Unattributed time must not be labeled GPU time or GC time without further evidence.

Read-only memory counters showed about 2.8 GiB Mono used heap and 1.25 GiB Unity
allocated memory. These counters overlap and must not be added. A rendering-mode
diagnostic suffered stale telemetry and is not accepted as a quantitative result.

The sampler now records structured camera/world/viewport state before and after
measurement and rejects missing or changed views. Raw game logs and private saves
remain local. Whole-game performance attribution remains unverified.

A subsequent zoom-8 series measured 15.74 median camera callbacks/s but was
rejected because the camera changed and control overhead exceeded two seconds.
It is retained as a failed measurement, not evidence of improved FPS.


## 2026-11-14：协程与液体批次裁剪

- 单个当前线程 Tokio 执行器复用原生仿真工作线程；CPU 气体任务复用现有有界 Rayon 池。没有新增游戏进程、运行时线程池或独立仿真进程。
- 私有 staging 的所有权随任务移动，不复制整图。GPU 读回协程与 CPU 任务重叠；世界修改和事件仍按原顺序发布。没有液体读回可重叠时，CPU 计算继续直接执行。
- GPU 短任务先有界协作让出，再使用定时器退避，避免短读回直接支付毫秒定时器粒度。任务组退出时取消未完成读回；错误不会发布半批结果。
- 液体范围扫描同时收集有效质量的边界，摘要仍受原有 4 KiB 栈预算约束。裁剪保留一格邻域，按 32 格分块，发布时恢复完整区域所有权掩码。

同进程 ABBA：8 个现有 CPU worker，每组两条对照、两条候选，共 12 组、每路径 24 个计时样本。另做 12 步独立回放，逐字节比较完整世界与发布数据；两种尺寸均一致，重大缺页均为 0。对照是测试专用的同步 CPU / 原始整区读回路径，不是另一套物理算法。

| 场景 | 对照中位数 | 候选中位数 | 配对平均耗时变化 |
| --- | --- | --- | --- |
| 256×224，协程与裁剪组合 | 14.141 ms | 11.246 ms | -21.6% |
| 512×448，协程与裁剪组合 | 41.872 ms | 34.897 ms | -16.0% |

配对比值 bootstrap 95% 区间分别为 0.714–0.861、0.795–0.884。它们是合成稀疏液池的完整原生链结果，不是游戏 FPS。单独初版协程没有稳定收益；调整让出策略后，未裁剪场景的配对平均耗时下降约 6.6%，再叠加裁剪得到上表结果。

复现入口：`native_chain_tests::benchmark_coroutine_native_chain`，Release、显式 `CYCLETRIM_ONISIM_GPU=1`、`CYCLETRIM_ONISIM_WORKERS=8`，手动运行 ignored 测试；尺寸通过 `ONISIM_CHAIN_WIDTH` / `ONISIM_CHAIN_HEIGHT` 选择。关闭 `CYCLETRIM_GPU_PROFILE` 计时。

本机验证：原生 Release 219 通过、17 手动基准忽略；执行器同线程调度与取消 2 项通过。开发计数器保持每个分片元组一致，四写入线程的记录开销中位比值为 0.092x，串行为 1.271x，一百万次记录分配 0 B，原 2.0x 门槛不变。计数器改进不计入上述 Rust 原生链收益。
