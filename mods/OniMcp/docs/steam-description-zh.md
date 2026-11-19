[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/.agents/skills/oni-workshop-description/assets/ai-generated-congratulations.png[/img]
[h1]恭喜！[/h1]
[b]这个 Mod 完全由 AI 生成！[/b]
该声明指本项目的 AI 开发方式。第三方库、协议规范和游戏素材各有原作者，继续适用各自署名与许可。

[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/mods/OniMcp/preview.png[/img]

[h1]OniMcp · 让客户端读懂你的殖民地[/h1]
OniMcp 为《缺氧》提供本地 MCP 服务。连接支持 MCP 的客户端后，可以读取殖民地状态、查找问题，并调用明确的游戏操作。模型与客户端由你选择。

[b]0.2.9 版本说明。必需前置：Jlib 0.3.10 或更新的兼容版本。[/b]
[url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]订阅 Jlib[/url] 后，在游戏 Mods 菜单同时启用两者并重启。多个依赖 Mod 共用一份 Jlib。工坊实际包版本以你安装的包为准。

[h2]三步开始[/h2]
[list]
[*]载入殖民地。服务可以在主菜单启动，但地图读取需要存档已经就绪。
[*]在 MCP 客户端添加 [code]http://localhost:8788/mcp/[/code]。浏览器状态页是 [code]http://localhost:8788/[/code]。
[*]先发现工具与资源，再读取当前状态：[code]world_editor command=read path=/active/colony/current.oni[/code]
[/list]

[h2]现在能做什么[/h2]
[list]
[*]通过公开的七个工具入口读取状态、操作镜头、规划建筑、下达任务并控制游戏。
[*]使用 world_editor 浏览虚拟目录，或通过 resources/read 读取 oni:// 资源；read_control 已不是公开工具。
[*]读取格子完整组分质量与温度；提供者缺席时明确报告不可用。
[*]使用保持合法 JSON 的状态预览，再按给出的路径读取完整文档。
[*]查看请求错误与任务状态，按当前会话的 tools/list 核对参数。
[/list]
格子资源示例：[code]oni://world/cell/81/111[/code]，请换成当前地图的坐标。公开入口为 world_editor、game_control、navigation_control、building_control、orders_control、server_control、benchmark。

[h2]数据和验证边界[/h2]
多组分压力、分压与体积分数各自带可用性标记。当前 OniSim 压力与分压报告为不可用，请按可用性标记解释数据。液体占格比例来自求解器的标称容量，表示占格份额。

本轮已在 U60 检查公开入口发现、当前状态 JSON 和格子读取。定向宿主回归通过 50 个断言；其余游戏操作仍需分别验收。最新六处后续提示已修正，实机复查待完成。自动游玩效果和帧率取决于客户端策略与实际游戏场景，需另外测量。

[h2]连接说明[/h2]
默认只监听本机。改为非回环监听时必须开启认证；Bearer token 不会加密传输。远程使用请自行配置 VPN 或 TLS 反向代理。MCP 协议版本为 2025-11-25，客户端需正常初始化会话。读档、重启和大范围修改前，应明确操作范围并确认。

[h2]保留的演示图片[/h2]
下列实拍来自仓库历史演示，展示当时的操作场景。
[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/docs/images/gpt-oni-demo/mcp-workflow.jpg[/img]
[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/docs/images/gpt-oni-demo/colony-building.jpg[/img]
[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/docs/images/gpt-oni-demo/colony-overview.jpg[/img]

开发流程手绘示意；测量结果另见验证记录。
[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/.agents/skills/oni-workshop-description/assets/onim-illustrated-onimcp.gif[/img]

[h2]文档与反馈[/h2]
[url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README.md]完整文档与接入说明[/url] · [url=https://github.com/LIghtJUNction/OniMods/issues]问题反馈[/url]
反馈请附游戏/Mod 版本、请求、错误和最小复现，移除令牌及私人账号信息。
[url=https://donate.lmm.best/?project=onimcp]支持开发与测试[/url]
