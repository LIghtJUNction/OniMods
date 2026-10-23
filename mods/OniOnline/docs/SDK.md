# OniOnline SDK 1 开发预览

OniOnline 是联机框架，自带 `onionline.one-planet-per-player` 玩法。内置玩法和其他玩法使用同一个 `IGameplayRules` 接口。

SDK 的发布载体是独立的 **OniOnline.Framework.dll**，目标为 .NET Standard 2.0，不引用 Unity、游戏程序集或 PLib，命名空间为 `OniOnline.Sdk`。开发者引用已经构建的 DLL，不复制或重新编译框架源码，也不要在自己的模组中再打包一份 OniOnline.Framework.dll。这个 API 仍处于开发预览，没有承诺后续二进制兼容性。

## 最小调用

在模组加载期间注册玩法，连接之前选择。`Id` 是稳定的英文标识，`Version` 是网络规则版本，不是界面显示名。所有参与者需要装同一版本。

```csharp
GameplayFramework.Register(new MyGameplay());
GameplayFramework.Select("example.my-gameplay");
GameplayFramework.SessionChanged += OnSessionChanged;
```

实现 `IGameplayRules.ValidatePlanetAdmission`。返回 null 接受；返回简短错误键拒绝。候选玩家身份由传输层提供，星球清单是防御性副本。回调只校验，不创建原生对象。框架始终保留稳定星球身份的唯一性约束，玩法不能通过放行回调制造重复的星球身份。内置规则见 `Gameplay/OnePlanetPerPlayer.cs`：限制一名玩家在当前星系绑定一颗原生星球，允许重连及信息更新，拒绝重复星球和同源分支。

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

`session.ModStates` 是框架管理的状态传输服务，不需要插件自行 Pump 或 Dispose。用 `session.SessionId` 创建绑定，并实现纯字节的 `IModStateReducer`。发布者必须明确观察权限；接收方先通过已认证消息获得权威身份、epoch 和 schema，再创建订阅。快照最多 64 KiB，支持增量和缺口恢复；未知 schema 保留最后正确检查点并隔离新数据。传输不会自动执行原生方法，也不会重放业务操作。

`session.AdmissionGeneration` 是本端当前准入代次，未就绪时为零。`Changed` 也会通知代次变化。即使两次检查都得到 `Ready == true`，代次变化仍要求释放旧的发布和订阅，重新取得描述信息；示例包含这条处理。这个数字只用于本端失效检查，不能用作跨玩家身份。

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
