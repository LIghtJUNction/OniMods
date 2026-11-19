# CycleTrim 的分层测量

先定位耗时，再做关闭/开启对照。诊断运行和性能验收分开，不能拿 profiler 下的
数字算提速。以下工具不自动启动游戏、不安装 profiler、不修改系统安全设置。

## 1. 选择诊断强度

诊断开关在启动时解析，重启游戏后生效。默认不安装专用计时补丁。先用
`--cycletrim-frame-profile` 看完整帧和粗粒度阶段；只有需要进一步归因时才开启
逐方法或原生采样。[开关策略](../mods/CycleTrim/Core/CycleTrimPatchOptions.cs)与
[实际安装点](../mods/CycleTrim/ModInfo.OniSim.cs)决定下列行为：

| 启动参数 | 完整帧与阶段 | 逐方法、状态机桶及热点回调 | 原生 CPU/GPU 阶段日志 |
| --- | --- | --- | --- |
| 无诊断参数、配置关闭 | 关闭 | 关闭 | 关闭 |
| `--cycletrim-frame-profile` | 开启 | 关闭 | 关闭 |
| `--cycletrim-managed-profile` | 开启 | 开启 | 不自动开启 |
| `--cycletrim-native-profile` | 关闭 | 关闭 | 开启 |
| `--cycletrim-frame-profile --cycletrim-native-profile` | 开启 | 关闭 | 开启 |
| `--cycletrim-perf-probe` | 开启 | 开启 | 开启，保留既有行为 |

`--cycletrim-gpu-profile` 保留为原生采样的旧名称；它使用的
`CYCLETRIM_GPU_PROFILE` 也控制部分 CPU 原生阶段日志，并不只测 GPU。
`--cycletrim-managed-profile` 保留原有“不自动打开原生采样”的优先级；
显式添加 `--cycletrim-native-profile` 或 `--cycletrim-gpu-profile` 仍会打开原生采样。
配置中的 `EnablePerformanceDiagnostics=true` 或 `CYCLETRIM_PERF_PROBE=1/true/yes`
表示完整诊断，不会被同时添加的粗粒度参数降级。

已有的原生采样环境设置不会被清除。做无探针对照时，应确认配置关闭，并且没有
`CYCLETRIM_PERF_PROBE`、`CYCLETRIM_GPU_PROFILE` 和诊断启动参数；
原生代码按环境变量是否存在判断，不能用 `CYCLETRIM_GPU_PROFILE=0` 代替移除。
Steam 向既有客户端转发游戏参数，但不保证转发调用者的环境，所以优先使用明确的启动参数。

粗粒度模式输出 `[CycleTrim][FrameChain]`、更新频率及内存摘要，记录相邻
`Game.Update` 入口之间的完整间隔。每个窗口约一秒，最多 2,048 个样本；
包含帧号、总耗时、P50/P95/P99、阶段 inclusive/self 时间和未归因时间。
它不安装 `DetailedFrameProbe`、`StateMachineBucketTelemetry` 或寻路/物流热点回调。
[计时窗口](../mods/CycleTrim/Core/FrameTimingWindow.cs)仍有观察开销，需要用同一 DLL
分别测关闭和开启诊断的成本。

```sh
rtk proxy python3 scripts/analyze_cycletrim_frame_chain.py /path/to/player.log \
  --output /path/to/frame-chain.json
```

这个分析器读取整份日志，不会自动排除载入、预热和暂停。正式比较前须按运行记录的
帧号截取完整测量窗口，排除跨越开始/结束边界的窗口。所有帧的平均耗时可由总 ticks
除以总帧数求得；各窗口的 P95 不能平均后当作整段 P95。更新入口频率不是显示器呈现
FPS，未归因时间不是 GPU 时间，父子阶段或并行线程的耗时也不能直接相加。

### 详细热点、GC 和探针归属

通过 Steam 给隔离测试进程加 `--cycletrim-perf-probe`，或使用不自动启用原生采样的
`--cycletrim-managed-profile`。详细探针覆盖寻路调度、工作线程寻路、导航更新、
取货候选、选择任务、BrainScheduler、房间更新；还会安装逐方法和状态机桶诊断。
在暂停的测试殖民地记下 `reportSequence`，执行有界短步，再暂停并等待延迟报告。
只分析这次基线之后的新调用：

```sh
rtk proxy python3 scripts/analyze_cycletrim_perf_probe.py /path/to/player.log \
  --series --after-sequence 2 --reject-external-patches \
  --summary-json /path/to/new-hotspots.json
```

`--after-sequence` 使用实际记录的序号。报告按包含子调用的耗时排序，列出新调用数、
单次平均毫秒、每观察秒调用数，以及 GC 次数和最后的托管堆大小。
平均值不是 p95；堆大小不是累计分配量。没有报告、序号断档、跨 Game 代次、
没有新调用或外部补丁会拒绝相应严格检查。可用重复的 `--require` 只选择此次场景
确实应运行的目标，但不能据此宣称未选择的系统通过。

这些目标会嵌套、跨线程和包含等待，**不能相加当成整局 CPU**。
暂停期间仍有主循环工作，观察时间也不等于实际模拟推进时间。

## 2. Linux 进程、每线程 CPU 和内存压力

显式指定本轮拥有的游戏 PID，或者原生用例 PID：

```sh
rtk proxy python3 scripts/profile_cycletrim_process.py --pid 12345 \
  --seconds 30 --interval 1 --output /path/to/new-process-profile.json
```

采集进程 CPU、每线程 CPU、调度运行/可运行队列等待、主/次缺页、RSS/PSS、
Swap/SwapPSS、进程 I/O、系统 load 和 CPU/内存/I/O PSI。I/O 字段沿用 Linux
名称：`read_bytes/write_bytes` 是字节，`syscr/syscw` 是调用数。
只记录映射中名字含 CycleTrim/OniSim/SimDLL 的库及哈希，不读取命令行或环境。
托管 DLL 不一定出现在 maps，仍须核对游戏内实际程序集加载回执。

PID/TID 的启动时间参与身份校验。进程退出、僵尸状态或身份变化不算完整测量；
中途失败保留已读样本。线程出生/退出/复用会标记覆盖不完整，线程合计只是
成功匹配区间的观测量。`schedstat` 等待仅指可运行但未得到 CPU 的时间，
不是睡眠或锁等待的总量。无法读取的可选接口保留 null/缺失字段，不假装为零。

默认每秒采样；更密的采样会增加开销。报告记录每次读取耗时，采样不原子，
并不将 profiler 自身算入目标进程 CPU。不要把高换页、低 CPU 直接归咎于算法。

## 3. 原生用例的 Python/FFI 和完整进程归因

使用已审阅的 fixture 和指定哈希的库，不替换生产游戏 DLL：

```sh
rtk proxy python3 scripts/profile_cycletrim_native.py \
  --library /absolute/path/liboni_sim_core.so --library-sha256 ACTUAL_SHA256 \
  --role candidate --scene thermal --size 192 --workers 1 \
  --cpus 0,2,4,6 --output /path/to/new-native-profile
```

默认 `benchmark_frames.py` 使用它既有的固定帧数。子系统候选的已审阅独立工作区
可显式指定 `--harness-dir` 和 `--driver benchmark_subsystems.py`，例如
`--scene thermal_dense --size 512 --frames 100 --warmup 4 --workers 8`。
脚本记录所有同目录 Python 依赖和实际库哈希，前后校验输入，保留子进程日志、
原生结果、Python pstats 和进程/线程样本。非零退出、缺失结果、未完成帧和库身份
不匹配不能算通过。已有 ONI 时拒绝运行；途中出现游戏也停止自己的诊断子进程。

Python 归因覆盖**整个子进程**，包含初始化、预热、帧、结果读回、校验和序列化，
不能把整段占比冒充纯计时区间占比。cProfile 看不到 Rust 工作线程栈；FFI 调用耗时
可以包含原生计算或等待。线程采样可以区分 Python、`onisim-physics` 和
`OniSim-cell-*`，避免把“线程池之外”全部叫作串行 solver。

## 4. 单独运行系统调用/等待分析

在第 3 项的同一命令上加 `--mode syscalls`，使用另一个输出目录。
需要系统已有 strace；权限不足时记录失败，不降低 ptrace 等安全限制。
工具使用 `strace -f -w -c`，将 `futex`、`sched_yield`、`clock_nanosleep`
等调用的次数、错误返回和包含阻塞的墙钟耗时写入 `syscalls.txt`。
这里采样的是被跟踪 fixture 的 PID，不是 strace 自己。

各线程的系统调用耗时相互重叠，合计可以超过进程墙钟时间；futex 很多不等于
已经证明线程池是瓶颈。错误返回也可能是 EAGAIN 等正常同步行为，需按调用分析。
strace 会改变调度，不能把该运行的帧耗时和未跟踪运行直接比较。

## 5. 同存档的实际游戏 A/B

用 Steam 和 OniHarness，每次从同一测试检查点副本开始，核对游戏版本、原生库和
已加载 Mod 哈希、其他 Mod、复制人数量、图形模式、速度、更新上限和存档字节。
先无头定位模拟开销，图形模式另测渲染，不能互相替代。

### U60 的输入与有效样本门禁

小存档用于验证启动、自然时钟推进和多组分读取；真实性能结论需要有代表性的
大存档负载。每个 A/B 运行都从同一副本的相同字节开始，不覆盖原存档，也不把
已经推进过的 A 存档交给 B。两侧必须使用相同 U60 构建与 Unity 渲染后端，
保持相同相机、分辨率、图层、速度和其他 Mod。OniSim 的 wgpu 计算后端与
Unity 渲染后端分别记录，不能将 Vulkan 计算日志当作游戏使用 Vulkan 渲染的证据。

比较局部性能候选时，A/B 应共享同一套 U60 兼容及存档修复，只改变被评估的优化。
原生被拒绝的 managed-only 基线可以用于整体产品对照，但不能证明多组分算法的
同语义提速；必须记录各侧实际映射的库、provider 可用性和模型差异。

多组分正确性先通过公开只读资源 `oni://world/cell/X/Y` 验证：
检查 `multicomponent.available`、`componentCount`、`totalMassKg` 和
`components` 的完整元素/质量库存。缺少该结构的旧候选可读取
`world_editor command=read path=/active/map/cell_X_Y.md` 的 Base 部分。
只有主元素的结果不能证明额外组分存在。测量前后保留这些响应；守恒或存档恢复
失败的样本不能计为有效提速。[读取实现](../mods/OniMcp/Tools/Impl/World/WorldCellInfoReadTools.cs)

先等待载入完成并检查原生窗口，确认没有阻塞模态框，再确认新鲜状态中的 PID、
`gameLoaded`、`paused`、`nativePauseCount`、`gameError` 与 `gameHasCrash`。
`gameLoaded=true` 本身只说明存在 Game 对象，不能独自证明载入完成。
不清空 `pauseCount`，不连续重试解除未知暂停，也不屏蔽游戏错误。
`step_until` 用于按条件游玩，会在 critical 通知、模态导致的暂停或外部暂停时
提前结束；这种结果是中止样本，不是性能成绩。测量用下面已有的有界墙钟步进，
保留前后警报信息，由 native-clock 正向推进与运行中暂停监控判定样本有效性。

需要 Unity 原生计数器时，可在正常图形模式启动测试实例：

```sh
rtk proxy tools/OniHarness/.venv-runtime/bin/python tools/OniHarness/scripts/runtime.py \
  --output /path/to/new-owned-runtime launch --graphics normal --engine-timings
```

粗粒度归因运行在该命令后另加 `--game-arg=--cycletrim-frame-profile`。
正式无探针对照不加该参数，并确认完整诊断配置和原生诊断环境也关闭。
`engineTimings` 中的 GPU 数据不可用时保留 null；
当前 companion 约每秒写一次状态，只记录各 recorder 的最新值，
这些稀疏样本不能算作全部帧的 P95/P99。
[原生计数器实现](../tools/OniHarness/runtime/OniHarness.Runtime/EngineTimingProbe.cs)

已暂停且加载的单实例 companion 可用既有脚本：

```sh
rtk proxy /path/to/runtime-venv/bin/python tools/OniHarness/scripts/measure_steps.py \
  --output /path/to/owned-runtime --label baseline01 \
  --samples 5 --warmup 1 --seconds 3 --speed 1
```

这测的是“有界推进、确认、重新暂停”的完整操作成本。每步必须实际推进原生时间，
并验证最终暂停。看 `gameCpuSecondsPerNativeSecond`、控制阶段耗时、缺页和内存，
不能把客户端确认更快算作模拟更快。固定 1x/更新上限也不是最大吞吐或 FPS 测量。

至少五组交替 A/B（每组两侧重新加载相同副本），保留每个失败和慢样本，报告中位数、
范围、实际工作量和环境压力。先比较同 DLL 的探针开/关以估计观察开销；正式性能
比较关闭 profiler。缺少实际推进、同负载、相同输入或稳定环境时只报告诊断，
不选择赢家。不改变既有性能门槛来接受候选。

## 本地回归

```sh
rtk proxy python3 scripts/verify_cycletrim_process_profile.py -v
rtk proxy python3 scripts/verify_cycletrim_perf_probe_analyzer.py
```

两个检查均由 `scripts/check_mods.py` 的既有 `verify_*.py` 发现机制自动接入。
Linux 诊断回归在没有 `/proc` 的平台明确跳过，不声称跨平台支持。
详细原始采样、日志和存档留在本地；对外只发布审阅后的摘要。
