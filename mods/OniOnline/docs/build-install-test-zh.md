# 构建、安装与测试 OniOnline 0.4.0

这是源码开发预览的安装与测试流程，不是 Steam 发版公告。OniOnline 内置“一名玩家控制一颗星球”：每个人运行自己的原生殖民地，星系负责连接、登记和授权观察。当前不搬迁完整殖民地，不提供火箭互访或离线代算。

先验证局部模块，再进行安装和游戏内集成测试。新版浏览、开局、通讯和模组对齐界面的图形验收仍待完成，下面的游戏步骤是验收清单，不代表已经通过。

## 准备环境

通过 Steam 安装并正常启动一次《缺氧》。后续游戏启动也使用 Steam。CycleTrim、Spaced Out! 和单独的 PLib 模组均不是固定前置；OniOnline 已内置 PLib。普通联机不需要 OniMcp 或 OniHarness。

当前原生适配器登记游戏构建 **744825**：本体提供星球身份和观察采样适配，Spaced Out! 另提供原生星图。未知构建的原生功能关闭，不能把基础协议连接成功理解成任意游戏版本兼容。最低构建要求和细节见 [兼容边界](FRAMEWORK-COMPATIBILITY.md)。

源码构建需要 Git、Rust stable/Cargo 和 .NET 10 SDK。Linux 另需 `unzip`、`tar`；Windows 的构建工具说明见 [WINDOWS_DEV.md](../../../docs/WINDOWS_DEV.md)。所有游戏程序集来自自己的合法安装，不分发游戏 DLL。

```sh
git clone https://github.com/LIghtJUNction/OniMods.git
cd OniMods
cargo install --path . --locked
onim --version
```

测试双方先核对源码提交或候选包身份。新版本首次验收建议使用同一候选，减少未验证组合；协议 9 及更早客户端不能加入协议 10。后续是否可连接由协议、玩法合同、功能能力和模组清单决定，不要求游戏构建号或 DLC 标签简单相等。

构建 OniOnline 无需初始化可选的 `oni-skills` 或 OniHarness 子模块。保留已有未提交修改，不通过重置仓库来切换测试版本。

## 配置本机游戏路径

首次配置将 `Directory.Build.props.example` 复制为 `Directory.Build.props`；已有文件直接检查，不覆盖。在其中填写 Steam 实际安装目录，例如：

```xml
<OniGamePath>/home/你的用户名/.local/share/Steam/steamapps/common/OxygenNotIncluded</OniGamePath>
```

Windows 可使用 `C:/Program Files (x86)/Steam/steamapps/common/OxygenNotIncluded`，自定义 Steam 库填写实际目录。可从 Steam 游戏属性的“已安装文件 → 浏览”定位。

保留仓库已有的 `onim.toml`，不需要重新执行 `onim setup`。检查：

```sh
onim list
onim doctor
```

列表应有 `OniOnline`；诊断中的游戏、Managed 引用、SDK 和用户模组目录应有效。诊断进程退出成功不代表每一项都通过。

## 先运行局部测试

以下命令不启动游戏，根据改动选择相关模块，不必每次全部执行：

```sh
dotnet run --project tests/OniOnline.Tests -c Release -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -- --sdk
dotnet run --project tests/OniOnline.Tests -c Release -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -- --compatibility
dotnet run --project tests/OniOnline.Tests -c Release -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -- --mod-sync-session
dotnet run --project tests/OniOnline.Tests -c Release -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -- --mod-content-sync
```

核对退出码和实际选择的用例。编译失败、空选择、超时或断言失败均不算通过。SDK、内容同步和兼容测试只验证各自生产逻辑及协议边界，不证明 Unity、Steam 下载或原生界面已经可用。

只开发独立框架时，可先构建 `sdk/OniOnline.Framework`，无需游戏引用；玩法示例与调用方式见 [SDK.md](SDK.md)。

## 构建和安装候选

只构建，不安装：

```sh
onim -m OniOnline build --release
```

Release 包位于 `dist/OniOnline.zip`，模组项目内也有 `mods/OniOnline/dist/OniOnline.zip`。包中应同时包含 `OniOnline.dll`、`OniOnline.Framework.dll`、元数据和说明，不含游戏 DLL。

安装前正常保存并退出游戏。选择一条安装路线：

```sh
# Release 测试：重新构建并安装到 Local/OniOnline
onim -m OniOnline install
```

反复修改源码时可改用 `onim -m OniOnline dev`，它重新构建 Debug 并启用 `Dev/OniOnline`，不是 Release。不要连续安装两份并同时启用。

从 Steam 启动游戏，在原生模组列表中启用本轮 OniOnline，按游戏提示重启。同一 `LIghtJUNction.OniOnline` 只启用一份 Local、Dev 或 Workshop 来源，保留其他模组和存档。Linux 用户模组目录通常为 `~/.config/unity3d/Klei/Oxygen Not Included/mods`；Windows 位于实际“文档”目录下的 `Klei/OxygenNotIncluded/mods`，以安装输出为准。

检查 `Player.log` 中实际加载程序集的路径、版本与 SHA256，并与候选比对。构建 ZIP 成功、文件已经复制或游戏已经启动，都不等于候选已经加载。

## 验收入场和模组对齐

使用一次性测试殖民地或自己的存档副本。不要把同一存档复制成两份后当成两个不同玩家的星球；内置规则会拒绝同源分支重复登记。

1. 从 **新游戏 → 生存模式 → 联机** 进入原生配置和开局流程，完成后创建星系。主菜单 **浏览星系** 只负责浏览与加入，不能创建。菜单、加载和初始复制人选择期间不出现联机 HUD。
2. 另一名玩家通过浏览列表或星系码加入。LAN 可选择发现或 `oni://可达IP:27140`；Steam 使用 `oni://steam/<lobby id>`。
3. 先用一致的必需模组清单确认正常准入，再分别制造缺失、未启用、版本或内容不同的情况。没有对齐前，不能登记星球、观察或操作其他玩家的内容。
4. 差异确认窗应先给摘要，完整差异可选查看；不需要翻完所有条目才能下载。确认只作用于当前连接和当前清单，离开或清单改变后旧按钮失效。
5. 公开工坊内容可经确认下载。下载结束后，进入原生 **管理模组** 启用或停用对应项，并按游戏要求重启，再重新加入。必须核对新进程实际加载的清单，不能把 Steam 下载回调当作安装或加载完成。
6. Local/Dev 模组需要自行取得对应来源和版本；它们不会自动上传或传给其他玩家。星系主人使用 CycleTrim 或其他默认必需模组时，客人仍需按清单对齐。
7. 短暂断网、恢复和主动离开各测一次。重连应重新确认当前清单，旧连接的就绪或授权不能复用；主动离开后不能自动连回。

模组对齐的加载证据与下载阶段说明见 [MOD-CONTENT-SYNC.md](MOD-CONTENT-SYNC.md)。基本会话按能力协商，不代表没有模组准入检查。

## 验收聊天、观察和注册状态

进入殖民地后，点击原生管理栏的气泡或按默认 `Ctrl+T` 直接打开通讯。双方各发不同中文消息，检查昵称、内容、消息顺序、中文输入法、回车发送、焦点与关闭后草稿。窗口和入口应沿用游戏原生控件，不遮挡建造栏；主菜单、世界配置和初始复制人选择期间应隐藏入口。

星球主人保持未共享时，另一端不能看到地块；授权后可观察已探索区域。检查平移、缩放、温度与单格详情，未探索区域不泄漏。关闭共享后，另一端停止更新并清理观察数据。普通预览包不能通过观察页执行原生挖掘、建造或复制人指令。双方本地暂停和倍速互不控制。

注册状态同步需要使用 SDK 的模组参与测试：由其注册状态频道和观察权限，验证快照、增量、断线恢复及不兼容数据版本。没有接入 SDK 的任意第三方模组不会自动获得状态同步。接口和开发示例见 [SDK.md](SDK.md)。

语音可单独验收：默认按住 `Ctrl+V` 轮流说话，松开、编辑文字、切出窗口及离开星系后停止采集；断线后不重放旧音频。实际麦克风、Steam 编码和两个真实游戏之间的听音未被宿主测试覆盖。

新版界面的外观、真实鼠标与输入法仍需图形测试。探针调用事件处理器不能替代这些检查。实际 Steam 下载、ONI 原生安装与重启、两个真实账号的 P2P 和两端游戏观察均需单独留存证据。

## 最后进行完整集成验证

局部模块通过后，才运行完整宿主回归并集中进行 Steam 实机测试：

```sh
dotnet run --project tests/OniOnline.Tests -c Release -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -p:TreatWarningsAsErrors=true
```

[两个完整游戏客户端验收](../../../tests/OniOnline.Tests/MULTIPLAYER-RUNTIME.md)要求两个真实 Steam 游戏进程。单机协议进程、假 SDK 和纯宿主用例不能填作双游戏通过。测试探针只属于专门测试构建，普通包不包含，也不能发布。

反馈请附候选提交或哈希、游戏构建、DLC、系统、实际启用模组清单、Steam/LAN、复现步骤和必要的短日志。先删去密码、连接密钥、Cookie、账号隐私及私人路径；不要上传游戏 DLL 或私人存档。
