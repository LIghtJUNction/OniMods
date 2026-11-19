[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/.agents/skills/oni-workshop-description/assets/ai-generated-congratulations.png[/img]
[h1]恭喜！[/h1]
[b]这个 Mod 完全由 AI 生成！[/b]
该声明指本项目的 AI 开发方式。第三方库和原版游戏素材各有原作者，继续适用各自署名与许可。

[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/mods/OniOnline/preview.png[/img]

[h1]OniOnline · 大厅、聊天与联机开发框架[/h1]
[b]默认玩法提供大厅与文字聊天；世界模拟需要另行适配的玩法 Mod。[/b]
待发布目标为 0.5.3，协议 11；0.5.3 尚待工坊上传确认。必需前置为 [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]Jlib[/url]；本轮配套使用 0.3.10。基础会话不要求 CycleTrim 或 DLC。实际工坊包版本以安装包为准。

[h2]从哪里开始[/h2]
订阅并在 Mods 菜单启用 Jlib 和 OniOnline，重启游戏。
[list]
[*]主菜单点“浏览大厅”，选择 Steam / 局域网或输入大厅码。
[*]点“创建大厅”进入连接表单，填写昵称、大厅名；网络设置中选择传输、端口和可选密码，再确认创建。
[*]在房间复制大厅码。对方加入聊天玩法后进入房间聊天界面。
[*]已连接并载入殖民地后，用“通讯”气泡或默认 Ctrl+T 打开聊天，Esc 关闭。快捷键可重绑定。
[/list]
[b]上述新建入口和加入后的房间跳转已在源码接通，本轮 U60 实机与双端验收尚待完成。[/b] 未连接时不显示聊天 HUD。

[h2]现在有哪些能力[/h2]
[list]
[*]大厅、成员列表、文字聊天和断线重连使用独立会话层，不受 744825 原生适配门禁限制。
[*]状态区分连接尝试、重试等待与最终失败，并保留失败原因；聊天发送失败保留输入。
[*]连接核验 Mod 清单及兼容性。下载工坊内容后仍需确认启用，并在需要时重启加载。
[*]SDK 可承载明确注册的消息和状态通道；不会自动复制任意 Mod 内存或上传本地开发 Mod。
[/list]
SDK 发送成功的语义是传输层接受消息；远端处理与逐个参与者的送达确认需要调用者自己的协议。

[h2]U60 哪些功能不可用[/h2]
[b]在 U60（763796），原生行星、世界观察与原生星图协作不可用。[/b] 当前原生适配白名单仍只有 744825；星图还要求 Spaced Out!。即使在受支持版本，也要有额外玩法 Mod 主动注册、选择并启用世界服务。

世界/观察按钮会在能力不可用时禁用并显示原因。U60 提示为“当前游戏版本尚未支持此功能。”，错误码 native_adapter_unavailable。

单独安装本包不会提供完整殖民地迁移、复制人或资源运输、火箭运输、离线追赶。默认 ChatOnly 不自动启动世界服务。Ctrl+V 语音仍属开发能力，麦克风与双端体验未验收。试用未知玩法前请保留存档副本。

[h2]验证边界[/h2]
独立宿主通过 4,808 个断言：包含真实回环 TCP、三个独立客户端进程、限流延迟与断线重连。游戏世界/时间边界使用替身。
[b]U60 两台设备或两个真实游戏客户端的多人验收仍待完成。[/b] Steam 内容下载、语音和新 UI 入口仍须分别实机检查。

局域网默认 TCP 27140、UDP 27141，直连格式 oni://IP:27140；Steam 格式 oni://steam/大厅ID。局域网面向可信网络，密码不加密 TCP 数据。

[h2]框架示意与文档[/h2]
手绘图说明框架和工具链；玩法状态见上方能力说明。
[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/.agents/skills/oni-workshop-description/assets/onim-illustrated-onionline.gif[/img]
[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/OniOnline/assets/onionline-logo-v1.png[/img]

[url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniOnline/README.md]使用入口与完整边界[/url] · [url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniOnline/docs/SDK.md]玩法 SDK[/url] · [url=https://github.com/LIghtJUNction/OniMods/issues]问题反馈[/url]
请附双方版本、玩法、传输方式与错误码，移除密码、私人地址和账号信息。
