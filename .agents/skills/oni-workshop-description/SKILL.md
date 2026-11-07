---
name: oni-workshop-description
description: 为缺氧 (Oxygen Not Included) Steam 创意工坊模组生成或改写具有纯正社区风味的图文介绍页面（BBCode/Markdown），包含头部横幅、阶段警告、Supported DLC 文字说明、API/架构、快捷键与 QoL、Roadmap、贡献指南、免责声明及幽默结尾动画。
---

# 缺氧 Mod 创意工坊社区风味页面设计技能 (ONI Workshop Description Flavor)

本技能指导 Agent 生成具有极高辨识度、专业感和社区号召力的 Steam 创意工坊模组描述页面。该风格源自《缺氧》知名模组 **Oxygen Not Included Together (ONIT)**。

---

## 一、风味精髓与视觉架构 (Flavor Anatomy)

纯正的社区风味描述由以下 11 个模块有机组合而成：

```
+-------------------------------------------------------------+
| 1. 顶部视觉大图 / 横幅 Banner ([img]...[/img])                |
+-------------------------------------------------------------+
| 2. 核心吸引力标语 (Elevator Pitch & Hook)                     |
|    - 热情迎宾句: "Welcome to [Mod Name]! The definitive..."  |
|    - 粗体一句话核心玩法: "[b]all players share controls...[/b]"|
|    - 情感共鸣句: "See where dupes are... and let dupes thrive"|
+-------------------------------------------------------------+
| 3. 当前开发阶段与警示 (Current Stage & Caveats)               |
|    - [h2]Current Stage: vX.Y.Z [Alpha/Beta][/h2]            |
|    - 斜体 Bug 心理预期: [i]expect bugs and maybe crashes[/i]  |
|    - 斜体 兼容性提醒: [i]use other mods at own discretion[/i]  |
+-------------------------------------------------------------+
| 4. DLC 兼容性说明 (Supported DLC) |
|    - [h3]Supported DLC:[/h3]                                |
|    - 按实际支持范围填写文字，不使用 Sgt_Imalas 的兼容性图标|
+-------------------------------------------------------------+
| 5. 开发者与模组接口 (MODDING API / SDK)                       |
|    - NuGet SDK、模块化通信、示例 Mod 链接                     |
+-------------------------------------------------------------+
| 6. 同步/特性进度看板 (Features / Sync Matrix / Roadmap)       |
|    - Trello / GitHub Projects 公开路线图链接                  |
+-------------------------------------------------------------+
| 7. 操控指南与体验优化 (Controls & Quality of Life)             |
|    - 玩家光标/操作意图、标记涂鸦快捷键、一键重置等             |
+-------------------------------------------------------------+
| 8. 开源与社区共建 (Contributions)                            |
|    - PR 提交指引、文档要求、GitHub 仓库直链                   |
+-------------------------------------------------------------+
| 9. 持续迭代与免责声明 (Disclaimer)                           |
|    - 积极维护承诺、反馈渠道说明                              |
+-------------------------------------------------------------+
| 10. 玩家社区与幽默结语 (Community & Closing Joke)             |
|    - Discord 社区链接                                       |
|    - 标志性冷幽默金句 (如 "When together oxygen is not included")|
|    - 复制人魔性反应 GIF 动图 ([img]...[/img])                |
+-------------------------------------------------------------+
| 11. 赞助通道 (Tip Jar / Sponsor)                             |
|    - Ko-Fi / 爱发电 / GitHub Sponsors 链接                   |
+-------------------------------------------------------------+
```

---

## 二、GitHub 现成图床与徽章资源速查 (可以直接复用)

社区作者已经将高清矢量/位图资产托管在 GitHub 上，Steam 工坊直接支持解析这些 raw 图链：

### 1. 顶部视觉大图 / 横幅 (Header Banner) ⭐极其重要防裂图规范

> [!CAUTION]
> **绝对禁止使用 404 或未验证的图片 URL！**
> Steam 工坊详情页若遇到 404 链接，会在页面正上方最显眼处展示一个破碎图片图标（白纸撕角与绿山裂缝），极度破坏页面美感。
> - **严禁使用临时/历史分支**（如已合并删除的 `release/*` 分支）。
> - **必须统一使用 `main` 分支验证可用的 Raw 图链**。
> - **若该 Mod 无远程可用图床，必须直接删除最顶部的 `[img]...[/img]`**，宁可不要 Banner 也绝不能放死链！

| 模组名称 | 官方/仓库位置 | GitHub Raw 稳定直链（200 OK，已验证） |
| :--- | :--- | :--- |
| **Jlib** | `LIghtJUNction/Jlib` | `https://raw.githubusercontent.com/LIghtJUNction/Jlib/main/Jlib/preview.png` |
| **CycleTrim** | `LIghtJUNction/OniMods` | `https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/mods/CycleTrim/preview.png` |
| **OniMcp** | `LIghtJUNction/OniMods` | `https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/mods/OniMcp/preview.png` |
| **OniOnline** | `LIghtJUNction/OniMods` | `https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/mods/OniOnline/preview.png` |

### 2. Supported DLC 兼容性说明

使用文字说明模组实际支持的游戏本体和 DLC；发布前核实支持范围。不要使用 Sgt_Imalas 的兼容性图标或其变体。

### 3. 常用动图与表情包 (Closing Reaction GIFs)
- 奔跑/惊慌复制人经典循环 GIF：
  `https://i.ibb.co/LXdJf9K9/I3P8A6f.gif`

---

## 三、双语模板样例 (直接复制与填空)

Steam 创意工坊支持按语言分开提交，中文与英文应保持 1:1 对齐风味：

### 1. 英文模板 (`steam-description-en.md`)
```bbcode
[img]YOUR_BANNER_URL_HERE[/img]

Welcome to [Mod Name]! The definitive [multiplayer/automation/utility] experience!
With this mod, [b]all players share controls over a single colony.[/b]

See where dupes are, see what they are doing, collaborate together with friends or strangers and let your dupes thrive

[h2]Current Stage: v0.7.3 Alpha[/h2]
[i]This is an alpha stage mod, so expect there to be bugs and maybe crashes sometimes[/i]
[i]Use other mods at your own discretion as they may cause issues and instability[/i]

[h3]Supported DLC:[/h3]
[Fill in verified base game and DLC compatibility.]

[h3]MODDING API[/h3]
This mod includes a modding API so any of you modders out there can also add custom functionality to your own mods!
You can find the API on [url=https://www.nuget.org/]NuGet[/url]
Example Mod: [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3286559448]BathTub[/url]

[h3]What is synchronized?[/h3]
There is a publicly available [url=https://trello.com/]Trello board / Project Roadmap here[/url] that shows you [i]what is synchronized, what features are complete, whats being worked on, what will be in the next update etc.[/i]

[h3]Controls and Quality of Life[/h3]
The mod includes some extra quality of life features to allow easier collaboration with other players:

* Player cursors show where a player is pointing AND the icon of their cursor shows what action they are trying to perform (Digging, harvesting, prioritizing etc)

* Pinging and drawing [i](fully synchronized)[/i]
 - CTRL + Right click to ping
 - CTRL + Right click + Drag to draw
 - CTRL + Left click a drawing to delete the drawing (any player can delete any drawing)

* Shift + R will reset your chatbox to default settings.

[h3]Built with onim[/h3]
This mod comes from [url=https://github.com/LIghtJUNction/OniMods]OniMods[/url]. onim is the toolchain in that repo. It takes an Oxygen Not Included mod straight through development, testing, and Workshop publishing.

onim dev tries it in game. onim build packs it. onim publish sends it to Steam.

[h3]Contributions[/h3]
Contributions are welcome!
If you have improvements, fixes, or new features, feel free to open a Pull Request.

Please make sure your changes are clear and well-documented where necessary.

You can find the [url=https://github.com/LIghtJUNction/OniMods]Github repo here[/url]

[h3]Disclaimer[/h3]
Please remember that this mod is under active development and will receive regular updates and improvements.
Your feedback and suggestions are highly appreciated.
The mod supports the base game and DLC, report any issues you run into!

[h3]Contact[/h3]
[url=https://discord.gg/]Join our discord community[/url] to connect with other players, suggest features for the mod, and stay up-to-date.

That's about it, have fun out there and remember. When together oxygen is not included.
[img]https://i.ibb.co/LXdJf9K9/I3P8A6f.gif[/img]

Like what I'm doing? You can support my work:
[url=https://ko-fi.com/]Ko-Fi[/url]
```

### 2. 中文模板 (`steam-description-zh.md`)
```bbcode
[img]YOUR_BANNER_URL_HERE[/img]

欢迎来到 [模组名称]！终极[联机/自动化/工具]游玩体验！
有了这个模组，[b]所有玩家将共同掌控同一个殖民地。[/b]

看清复制人们身在何处、正在做什么，与好友或路人默契协作，让你们的复制人们欣欣向荣！

[h2]当前阶段: v0.7.3 Alpha[/h2]
[i]本模组目前处于 Alpha 测试阶段，可能会遇到 Bug 或偶尔崩溃[/i]
[i]请谨慎搭配其他第三方模组使用，部分模组可能引发冲突或稳定性问题[/i]

[h3]支持的 DLC:[/h3]
[填写已核实的游戏本体与 DLC 兼容范围。]

[h3]MOD 拓展接口 (API)[/h3]
本模组自带模组开发接口（Modding API），任何模组开发者都可以为自己的 Mod 轻松接入支持！
你可以在 [url=https://www.nuget.org/]NuGet[/url] 获取 SDK 包。
示例 Mod: [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3286559448]BathTub[/url]

[h3]同步与功能开发进度[/h3]
我们提供了公开的 [url=https://trello.com/]开发看板与路线图[/url]，你可以在这里随时查看[i]已同步机制、已完成特性、正在开发的功能以及下个版本的更新预告[/i]。

[h3]操作指南与体验优化 (QoL)[/h3]
模组包含多项贴心的协同与便利功能，让你与队友的配合更加丝滑：

* 玩家光标：实时显示队友指向的位置，光标图标还会展示对方当前正在执行的操作（挖掘、收获、调整优先级等）

* 标记与涂鸦 [i](全量网络同步)[/i]
 - CTRL + 鼠标右键：快速发出位置标记（Ping）
 - CTRL + 鼠标右键 + 拖拽：在地图上直接画线涂鸦沟通规划
 - CTRL + 鼠标左键点击图线：擦除涂鸦（所有玩家均可清除任意标记）

* Shift + R：一键重置聊天框位置与默认设置。

[h3]由 onim 完成[/h3]
本模组来自 [url=https://github.com/LIghtJUNction/OniMods]OniMods[/url]。onim 是这个仓库里的工具。它把缺氧 Mod 的开发、测试和创意工坊发布连成一条直线。

onim dev 放进游戏里试。onim build 打包。onim publish 发到 Steam。

[h3]开源贡献[/h3]
非常欢迎社区贡献！
如果你有优化、Bug 修复或新功能建议，欢迎提交 Pull Request。

请确保你的提交条理清晰并在必要处附带说明文档。

项目地址：[url=https://github.com/LIghtJUNction/OniMods]Github 仓库传送门[/url]

[h3]免责声明[/h3]
请注意本模组仍在紧密迭代中，我们将保持频繁更新与维护。
我们非常重视大家的反馈与建议！
模组同时兼容本体与各大 DLC，遇到任何问题欢迎向我们反馈汇报。

[h3]联系我们[/h3]
[url=https://discord.gg/]加入我们的 Discord 玩家社区[/url]，与其他小伙伴联机开黑、提交新想法，并获取第一手更新动态！

祝大家游戏愉快，时刻牢记：聚在一起，缺氧也快乐。
[img]https://i.ibb.co/LXdJf9K9/I3P8A6f.gif[/img]

如果喜欢我的工作，欢迎赞助支持：
[url=https://ko-fi.com/]Ko-Fi[/url]
```

---

## 四、自动化脚本工具使用指南

技能内置了开箱即用的生成与校验脚本 `.agents/skills/oni-workshop-description/scripts/generate_workshop_description.py`。

### 1. 快速命令行生成并预览
```bash
python3 .agents/skills/oni-workshop-description/scripts/generate_workshop_description.py \
  --name "OniOnline" \
  --name-zh "OniOnline 联机框架" \
  --stage "v0.5.0 Alpha" \
  --dlc all \
  --github "https://github.com/LIghtJUNction/OniMods"
```

### 2. 直接输出到 Mod 文档目录
```bash
python3 .agents/skills/oni-workshop-description/scripts/generate_workshop_description.py \
  --name "OniOnline" \
  --name-zh "OniOnline" \
  --stage "v0.5.0 Preview" \
  --dlc all \
  --output-dir "mods/OniOnline/docs"
```

脚本会自动校验 Steam 的 **8,000 字符限制** 以及 BBCode 闭合标签。

---

## 五、发布与同步工作流 (配合 onim)

生成描述文件后，直接调用仓库既有的 `oni-workshop-publish` 技能，通过 `onim` CLI 更新工坊页面，**无需打开浏览器，也不会触碰游戏核心 ZIP**：

```bash
# 提交双语描述到创意工坊页面
onim workshop page set <WorkshopID> \
  --description-file mods/<ModKey>/docs/steam-description-en.md \
  --chinese-description-file mods/<ModKey>/docs/steam-description-zh.md \
  --change-note "Update workshop description with community flavor"
```
