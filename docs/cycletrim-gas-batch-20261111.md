# Cross-world gas submission batching

This change batches active gas worlds in the production native fixed step. It
removes the host wait between independent worlds' gas chains. It does not make
liquid flow, disease, buildings, or the complete simulation GPU-resident.

## Execution and ownership

Each active world packs all its gas species, without truncating the mixture to
primary/secondary display slots. A batch holds up to eight independent pipeline
instances. Equal dimensions do not imply shared buffers within a batch. Mass,
enthalpy, density velocity, conductivity, bounded transport, and temperature
operators remain separate dependent GPU kernels, recorded into one command
encoder. All result copies are appended before a single queue submission. All
maps are requested before one device wait. Results are validated as a batch
before any output slice is overwritten. Invalid device velocities invalidate
all caller outputs, release mappings, and permit a subsequent fresh upload.

The existing per-region CPU stages and event order are preserved. Gas batches
prepare after global thermal diffusion, before per-region phase transitions.
At the original gas consumption point, exact source snapshots check species,
mass, gas temperature and obstruction. Phase transitions, preceding regions,
and components can invalidate a prediction. Only that world is recomputed from
its current inputs. Overlapping regions are covered by this check. GPU errors
remain errors and do not silently switch the batch to another backend.

Settled worlds retain the existing sleep cache. Sleeping worlds perform no dense
packing, uploads or dispatches. Changes to gas state, blocking geometry or gas
material properties wake them. Small regions use their existing CPU path.
Profiler output reports dispatched/reused/invalidated worlds, allowing a real
save to expose ineffective speculation. Host staging and exact source-snapshot capacity persist per
region. Independent region packing uses the existing bounded cell worker pool,
with the submitting thread's floating-point environment installed per task; the device pipeline cache retains at most eight entries within its
existing estimated 96 MiB retention budget. In-flight batches need separate
buffers and can temporarily exceed that retained-cache estimate.

## Local validation

- GPU library: 50 passed, 6 manual tests ignored. Includes batch-wide rejection
  and successful reuse after a non-finite device velocity.
- Native library: 148 passed, 13 manual tests ignored. Includes serial/batched
  byte-identical world and event snapshots, equal-shaped worlds, changed inputs,
  overlapping regions and settled-world wakeup.
- Paired release benchmarks alternate serial/batch order, restore identical
  inputs, discard ten warmups and collect one hundred samples per mode. They compare
  every mass and temperature output, with a second fixture including host
  packing, exact source validation, publication and full world/event comparison.

Timing and runtime acceptance are recorded below after execution. Local timing
is not a whole-game FPS claim. The preceding version's actual 3x simulation rate
was only 1.10–1.41x in the measured colony, as recorded in
[the runtime report](cycletrim-async-simulation-20261110.md).

## Paired local timings

RTX 4060 Laptop Vulkan, optimized Rust build, single test thread, existing
8-worker pool, ten warmups and 100 alternating pairs. The test process had a
2 GiB memory limit and low I/O weight, without a CPU quota during timing. Builds
were separately limited to two CPUs and one compilation job.

| Fixture | Serial median | Batch median | Batch faster pairs |
| --- | ---: | ---: | ---: |
| Eight heterogeneous gas GPU chains | 14.241 ms | 10.024 ms | 69/100 |
| Eight 32x128 worlds, including CPU staging/publication | 6.174 ms | 4.912 ms | 70/100 |

The second fixture improved about 20% in median wall time. Dispersion remains
large (serial 3.592–22.365 ms, batch 3.471–12.629 ms); this is a local workload,
not an in-game throughput guarantee. All output arrays and complete world/event
snapshots matched the serial path.

Earlier CPU-quota-constrained 20-pair samples were unstable: the full-staging
fixture varied from 7.883→4.568 ms to 5.661→6.877 ms. A 100-pair run with serial
packing and reused snapshots was effectively tied (4.689→4.679 ms, 48/100 wins).
This prompted parallel packing, rather than selecting the favorable sample.
The first heterogeneous benchmark fixture incorrectly placed gas inside blocked
cells; input validation rejected it before measurement. The fixture was fixed
and all subsequent runs validated their outputs.

Private evidence: `.cache/gas-batch-20261111/`, including `gpu-tests.log`,
`native-final.log`, `paired-parallel-100.log` and the retained earlier attempts.

## Whole-frame acceptance: NOT ACCEPTED

The user's acceptance criterion is whole-frame latency and actual simulation
progress. Local gas timings are not the acceptance criterion.

The unchanged installed 0.4.15 baseline was verified against its 115-file receipt.
Steam launched the graphical client, OniMcp loaded the same untouched test save
(15 duplicants), and the camera used world 0, (98.53, 223.5), zoom 160 at 1220x1521.
Rendering stayed at every frame. After an eight-second 1x warmup, bounded running
windows were paused and verified between captures.

| Baseline scenario | Mean whole-frame interval | Render FPS | Actual simulation rate | Game major faults during window |
| --- | ---: | ---: | ---: | ---: |
| Full map 1x | 119.60 ms | 8.36 | 0.704x | 113,284 |
| Full map 3x | 71.97 ms | 13.89 | 1.428x | 27,300 |
| Full map 3x repeat | 72.89 ms | 13.72 | 1.358x | 42,065 |
| Full map paused | 32.31 ms | 30.95 | 0 | 1,164 |

Means above are complete telemetry-window elapsed time divided by frame count.
Game.Update entry-to-entry profiling windows independently measured 116.99,
70.00, 75.40 and 32.32 ms respectively. P95/P99 remain per-window measurements
in `baseline/whole-frame-summary.json`; pooling those percentiles is invalid.
The running-window tails and heavy paging make this unsuitable as a clean
optimization baseline. Another project's compilation was active, with observed
host swap traffic reaching hundreds of MB/s. No claim attributes all of that
traffic to ONI.

The original Steam launch was rejected because `oni-blueprints.service` had
registered the same game AppID. It was temporarily stopped for testing. The
baseline game then quit without saving. Release 0.4.16 was built successfully
and installed into the existing Dev directory, verifying all 115 packaged files.

The user declined pausing the other project's compilation and explicitly chose
to leave acceptance pending. The candidate Steam launch had already been
requested after that compilation naturally ended. No controlled candidate
performance sampling was performed. The log nevertheless shows the test save
was loaded at 02:59:27 UTC (the launch command contains no save-loading flag).
Do not describe this run as remaining at the main menu.

A captured complete Game.Update entry-to-entry interval lasted **1,038.92 ms**.
Within that interval, main-thread self scopes included StateMachineUpdater
subticks 365.48 ms, Game.StepTheSim 235.14 ms, and RenderEveryTick 132.07 ms.
These are wall durations and may include scheduling, paging and waits; they do
not by themselves prove CPU arithmetic was the cause. The first native step
reported global setup plus thermal 225.17 ms and gas batch preparation 79.05 ms;
those are separate step measurements and must not be summed into that frame.
Production batch counters confirmed 8 dispatched, 7 reused and 1 invalidated.

During this startup/shutdown run the game accumulated 807,379 major faults and
observed host swap traffic peaked at 675 MB/s. These are run-level/resource
measurements, not statistics for the single long frame or proof that all swap
traffic belongs to ONI. Steady-state candidate whole-frame latency and the
benefit or regression of batching remain unestablished.

The first MCP initialize request timed out during loading; retry succeeded,
fresh tools/list was saved, and game_control quit to desktop without saving.
No game PID remained, and oni-blueprints.service was restored and verified
active. The release is installed but whole-frame acceptance remains pending.


## Local complete-cycle optimization on 2026-11-11

The current target is a mean below 5 ms for the complete local native cycle.
The user tightened the earlier 10 ms target during this iteration.
The fixture uses 512 by 448 public cells, three regions, 17 warmup cycles and
68 measured cycles. The timed scope includes submission, worker simulation,
publication and finish_prepare. Snapshot hashing runs outside the timed scope.
These measurements cover the native production chain. Unity frame acceptance
remains pending under the user's instruction to keep the full game closed.

The initial eight-worker complete-cycle mean was 45.518 ms. Subsequent worker
experiments selected 16 workers, matching the existing production pool limit.
Comparisons below hold that worker count and workload constant, use sequential
ABBA ordering and allow the unrelated project compilation to continue.

| Iteration | Previous mean | Candidate mean | Disposition |
| --- | ---: | ---: | --- |
| 18 | 14.986 ms | 13.764 ms | Retained |
| 19, AVX2 | 15.261 ms | 17.953 ms | Reverted |
| 20, publication memory traffic | 14.894 ms | 13.860 ms | Retained |
| 21, sparse-liquid precheck and ThinLTO | 14.200 ms | 11.858 ms | Retained |
| 22, uniform thermal and parallel SOA copy | 13.746 ms | 14.194 ms | Parallel SOA copy reverted |
| 23, compact snapshots and overlapping publication | 13.099 ms | 11.565 ms | Retained |
| 24, deferred notifications and smaller sunlight tiles | 13.552 ms | 11.427 ms | Retained |
| 25, one task per Grid stream | 12.589 ms | 13.809 ms | Replaced with cell-tiled scheduling |
| 26, immediate CPU regions and tiled Grid transfer | 9.974 ms | 9.778 ms | Retained |
| 27, fused uniform AVX2 | 12.859 ms | 37.028 ms | Reverted |

Iteration 20 passed 157 native tests. Its sampled gas preparation stage averaged
3.903 ms. This remains short of the complete-cycle target. The two baseline
runs ranged from 11.646 to 18.143 ms, so host variation limits fine-grained
speedup claims. Full ABBA logs are retained under the ignored local directory
`.cache/loop-under-10ms-20261111/`.

The CPU gas path shares the GPU operator definitions and keeps every species
authoritative. Multi-step CPU/GPU comparisons observed maximum mass deviation
0.000010192 kg and temperature deviation 0.001129150 K in the covered fixtures.
These tolerances and local fixtures do not establish full-game equivalence.

Inspection of Game.StepTheSim confirmed that its substance-change loop updates
Grid.Element. WorldGenSimUtil also copies mass, temperature and other cell fields
through those notifications. Mass-only events remain supported for that caller.


Iteration 24 passed 159 native tests. Its complete ABBA comparison produced
87 publication/world snapshot pairs per run, all identical across both versions
and both repetitions. Sampled gas preparation averaged 3.022 ms. Iteration 25
passed 160 tests but regressed its complete-cycle comparison. Its sampled gas
preparation averaged 3.684 ms. Both complete-cycle means remain above the target.
A new compact-snapshot test initially used an unsupported 8 by 8 fixture. It was
corrected to the fixture's minimum 16 by 16 dimensions and passed.


Iteration 26 passed 160 native tests and preserved all 87 complete snapshot pairs
in its ABBA comparison. Sampled regional gas handling totaled 3.659 ms per sampled
step across all three regions. Its first test attempt exposed a small-region
policy mismatch between batched and immediate dispatch under test builds. The
immediate production entry now preserves the same 256-cell cutoff for multi-region
steps. The CPU route avoids speculative GPU batching after its backend is known,
while workloads requiring GPU operators retain batching on subsequent steps.

Iteration 27 passed 161 tests, including bitwise vector/scalar comparison across
four rounding modes and FTZ/DAZ combinations, and matched all complete snapshots.
It still regressed complete-cycle timing. One candidate run averaged 58.729 ms,
recorded 14 major faults and contained 20 cycles above 100 ms. The other candidate
run averaged 15.327 ms with zero major faults, also slower than the two scalar
runs. The experiment was removed. This evidence establishes a regression in the
covered comparison without assigning every long-tail interval to paging or SIMD.


Iteration 28 ran Grid/event publication concurrently with texture/species
publication. Species publication reads authoritative world arrays directly.
Its complete ABBA means were 10.658695 ms for iteration 26 and 9.831473 ms for
iteration 28. All 87 publication/world snapshot pairs matched across four runs.
All 161 native tests passed. Sampled regional gas handling totaled 3.7071 ms
across three regions. The 5 ms complete-cycle target remains open.

Iteration 29 exchanges owned CPU gas output vectors with staging after successful
computation and validation. Four CPU gas tests passed, including repeated reuse
and failed-input atomicity. Its ABBA timing was contaminated by host swapping.
The identical baseline rose to about 25 ms and candidate runs recorded 46 and 12
major faults. No speedup is accepted from those runs. The worker-count sweep was
also unstable, so the production worker policy remains unchanged.

Iteration 30 introduces explicit game-owner publication views. The game loader
opts in before SIM_Initialize and reuses the existing Jlib mapping. The current
session shares the immutable published Grid. Standalone TryInitialize keeps
stable arrays, and GetGrid detaches a stable view for the current session.
Clone refreshes native backing pointers. Publication alias checks include the
shared Grid's backing arrays and descriptor. Range reservation covers all 55
current Grid, event, texture and species storage ranges. All 163 native tests
passed. The same-binary ABBA comparison measured 10.505657 ms for stable views
and 9.783606 ms for game publication views. All 87 complete publication/world
snapshot pairs matched across four runs. Major faults were 0, 0, 1 and 0.
Sampled regional gas handling in the publication-view runs totaled 4.174 ms.
The disease-growth stage totaled 2.196778 ms and exposed avoidable GPU cleanup
of cells whose disease index is absent and whose count is below one.

Iteration 31 routes those inactive rectangles through the existing CPU cleanup.
It preserves infestation advancing to one and clearing stale growth, including
noncanonical clean input. The canonical already-clean early exit is retained.
All 163 tests passed and all 87 complete snapshot pairs matched in four runs.
Candidate runs averaged 11.758581 ms and 12.062467 ms with zero major faults.
The second baseline recorded 103 major faults and averaged 32.082341 ms, so the
whole-loop acceleration ratio is unaccepted. Sampled disease growth fell from
3.112667 ms to 0.187556 ms in this comparison. Candidate regional gas handling
remained the slowest sampled stage at 5.548556 ms across three regions.

Iteration 32 places the complete worker batch in the existing bounded thread
pool, reducing repeated external-thread handoffs between parallel phases.
Tasks inherit the simulation thread's floating-point controls. Masked sticky
status flags merge back after the batch, and unmasked execution retains its
original calling thread. All 163 tests passed and all 87 snapshot pairs matched.
The complete ABBA means were 8.342651 ms for iteration 31 and 8.800785 ms for
iteration 32, with zero major faults in every run. The scheduling experiment
was reverted. The retained iteration 31's sampled regional gas handling totaled
3.636222 ms across three regions. The 5 ms target remains open.

The retained source built successfully with `onim -m CycleTrim build --release`.
Managed compilation and native packaging completed. Rust reported existing
unused/dead-code warnings. No full-game runtime acceptance was performed.


## 实机失败现场与第 33 次局部迭代

实机进程 2234449 的加载路径与候选 DLL、原生库哈希已经核对，之后出现长时间无响应，验收失败。失败日志保存在 `.cache/loop-under-10ms-20261111/e2e-failed/player.log`。最后 30 个 FrameChain 窗口共 306 帧，整帧平均 405.603 ms。包含 GC 的三个窗口出现数秒到数十秒停顿。GDB 主线程栈停在 Boehm GC 的标记与分配路径，进程伴随大量换页。阶段 self 时间包含等待和停顿。排除 GC 计数增加的窗口后，整帧平均仍为 178.786 ms，状态机更新约 48.463 ms/frame，属性纹理任务约 1.231 ms/frame，未归因约 105.336 ms/frame。

部署曾把 CycleTrim 的来源从 Dev 切换到 Local，造成测试存档的模组差异。已把相同 Release 文件恢复到 Dev 并还原加载顺序，恢复后尚待重启验证。

OniMcp 的初始化与 tools/list 先前依赖游戏主线程，主线程阻塞时元数据请求也超时。现已改为启动时捕获工具清单，由 HTTP 线程直接处理元数据，保留原会话与容量限制。23 项服务端回归通过，实机 tools/list 用时约 0.319 秒。游戏状态操作仍依赖 Unity 主线程。

第 33 次迭代增加 Jlib 的有界、可关闭状态机 bucket 计时与分配诊断，完整执行原回调并保留异常语义。CycleTrim 将记录最慢 20 个 bucket 的调用数、总耗时、单次峰值和线程分配量。另在已选择游戏发布视图且 headless=0 时，最终发布的 SubstanceChangeInfo 只保留元素身份实际变化的记录。原生质量、温度数组照常发布，Worldgen 与独立会话保留原事件语义。该改动用于减少 Game.StepTheSim 的同值元素赋值，尚不能据此解释全部实机停顿。

第 31 次完整本地原生循环复测平均 8.342651 ms，最慢采样阶段为三个区域的气体处理合计 3.636222 ms。5 ms 目标未达到，任何本地原生循环数字均不能替代整帧验收。第 33 次数据仍需重新测量。

全图运行采样已执行，相机半高190，屏幕1219×1521，三倍速且取消暂停。连续132个窗口共1659帧、161.020秒，平均97.059ms/帧，10.303 FPS，最慢18943.846ms。一个GC窗口，排除该窗口后均值85.892ms。主阶段self均值为状态机推进17.240ms、StepTheSim15.706ms、每帧对象渲染6.422ms、动画更新4.868ms、动画绘制4.095ms、导航3.665ms、属性纹理任务2.490ms，另有29.247ms未归因。BrainScheduler RENDER_EVERY_TICK累计9683.350ms，约5.837ms/帧，单次峰值454.131ms。源数据见本次cache中的fullmap-performance.json与fullmap-frame-windows.json。分位数只保留每窗口摘要。此次保持游戏运行供用户测试，oni-blueprints服务仍临时停用。OniMcp暂停引用与step_until按需采样修复已局部验证，尚未部署到此进程。


## 第 34 至 36 次局部迭代

第 34 次保留版本修复液体移动、相变、消耗后的残余物质显示，启动时一次性修复空显示字段。历史 0 K 数据缺少原始温度时使用 293.15 K 估计。普通纯物质清空和整格 ReplaceElement 维持各自语义。含 extras 的气体休眠快照改为有序行块并行，保留全部浮点位。

第 34 次以第 31 次不可变测试程序作 ABBA 对照，512×448、三个区域、17 次暖身和每次 68 个计时循环。第 31 次两轮均值 9.830463 ms，第 34 次 8.485715 ms。每轮 87 个完整物理快照相等。计时区间内的采样输运阶段合计均值 3.586250 ms。该阶段包含数据整理与气体计算，不能仅凭名称归给单个 kernel。

第 35 次尝试保留每区域独立 staging 与 CPU workspace。198 项原生回归通过，但同条件完整循环从第 34 次 17.437604 ms 回退至 23.581945 ms，零重大缺页，物理快照相等。该缓存实验已撤回。该轮与上一轮的机器负载不同，仅使用轮内对照。

第 36 次独立验证液体种类发现。最多 64 个固定栈摘要按输入顺序合并，保持 16 种上限，小区域保留串行。197 项原生回归通过。mixed 场景下第 34 次完整循环均值 26.629689 ms，第 36 次 24.872699 ms，每轮 87 个完整物理快照相等。

Jlib 新增事件失效的动画可见区域缓存和带发布版本契约的压力纹理缓存，完成真实 Harmony 接线、私有 ref struct、生命周期、异常与重复调用零分配回归。主线程诊断增加 Linux 线程 CPU 时间，与墙钟时间同时记录。Jlib Debug/Release 构建完成，宿主 2290 项计数断言及缓存回归通过。CycleTrim 要求 Jlib 0.3.6。Steam 启动包装器已移除普通启动时强制开启原生详细诊断的赋值，显式诊断配置仍保留。

先前调试器恢复操作曾触发一次游戏崩溃，已保存 diagnostic-crash-player.log 并恢复相同测试存档。重复 SIGSTOP 采样未保持运行，样本全部作废。后续使用 Mod 内部计时。上述 1659 帧全图记录采集于该调试事件之前，仍保留有效。


## 液体体积输运重写，0.4.18

原生产液体入口已接入 `liquid_transport.rs`。重力按液体总体积输运全部组分，同时按被占用空间反向移动气体。下落阶段使用当前库存，底部流走后重新判断上方覆盖面。横向通量使用统一液位快照，落地后铺展，跨区域传递压力。纯液体沿用隐式压力求解，混合液柱使用同一体积压缩规律。

库存归一化保留所有物态和任意数量组分。双组分常见路径免分配。GPU 双字段液体扩散跳过有额外组分的格子。旧库存测试改为逐组分统计质量和热量。旧混合格最小流量测试原先按显示字段判断液位，现改为检查全部组分体积、共同输运比例及总量。

Release 原生回归 211 项通过，13 项手动基准忽略。新增 12 项场景覆盖密闭气体下落、部分液位置换、20 种液体、热量和病菌、连续液柱、落地铺展、稳态、黏度、阻挡、分层、排出边界和 GPU 额外组分保护。

初版全区域扫描使 mixed 合成循环从 20.561535 ms 回退至 23.967420 ms，候选一轮含 19 次重大缺页。已改为液体范围及一格边缘内执行压力和流动扫描，并修复旧压力覆盖面阻挡连续液柱的问题。

最终 ABBA 使用原第 37 次不可变测试程序作基线，512×448、GPU、16 workers、17 次暖身、每轮 68 次完整 worker/Engine/发布循环。旧版两轮为 25.689883、20.177426 ms，新版为 19.597560、21.277660 ms，均无重大缺页。旧版均值 22.933654 ms，新版 20.437610 ms。采样液体阶段由 3.670875 ms 降至 1.664875 ms，候选最慢的采样输运阶段为 6.637625 ms。两轮离散较大，机器发生过磁盘 I/O 拥塞。本节计时属于合成原生循环，游戏整帧与视觉验收另行记录。


## CPU 导航局部迭代，0.4.19

游戏保持关闭。Jlib 0.3.7 新增 NavigationGraphExpansion 与 NavigationLinkParallelism，CycleTrim 在 OnAllModsLoaded 的 CPU 优化入口直接安装并报告结果。

脏格扩展按字节位图跳过已覆盖区间，保留初始种子顺序、新增格顺序、原图更新、通知与清理。连边阶段复用原版逐格 CreateLinks 和游戏 GlobalJobManager，校验阶段先完成，所有连边作业结束后才继续原通知。小批量和低成本更新串行，超大空表清理可并行。重复目标、非主线程和嵌套作业保持原调度。线程局部集合复用，异常等待所有作业结束后返回调用方。

局部宿主回归通过 561 项扩展断言和 29 项连边断言。覆盖随机边界、位图与列表顺序、回调、空更新、重复目标、嵌套调度、异常和重试、超大空表清理、安装幂等。安装版 Assembly-CSharp 与 firstpass DLL 只作为元数据读取，核对所需私有字段和方法。Jlib 双目标 Release 构建通过，52 项既有警告。CycleTrim Release 构建零警告零错误。

受控宿主 ABBA 中，脏格扩展均值由 0.242049 ms 降至 0.079691 ms，内部调用由 70000 次降至 484 次。连边调度使用 4096 格、8 线程、只读邻域计算夹具，均值由 16.977292 ms 降至 3.926266 ms。夹具后端使用受控计算，实机原版 CreateLinks 的耗时与游戏整帧仍待验收。

完整原生循环另行复测 mixed、512×448、GPU、16 workers、17 次暖身、68 次计时。均值 25.853755 ms，中位数 24.041264 ms，重大缺页为零。最慢采样阶段 transport 均值 8.973750 ms，液体阶段 2.200500 ms。该轮没有原生源码改动，主机负载和单轮结果不足以建立版本性能对照。另有一轮与构建重叠的日志，未用于结论。5 ms 完整循环目标尚未达到。

已覆盖部署本地 Dev/CycleTrim 0.4.19 和 Dev/Jlib 0.3.7，逐文件 SHA-256 与 ZIP 核对。证据位于 .cache/loop-under-10ms-20261111/navigation-*.log、navigation-summary.json、navigation-deployment.json。游戏运行验收未执行，Steam 发布未执行。


## 2026-11-12 CPU 输运循环，0.4.20

当前保留三个生产改动。多组分气体的扩散、速度、平流以及温度输出通过完整行迭代坐标，复用 neighbors_at，消除逐格除法和取模。多组分与最终温度输出的任务块从八行细分到两行。混合区域后处理按当前行状态判断空操作，核对上下及左右边界升华供体，逐行处理顺序继续覆盖液体、残余清理与随机事件。

诊断新增 gas transport detail，按既有详细诊断选项与每 17 步采样，记录准备、计算、回写及休眠检查。分段记录与主计时独立运行。早期从仓库根目录执行 Cargo 的命令使用默认工具链，后续全部改为项目固定 Rust 1.99.1。与构建重叠的最早一轮已排除。

尝试过两组分编译期展开、扩散后密度缓存、共享边界通量和纯组分零扩散分支。正确性检查通过，整循环收益和计算段收益不足以确认，已撤回这些实现。8 线程对照均值为 22.587183 ms，16 线程为 20.649674 ms，继续使用原线程配置。

最终主计时关闭逐步诊断日志，使用 mixed、512×448、GPU、16 workers、17 次暖身和每轮 68 次完整 worker/Engine/发布循环，顺序为 ABBA。基线两轮 23.301235、22.388668 ms，候选 19.630825、19.739123 ms。基线均值 22.844951 ms，候选 19.684974 ms，下降 13.8323%。四轮重大缺页均为零，每轮 87 份完整物理与事件快照相等。

另行开启分段诊断的候选采样均值，输运 5.130000 ms，其中准备 0.512750 ms、计算 3.660250 ms、回写与休眠检查 0.854125 ms。液体阶段 1.555625 ms，后处理 0.940875 ms。分段采样来自独立运行，不能与主计时相加。机器同时存在 I/O 和内存压力，早期多轮墙钟差异明显，报告保留轮内对照。

证据位于 .cache/gas-transport-20261112/summary.json、rows-quiet-abba.json、各阶段测试 JSON 和日志。新增坐标回归覆盖单列、奇数尺寸、尾部行块与空区间。完整原生循环 5 ms 目标尚未达到，游戏整帧分布与实际帧率待运行时验收。

最终 Release 原生回归 212 项通过，13 项手动基准忽略。生产 Release 构建零警告零错误，已覆盖部署本地 Dev/CycleTrim 0.4.20，115 个文件逐项与 ZIP 核对。部署记录见 deployment.json。游戏保持关闭。


## 2026-11-12 发布与扩散检查，0.4.21

流动图像与日照、液体图像并行生成，使用原有有界池的 in_place_scope。日照和液体计算继续在调用线程执行，作用域完成后合并流动任务的浮点粘滞标志。异常未屏蔽或没有线程池时使用原串行实现。新增 96×97 大图回归，检查四种舍入模式、信号 NaN、全部图像逐位结果、调用线程 MXCSR 与 errno。比较前先初始化共享池，避免将首次线程池发现造成的 errno 副作用误算为图像计算差异。

混合区域扩散复用现有气体遮罩。当前行与下一行都已由张量输运覆盖时，仅继续处理原有右边界与底部边界回退。内部原本已处理的边，以及非气体的空操作，被整行跳过。事件顺序与边界处理通过独立回放逐步对照。

液体贴图整行分类尝试回退，完整循环从 16.505297 ms 变为 17.899320 ms，已撤回。仅贴图并行的同进程完整循环对照为 17.283559 → 16.869899 ms，均无重大缺页，收益约 2.4%。跨进程八轮均值看似下降约 6%，但部分轮有 5、9、12、43 次重大缺页，未作为最终性能结论。

新增仅在测试构建中启用的参考路径选择器，以及 benchmark_optimized_native_chain。同一进程和同一个持续演化的 world 内，ABBA/BAAB 交替执行整条 worker、Engine、发布循环。参考模式使用旧的串行贴图与混合区域扩散检查。正式库直接使用生产优化路径。计时阶段不做逐步完整快照序列化，正确性使用另外的独立回放验证。

mixed、512×448、GPU、16 workers、17 次暖身，每轮每种实现 68 次完整循环，共两轮。第一轮 17.180567 → 15.097296 ms，第二轮 20.399635 → 18.530686 ms，四组重大缺页均为零。参考均值 18.790101 ms，候选 16.813991 ms，下降 10.5168%。独立旧版/新版回放的 87 份完整物理、事件和图像快照相等。完整原生回归 213 项通过，14 项手动基准忽略。

另外开启分段诊断的候选采样，输运均值 5.486500 ms，扩散及同步 0.838500 ms，液体 1.648000 ms，后处理 0.913750 ms。一份发布采样为 2.159 ms，其中纹理墙钟 1.680 ms、Grid 发布 2.142 ms、组分发布 1.373 ms。这些发布任务并行，分段来自独立诊断，不能与主计时相加。

原始结果与最终汇总位于 .cache/textures-20261112/，final-summary.json 记录源码摘要、全部样本、缺页与正确性证据。完整原生循环 5 ms 目标尚未达到，Unity 整帧与实际帧率仍待运行时采样。游戏保持关闭。
