# 两个完整游戏客户端验收

`multiplayer_runtime.py` 控制两个**已经运行**的 ONI 测试客户端。
它不会启动游戏，不会启动 .NET 协议替身，不会创建或登录 Steam 账号。
实际操作通过只在测试构建中存在的 `RuntimeProbeMultiplayer.cs` 执行。
聊天使用正式 `RoomSession.SendChat`，接收证据来自正式 `RoomSession.Chat`。
只读观察使用同一游戏会话的正式 `WorldInterestSession` 和 `WorldCellDetailsSession`，
不启动协议替身，也不执行挖掘、建筑、人员指令或模拟推进。

## 准备两个客户端

1. 两台机器使用不同的 Steam 账号，分别拥有游戏和所需 DLC，并通过 Steam 启动游戏。
   默认 `--transport steam` 要求两个不同的账号和设备。账号 ID、设备 ID 不写入回执；
   探针用本次随机验收 nonce 加盐后计算 SHA-256。每次运行的结果不能作跨次身份标识。
2. 两端使用同一组明确版本的 OniOnline 探针 DLL、CycleTrim DLL 和游戏程序集。
   探针构建必须与普通构建使用不同的输出和中间目录。它不能打成正式 Mod 包。
3. 在每端创建一个新的绝对探针目录。在该目录下创建 `multiplayer-disposable`，
   将获准使用的一次性测试存档**复制**成 `test.sav`。不要移动私人存档，不要使用硬链接、
   符号链接或符号链接目录。使用普通复制的当前修改时间，记录副本 SHA-256。
4. 为各端的 Steam 游戏启动项加入 `--onionline-probe=/该端的绝对探针目录`。
   在游戏中加载上述副本，加入同一个只有这两名玩家的房间，暂停双方游戏。
   双方须各自登记一颗原生星球，选中的星球必须就是当前活动原生世界。
   两端的 PlanetId 和 LineageId 必须不同；复制同一个已带身份的殖民地不能当成两颗独立星球。
   使用没有预先授予对方 `ViewMap` 权限、没有进行中观察的测试房间。
   每颗星球都须有一个同时包含已探索格和未探索格的小视窗，缺少基线时测试拒绝开始。
   启动项本身不证明游戏已运行。采集器必须收到两端本次挑战的实际回复。
   必须使用新开的测试进程：多人探针会拒绝已有的原生工作、人员、导航、建筑、UI、
   生成或时间步进测试状态；已完成的旧测试夹具也不能复用。
5. 将两个探针目录以挂载或双向同步的方式提供给采集器。必须能够读状态、读存档副本，
   并写入各自的 `command.json`。两个目录不能指向同一个游戏。
   两台机器的 UTC 时间需要基本一致：状态不得超前采集器两秒或滞后十秒。

以下构建命令以已配置好 `OniManagedPath` 和依赖的环境为例：

```bash
rtk proxy dotnet build mods/OniOnline/OniOnline.csproj -c Release \
  -p:OnlineRuntimeProbe=true \
  -p:OutputPath=/tmp/onionline-probe-build/ \
  -p:BaseIntermediateOutputPath=/tmp/onionline-probe-obj/
```

使用**最终实际加载的 DLL**计算哈希；不要使用合并 PLib 前的中间 DLL。
游戏程序集哈希指向 `Assembly-CSharp.dll`。版本是实际运行的游戏 changelist，
不是程序集镜像的标注。探针还要求 Steam SDK 已初始化、账号拥有游戏，且原生世界已加载。

## 运行

以下哈希是占位符，必须替换为本轮实际文件的 64 位小写 SHA-256：

```bash
rtk proxy python3 tests/OniOnline.Tests/multiplayer_runtime.py \
  --first /mnt/oni-a/probe \
  --second /mnt/oni-b/probe \
  --output /tmp/oni-two-client-receipt.json \
  --transport steam \
  --game-version 744825 \
  --mod-sha256 ONIONLINE_PROBE_DLL_SHA256 \
  --game-assembly-sha256 ASSEMBLY_CSHARP_DLL_SHA256 \
  --cycletrim-sha256 CYCLETRIM_DLL_SHA256 \
  --first-save-sha256 FIRST_DISPOSABLE_SAVE_SHA256 \
  --second-save-sha256 SECOND_DISPOSABLE_SAVE_SHA256
```

默认存档相对路径为 `multiplayer-disposable/test.sav`。
回执必须为位于两个探针目录之外的 JSON 文件，避免覆盖游戏状态、命令或存档。
可以通过 `--first-save`、`--second-save` 指定该子目录下的其他 `.sav` 文件。
采集器绑定实际副本的哈希、长度和修改时间。探针要求当前活动存档就是该副本，
结束时再次计算内容哈希。采集器和此多人探针都不调用游戏保存，也不解除暂停。
存档副本未变化的结果不能用来证明游戏其他保存路径或其他 Mod 没有写文件。

默认操作全程保持双方暂停：

- 核对两个完整游戏进程、各自的运行 nonce、当前房间和不同的玩家 ID。
- 用新随机 nonce 要求双方探针在新的游戏帧中回复，拒绝旧状态和进程重启。
- 两端各发一条唯一聊天，包含本轮盐化账号哈希。
- 两端都必须恰好收到这两条聊天；发送者、序号、消息 ID 必须一致，待确认队列必须清空。
- 默认继续观察六秒，检查重试没有产生重复聊天，双方原生游戏时间没有变化。
- 观察期间两端都必须持续发布新帧：两次新发布之间的采集间隔不得超过两秒，
  原生发布时间跨度和采集器观察时长都必须达到完整六秒。反复读取尚未过期的同一份状态不能通过。
- 双方结束时仍需暂停，并且活动测试存档内容没有变化。

挂载或同步路径延迟超过两秒时会保守地拒绝通过，需要改善同步或直接挂载后重试。
控制器结束时尝试解除本轮探针的验收状态；若探针已经报错或失联，回执会明确要求人工检查，
不会声称清理成功。游戏时间始终由玩家的暂停状态保持，这个工具没有恢复模拟的命令。

`--transport tcp` 允许两个完整游戏在同一台机器上使用 TCP。
这时回执写明 `distinctDevices` 和 `distinctSteamAccounts`；它不构成两机 Steam P2P 验收。
无论使用哪种传输，普通 .NET 协议端都不能替代完整 ONI 进程。

### 双向只读观察

采集器在上述活体、存档与暂停保护下，先后测试 A 观察 B、B 观察 A。
源端从原生持久探索状态选择一个最多 16 × 12 的混合视窗，记录已探索格的视觉基线、
隐藏格索引和一个已探索格的温度、质量；不改变探索状态，不记录隐藏格的内容。

- 关闭源端共享：观察订阅必须被拒绝，视觉、实体和详情缓存均为空。
- 开启共享：远端接收的格子集合及元素、颜色须与源端基线一致；隐藏格不出现，
  默认视图不附带温度或质量。源端有实体时，也核对实体编号和坐标。
- 请求隐藏格详情必须被拒绝；已探索格详情须与源端原生温度、质量一致。
- 撤销共享：远端的三类缓存必须清空，源端的视窗和详情观察者均须释放。
- 每个方向的观察流量期间各发送一次原有生产聊天，再执行持续活体与恰好收到一次检查。

新增动作只存在于显式测试构建的 `multiplayer_*` permit 内，沿用实际运行 nonce、
本轮挑战、DLL/存档身份和暂停检查。合法动作刷新 120 秒空闲期限；结束或活体保护失败时，
探针停止本地订阅并恢复测试前的共享开关，明确报告清理错误。
双方暂停时只能验证实体通道和坐标，不能证明实体实际移动。
直接调用详情订阅也不能代替真实鼠标 hover 与观察窗口的视觉验收。

### 与 OniHarness 隔离实例配合

保持 Harness 的 `pause/load/step/quit` 和存档 allowlist 接口不变。
控制器已将各自批准的源存档复制到该实例私有 `save_files/OniHarness/test.sav`；
本轮全新 group 可把各自 `--onionline-probe` 设为该实例的私有 `save_files` 绝对路径，
并向采集器传 `--first-save OniHarness/test.sav --second-save OniHarness/test.sav`。
探针只额外接受这个专用相对目录；仍拒绝路径穿越、符号链接和改变的存档内容。
每轮使用全新 group 和探针目录，不将旧命令目录带入新进程。
先通过默认容量检查再启动两个完整游戏，不降低门槛；同账号本机 TCP 结果仍与两账号 Steam P2P 分开。

### 本地时间独立的边界

本工具不提供解除暂停或短时推进功能，`--verify-independent-time` 不受支持。
即使很短的推进也可能跨越原生自动保存边界；在保存入口的完整保护尚未验证前，不开放该操作。
暂停聊天只能证明双方在此窗口保持暂停，不能证明一端运行时另一端的时间仍然独立。

## 回执和证据边界

回执仅在两端完成本次活体挑战、版本/世界/房间/存档/身份校验后，
才写 `completeGameInstances: 2`。未满足这些条件写 `blocked` 和 `0`。
已确认两端真实参与、但后续聊天或连续活体观察失败时写 `failed`，并保留故障边界。

通过只证明本轮明确记录的暂停聊天及双向只读观察场景。
不证明独立时间推进、自动保存、断线恢复、进程重启、存读档、整颗星球迁移、复制人互访或房主接替。
工具不会自动发送结果给其他人，也不会上传游戏文件。

`test_multiplayer_runtime.py` 使用人工输入验证采集器的拒绝条件。
**这些测试通过不属于真实多人联机通过**：

```bash
rtk proxy python3 -m unittest discover \
  -s tests/OniOnline.Tests -p 'test_multiplayer_runtime.py' -v
```

2026-10-21 本轮开发环境没有可用的 Steam 客户端、授权会话或正在运行的 ONI 探针。
其 cgroup 总内存上限为 8 GiB；现有两完整游戏的容量预算为 11 GiB。
因此本轮没有执行两机 Steam 或两个完整游戏验收。工具的实际无探针调用应记录
`blocked`、`completeGameInstances: 0`、`protocolClientProcesses: 0`，不能把预置 JSON 当作活体通过。
