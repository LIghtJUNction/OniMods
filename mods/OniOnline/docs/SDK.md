# OniOnline SDK 1 开发预览

OniOnline 是联机框架。内置玩法只有 `onionline.chat`：它可以加入大厅并聊天，不登记星球。其他玩法使用同一个 `IGameplayRules` 接口，由独立模组注册。

SDK 的发布载体是独立的 **OniOnline.Framework.dll**，目标为 .NET Standard 2.0，不引用 Unity、游戏程序集或 PLib，命名空间为 `OniOnline.Sdk`。官方 NuGet 包是 `LIghtJUNction.OniOnline.Framework`，当前可安装版本是 **0.4.0**。仓库源码已经是 0.5.0，NuGet 上还没有 0.5.0。开发者通过 NuGet 引用已构建的包，不复制或重新编译框架源码，**严禁在自己的模组产物中重复打包 OniOnline.Framework.dll**。这个 API 仍处于开发预览，没有承诺后续二进制兼容性。

## 第三方 Mod 接入规范（NuGet 依赖与前置 Mod 声明）

### 1. 编译期：引入官方 NuGet 包
在您的玩法 Mod 项目中，通过 NuGet 引入独立框架：

```bash
dotnet add package LIghtJUNction.OniOnline.Framework --version 0.4.0
```

或在 `.csproj` 中声明：
```xml
<ItemGroup>
  <PackageReference Include="LIghtJUNction.OniOnline.Framework" Version="0.4.0" PrivateAssets="all" ExcludeAssets="runtime" />
</ItemGroup>
```
> [!IMPORTANT]
> 必须设置 `ExcludeAssets="runtime"`（或 `Private="false"`），确保您的 Mod 产物 ZIP/目录中**不包含** `OniOnline.Framework.dll`，完全由运行时宿主 OniOnline 提供。

### 2. 运行期：声明 OniOnline 为前置 Mod
- **Steam 创意工坊**：在您的 Mod 工坊页面右侧【所有者控制】中，点击【添加/移除必需的项目】，将 **OniOnline** 标记为必需前置。
- **游戏内 Mod 列表**：提示玩家确保 **OniOnline** 处于已启用状态，且排在您的玩法 Mod 前面加载。

### 3. 玩法管理与配置菜单
在模组加载（`UserMod2.OnLoad` 或 `OnAllModsLoaded`）时，调用 `GameplayFramework.Register(new MyGameplay())` 完成注册。
- 注册后的玩法会自动出现在 OniOnline 游戏内选项界面的 **【🎮 联机玩法管理】** 中。
- 玩家在【🎮 联机玩法管理】里看到已注册玩法。勾选表示启用，取消勾选表示禁用。点确定后立即生效，不用重启游戏。已经加入的房间继续原来的玩法；下一次创建或加入使用仍勾选、且上次保存过的那一项，若该项已取消勾选，则改用列表里剩下的第一项。
- 配置界面中提供 **【📖 联机玩法开发指南】** 按钮，点击可一键在系统浏览器中打开本文档。

## 最小调用

在模组加载期间注册玩法，连接之前选择。`Id` 是稳定的英文标识，`Version` 是网络规则版本，不是界面显示名。所有参与者需要装同一版本。

```csharp
GameplayFramework.Register(new MyGameplay());
GameplayFramework.Select("example.my-gameplay");
GameplayFramework.SessionChanged += OnSessionChanged;
```

实现 `IGameplayRules.ValidatePlanetAdmission`。返回 null 接受；返回简短错误键拒绝。候选玩家身份由传输层提供，星球清单是防御性副本。回调只校验，不创建原生对象。框架始终保留稳定星球身份的唯一性约束，玩法不能通过放行回调制造重复的星球身份。聊天玩法直接拒绝登记。`Gameplay/OnePlanetPerPlayer.cs` 是可复用的一人一星球校验，框架不自动注册；玩法模组调用 `OnePlanetPerPlayer.RegisterIfMissing()`，并在能力合同中提供和要求 `GameplayFramework.WorldsCapability`（`onionline.worlds.v1`）。只有声明了这个能力，适配器才会启动星球登记和世界同步。

玩法如果需要额外的联机能力，可以同时实现 `IGameplayCapabilityContract`：

```csharp
public sealed class MyGameplay : IGameplayRules, IGameplayCapabilityContract
{
    public string Id => "example.my-gameplay";
    public string Version => "1";
    public IEnumerable<string> Capabilities => new[] { "example.rules.v1" };
    public IEnumerable<string> RequiredCapabilities => new[] { "example.rules.v1" };
    public string ValidatePlanetAdmission(PlanetClaim candidate, IReadOnlyList<PlanetClaim> current) => null;
}
```

这些能力会和适配器的原生能力、模组同步能力合并后参与双向协商。`Capabilities`
表示本端提供的合同，`RequiredCapabilities`表示要求对端提供的合同；后者可以是
只由远端提供的能力。能力 ID 必须符合框架规则，重复值会被规范化。玩法不能用
能力合同绕过模组对齐、准入权限或业务参数校验。

```csharp
static IDisposable commands;
static void OnSessionChanged()
{
    commands?.Dispose();
    commands = null;
    var session = GameplayFramework.Current;
    if (session == null) return;
    commands = session.Subscribe("example.signal", message => {
        if (!session.IsHost) return;
        // message.SenderId 由框架认证，不能从 Payload 中读取或替代。
        // 验证自己的业务参数、权限、请求编号，再调用自己负责的游戏逻辑。
        session.SendToPlayer(message.SenderId, "example.reply", new byte[] { 1 });
    });
}
```

`SendToHost` 用于提交请求；只有主人可以 `SendToPlayer` 回复。SDK 不自动执行、转发或广播自定义指令，也不赋予调用者其他玩家星球的操作权。`Changed` 表示当前会话或星系快照发生变化。连接、重连和离开期间先检查 `Ready`。

## 模组对齐与共享状态

未知模组默认需要双方一致。仅影响本机显示的模组可以在加载时调用 `ModSyncDeclarations.Register(modId, ModSyncPolicy.ClientOnly)`；可选内容使用 `Optional`。这是本机模组代码的声明，远端不能替本机登记政策。启动模组全部加载完后，适配器固定本次进程的声明和受控内容清单。未完成双向清单校验时，`Ready` 为 false，玩法消息、星球登记和原生观察都不放行。

如果模组把联机相关资源放在内置摘要扩展名之外，可以在第一次清单快照前声明扩展名：

```csharp
ModSyncDeclarations.RegisterContentExtensions("example.my-mod", new[] { ".planetdata", ".atlasx" });
```

扩展名必须是一个点号开头、后面只含 ASCII 字母、数字、连字符或下划线的单一后缀；多段后缀和路径分隔符会被拒绝。扩展名声明只影响受控模组包的内容摘要，不会上传或读取用户配置、存档、凭据和日志；私有目录仍会被排除。声明和模组政策在快照后一起冻结，远端清单不能修改本机声明。没有声明的自定义文件不会被摘要纳入，发布前应为会影响玩法的资源补上声明。

`session.ModStates` 是框架管理的状态传输服务，不需要插件自行 Pump 或 Dispose。用 `session.SessionId` 创建绑定，并实现纯字节的 `IModStateReducer`。发布者必须明确观察权限；接收方先通过已认证消息获得权威身份、epoch 和 schema，再创建订阅。快照最多 64 KiB，支持增量和缺口恢复；未知 schema 保留最后正确检查点并隔离新数据。传输不会自动执行原生方法，也不会重放业务操作。

`session.AdmissionGeneration` 是本端当前准入代次，未就绪时为零。`Changed` 也会通知代次变化。即使两次检查都得到 `Ready == true`，代次变化仍要求释放旧的发布和订阅，重新取得描述信息；示例包含这条处理。这个数字只用于本端失效检查，不能用作跨玩家身份。

`session.LocalEnvironment` 和 `session.HostEnvironment` 是当前会话的只读环境快照；`session.EnvironmentFor(playerId)` 只返回本端、主机或主机可见的已认证成员。快照中的游戏构建、DLC 和 CycleTrim 版本是适配线索，不是准入条件；具体功能仍要检查 `Capabilities`、`RequiredCapabilities`、`Ready` 和模组同步状态。断线或准入代次变化后，重新读取快照，不要缓存旧对象。

## 共用模拟的可选底层能力

默认聊天不声明这些合同。玩法要在 `IGameplayCapabilityContract` 里自己加入，双方协商到之后才能启用：

| 合同 | 类型 | 作用 |
| --- | --- | --- |
| `onionline.shared-simulation.v1` | `SnapshotAdmission`、`ContentDigestBoard` | 权威端先暂停出快照，快照带编号、校验和、指纹。客户端加载后必须回报同一份指纹。客户端等待 90 秒，权威端总期限 180 秒，5 秒没有新字节记一次停滞。之后客户端按块上报哈希；少数不一致返回要补的块，多数不一致要求重发快照，未知快照隔离且不抹掉已发布哈希。 |
| `onionline.strict-admission.v1` | `StrictAdmission` | 比较玩法版本、游戏构建号、已启用 DLC 和其他模组的 ID 与版本。最多列出 10 条差异。未协商这个合同时，构建号不同仍然可以进大厅。 |
| `onionline.presence.v1` | `PresenceBoard` | 实时同步光标位置（`PresenceCursor`）、地图标记（`PresenceMark`）、拖选工具预览（`ToolDragPresence`：挖掘、建造、擦水等框选与优先级）、以及地图高亮交互信号（`PlayerPing`）。未协商时输入被丢弃。 |
| `onionline.shared-clock.v1` | `SharedClockPolicy` | 必须同时协商共用模拟。任何参与者可以暂停，速度取更新的修订号，只接受 1 到 3。 |
| `onionline.entity-identity.v1` | `DeterministicNetId`、`NetworkIdentityMap<T>` | 跨进程确定的 64 位实体网络映射。静态建筑与格子由 64 位 FNV-1a 强哈希确定生成（避免 .NET string.GetHashCode 跨进程随机化）；动态实体由主机/席位段和流水号划分，杜绝线性探测冲突。提供防 ABA 复用的死亡墓碑缓冲与双向 O(1) 实体查找。 |
| `onionline.anim-sync.v1` | `AnimReconciler`、`AnimSyncSharder` | 动画对齐与增量调速。检测动画名称/哈希不符时强制重播（Replay），检测小范围时间漂移时通过推荐微调速度（NudgeSpeed）平滑收敛，避免关键帧硬跳动；大漂移强制校正（SnapTime）。分片器将大量动画实体的同步轮询平摊到多个帧中，消除瞬时流量洪峰。 |

## 接口详细参考 (API Reference)

### 1. 跨进程确定性实体网络标识 (`DeterministicNetId` 与 `NetworkIdentityMap<T>`)

声明能力：`onionline.entity-identity.v1`

解决跨客户端同步中实体标识冲突、跨平台/跨进程哈希不一致以及对象生命周期重建时的 ABA 错位问题。

- **静态实体 ID 生成**：
  - `DeterministicNetId.ForBuilding(int worldId, int cell, string prefabId, int layer)`：利用 64 位 FNV-1a 强哈希结合世界 ID、网格单元格号、Prefab 标识与所在建筑层计算。返回正数（最高位为 0）。不同客户端输入相同的世界与建筑参数，计算出的 ID 严格一致。
  - `DeterministicNetId.ForCell(int worldId, int cell, string subCategory)`：计算网格单元及子分类（如挖掘、涂抹等）的确定性静态 ID。
- **动态实体 ID 生成**：
  - `DeterministicNetId.ForDynamic(byte peerId, uint sequence, ushort typeTag)`：最高位（bit 63）置 1，高 7 位存储生成席位 ID（0..127），中 16 位存储类型标记（TypeTag），低 32 位存储单调递增流水号。确保动态生成的实体（如掉落物、杂物、临时投射物）在各客户端间天然命名空间隔离，零碰撞。
  - 辅助解析：`IsDynamic(long id)`、`IsStatic(long id)`、`GetPeerId(long dynamicId)`、`GetTypeTag(long dynamicId)`、`GetSequence(long dynamicId)`。
- **实体映射表与生命周期管理 (`NetworkIdentityMap<T>`)**：
  - 双向 O(1) 查找：`TryRegister(long netId, T instance)`、`TryGetEntity(long netId, out T instance)`、`TryGetId(T instance, out long netId)`。
  - 防 ABA 墓碑机制：当实体销毁注销时调用 `Unregister(long netId)`，ID 不会立即回收，而是进入带时间戳的墓碑缓冲区（默认保留 10 秒）。在此期间任何迟到的网络包试图访问该 ID 都会被识别为 `IsTombstoned(netId)` 或 `IsTombstoned(netId, now)` 并安全丢弃，避免实体销毁重建后旧网络包作用到新实体的 ABA 隐患。调用 `PurgeExpiredTombstones(now)` 可清理过期墓碑，`Clear()` 重置全部映射与墓碑。

### 2. 玩家光标与交互态共享 (`PresenceBoard`)

声明能力：`onionline.presence.v1`

负责多玩家协作时的视口焦点、工具拖选交互与战术高亮信号呈现。

- **光标位置与视口 (`PresenceCursor`)**：
  - `PlayerId`, `WorldId`, `X`, `Y`：记录玩家当前鼠标所在世界与网格坐标。
  - `TryCursor(PresenceCursor cursor)`、`CursorFor(string playerId)`、`Cursors`（获取全部活跃光标数组）。
- **拖选工具预览 (`ToolDragPresence`)**：
  - 支持多玩家正在进行中的交互框选预览（如挖掘框、拆除框、擦水框、电线/管道拉线）。
  - 包含字段：`PlayerId`, `WorldId`, `ToolId`（如 "dig"、"build"）, `Mode`（`ToolDragMode.Box`, `Line`, `Point`）, `StartX`, `StartY`, `EndX`, `EndY`, `Priority`（1..9 优先级）, `MaterialTag`, `ColorRgba`。
  - `TryToolDrag(ToolDragPresence drag)`、`ToolDragFor(string playerId)`、`ClearToolDrag(string playerId)`、`ToolDrags`（获取全部活跃拖选数组）。
- **战术标记与高亮信号 (`PresenceMark` & `PlayerPing`)**：
  - `PresenceMark`：带递增序号的地块标记，供多端同步静态指示。
  - `PlayerPing`：带 `Category`（如 "alert"、"look_here"）与 `Timestamp` 的瞬时高亮脉冲，队列自动轮转，上限 32 个。
  - 玩家断开离开时通过 `Drop(playerId)` 清理单人状态；退出房间或重连时调用 `Clear()` 一键清空全量板载状态。

### 3. 动画增量对齐与分片 (`AnimReconciler` 与 `AnimSyncSharder`)

声明能力：`onionline.anim-sync.v1`

解决多人共享殖民地中复制人和生物动画随网络延迟发生微小相位漂移或错位的问题。

- **状态快照 (`AnimSnapshot`)**：
  - 包含 `EntityId`, `AnimHash`（动画名称哈希）, `PlayMode`（单次/循环）, `Speed`（播放速度）, `ElapsedTime`（当前已播放秒数）, `Timestamp`（采集时间戳）。
- **智能平滑校正决策 (`AnimReconciler.Evaluate`)**：
  - 若两端播放的动画不同（`AnimHash` 不符）或播放模式不同（`PlayMode` 不符）：返回 `AnimCorrectionAction.Replay`，强制重置为权威端目标动画与播放模式。
  - 若动画一致，根据时延对齐计算期望播放时间后得到绝对时间漂移 `drift`：
    - 漂移 `<= driftTolerance`（默认 0.15s）：返回 `AnimCorrectionAction.None`，无需任何干预。
    - 小范围漂移（`driftTolerance < absDrift <= maxNudgeDrift`，默认上限 0.6s）：返回 `AnimCorrectionAction.NudgeSpeed`，平滑微调播放速度（慢了加速 1.15 倍，快了减速 0.85 倍）。肉眼无感知平滑追赶，消除关键帧突变闪烁。
    - 严重脱节（`absDrift > maxNudgeDrift`）：返回 `AnimCorrectionAction.SnapTime`，硬跳至权威时间点。
- **实体分片减负 (`AnimSyncSharder`)**：
  - `GetShard(long entityId, int totalShards)` / `IsEntityInShard(long entityId, int targetShard, int totalShards)`：基于 64 位实体 ID 的正数取模算法，将成百上千个动植物实体均匀分布到 N 帧（如 5 到 10 帧）中分批同步，彻底避免同帧广播造成的瞬时网络尖峰。

## 接口文档维护规范 (Documentation Maintenance Protocol)

维护 OniOnline 项目时，必须遵守以下规范：
1. **接口变更必同步**：凡是在 `OniOnline.Framework`、`OniOnline.Sdk`、`OniOnline.Network` 中新增、修改或弃用任何公开类型、方法、能力合同或数据结构时，必须在本文件（`SDK.md`）及关联契约文档（`FRAMEWORK-COMPATIBILITY.md`、`contracts.md`）中进行同步更新与详述。
2. **严禁脱节**：禁止只更新 C# 源代码而不更新接口文档，禁止把接口签名留空或伪造为已实现。
3. **参数与边界完整**：新增接口文档必须写清命名空间、声明能力键、参数含义、取值范围、异常与防御策略、线程安全边界以及示例用法。

`CommandInFlight` 按命令键去重：权威端答复、超时或准入代次变化之前，同一条命令不会再次发出。这些类型不冻结游戏模拟，也不替玩法拦截 `SimMessages`。模拟冻结仍由声明共用模拟的玩法自己实现；补丁贴不上时拒绝的是该玩法。

`TestSlotLayout` 给第二个本机客户端分配 `onionline-slotN/save_files` 和 `playerprefs`。`SessionLogPrivacy` 从报错文本里去掉 Steam ID、调用方给出的名字，以及以 `CHAT ` 或 `chat:` 开头的行，再写成 zip。

适配器离开或切换玩法时先解除当前绑定；框架会释放旧的 `GameplaySession`，再通知 `SessionChanged`。插件应在回调中丢弃旧订阅，并从 `Current` 取得新会话，不要缓存旧对象继续发送。

完整合同见[模组状态同步](SDK-MOD-SYNC.md)、[原生模组对齐](MOD-CONTENT-SYNC.md)和[版本与能力协商](FRAMEWORK-COMPATIBILITY.md)。

## 原生存档

```csharp
var session = GameplayFramework.Current;
var saved = session.ReadLocalState();
if (saved != null) { /* 按 saved.Schema 迁移自己的 saved.Payload */ }
session.WriteLocalState(1, myBytes);
```

状态槽使用当前玩法 ID，属于当前进程加载的原生殖民地；写入槽不会自动保存游戏。正常游戏保存时一并持久化，读档时恢复。一个槽最多 16 KiB，一份存档最多 16 个槽。其他玩法或已卸载玩法的数据会保留。未知容器版本、损坏数据保持原始内容并拒绝覆盖，开发者不能用一个空对象覆盖迁移失败的数据。

## 消息与生命周期

- 每包最多 4096 字节。接收侧每个已认证玩家每秒最多 32 包、64 KiB，超出直接丢弃。
- 频道名使用英文数字、点、下划线和连字符，最多 64 字符；同一会话不重复注册同一频道。
- 消息属于当前会话，不进入聊天历史或存档，不跨断线重放。业务请求要自行携带请求编号并实现幂等处理。
- 回调在 `RoomSession.Pump` 所在线程运行，游戏适配器使用 Unity 主线程。不要在回调中阻塞或直接访问后台线程的原生世界。
- 订阅返回 `IDisposable`；离开时旧会话与订阅全部失效。插件回调异常不会中止整个房间循环。
- `Players` 和 `System` 是副本。不要把其中的连接席位 ID 当成 Steam 账号或跨存档的玩家身份。

## 原生交互约束

第三方玩法应复用原生界面控件和导航，随原生页面显示和隐藏。不要在主菜单、新建世界、载入、初始复制人选择期间显示游戏 HUD，不要覆盖原生侧栏、建造栏和弹窗。通讯入口使用殖民地 HUD 的原生管理栏按钮和快捷键；星图操作在星图的原生星系菜单内。

## 目前没有的能力

SDK 不提供跨游戏进程创建世界、复制或搬迁复制人、火箭互访、离线代算、远程建造或任意原生方法调用。这些需要各自独立的原生适配、事务与实机验收，不能把网络消息或星球登记当成实现。内置玩法里玩家控制的是自己的原生殖民地，其他成员只能获得已经授权的观察信息。

本地验证：`dotnet run --project tests/OniOnline.Tests -c Release -- --sdk`。这个命令调用生产协议、规则和存档容器；不启动游戏，不能证明 Unity 生命周期、存读档或界面视觉已经验收。

## 独立开发者示例

仓库 `examples/OniOnline.PlanetSignals` 是独立程序集，通过引用打包后的公开 SDK 编译，而不是直接链接生产源码。先构建 OniOnline，再执行：

```sh
dotnet build examples/OniOnline.PlanetSignals -c Release
```

示例注册独立的玩法 ID，复用内置的星球准入规则，并实现实际共享的信号计数器，包含认证描述信息、发布权限和断线恢复。详见[示例说明](../../../examples/OniOnline.PlanetSignals/README.md)。它不会自动安装到游戏。
