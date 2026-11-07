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
- 玩家可以在配置菜单中自由启用/禁用特定玩法、切换首选房间规则。
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
| `onionline.presence.v1` | `PresenceBoard` | 每个玩家只保留最新光标，地图标记要求序号递增。未协商时输入被丢弃。 |
| `onionline.shared-clock.v1` | `SharedClockPolicy` | 必须同时协商共用模拟。任何参与者可以暂停，速度取更新的修订号，只接受 1 到 3。 |

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
