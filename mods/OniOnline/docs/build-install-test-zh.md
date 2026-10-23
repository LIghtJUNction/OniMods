# 从源码构建、安装并测试 OniOnline

这份教程会把 OniOnline 装进你自己的《缺氧》，再和另一名玩家测试文字聊天与授权只读观察。当前是开发预览，每个玩家运行自己的殖民地。房主管理自己的星系，客人登记自己存档中的一颗星球，其他人经过授权才能观察。

完整殖民地迁移、火箭互访和远程施工不属于普通预览包的可用功能。带星球加入房间，目前表示登记身份与观察数据，不会把客人的殖民地复制进房主存档。

## 1. 准备游戏和工具

先在 Steam 安装并启动一次《缺氧》和《眼冒金星 / Spaced Out!》DLC。后面的游戏启动也全部通过 Steam 进行。

安装并启用与房主匹配的 CycleTrim。OniOnline 内置 PLib，不用另装 PLib 模组。只参加联机测试不需要 OniMcp 或 OniHarness。

安装以下构建工具，然后重新打开终端：

- [Git](https://git-scm.com/downloads/)。
- [Rust stable 和 Cargo](https://rustup.rs/)，用于构建 `onim`。
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)，用于构建模组和运行测试。选择 SDK，只有 Runtime 不够。
- Linux 需要 `unzip` 和 `tar`。Windows 使用 PowerShell 解压，并需要 Visual Studio 的 C++ 构建工具来编译 Rust 程序。安装步骤见 [Windows 开发说明](https://github.com/LIghtJUNction/OniMods/blob/main/docs/WINDOWS_DEV.md)。

这些工具不包含游戏程序集。构建使用你本机合法安装的游戏文件，不要向别人索取或分发游戏 DLL。

## 2. 获取双方一致的源码

打开终端，克隆仓库：

```sh
git clone https://github.com/LIghtJUNction/OniMods.git
cd OniMods
git rev-parse HEAD
```

把最后输出的提交号发给房主核对。测试双方要使用同一个提交，并使用相同的 ONI、CycleTrim 和 DLC 组合。

直播中的本地修改可能尚未推送。克隆 `main` 不会得到未推送的修改。房主公布测试提交后，再在新克隆的仓库中切换到那个提交：

```sh
git fetch origin
git switch --detach 房主公布的提交号
```

把中文占位符替换为真实提交号后再运行。已有自己的未提交修改时，先保留这些工作，不要用重置命令覆盖。

构建 OniOnline 不需要初始化 `oni-skills` 或 OniHarness 子模块。

## 3. 配置本机游戏路径

在仓库根目录安装 CLI：

```sh
cargo install --path . --locked
onim --version
```

本文核对的 CLI 为 `onim 0.1.8`。安装后找不到 `onim` 时，重新打开终端，确认 Cargo 的可执行文件目录已加入 `PATH`。

首次配置时，把 `Directory.Build.props.example` 复制为 `Directory.Build.props`。Linux 使用：

```sh
cp Directory.Build.props.example Directory.Build.props
```

Windows PowerShell 使用：

```powershell
Copy-Item Directory.Build.props.example Directory.Build.props
```

已经有 `Directory.Build.props` 时，直接检查已有内容，不要覆盖。编辑其中的 `OniGamePath`，填入 Steam 的游戏安装目录。可在 Steam 的《缺氧》属性中打开 **已安装文件 → 浏览**，找到这一层目录。

Linux 的示例值：

```xml
<OniGamePath>/home/你的用户名/.local/share/Steam/steamapps/common/OxygenNotIncluded</OniGamePath>
```

Windows 的示例值：

```xml
<OniGamePath>C:/Program Files (x86)/Steam/steamapps/common/OxygenNotIncluded</OniGamePath>
```

这两个值都只是示例。自定义 Steam 库要填写实际位置。Windows 和 Linux 的 `OniManagedPath` 保留模板值即可。

仓库已经登记了 OniOnline，本教程不需要运行 `onim setup`。保留原来的 `onim.toml`，不要改成模板模组列表。

检查配置：

```sh
onim list
onim doctor
```

`list` 中要有 `OniOnline`。`doctor` 中的游戏目录、`Assembly-CSharp.dll`、SDK 和 Mod 用户目录都要指向本机有效位置。缺少某项时先修正，再继续构建。

## 4. 构建并安装

先正常保存并退出《缺氧》。安装不会让已运行的游戏热加载新 DLL。

普通测试者安装 Release 版本：

```sh
onim -m OniOnline install
```

这个命令先构建 Release，再解压到用户 Mod 目录的 `Local/OniOnline`。构建成功时，仓库中也会有本轮的 `dist/OniOnline.zip` 和 `mods/OniOnline/dist/OniOnline.zip`。

想反复修改源码的开发者，可以改用：

```sh
onim -m OniOnline dev
```

`dev` 会重新构建 Debug，安装到 `Dev/OniOnline`，并在已有 `mods.json` 中启用该 Dev 条目。它没有 `--release` 选项。两条安装命令选一条使用，不要把先构建 Release、再运行 `dev` 的结果当成 Release 测试。

仅检查是否能编译，不安装到游戏时，运行：

```sh
onim -m OniOnline build --release
```

当前 CLI 识别的 Mod 用户目录为 Linux 的 `~/.config/unity3d/Klei/Oxygen Not Included/mods`，以及 Windows 实际 **文档**目录中的 `Klei/OxygenNotIncluded/mods`。Windows 文档目录可能被重定向，以安装输出和游戏加载日志为准。

现在从 Steam 启动游戏，在 **模组**列表中启用 OniOnline 和 CycleTrim。若 OniOnline 同时出现 Dev、Local 或 Workshop 副本，只启用本次安装的一份。相同的 `staticID` 为 `LIghtJUNction.OniOnline`，不要靠显示名或版本号猜测加载来源。先在游戏中停用旧副本，再退出游戏处理确定无用的旧安装目录。不要删除存档目录，也不要删除其他模组。

按游戏提示重启，再检查 `Player.log` 中实际加载的 `OniOnline.dll` 路径。只看到 ZIP 构建成功，不能证明游戏加载了这个版本。

## 5. 和另一名玩家测试

双方各自正常创建一个独立殖民地，或使用自己已有殖民地的测试副本。各自选一颗星球参与，不要复制同一个存档来冒充两名玩家的不同星球。

没有测试存档时，打开 **新游戏 → 生存模式 → 联机**，进入原生眼冒金星目的地配置，默认关闭沙盒。继续选择星球并完成原生开局，再开放自己的星系。主菜单的 **浏览游戏大厅** 只列出大厅和加入入口，不提供创建星系按钮。本次入口调整尚待游戏内验收。

1. 房主加载自己的测试殖民地，打开原生星图中的星系入口，设置星系名并开放星系。
2. 客人加载自己的测试殖民地，打开 **联机**，找到房主的公开星系。使用 LAN 时，双方在连接设置中选择 LAN，也可以输入房主给出的房间码或 IP 与端口。
3. 客人已有殖民地时，直接在游戏原生星图底部切换“我的星系”和“房间星系”。自己的星图中点选一颗已发现星球，再点“选择此星球”；房间星图中点选空格，等待预留成功后点“确认加入”。房间视图仅展示房主已发现的静态目录和已登记玩家，不展示客人的本地星图冒充房主。无存档先完成原生创建或加载。位置规则仍在开发，不把当前选位方式当成最终玩法。
4. 双方各发一句不同的中文聊天，确认两边都收到，昵称、文字和消息条数正确。点击编辑框后检查光标、方向键和选中文字的显示。
5. 星球主人先保持未共享，确认另一端看不到该星球的地块。主人开启共享后，再观察已经探索的区域。
6. 查看地形、对象和单格详情。未探索的格子不能泄漏数据。观察者不能挖掘、建造或替主人下指令。
7. 主人关闭共享，确认另一端停止更新并清掉该星球的观察数据。
8. 用测试房间短暂断开网络，再恢复网络。检查断线提示、重连后的聊天是否重复，以及观察权限是否仍正确。主动离开房间后，不能自动重连回旧房间。
9. 双方连接麦克风和耳机，默认按住 `Ctrl+V` 轮流说话，确认对方能听到。松开、编辑文字或切出窗口时确认麦克风停止；断网再连接后不能重放旧语音。设置中关闭语音或调节接收音量，检查实际效果。原生控制设置中可以修改说话和聊天快捷键。

LAN 默认使用 TCP `27140` 和 UDP 发现端口 `27141`。发现列表为空时，可以用对方实际可达的 LAN IP 直连。LAN 预览面向可信局域网；跨互联网优先测试 Steam 大厅，不把局域网端口直接开放到公网。

这轮主要测试低频状态同步和聊天，不传直播视频。主人仍在自己的电脑上模拟殖民地。高延迟、断流恢复和换房后的授权清理，都需要记录实际结果，不能只凭“加入成功”判定通过。

## 6. 在本机运行源码测试

下面的测试不启动游戏。它们验证协议、权限、缓存、聊天和模拟弱网，不能代替上一步的两个真实游戏客户端。

运行完整宿主测试：

```sh
dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -p:TreatWarningsAsErrors=true
```

单独测试真实 TCP 连接上的限速和断流恢复：

```sh
dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -- --multiplayer-tcp
```

等命令结束并核对退出码。编译失败、超时或断言失败都不算通过。两端 ONI 的自动采集流程见 [两个完整游戏客户端验收](https://github.com/LIghtJUNction/OniMods/blob/main/tests/OniOnline.Tests/MULTIPLAYER-RUNTIME.md)。其中的测试探针只能用于专门的测试构建，不能打进普通发布包。

## 7. 把可复现的问题发回来

反馈时写明源码提交号、ONI 版本、系统、CycleTrim 版本、使用 Steam 还是 LAN，以及问题发生前的操作。附上预期结果、实际结果和一小段相关日志。

画面问题可以附截图。弱网问题写明延迟、断网时长、恢复时间和是否出现重复聊天。先删去日志中的房间密码、连接密钥、Cookie 和其他私人信息，不上传游戏 DLL 或私人存档。

直播时可以用弹幕描述问题，也可以在 [OniMods 的 GitHub Issues](https://github.com/LIghtJUNction/OniMods/issues) 提交复现步骤。提交前搜索同类问题，在已有讨论中补充本轮证据。
