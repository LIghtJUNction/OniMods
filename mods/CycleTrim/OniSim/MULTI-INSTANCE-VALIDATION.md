# CycleTrim 多实例验收接入

状态：验收方案；尚未完成 CycleTrim 四个真实游戏实例同时运行的验收。
复用 [OniHarness 实例控制器](../../../tools/OniHarness/docs/multi-instance.md)
所述底座，不另建启动器。底座正在独立开发，接口以其当前文档为准。

## 已落地的接入工具

`conformance/prepare_instances.py` 从现有 CycleTrim ZIP 中只取运行所需的文件，
固定托管 DLL、原生库、控制插件和存档副本的指纹，再调用实例控制器的 `init`。
它创建独立测试组，默认一个图形实例、三个无头实例、每实例两个工作线程。
原始存档只读使用且核对指纹；测试输入及每实例工作副本设为 `0600`，每次准备
使用新目录。ONI 的加载检查要求读写访问，`0400` 副本会被当作不可加载文件。
准备不等于启动或验收。

```sh
rtk proxy python3 mods/CycleTrim/OniSim/conformance/prepare_instances.py \
  --harness tools/OniHarness --group-root /tmp/ct4-new \
  --stage mods/CycleTrim/OniSim/conformance/artifacts/ct4-new \
  --package dist/CycleTrim.zip --game-path /installed/OxygenNotIncluded \
  --companion /built/OniHarness.Runtime --save /private/Test.sav
```

`--native /path/to/candidate.so` 可显式替换测试组的原生候选；这只固定测试输入，
不表示 ABI、性能或游戏行为已验收。默认仅复用包中已有的原生库。
控制插件也复制到该测试组自己的暂存目录，后续另一任务的重建不会改变这些输入。

`conformance/verify_instances.py` 是只读校验器，复用现有控制器的实例归属、
Steam 启动链、隔离目录及新鲜状态检查，并额外核对实际加载的 CycleTrim.dll
和 `/proc/PID/maps` 中的 Rust 库。它会核对映射文件的设备号、inode 和文件指纹，
拒绝缺少原生库、文件已替换、错误 DLL、旧状态、跨实例回执及 PID 复用。

```sh
rtk proxy python3 mods/CycleTrim/OniSim/conformance/verify_instances.py \
  --harness tools/OniHarness --group-root /tmp/ct4-new --name guest1 \
  --managed /staged/mod/CycleTrim.dll --native /staged/mod/OniSim/liboni_sim_core.so \
  --output mods/CycleTrim/OniSim/conformance/artifacts/ct4-guest1-audit.json
```

四实例完成控制器的原生并发 `verify` 后，省略 `--name`，传入
`--concurrency-receipt /tmp/ct4-new/receipts/实际回执.json`。校验器重新检查四个
不同进程、精确存档、正向原生时钟、重叠推进区间及最终暂停；图形实例需有真实
图形设备和相机渲染，无头实例需为 Null 设备且相机回调为零。
这项检查仍不证明纹理正确、存读档往返、无头先于图形的完整流程或整体性能。

当前已准备 `/tmp/ct4-20261021-a`，使用 `ea8c59a3` 托管 DLL 和 `1f56c9b2`
原生库，尚未启动实例。输入回执见
[测试组准备记录](conformance/results/linux-744825-cycletrim-instance-preparation.json)。
本轮新增接入工具的 13 项宿主测试及原有 12 项驱动测试通过；这些测试使用 API
替身、伪造的 `/proc` 文件和不执行的夹具文件，不是四实例游戏通过证据。

去重候选另有独立测试组 `/tmp/ct4-dedup-20261021`，暂存目录为
`conformance/artifacts/cycletrim-dedup-group-20261021`。它固定 `ea8c59a3`
托管 DLL、`9ac4bae9` 候选原生库和控制插件副本；没有启动任何实例。
后续容量回归已确认此候选存在位图预留空间超限问题；该组已标记为被替代，
仅保留复现输入，不作为下一次启动目标，待修正候选完成验证后重新暂存。
当前四实例容量预检失败：可用内存约 4.5 GiB，默认估算需要 13 GiB。
此时还有其他任务拥有的真实游戏运行，不启动、重启或停止其进程。
准备工具现已在创建目录前检查原生覆盖文件，缺少或无效的候选不会留下部分
解包目录；相关回归及全部 25 项宿主测试通过。见
[候选准备与容量记录](conformance/results/linux-744825-dedup-instance-preparation.json)。

已修正的 `4e677de1` 候选另行准备在 `/tmp/ct4-dedup2-20261021`，暂存目录为
`conformance/artifacts/cycletrim-dedup2-group-20261021`。31 项原生单元测试及
单、四线程各 224 项原版对照通过；这不替代性能或实机检查。最新四实例容量
快照仍未通过：可用内存约 2.9 GiB，默认估算需 13 GiB。此准备记录没有包含启动，详见
[修正版准备记录](conformance/results/linux-744825-dedup-v2-instance-preparation.json)。
测速工具新增开跑输入指纹与失败回执，当前相关宿主测试共 26 项通过；失败时
回执只列出已通过游戏干扰检查及结果对照的样本，不将原始子进程输出视为通过。

后续单实例实机接入已发现并修正保存副本权限问题：原先 `0400` 暂存文件的权限
被控制器 `copy2` 带入实例，导致 `WorldGen.CanLoad` 报权限拒绝，随后误走世界
生成分支。原始存档未改动；私有测试副本改为 `0600`，内容指纹保持不变。
准备工具和相关宿主回归均已修正，实机重试仍需单独验收。
当前修正后的重试被单实例容量预检拒绝，两个已启动的无头进程均已正常退出，
该测试组的 Steam launcher 也已停止；没有控制其他任务的游戏。详见
[实机尝试与权限修复记录](conformance/results/linux-744825-notification-dedup-v2-runtime-attempts.json)。

### 无头实例的单次短步

加载并暂停独立实例后，使用 `conformance/step_instance.py` 进行不超过两秒的
原生有界推进。工具先检查准备记录与实际组配置，再核对加载的托管/原生库；
响应与最终状态必须属于同一 PID、运行标识、精确存档，且时钟前进并最终暂停。
失败后的暂停清理仍持有该实例的控制锁；不同实例使用独立的锁，可以并行测试。
身份变化时保留失败信息。

```sh
rtk proxy python3 mods/CycleTrim/OniSim/conformance/step_instance.py \
  --harness tools/OniHarness --group-root /tmp/ct4-new \
  --stage mods/CycleTrim/OniSim/conformance/artifacts/ct4-new \
  --managed /staged/mod/CycleTrim.dll --native /staged/mod/OniSim/liboni_sim_core.so \
  --name guest1 --seconds 1 --first-headless \
  --output mods/CycleTrim/OniSim/conformance/artifacts/ct4-headless-step.json
```

`--first-headless` 还会检查图形实例没有启动记录，短步前后均检查，以记录
“先无头”的前置条件。后续驻留重复测试可省略该选项。工具不负责启动实例；
准备、加载或宿主测试成功均不能代替实际短步通过。它复用控制器的原生命令方法，
运行期间同一个实例由一个调用方控制，不同时从其他 CLI 对它发送游戏命令。

当前通用 companion 的实例命令仅有 pause、step、load、quit，尚无保存与
新保存文件的批准加载接口。因此多实例存读档往返仍待接入，不能用重载最初的
夹具文件冒充往返成功。具体交接见
[实例保存接口需求](conformance/INSTANCE-CONTROL-SAVE-HANDOFF.md)。

### 可选 OniMcp 验收配置

准备时增加 `--mcp-package dist/OniMcp.zip`，可将已有 OniMcp 的运行文件冻结到
该测试组。每个实例生成私有 `OniMcpConfig.json`，使用各自声明的端口、loopback
地址及不同凭据，文件权限为 `0600`；不导入 ZIP 中的旧配置，不在回执中输出凭据。
这是 CycleTrim 的测试配置，不改变发布包依赖。

当前 `/tmp/ct-mcp-dedup2-20261021` 使用 `4e677de1` 原生候选，声明的端口为
30140–30143。准备与宿主检查通过，启动被内存预检拒绝，没有启动游戏。
见 [MCP 配置准备记录](conformance/results/linux-744825-dedup-v2-mcp-preparation.json)。

`conformance/mcp_roundtrip.py` 复用已有 `runtime.py` 的游戏侧停止租约和正常 MCP 保存/加载接口，先校验
实例归属、实际加载文件、精确测试存档和原生暂停状态。只操作该 profile 内新建
的测试保存文件，并记录保存时钟、文件指纹与重载后的时钟。这条 MCP 验收路径
已经通过本机三复制人存档的无头实机检查；通用实例控制器的保存接口仍待接入。

```sh
rtk proxy python3 mods/CycleTrim/OniSim/conformance/mcp_roundtrip.py \
  --harness tools/OniHarness --group-root /tmp/ct-mcp-dedup2-20261021 \
  --stage mods/CycleTrim/OniSim/conformance/artifacts/dedup2-mcp-runtime-20261021 \
  --managed /staged/mod/CycleTrim.dll --native /staged/mod/OniSim/liboni_sim_core.so \
  --name guest1 --seconds 1 --first-headless --release-native-pause \
  --output /private/acceptance/headless-roundtrip.json
```

`--release-native-pause` 仅用于已检查的测试存档，默认不启用；游戏侧停止期限始终
保留。脚本先推进并保存新检查点，再推进到更晚时钟，最后重载并要求时钟回退到
保存值，随后再次推进。旧场景仅改存档路径不能满足这项验收。
失败时保留回执并核对暂停，同一实例仍用独立控制锁。凭据仅传入子进程环境。
当前相关宿主测试共 39 项通过，其中 API 替身不能代替实际游戏运行。

`4e677de1` 的三次实机短步分别推进约 0.95、1.20、0.95 原生秒，保存时钟
51.56664276123047，重载时钟精确相同。图形测试组 `/tmp/ct-gfx-dedup2-20261021`
从这个新检查点准备，增加已验证构建的只读纹理探针，仅为图形成员启用探针参数；
准备可传 `--texture-probe-receipt /path/to/receipt.json`。图形启动目前被内存预检
拒绝，尚未通过。见 [实机回执](conformance/results/linux-744825-notification-dedup-v2-runtime.json)。

原生对照测试另可用 `run.py --jobs 2`；默认仍为单任务以控制内存。当前单、
四线程的完整对照各通过 224 项、8440 次发布比较，相关宿主测试共 30 项通过。
这些独立原生子进程不能计为四个完整游戏实例。见
[并发原生测试记录](conformance/results/linux-744825-parallel-fixture-runner.json)。

## 执行顺序

1. 不启动游戏的原生对照用例先运行；不同用例使用独立子进程、世界和输出目录。
2. 先完成 CycleTrim 无头实机检查：加载、短步推进、保存、重载、再推进、最终暂停。
3. 无头检查通过后，再运行有画面实例，检查地图显示与纹理上传。
4. 先确认两个实例的隔离和并发，再扩到一个有画面实例、三个或更多无头实例。
   四实例验收要求四个不同的存活 PID、同时加载的世界及重叠的模拟推进区间。

每个实例都加载 CycleTrim，使用同一只读源存档的独立副本；记录实际加载的托管 DLL、
原生库哈希及线程数。控制目录、日志、偏好、Mod 配置、存档和输出目录分别隔离。
实例名称 host/guest 仅用于控制器标识，CycleTrim 本身不提供联机功能。

## CycleTrim 专项断言

- 所有无头实例均为 batch mode、Null 图形设备、零相机渲染回调。
- 每个实例的原生时钟确实前进，步骤完成后暂停；存读档前后核对保存的时钟和世界。
- 更新 CycleTrim 后只重启选定实例，核对新文件确实加载；其他实例 PID 与控制状态保持独立。
- 单线程和多线程计算结果继续使用原版库对照验证；不同游戏实例之间不要求自然运行状态逐位相同。
- 异常、超时、加载失败和缺少观测均保留为失败，不能用启动成功代替行为验收。

## 内存与性能

并发测试用于增加覆盖、检查隔离和重启行为。测量 CycleTrim 加速比例时，只运行一个
测量实例，固定存档、模拟推进量、线程数和预热，交替运行基线与候选。
多实例争用 CPU 或换页时的吞吐量另行报告。

优先共享只读游戏文件；无头实例关闭图像采集和纹理探针，不持续导出全地图。
先测两实例 PSS、swap 和大缺页，再决定是否扩容；PSS 用于避免重复计算共享页。
复用已加载进程测试同一版本；DLL 或原生库变化后有序退出再启动，不能视为热加载。
内存不足时明确报告未达到四实例要求，不能将串行运行记为并发通过。

## 与底座开发的交接

CycleTrim 需要：每实例独立的构建产物部署与加载哈希、实例级线程设置、
无头组先验收再加入图形实例、单实例重启、资源采样和机器可读回执。
底座如暂不支持这些接口，先保留接入缺口，不直接修改另一位 agent 正在编辑的文件。
任何实际游戏启动都必须经 Steam；不操作其他任务拥有的游戏会话。

### 通知去重 V2 图形验收完成

2026-10-21，先无头完成正常存读档，再用该检查点启动 OpenGLCore 图形实例。
图形模式实际推进、存档、推进后重载回退时间、重载后推进全部通过。
重载前后五组完整属性纹理在原生端、Unity CPU 和 GPU 一致；
关闭本测试 profile 的 Mod 提示后，检查实际 Unity 截图，未观察到显示损坏。
实例已正常退出，控制器确认没有遗留游戏进程。
具体边界见 `conformance/results/linux-744825-notification-dedup-v2-runtime.json`。
这仍不是整局性能或四实例同时运行的验收。

### 最新热传导修正的验收准备

`c301cc3e…` 已在 `/tmp/ct-thermal-order-20261021` 的双工作线程
无头实例通过实际存读档往返；保存、继续推进、重载时钟回退和重载后推进
均通过，随后正常退出。其检查点用于另一个全新四工作线程测试组
`/tmp/ct-thermal-four-20261021`。四工作线程组的无头存读档往返随后也通过，核对了四个实际工作线程，
并正常退出。其检查点用于图形组 `/tmp/ct-thermal-graphics-20261021`。
各组均保持一图形加三无头的声明，尚未同时运行四个游戏，不能当作并发验收。
