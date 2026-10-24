# PlanetSignals：玩法与模组状态同步示例

这是独立编译的示例模组。它引用公开的 `OniOnline.Framework.dll`，不链接框架
源码，也不会在构建时自动安装到游戏。

示例沿用「每位玩家控制一颗星球」的准入规则，并增加一个由房主维护的共享
信号计数器。它用于展示 SDK 的完整调用流程，不增加游戏界面，也不修改原生
世界或复制人。

## 构建

先生成公开 SDK，再编译示例：

```sh
dotnet build sdk/OniOnline.Framework -c Release
dotnet build examples/OniOnline.PlanetSignals -c Release
```

项目需要本机《缺氧》托管程序集路径 `OniManagedPath`。仓库已有本地配置时
直接使用；外部开发者可显式传入游戏路径：

```sh
dotnet build examples/OniOnline.PlanetSignals -c Release \
  -p:OniManagedPath="/path/to/OxygenNotIncluded_Data/Managed"
```

输出为 `bin/Release/net48/OniOnline.PlanetSignals.dll`。示例程序集由 ModInfo
在启动时注册并选择 `example.planet-signals` 玩法；所有参与者都需要安装相同
玩法版本，并在进入星系前完成模组对齐。安装和实机验收应使用项目正常的模组
部署流程，本示例构建不会执行这些操作。

## 接入自己的界面或操作

`ModInfo.State` 是当前会话的 `StateExample`。可以把以下调用放到自己设计的
游戏按钮或明确的玩家操作中：

```csharp
if (ModInfo.SendSignal()) {
    // 请求已经交给传输层。真正的结果由 State.Changed 通知。
}

ModInfo.State.Changed += () => {
    if (ModInfo.State.HasCurrentState) {
        long count = ModInfo.State.Count;
        // 更新自己的界面；示例不创建额外 HUD。
    }
};
```

`Count` 可以保留上次读到的值，只有 `HasCurrentState` 为真时才表示当前有效
状态。界面销毁时应移除自己注册的 `Changed` 处理器。需要主动重新请求描述
信息时调用 `State.RequestState()`；它不会重发信号操作。

如果玩法需要根据宿主环境选择降级路径，可以读取只读快照：

```csharp
var host = GameplayFramework.Current?.HostEnvironment;
bool hostUsesSpacedOut = host?.SpacedOut == true;
string hostBuild = host?.GameVersion ?? "";
// 具体功能仍检查 host.Provides(capability) 和当前会话 Ready。
// GameVersion、CycleTrimVersion 和 DLC 状态本身不构成准入条件。
```

快照只在当前认证端仍有效时返回；断线、重连或准入代次变化后，应重新从
`GameplayFramework.Current` 读取会话和环境，不要缓存旧快照。

## 数据如何同步

1. 框架提供 `GameplaySession` 后，`StateExample` 注册三个认证消息频道。
   等待 `Ready` 后，房主创建计数器 publisher，并明确允许当前已准入参与者
   观察；普通参与者发送带随机请求 nonce 的描述信息请求。
2. 房主通过 `SendToPlayer` 返回会话 ID、真实权威玩家 ID、publisher epoch
   和 schema。接收端校验认证消息发送者确实是 roster 中的房主、authority
   等于该发送者、会话与请求 nonce 均匹配，然后才调用 `ModStates.Subscribe`。
   客户端不会猜测 epoch，也不会从陌生消息接受新权威。
3. 玩家每次明确调用 `SendSignal` 才发送一个新操作 nonce。房主对最近 256
   个操作去重，校验后发布一个计数增量。此去重窗口只是示例，生产业务应按
   自己的重试和持久化要求设计操作凭据。
4. `ModStates` 负责快照、增量、重复、缺口恢复和分片。纯 `CounterReducer`
   只验证/计算八字节计数快照和单字节增量，不调用游戏 API。
5. `Ready` 失效时销毁旧 publisher registration 和 watch；重新就绪后重新
   获取认证描述信息，并由 SDK 创建新 generation。框架不会自动重放信号操作。

`StateExample.Dispose()` 解除所有消息订阅、会话回调、publisher 和 watch。
`ModInfo` 在 `GameplayFramework.SessionChanged` 时先释放旧实例再连接新实例；
框架销毁会话时也会清理传输服务。代码不调用框架内部 API，也不自行 Pump 或
Dispose 框架拥有的 `ModStates`。

## 边界

计数状态只在当前会话实例内存中维护，没有写入游戏存档。接受字节状态不代表
原生对象已执行对应操作。实际玩法仍须提供自己的事务式原生适配和持久化。

完整合同与资源限制见 [模组同步 SDK](../../mods/OniOnline/docs/SDK-MOD-SYNC.md)。
独立程序集编译和内存房间测试不能替代 Steam 启动后的多客户端实机验收。
