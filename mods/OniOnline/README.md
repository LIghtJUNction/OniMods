# OniOnline — 联机框架开发预览

<p align="center">
  <img src="assets/onionline-logo-v1.png" alt="OniOnline" width="320" />
</p>

OniOnline 是《缺氧》的联机模组开发框架。安装后默认只有大厅和聊天。一人一星球或其他玩法要做成单独的模组，向框架注册后再选中。

当前源码候选为 **0.5.0，协议 11**，仍是开发预览。此版本号不表示已发布到 Steam。图形交互、实际 Steam 模组下载与两个真实游戏客户端的完整验收尚未完成。

## 开始使用

- 主菜单 **浏览大厅**：查找、加入或创建房间。默认玩法是聊天。
- 进入殖民地后，原生管理栏的气泡按钮直接打开 **通讯**；默认 `Ctrl+T` 打开聊天、`Escape` 关闭，快捷键可在游戏控制设置中修改。菜单、载入和初始复制人选择期间不显示联机操作 HUD。
- 玩法模组在 `OnLoad` 里 `GameplayFramework.Register`，并在连接前 `Select`。需要星球登记和世界同步时，在能力合同里声明 `onionline.worlds.v1`。未声明时，框架只保留大厅、聊天、会话和模组同步。

`OnePlanetPerPlayer` 仍在 SDK 里，供单独的玩法模组调用 `RegisterIfMissing()`。框架本身不登记、也不默认选中它。房间 ID、观察世界 ID 和跨存档 PlanetId 仍是不同身份。

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

发现差异时，原生确认窗提供摘要、可选完整列表，以及下载或管理模组入口。Steam 只负责下载公开工坊内容；**下载完成不等于 ONI 已安装、启用或加载**。下载并核验成功后，玩家可以明确选择“启用并重启”，由游戏原生模组管理器写入设置并重启；来源不明确或写入失败时仍回到模组菜单手动处理。Local/Dev 模组不自动上传或分发，需自行取得对应版本。详见 [模组内容同步](docs/MOD-CONTENT-SYNC.md)。

游戏中还可同步模组通过 SDK **显式注册的状态频道**。框架提供经过身份验证的传输、版本化快照和增量；模组开发者负责定义数据、权限与迁移规则。它不会自动同步任意第三方模组的内存、私有配置或整个原生模拟。

## 当前范围

- 大厅支持 Steam 公开房间、LAN 发现和大厅码，带密码、聊天、断线恢复和房主管理。
- 默认玩法只有聊天。星球登记、授权观察和星图属于声明 `onionline.worlds.v1` 的玩法模组。
- 开发候选包含按住说话的 Steam 语音，默认 `Ctrl+V`；麦克风、失焦停止和双客户端听音仍待实机验收。

完整殖民地迁移、火箭互访、物资运输和离线代算尚未实现。关闭负责模拟的游戏进程后，该殖民地不会继续运行。

LAN 默认 TCP `27140`，发现端口 UDP `27141`；广播不可达时可用 `oni://IP:27140` 直连。LAN 预览面向可信局域网，密码验证不等于传输加密。Steam 星系码使用 `oni://steam/<lobby id>`，沿用游戏现有 Steam 客户端。

## 给玩法开发者

`OniOnline.Framework.dll` 是独立的 .NET Standard 2.0 SDK，不引用 Unity、ONI 或 PLib。聊天和第三方玩法使用同一套注册接口；玩法模组可以添加准入规则、经过身份验证的消息、注册状态同步和按殖民地保存的版本化数据。

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

测试探针只在专门构建中存在，普通包不包含它，也不能打包探针构建。旧的一人一星球设计稿和阶段验收记录已从发布包移除，不能当作 0.5.0 的当前说明。
