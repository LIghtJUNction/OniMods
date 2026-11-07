[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/.agents/skills/oni-workshop-description/assets/ai-generated-warning.png[/img]
[h1]WARNING[/h1]
[b]THIS MOD IS ENTIRELY AI-GENERATED[/b]

[h1]⚠ 高风险实验模组：仅供实验和尝鲜 ⚠[/h1]
[b]不适合长期稳定游玩！请勿用于珍贵的正式存档。[/b]
CycleTrim 正在重做底层模拟，可能发生崩溃、物质丢失、异常流动、存档损坏或与其他模组冲突。请先备份原存档，并只在可丢弃的测试存档上体验。
[b]未来实验性存档格式可能无法被原版游戏或旧版模组读取；不要假设可以卸载后继续游玩。[/b]

[h2]让一格容纳更多物质[/h2]
CycleTrim 正在探索不同的缺氧模拟：气体和液体可以在同一格中共存、流动和混合，同时尝试利用 GPU 加速计算。下面是开发版本的[b]游戏实拍[/b]，展示的是实验效果，不代表当前创意工坊版本已完整提供这些功能。

[h3]气体混合：一格里的氧气和二氧化碳[/h3]
[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/CycleTrim/docs/images/experimental-gas-mix.png[/img]
测试画面显示一格中两种气体的比例。扩散、分层和质量守恒仍需完整实机验收。

[h3]液体混合：水和污染水[/h3]
[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/CycleTrim/docs/images/experimental-liquid-mix.png[/img]
测试画面显示水与污染水出现在同一格中；纹理、比例和流动效果仍在调整。

[h3]多组分信息卡片[/h3]
[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/CycleTrim/docs/images/experimental-species-card.png[/img]
实验界面尝试分别展示每种物质。截图中的分压为 0：压力与分压数据尚未完成，不能作为正确的物理读数。

[h2]正在开发[/h2]
[list]
[*]三种及以上物质在同一格共存，贯通流动、扩散、交换和显示。
[*]气液管道一格多物：存储、输送、建筑输入输出、显示和存读档。目前尚未完成，也没有可展示的实机截图。
[*]保存并恢复全部组分，以及实验性存档格式的迁移。
[*]GPU 热扩散、压力和多组分计算，以及减少 CPU 与显卡之间的数据传输。
[*]多组分纹理、信息卡片、密度分层和气泡上浮的实机视觉与物理验收。
[/list]
上述内容是开发目标，不保证已经发布或能在你的存档中正常工作。GPU 加速和帧率提升取决于实际硬件、场景及当前构建；不承诺特定帧数。

[h2]安装与反馈[/h2]
需要前置模组 [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]Jlib[/url]：订阅并启用 Jlib 和 CycleTrim，然后重启游戏。设置可在模组选项中查看。

遇到崩溃、保存或物质异常时，欢迎在 [url=https://github.com/LIghtJUNction/OniMods/issues]GitHub Issues[/url] 报告，并附上游戏版本、复现步骤和不含私人信息的日志。

[url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/CycleTrim]查看源码与开发进度[/url]
