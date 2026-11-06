---
name: oni-online-sdk
description: "开发或接入基于 OniOnline.Framework 的第三方联机玩法、认证消息和模组状态频道。用于玩法程序集与 SDK 集成；不用于 OniOnline 核心、原生 UI、普通缺氧游玩或 Steam 发布。"
---

# OniOnline 第三方玩法 SDK

这个技能面向要给 OniOnline 写新玩法的开发者。目标是产出一个独立的玩法程序集：它引用公开的 `OniOnline.Framework.dll`，注册自己的玩法规则和状态同步，不复制框架源码，也不把 Unity 或 ONI 原生对象塞进 SDK 层。

## 先划清职责

- 框架核心、原生适配、菜单/HUD、美术、正式 Mod 包交给 [`oni-online-framework`](../oni-online-framework/SKILL.md)。
- 普通《缺氧》游玩使用 `oni-gameplay`，不要因为要测试一个玩法就直接启动游戏。
- Steam Workshop 发布不属于本技能；遵守仓库的 Legacy ZIP 发布规则，绝不使用 SteamCMD 目录上传。
- 开始前读取仓库根目录 `AGENTS.md`、`RTK.md`、`UPSTREAM.toml`、`docs/autonomous-iteration.md`，并检查 `git status --short`。保留 CycleTrim、`.local-tests` 和其他不相关改动。

## API 和程序集边界

先读：

- [`SDK.md`](../../../mods/OniOnline/docs/SDK.md)：公开入口、生命周期、存档槽和线程边界。
- [`SDK-MOD-SYNC.md`](../../../mods/OniOnline/docs/SDK-MOD-SYNC.md)：状态频道、权限、epoch/schema 和恢复合同。
- [`FRAMEWORK-COMPATIBILITY.md`](../../../mods/OniOnline/docs/FRAMEWORK-COMPATIBILITY.md)：DLC、游戏版本和能力协商。
- [`PlanetSignals` 示例](../../../examples/OniOnline.PlanetSignals/README.md)：独立程序集的完整最小实现。

必须保持以下不变量：

1. `OniOnline.Framework` 继续是 .NET Standard 2.0；玩法项目可按游戏 Mod 的目标框架编译，但只能引用打包后的公开 DLL。不要重新编译、复制或随玩法再次打包 `OniOnline.Framework.dll`。
2. 玩法 ID 和规则版本是稳定的网络合同。所有参与者必须使用兼容版本；游戏构建号、DLC 和 CycleTrim 通过能力协商处理，不能在玩法里偷偷改成无条件硬相等。
3. `ValidatePlanetAdmission` 只做纯校验，不能创建 Unity 对象、搬迁复制人或直接改别人的星球。内置一人一星球规则是可复用的准入基线，不是远端操作权限。

## 玩法生命周期

推荐顺序：

1. 在 `OnLoad` 注册 `IGameplayRules`；在所有模组加载后选择稳定的玩法 ID。
2. 订阅 `GameplayFramework.SessionChanged`，每次回调先释放旧的消息订阅、publisher、watch 和 UI 回调，再从 `GameplayFramework.Current` 取得新会话。
3. 任何发送、发布或观察前都检查 `session.Ready`。把 `AdmissionGeneration` 当作本端失效标记；代次变化就丢弃旧绑定，重新取得描述信息，不能继续写旧会话。
4. `Dispose` 必须幂等。不要自己 Pump 或 Dispose 框架拥有的 `ModStates` 服务；只释放自己注册的对象。

第三方玩法不应缓存旧 `GameplaySession`、把 `Players` 中的席位编号当作 Steam/跨存档身份，或在断线后自动重放一次旧业务操作。

## 消息和状态频道

- 自定义消息只表达经过身份验证的业务意图。发送者由传输层提供，不能从 payload 伪造 `SenderId` 或 authority。
- 每个请求带随机 nonce/请求编号，房主校验权限、参数和当前星球归属，并做幂等去重；接收回调不阻塞、不直接碰后台线程上的原生世界。
- `SendToHost` 是请求入口，`SendToPlayer` 只由权威端回复。SDK 不替玩法广播、执行或重试业务操作。
- 状态频道必须声明 authority、epoch、schema、权限和恢复策略。reducer 只校验和变换纯字节；未知 schema、缺口或失效代次保留最后正确状态并隔离新数据。
- 断线、重连和 `Changed` 事件后重新获取描述并重新订阅；不要猜 epoch，也不要把下载完成或房间连接当成状态已就绪。
- `Required`、`ClientOnly`、`Optional` 只在确实对应的模组声明上使用。Local/Dev 模组不能因为玩法消息而被自动公开或分发。

## 局部验证顺序

这些命令共享 `obj/bin`，必须串行执行；所有命令按仓库约定加 `rtk`。先构建依赖，再跑最小专项：

```sh
rtk proxy dotnet build sdk/OniOnline.Framework/OniOnline.Framework.csproj -c Release --no-restore
rtk proxy dotnet build examples/OniOnline.PlanetSignals/OniOnline.PlanetSignals.csproj -c Release --no-restore
rtk proxy dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore -- --sdk
rtk proxy dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore -- --gameplay-admission
rtk proxy dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore -- --mod-sync-declarations
rtk proxy dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore -- --mod-sync-contracts
rtk proxy dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore -- --mod-content-sync
rtk proxy dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore -- --mod-content-digest
rtk proxy dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore -- --mod-sync-session
```

示例项目需要 `OniManagedPath` 指向本机合法的 ONI `Managed` 目录；如果仓库没有本地配置，在第二条命令追加 `-p:OniManagedPath="/path/to/OxygenNotIncluded_Data/Managed"`。它只用于编译引用，不会把游戏 DLL 打进玩法程序集。

每项都记录退出码和 `PASS … assertions`。至少覆盖正常路径、解绑/重绑、断线重连、旧 generation、重复 nonce、权限撤销、未知 schema 和预算上限。玩法示例构建通过不代表它已经安装或被游戏加载。

## 实机边界

只有 SDK 和宿主专项通过、且用户明确要求实机时，才交给 `oni-harness` 技能按 Steam-only、容量门禁和一次性存档流程执行；本技能本身不启动或重启游戏。分别记录：安装的 DLL、实际加载的 DLL、进入/退出、聊天或玩法动作、保存/重启、重连和权限结果。宿主测试、同机 TCP、示例编译或 ZIP 哈希都不能替代双客户端真实 ONI 验收；容量不足时保留失败证据，不降低门槛。
