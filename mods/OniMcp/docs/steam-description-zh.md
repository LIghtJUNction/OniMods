[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/OniMcp/preview.png[/img]

[b]必需前置：Jlib[/b]。请订阅并在游戏模组菜单启用 Jlib，然后重启游戏。多个 Mod 共用一份 Jlib，不再分别内嵌。

[h1]ONI MCP Server[/h1]

欢迎来到 ONI MCP Server！把《缺氧》接到本机 Model Context Protocol 的桥梁。
有了这个模组，[b]AI 客户端可以读取殖民地，并且只在你开放的工具范围内行动。[/b]

把 Claude、Cursor、Codex 或 Gemini 指向本机服务。告警、管道、电网和复制人状态都能看见，模拟线程不会被遥测堵住。

[h2]当前阶段: v0.2.6[/h2]
[i]本地服务已在实际使用。工具覆盖和客户端提示仍会随版本调整。[/i]
[i]其他模组可能改变这些工具读到的数据。请把 Jlib 排在本模组前面再启用。[/i]

[h3]MOD 拓展接口 (API)[/h3]
玩家需要共享库 [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]Jlib[/url]。先订阅并启用它，再启用 ONI MCP Server，然后重启游戏。
开发者通过 [url=https://www.nuget.org/packages/LIghtJUNction.Jlib]LIghtJUNction.Jlib[/url] 引用，并设置 ExcludeAssets="runtime"。NuGet 包不能代替创意工坊里的前置模组。
对外接口是 MCP 端点加上 oni:// 资源。从 oni://tools/manifest 看当前工具清单。

[h3]客户端能碰到什么[/h3]
载入存档后可以读取殖民地状态、复制人检查、文本地图、建筑定义和库存。
聚合工具覆盖世界读取、镜头与图层、建造计划、挖掘清扫订单，以及服务器控制。
大规模挖掘、拆除和读档保持显式确认。它不是负责整局战役的自动玩家。

[h3]操作指南与体验优化 (QoL)[/h3]
服务离开模拟线程运行。殖民地资源要等存档载入后才出现。

* 浏览器首页：http://localhost:8788/
* MCP 端点：http://localhost:8788/mcp/
* 主菜单就会启动服务。oni:// 殖民地工具需要已载入的存档。
* 鉴权默认关闭。端口离开本机前，请在 OniMcpConfig.json 里打开 AuthEnabled。

[h3]由 onim 完成[/h3]
本模组来自 [url=https://github.com/LIghtJUNction/OniMods]OniMods[/url]。onim 是这个仓库里的工具。它把缺氧 Mod 的开发、测试和创意工坊发布连成一条直线。

onim dev 放进游戏里试。onim build 打包。onim publish 发到 Steam。

[h3]开源贡献[/h3]
非常欢迎社区贡献！
如果你有优化、Bug 修复或新功能建议，欢迎提交 Pull Request。

请确保你的提交条理清晰并在必要处附带说明文档。

项目地址：[url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/OniMcp]GitHub 仓库传送门[/url]

[h3]免责声明[/h3]
请注意本模组仍在紧密迭代中，我们将保持频繁更新与维护。
我们非常重视大家的反馈与建议！
问题请报到 GitHub。客户端只在读取时，请保持游戏暂停。

[h3]联系我们[/h3]
提问和缺陷请送到 [url=https://github.com/LIghtJUNction/OniMods/issues]GitHub Issues[/url]。

祝游戏愉快。复制人仍然需要氧气，模型只是来参观的。

如果这份工作对你有用，欢迎支持 OniMcp 的后续测试：
[url=https://donate.lmm.best/?project=onimcp]捐赠[/url]
