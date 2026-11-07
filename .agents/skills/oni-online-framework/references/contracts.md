# OniOnline 合同与源码地图

这份参考只在修改对应模块时读取，避免每次都展开整个仓库。

## 分层

| 层 | 主要路径 | 责任 | 不应承担 |
| --- | --- | --- | --- |
| 独立 Framework/SDK | `sdk/OniOnline.Framework/`、`mods/OniOnline/Sdk/`、`mods/OniOnline/Gameplay/` | 玩法注册、认证消息、状态频道、存档槽、准入合同 | Unity 对象、原生写入、DLC 判断 |
| 网络会话 | `mods/OniOnline/Network/` | RoomSession、能力协商、聊天、重连、速率/预算边界 | 直接创建世界或替代原生保存 |
| 模组同步 | `mods/OniOnline/Runtime/NativeModCatalog*`、`SteamModContentSync*`、`Sdk/ModSync*` | 入场清单、来源/版本/摘要、加载状态、运行中状态 | 自动公开 Local/Dev 模组、把下载回调当成加载 |
| Gameplay/世界 | `mods/OniOnline/Multiplayer/`、`Runtime/OnlineRuntime.cs` | 玩法声明 `onionline.worlds.v1` 后的星球登记、授权观察、状态路由、原生能力门。默认聊天不启动这些服务 | 通过消息直接修改其他星球；把一人一星球重新注册成框架默认玩法 |
| Native/UI | `mods/OniOnline/UI/`、`Patches/` | 原生控件、入口分流、HUD 通讯、加入/失败对话框 | 网页式面板、在开局前展示 HUD |

## 关键合同

- `GameplayFramework.Bind` 是适配器所有权边界。替换或解绑时旧 `GameplaySession` 必须失效，插件在 `SessionChanged` 中重新读取 `Current` 并释放旧订阅。
- `GameplaySession.Ready`、`ModStateSession.Ready` 和 `ModSyncSession.IsReady` 是不同层级的准入条件；不能用“房间已连接”绕过模组对齐或星球授权。
- `GameplaySession.LocalEnvironment`、`HostEnvironment` 和 `EnvironmentFor(playerId)` 只描述已认证端的游戏构建、DLC、CycleTrim 和能力合同。它们不授予权限；断线、重连等待、离开、撤销席位或关闭后不得继续使用旧快照。
- `AdmissionGenerationFor(playerId)` 是本端准入代次，不是玩家跨存档身份。代次变化要撤销旧发布者、订阅、relay 和待处理请求。
- 未知状态 schema 要保留最后正确状态并隔离新数据；不能把未知数据当成空状态写回存档。
- `NativeAdapterGuard` 只允许已登记游戏构建和能力进入原生调用。未知构建返回 `native_adapter_unavailable`，不读取、不写入、不破坏现有存档。
- `NativeChatEntry` 的入口属于殖民地 HUD；它只在 `NativeGameplayUi.Ready` 且联机会话存在时出现。重连期间保留入口，窗口显示重连状态并禁用发送。
- 大厅和聊天是框架能力。默认玩法是 `onionline.chat`。浏览页可以创建聊天房间。星球开局和星图只属于声明 `onionline.worlds.v1` 的玩法模组。
- `onionline.shared-simulation.v1`、`onionline.strict-admission.v1`、`onionline.presence.v1`、`onionline.shared-clock.v1`、`onionline.entity-identity.v1` 和 `onionline.anim-sync.v1` 不在默认会话里。共用模拟的快照确认、内容哈希、严格差异清单、光标与框选工具标记、共享暂停、跨进程确定实体 ID 映射与动画分片对齐均由对应玩法声明后再启用。共享暂停还要同时有共用模拟。聊天不因构建号、DLC 或模组清单不同而失败。

## 设计变更时的检查

1. 是否改变了 wire/schema、generation、manifest 或存档格式？如果是，先加兼容/拒绝用例，再改生产代码。
2. 是否把游戏版本、DLC 或 CycleTrim 重新变成硬相等门槛？除非新增明确能力合同，否则不能这样做。
3. 是否让远端消息直接触碰 Unity/原生对象？把它改成经过身份、权限、世界 epoch、准入代次和幂等请求的本地意图。
4. 是否让 UI 在菜单、创建、载入或初始复制人选择期间出现？检查 `NativeGameplayUi.Ready` 和场景取消路径。
5. 是否扩大了实机结论？更新验证矩阵，不要把宿主 doubles 或包哈希写成真实 ONI 通过。
6. 是否修改或新增了框架暴露的类型、方法、契约或底层能力？如果是，必须同步维护 `mods/OniOnline/docs/SDK.md`、`mods/OniOnline/docs/FRAMEWORK-COMPATIBILITY.md` 等对应接口文档，确保公开 API 与文档绝对一致。
