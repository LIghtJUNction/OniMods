# OniOnline — 联机框架开发预览

<p align="center">
  <img src="assets/onionline-logo-v1.png" alt="OniOnline" width="320" />
</p>

OniOnline 为《缺氧》提供联机会话和玩法 SDK，内置“一名玩家控制一颗星球”。每名玩家运行自己的殖民地，保留本地存档、暂停和倍速；其他人只能观察主人已授权分享的内容。

当前源码候选为 **0.4.0，协议 10**，仍是开发预览。此版本号不表示已发布到 Steam。新版原生浏览、开局和通讯界面正在整合，图形交互、实际 Steam 模组下载与两个真实游戏客户端的完整验收尚未完成。

## 开始使用

- 主菜单 **浏览星系**：查找、浏览并加入其他人的星系，不提供创建入口。
- **新游戏 → 生存模式 → 联机**：创建自己的星系，继续游戏原生的世界配置和开局流程。菜单、载入和初始复制人选择期间不显示联机操作 HUD。
- 进入殖民地后，原生管理栏的气泡按钮可直接打开 **通讯**；游戏设置菜单也提供同一入口。默认 `Ctrl+T` 打开聊天、`Escape` 关闭，快捷键可在游戏控制设置中修改。

现有殖民地可选择一颗星球登记到星系。内置规则只允许每名玩家在当前星系绑定一颗星球，并拒绝重复星球及同一存档分支。换星球需先离开。登记的是稳定身份、位置和观察信息，不会把殖民地搬进另一名玩家的存档。

从源码安装及分阶段测试见 [构建、安装与测试](docs/build-install-test-zh.md)。游戏始终通过 Steam 启动。OniOnline 内置 PLib，无需另装 PLib；普通玩家无需安装 OniMcp 或 OniHarness。

## 兼容与模组同步

CycleTrim 和 Spaced Out! **不是固定前置**。基础会话按协议、玩法版本和双方提供的功能合同协商，不以游戏构建号、DLC 标签或 CycleTrim 版本的简单相等作为准入条件。

当前游戏适配器登记的构建是 **744825**：

| 原生功能 | 游戏本体 | Spaced Out! |
| --- | --- | --- |
| 星球身份与清单 | 已有适配 | 已有适配 |
| 授权观察采样 | 已有适配 | 已有适配 |
| 原生星图 | 不提供 | 已有适配 |

未知构建的原生读取明确不可用，保留可用的基础会话与纯框架功能。模组元数据的最低构建仍为 744825；这不承诺任意游戏版本能加载模组，也不代表本体和跨版本原生交互已完成实机验收。详见 [兼容边界](docs/FRAMEWORK-COMPATIBILITY.md)。

加入玩法前，双方交换并确认模组清单。必需模组的来源、版本、受控内容摘要和实际加载状态必须对齐；重连需重新确认。未声明用途的模组默认必需，因此星系主人启用的 CycleTrim 或其他模组仍可能需要客人一并安装。

发现差异时，原生确认窗提供摘要、可选完整列表，以及下载或管理模组入口。Steam 只负责下载公开工坊内容；**下载完成不等于 ONI 已安装、启用或加载**。需要在原生模组菜单调整启用状态并重启游戏，再重新加入。Local/Dev 模组不自动上传或分发，需自行取得对应版本。详见 [模组内容同步](docs/MOD-CONTENT-SYNC.md)。

游戏中还可同步模组通过 SDK **显式注册的状态频道**。框架提供经过身份验证的传输、版本化快照和增量；模组开发者负责定义数据、权限与迁移规则。它不会自动同步任意第三方模组的内存、私有配置或整个原生模拟。

## 当前玩法范围

- Steam 公开星系、LAN 发现和星系码加入，支持密码、聊天、断线恢复及主人管理。
- 一名玩家控制自己的原生星球；各自暂停和倍速互不影响。
- 主人授权后，可观察已探索区域，按视口获取变化并请求温度或单格详情。未探索区域不公开，撤权后停止观察。
- 普通预览包提供授权只读观察，原生远程施工和复制人操作入口关闭。
- 开发候选包含按住说话的 Steam 语音，默认 `Ctrl+V`；麦克风、失焦停止和双客户端听音仍待实机验收。

完整殖民地迁移、火箭互访、物资运输、离线代算和主人迁移尚未实现。关闭负责模拟的游戏进程后，该殖民地不会继续运行。后续玩法草案见 [星系设计](STAR-SYSTEMS.md)，草案不代表当前可用功能。

LAN 默认 TCP `27140`，发现端口 UDP `27141`；广播不可达时可用 `oni://IP:27140` 直连。LAN 预览面向可信局域网，密码验证不等于传输加密。Steam 星系码使用 `oni://steam/<lobby id>`，沿用游戏现有 Steam 客户端。

## 给玩法开发者

`OniOnline.Framework.dll` 是独立的 .NET Standard 2.0 SDK，不引用 Unity、ONI 或 PLib。内置玩法与第三方玩法使用同一套注册接口；第三方可以添加准入规则、经过身份验证的消息、注册状态同步和按殖民地保存的版本化数据。

- [SDK 入口、生命周期与边界](docs/SDK.md)
- [独立框架项目](../../sdk/OniOnline.Framework/README.md)
- [独立开发示例](../../examples/OniOnline.PlanetSignals)
- [兼容与原生适配](docs/FRAMEWORK-COMPATIBILITY.md)
- [模组对齐、下载与加载证据](docs/MOD-CONTENT-SYNC.md)

SDK 仍处开发预览，尚未承诺后续二进制兼容。不要随第三方玩法重复打包框架 DLL，也不要把网络消息当成任意操作其他玩家星球的权限。

## 构建和验证

游戏适配器从本机合法安装的游戏读取引用，不分发游戏 DLL。配置 `Directory.Build.props` 后，在仓库根目录构建：

```sh
onim -m OniOnline build --release
```

产物为 `dist/OniOnline.zip`，包含 **OniOnline.dll** 和 **OniOnline.Framework.dll**。实际安装使用 `onim -m OniOnline install`；它会重新构建 Release，安装前正常保存并退出游戏。开发用 `dev` 安装 Debug，两种路线选一条，不把 Debug 当成 Release 验收。

先选受影响模块做局部宿主测试，例如：

```sh
dotnet run --project tests/OniOnline.Tests -c Release -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -- --sdk
dotnet run --project tests/OniOnline.Tests -c Release -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -- --mod-sync-session
```

宿主测试不启动游戏。完整宿主回归和 Steam 实机集成留到局部模块通过后进行；构建、打包、安装、进程实际加载、图形验收和双客户端联机分别记录，不互相替代。[双完整游戏验收工具](../../tests/OniOnline.Tests/MULTIPLAYER-RUNTIME.md)要求真实 Steam 游戏进程，本机协议客户端不能代替完整游戏。

测试探针只在专门构建中存在，普通包不包含它，也不能打包探针构建。此前的 [验收记录](PHASE1-VALIDATION.md)描述当时覆盖范围，不自动验收 0.4.0 的新界面和同步链路。
