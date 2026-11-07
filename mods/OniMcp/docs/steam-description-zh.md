[h1]ONI MCP Server[/h1]

[b]把大语言模型 (Claude / Gemini / Cursor) 带入《缺氧》的智能服务端 Mod[/b]

想让 AI 成为你基地里的“首席工程顾问”吗？
[b]ONI MCP Server[/b] 是专为《缺氧》打造的 [b]Model Context Protocol (MCP)[/b] 桥梁。它让前沿的 AI 编程助手与大模型客户端能够实时读取殖民地全局数据、诊断工程难题，并在严格安全授权下协助你建设繁荣的太空基地！

[h2]⚠️【必需前置 Mod】[/h2]
本模组接入统一前置库 [b]Jlib[/b]（创意工坊 ID: [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]3835083839[/url]）。
[b]请先订阅 Jlib，并在游戏模组列表中将其排列在 ONI MCP Server 之前启用。[/b]

[h2]🤖 核心功能亮点[/h2]
[list]
[*] 🌐 [b]标准化 MCP 协议生态[/b]：完全遵循开放的 Model Context Protocol 标准，无缝对接 Claude Desktop、Cursor、Gemini Antigravity、Codex 等任意支持 MCP 的客户端。
[*] 🔍 [b]全景殖民地感知[/b]：AI 能够通过虚拟资源接口（`oni://`）秒级洞察全图元素分布、温度热场、电力网络、管道走向、小人健康与压力指标。
[*] 💡 [b]智能工程问诊与建议[/b]：遇到“为什么高压制氧过热”、“这根排气管为什么堵塞”、“食物为什么断崖式下跌”？让 AI 结合当前实时存档数据给你最专业的诊断分析！
[*] 🛡️ [b]坚如磐石的安全边界[/b]：所有游戏交互严格遵循只读感知优先，操作类指令带明确范围限制与审计日志，坚决不作弊、不破坏游戏平衡。
[*] ⚡ [b]高性能零干扰[/b]：异步 HTTP 服务与游戏主循环无缝衔接，即使 AI 频繁查询也不会拖慢游戏主线程。
[/list]

[h2]🚀 快速入门指南[/h2]
[olist]
[*] [b]安装与启用[/b]：订阅本 Mod 及前置 [b]Jlib[/b]，在游戏 Mod 列表中依次勾选并启用。
[*] [b]进入游戏[/b]：载入任意存档，本地 MCP 服务即在后台自动就绪（默认端口 `8788`）。
[*] [b]连接 AI 客户端[/b]：在你的 AI 客户端（如 Claude Desktop 或 Cursor）的 MCP 配置中填入服务端地址，即可开始与你的基地对话！
[/olist]

[h2]开源与开发者生态[/h2]
本模组完整开源，包含丰富的虚拟文件系统、工具集规范与开发指南：
[url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/OniMcp]GitHub 项目主页与开发文档[/url]
