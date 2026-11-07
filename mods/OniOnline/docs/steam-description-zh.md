[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/OniOnline/assets/onionline-logo-v1.png[/img]

[b]前置模组：Jlib。[/b] 在模组菜单订阅并启用 Jlib，然后重启缺氧。本模组使用共享前置，不再内嵌一份。

[h1]OniOnline[/h1]

欢迎来到 OniOnline！《缺氧》的多人联机框架。
有了这个模组，[b]好友共享一个大厅、一条聊天频道和一场会话。具体玩法规则由单独的模组注册进来。[/b]

看清谁在房间里，殖民地载入时也能说话。真正要共享的世界，交给声明了能力的玩法模组。

[h2]当前阶段: v0.5.0 Preview[/h2]
[i]这是开发预览。请预期有缺陷、未完成的界面，以及仍然需要备份的会话。[/i]
[i]CycleTrim 和眼冒金星不是固定前置。未声明用途的模组会当成必需项，客人可能仍要装上同一份列表。[/i]

[h3]MOD 拓展接口 (API)[/h3]
OniOnline.Framework 是 .NET Standard 2.0 SDK，不引用 Unity、游戏或 PLib。
在 OnLoad 里调用 GameplayFramework.Register，连接前再 Select 要使用的玩法。
只有需要星球登记和世界同步时才声明 onionline.worlds.v1。没有这份合同时，会话只保留大厅、聊天和模组对齐。
不要把第二份框架 DLL 打进玩法模组。
SDK 说明：[url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniOnline/docs/SDK.md]SDK.md[/url]
示例工程：[url=https://github.com/LIghtJUNction/OniMods/tree/main/examples/OniOnline.PlanetSignals]OniOnline.PlanetSignals[/url]

[h3]同步了什么[/h3]
内置玩法是大厅聊天。Steam 公开房间、局域网发现和房间码都可以开这样的房间。
开局前双方比对必需模组、版本、内容摘要和加载状态。不一致时弹出游戏自己的确认窗，并给出下载公开工坊文件的入口。下载完成不等于模组已经启用。
重连必须重新确认清单。Local 和 Dev 模组不会替你上传。
玩法模组显式注册的状态频道可以同步它定义的快照。私有配置和整份原生模拟不会被镜像。
火箭互访、物资运输、共同施工和离线代算不在这个预览里。关掉负责模拟的客户端，那座殖民地就停了。

[h3]操作指南与体验优化 (QoL)[/h3]
菜单、载入和初始复制人选择期间，管理栏不会冒出联机操作。

* 主菜单：浏览大厅，查找、加入或创建房间。默认玩法是聊天。
* 殖民地内：管理栏气泡打开通讯。Ctrl+T 打开聊天，Escape 关闭。两个快捷键都可以改。
* 开发候选里有按住说话，默认 Ctrl+V。麦克风、失焦停止和双客户端听音还没有完成验收。
* 局域网监听 TCP 27140，发现端口是 UDP 27141。直连写成 oni://IP:27140。Steam 房间链接写成 oni://steam/<lobby id>。
* 局域网密码不是传输加密。请在你信任的网络上使用。

[h3]由 onim 完成[/h3]
本模组来自 [url=https://github.com/LIghtJUNction/OniMods]OniMods[/url]。onim 是这个仓库里的工具。它把缺氧 Mod 的开发、测试和创意工坊发布连成一条直线。

onim dev 放进游戏里试。onim build 打包。onim publish 发到 Steam。

[h3]开源贡献[/h3]
非常欢迎社区贡献！
如果你有优化、Bug 修复或新功能建议，欢迎提交 Pull Request。

请确保你的提交条理清晰并在必要处附带说明文档。

项目地址：[url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/OniOnline]GitHub 仓库传送门[/url]

[h3]免责声明[/h3]
请注意本模组仍在紧密迭代中，我们将保持频繁更新与维护。
我们非常重视大家的反馈与建议！
请使用测试存档。当前适配目标是协议 11 和构建 744825。未知游戏构建会保留基础会话，并拒绝原生世界读取。

[h3]联系我们[/h3]
提问和缺陷请送到 [url=https://github.com/LIghtJUNction/OniMods/issues]GitHub Issues[/url]。

祝游戏愉快，并请记住：聚在一起，氧气照样没算在内。
