[b]必需前置：Jlib[/b]。请订阅并在游戏模组菜单启用 Jlib，然后重启游戏。多个 Mod 共用一份 Jlib，不再分别内嵌。

[h1]ONI MCP Server[/h1]

[b]《缺氧》Model Context Protocol (MCP) 服务端模组[/b]

将大语言模型（Claude、Gemini、Cursor 等）接入《缺氧》，实现对殖民地的实时感知、工程诊断与安全辅助。

[h2]【必需前置】Jlib[/h2]
本模组依赖公共库 [b]Jlib[/b]（创意工坊 ID: [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]3835083839[/url]）。
请先订阅 Jlib，并在游戏模组列表中置于 ONI MCP Server 之前启用。

[h2]核心功能[/h2]
[list]
[*] [b]标准 MCP 协议[/b]：完全兼容开放的 Model Context Protocol 规范，支持 Claude Desktop、Cursor、Gemini Antigravity、Codex 等主流客户端。
[*] [b]全景数据遥测[/b]：通过 virtual resource (oni://) 接口向 AI 开放地图网格、热场、流体与气体流动、电网负荷以及复制人身心状态数据。
[*] [b]智能诊断与工程辅助[/b]：借助大模型的推理能力，辅助排查管道堵塞、热量失控、食物危机与复杂自动化逻辑。
[*] [b]安全审计与权限隔离[/b]：感知优先，写入与控制接口具备明确范围约束与审计记录，禁止越权与非授权篡改。
[*] [b]非阻塞异步服务[/b]：后台独立处理网络请求，高频查询不干扰游戏主渲染与逻辑模拟。
[/list]

[h2]快速配置[/h2]
[olist]
[*] [b]安装启用[/b]：在工坊订阅 Jlib 与本模组，并在模组列表中勾选启用。
[*] [b]启动服务[/b]：载入任意存档后，本地服务自动在后台就绪（默认端口 8788）。
[*] [b]客户端连接[/b]：在外部 AI 客户端的 MCP 配置文件中填入该服务地址即可建立连接。
[/olist]

[h2]人工智能使用说明[/h2]
本模组的协议集成、接口虚拟文件系统与工具分发体系由前沿模型（包括 GPT-6、Astra、Opus 5.5）辅助开发，文档内容由 Gemini 润色与整理。

[h2]开源文档[/h2]
[url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/OniMcp]GitHub 项目主页与开发指南[/url]
