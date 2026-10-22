# CycleTrim 的分层测量

先定位耗时，再做关闭/开启对照。诊断运行和性能验收分开，不能拿 profiler 下的
数字算提速。以下工具不自动启动游戏、不安装 profiler、不修改系统安全设置。

## 1. 游戏内热点、GC 和探针归属

通过 Steam 给隔离测试进程加 `--cycletrim-perf-probe`。已有探针覆盖：寻路调度、
工作线程寻路、导航更新、取货候选、选择任务、BrainScheduler、房间更新。
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
