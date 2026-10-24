# 框架兼容边界

`OniOnline.Framework.dll` 是不引用 Unity、ONI、PLib 或 DLC 的 .NET Standard 2.0 程序集。游戏适配器负责原生界面、存档和世界访问；框架连接成功，不代表某个原生接口已经适配。玩法开发入口见 [SDK.md](SDK.md)。

## 基础会话与功能协商

`SessionProfile.GameVersion`、`SpacedOut`、`CycleTrimVersion` 是描述信息。基础会话不会因游戏构建号、DLC 状态或 CycleTrim 版本不同而直接拒绝连接，CycleTrim 可以缺席。双方仍须使用相同的 `Protocol.Version` 和玩法 `GameplayId / GameplayVersion`；本地玩法必须已经注册。

认证完成后，`RoomSession.LocalEnvironment`、`HostEnvironment` 和 `EnvironmentFor(playerId)` 提供不可变的环境快照。快照包含游戏构建、CycleTrim 版本、DLC 状态、对端声明的能力和对端要求的能力；访客只能读取主机，主机只能读取当前已认证的席位。环境描述用于选择降级路径和记录诊断，功能权限仍必须通过能力协商和模组准入检查。断线、重连等待、离开或关闭后不再返回旧快照。

功能合同使用独立、带版本的能力 ID。`Capabilities` 表示本端提供的合同，`RequiredCapabilities` 表示要求对端提供的合同。双方要求都满足后才能认证入席，缺项返回 `capability_mismatch`；诊断见 `LastCompatibilityFailure.MissingAtHost / MissingAtPeer`。

已选玩法可以通过 SDK 的 `IGameplayCapabilityContract` 增加自己的能力和要求。
适配器会把它们与原生能力、`ModSynchronizationV1` 合并后再创建会话资料；玩法
能力仍然只描述协议合同，不替代模组清单、准入规则或权限校验。这样玩法可以在
不绑定具体游戏构建号或 DLC 标签的情况下声明自己的网络依赖。

| 公开入口 | 含义 |
| --- | --- |
| `SessionCompatibility.Evaluate(...)` | 双向检查必需合同，并计算双方能力交集 |
| `RoomSession.CapabilitiesFor(playerId)` | 已认证对端与本端的能力交集；查询自身时返回本端能力 |
| `RoomSession.CanUseCapabilityWith(...)` | 检查上述交集，适用于双方都实现同一合同的服务 |
| `RoomSession.PeerCapabilitiesFor(playerId)` | 已认证对端实际提供的能力，适用于读取端与提供端不对称的服务 |
| `RoomSession.EnvironmentFor(playerId)` | 已认证端的游戏、DLC、CycleTrim 和能力描述；不授予任何功能权限 |

能力查询返回副本。访客只能查询自己和主机的能力，不能从成员名单推断其他访客的可用功能。未连接、重连、离开或关闭时，不提供可用能力绑定。

默认 `SessionCapabilities.Core` 包含会话、玩法消息和星球登记合同。游戏适配器另外声明并要求 `ModSynchronizationV1`，由实际 `ModSyncSession` 完成模组对齐；裸 `RoomSession` 不自动宣称已经安装模组同步服务。能力存在也不能替代模组对齐、权限检查和业务参数校验。

实现：[SessionCompatibility.cs](../Network/SessionCompatibility.cs)、[RoomSession.cs](../Network/RoomSession.cs)、[Protocol.cs](../Network/Protocol.cs)。

## 当前原生适配器

原生能力表只登记游戏构建 **744825**。能力 ID 描述功能合同，不包含游戏构建号；将来经过适配的多个构建可以实现同一合同。

| 能力 | 744825 本体 | 744825 Spaced Out! | 当前用途 |
| --- | --- | --- | --- |
| `NativePlanetV1` | 支持 | 支持 | `WorldContainer` 星球清单、身份与尺寸采集 |
| `NativeObservationV1` | 支持 | 支持 | 本地世界快照与经过授权的视口观察采样 |
| `NativeStarMapV1` | 不支持 | 支持 | DLC 原生星图读取 |

本体的星球与观察适配不依赖 DLC 探测。原生星图在读取之前检查 Spaced Out!。这些合同没有授予第三方任意调用原生方法、跨进程创建世界或修改其他玩家星球的能力。

`SessionCapabilities.ForNativeAdapter / SupportsNativeAdapter` 提供能力表，`NativeAdapterGuard.Require / Read` 在执行原生读取前检查本机适配情况。`NativeAdapterUnavailableException` 的错误键为 `native_adapter_unavailable`，`Capability` 指出被拒绝的合同。普通网络或第三方插件能力不能用于通过这个原生检查。

游戏侧入口包括 [NativeAdapterAvailability](../Runtime/NativeAdapterAvailability.cs)、[OnlineColonyState](../Multiplayer/OnlineColonyState.cs)、[NativeWorldSource](../Multiplayer/NativeWorldSource.cs)、[NativeWorldInterestSource](../Multiplayer/NativeWorldInterestSource.cs) 和 [LocalNativeStarMap](../UI/LocalNativeStarMap.cs)。[WorldCoordinator](../Multiplayer/WorldCoordinator.cs) 在创建原生桥接前再次检查能力。

未知构建保留基础会话和纯框架功能，原生读取明确关闭，不将“适配器不可用”伪装成已经加载了一颗空星球。保存时跳过未适配的星球、复制人来源和施工采集，保留既有联机记录；这不承诺未知游戏版本的原生存档迁移安全。游戏模组元数据仍保留 `minimumSupportedBuild: 744825`，没有宣称旧版游戏都能加载本模组。

新增构建支持需要核对并适配真实原生实现、更新能力表，再完成相关实机验证。仅删除构建检查或扩大能力表不能证明原生兼容。

## 玩家席位与连接代次

玩家 ID 表示当前会话内可恢复的席位。同一玩家重新认证时可以保留 ID；它不是 Steam 账号，也不是跨会话、跨存档的永久身份。

`RoomSession.ConnectionGenerationFor(playerId)` 表示当前已认证连接的代次：

- 主机自己的代次固定为 `1`；主机记录的访客代次在每次成功认证新连接时递增。
- 访客每次成功接受新的 `Welcome` 时递增，查询自己或主机得到本次代次；查询其他访客得到 `0`。
- 尚未连接、重连等待、离开、关闭及已断开的保留席位返回 `0`。

入场确认和其他临时授权应绑定“会话实例、玩家 ID、连接代次”。同席位在线换连接时，成员名单的 `Connected` 可以一直为 true，仅监听断线或检查玩家 ID 会错误复用旧授权。代次改变后应撤销旧就绪状态，重新完成当前连接的模组清单确认；计数值不应跨进程比较或持久化为永久权限。

## 已有验证

[CompatibilityTests.cs](../../../tests/OniOnline.Tests/CompatibilityTests.cs) 已通过 **57 项局部宿主检查**，调用生产框架和真实 `RoomSession` 协议边界，覆盖能力协商、描述信息差异、环境快照、非法能力编码、错误 Welcome、原生读取守卫、同席位在线替换和重连代次。未知构建用例确认 DLC 与原生读取委托均未执行，模拟元数据保持原值。

可单独运行此模块：

```sh
rtk proxy dotnet run --project tests/OniOnline.Tests -c Release -- --compatibility
```

上述 57 项记录来自独立局部宿主；该仓库入口调用同一组用例。它们不启动游戏，不证明 Unity 生命周期、原生存读档、本体实机、跨版本原生交互或界面视觉已经验收。
