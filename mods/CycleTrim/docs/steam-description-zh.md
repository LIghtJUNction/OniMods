[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/.agents/skills/oni-workshop-description/assets/ai-generated-congratulations.png[/img]
[h1]恭喜！[/h1]
[b]这个 Mod 完全由 AI 生成！[/b]

[h1]⚠ 高风险实验模组：仅供实验和尝鲜 ⚠[/h1]
[b]不适合长期稳定游玩！请勿用于珍贵的正式存档。[/b]
CycleTrim 正在重做底层模拟，可能发生崩溃、物质丢失、异常流动、存档损坏或与其他模组冲突。请先备份原存档，并只在可丢弃的测试存档上体验。
[b]未来实验性存档格式可能无法被原版游戏或旧版模组读取；不要假设可以卸载后继续游玩。[/b]

[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/mods/CycleTrim/docs/images/handdrawn-gpu-explainer-zh.gif[/img]
[i]手绘风格动图：展示开发中的目标架构。[/i]

[h2]让一格容纳更多物质[/h2]
CycleTrim 正在探索不同的缺氧模拟：气体和液体可以在同一格中共存、流动和混合，同时尝试利用 GPU 加速计算。下面是开发版本的[b]游戏实拍[/b]，展示的是实验效果，不代表当前创意工坊版本已完整提供这些功能。

[h2]为什么改变“一格一物”？[/h2]
原版网格的常用接口让一格主要对应一种元素和一份质量。不同物质相遇时，模拟和游戏逻辑需要处理占格、排挤与邻格选择。CycleTrim 正在尝试让一个格子记录多种物质各自的质量与状态，让氧气和二氧化碳、或水和污染水可以共享空间。

这不只是换一套流体公式：呼吸、建筑抽取、传感器、显示和存档都需要理解组分。我们正在逐项适配，不能假定原版所有逻辑自动支持混合物。

[h2]怎样搬到 GPU？[/h2]
CPU 负责游戏事件、建筑和复杂控制；GPU 对大量格子并行执行规则计算。开发版把部分网格数据整理成数组，在 GPU 上执行热扩散，以及符合条件的区域气体和液体计算。单元分别计算相邻面的交换，避免多个线程同时写同一个格子。计算结果仍需经 CPU 同步回游戏逻辑。

[h2]怎样减少数据搬运？[/h2]
如果每个算子都上传、读回整张地图，等待和复制可能抵消加速。开发版让区域气体的有界传输和热计算在一次 GPU 提交内接力；部分输入使用紧凑格式和变化块上传，并尽量压缩读回结果。目前每个时间步仍存在 CPU 上传、GPU 等待和结果读回，距离全流程显存驻留还有工作。

[h2]目前做到哪里？[/h2]
0.4.22 通过单进程协程重叠 CPU 气体任务与 GPU 液体读回，保持结果顺序；修复微量气体归零后恢复旧质量，并复用临时缓冲区。全部气体组分参与，输入变化时重算。需要 Jlib 0.3.8；原生配对基准通过，整机帧率另行实测。
[list]
[*]0.4.13 的气体传输按所有组分平等计算，不再把每格气体截成两种。液体组分平等与完整混合物存读档仍未完成。
[*]开发版时间步已接入全图 GPU 热扩散；符合条件的气体区域接入浓度扩散、密度梯度速度、有界传输与热计算；符合条件的液体区域使用 GPU 多组分扩散。部分条件不满足时仍有 CPU 处理或区域排除。
[*]已有算子质量/能量守恒测试和测试存档短运行记录。气体区域中的 GPU 算子可共享显存缓冲，但仍有上传、同步与读回；不是全部游戏模拟都已上 GPU。
[*]相变、气泡处理、部分液体流动、疾病、辐射、建筑交互和结果发布等仍依赖 CPU。气体传输现已保留每格三种及以上组分；建筑交互、液体和存读档仍需端到端验收。
[*]气压、分压和液体体积占比的发布值尚未完成，呼吸、植物、管道等兼容性仍需端到端验收。
[/list]
以上区分独立测试、开发构建和创意工坊发布版本；它们不是同一个完成状态。游戏内帧率提升尚未得到完整验证。

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
需要前置模组 [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]Jlib[/url] 0.3.8 或更新版本：订阅并启用 Jlib 和 CycleTrim，然后重启游戏。设置可在模组选项中查看。

遇到崩溃、保存或物质异常时，欢迎在 [url=https://github.com/LIghtJUNction/OniMods/issues]GitHub Issues[/url] 报告，并附上游戏版本、复现步骤和不含私人信息的日志。

[url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/CycleTrim]查看源码与开发进度[/url]
