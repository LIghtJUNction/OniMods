[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/.agents/skills/oni-workshop-description/assets/ai-generated-congratulations.png[/img]
[h1]恭喜！[/h1]
[b]这个 Mod 完全由 AI 生成！[/b]
第三方库与原版游戏素材各有原作者，继续适用其署名和许可。

[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/mods/CycleTrim/preview.png[/img]

[h1]⚠ 高风险实验模组：仅供实验和尝鲜 ⚠[/h1]
[b]不适合长期稳定游玩！请勿用于珍贵的正式存档。[/b]
CycleTrim 正在重做底层模拟，可能发生崩溃、物质丢失、异常流动、存档损坏或与其他模组冲突。请先备份原存档，并只在可丢弃的测试存档上体验。
[b]当前开发构建的混合存档已使用专用格式：版本 0x4f4d0001 与必需的 ONIMIX 扩展，需要支持该格式的 OniSim。受测旧版 OniSim 会拒绝读取；不保证原版能读取，也不能假定卸载或降级后可继续游玩。本轮变更尚未发布到创意工坊。[/b]

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
待发布目标为 [b]CycleTrim 0.4.25 + Jlib 0.3.10[/b]，工坊上传确认尚待完成。已有多组分库存和保存路径继续保留，本轮进一步减少热传导中的无效预扫与未用 GPU 资源。

[h3]这轮具体减少什么工作？[/h3]
[list]
[*]先用已有热区域的面积估计合格格子的最大可能数量。如果连这个上界也达不到原条件，就跳过必然失败的 CPU 全图预扫，继续原有热计算路径；求解规则保持原有算法。
[*]删除模拟 worker 未使用的 GPU 缓冲及三个计算管线。225,828 个含边界格子的场景少请求 17.229 MiB GPU 缓冲；实际显存驻留和整机收益仍需分别测量。
[/list]
四项新增回归及既有 CPU/GPU 热传导一致性用例通过，Linux 原生构建通过。当前隔离测试桌面使用软件渲染；RTX 4060 的整机帧率仍待同存档、同设置的硬件对照。三平台构建及静态门禁已通过；构建回执不包含游戏运行验收，Windows/macOS 尚未实机验收。
[list]
[*]气体传输按所有组分计算，存档扩展保留主、次和额外组分。没有任何正质量的第二或后续组分时，仍使用原 SIMSAVE v15；混合世界需要新格式读者。22 组分精确往返已有 host/FFI 证据，更多实机场景仍需验收。
[*]开发版时间步已接入全图 GPU 热扩散；符合条件的气体区域接入浓度扩散、密度梯度速度、有界传输与热计算；符合条件的液体区域使用 GPU 多组分扩散。部分条件不满足时仍有 CPU 处理或区域排除。
[*]已有算子质量/能量守恒测试和测试存档短运行记录。气体区域中的 GPU 算子可共享显存缓冲，但仍有上传、同步与读回；不是全部游戏模拟都已上 GPU。
[*]相变、气泡处理、部分液体流动、疾病、辐射、建筑交互和结果发布等仍依赖 CPU。气体传输现已保留每格三种及以上组分；建筑交互、液体和存读档仍需端到端验收。
[*]压力和分压明确标为不可用；液体占格份额按求解器标称容量计算并带独立可用性标记。U60 已读到气体格的已知液体占格值 0；正液体填充仍待实机验收。呼吸、植物、管道等适配继续逐项验证。
[/list]
2026-11-20 的开发检查点 8db2654 已在 Linux x64 U60/763796 通过严格版本、运行时和 ABI 检查。经 Steam 正常图形启动，在三复制人存档副本中自然形成的 6 个氧气/二氧化碳混合格，新建检查点并同进程重载后，完整组分的质量、温度和总质量 binary32 位一致，随后继续推进。见[url=https://github.com/LIghtJUNction/OniMods/blob/main/docs/cycletrim-u60-multicomponent-live-20261120.json]实机证据[/url]和[url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/OniSim/docs/save-format.md]存档格式与旧读者边界[/url]。

这份实机记录覆盖每格两种正质量组分；22 组分覆盖来自独立宿主/FFI。Linux U60/763796 已有上述实机记录，Windows/macOS 原生适配仍限 744825。更多组分、长时运行、DLC 与整机性能对照仍待完成。新的压力可用性及液体占格检查单独记录；0.4.25 工坊上传确认尚待完成。

[h3]气体混合：一格里的氧气和二氧化碳[/h3]
[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/CycleTrim/docs/images/experimental-gas-mix.png[/img]
测试画面显示一格中两种气体的比例。扩散、分层和质量守恒仍需完整实机验收。

[h3]液体混合：水和污染水[/h3]
[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/CycleTrim/docs/images/experimental-liquid-mix.png[/img]
测试画面显示水与污染水出现在同一格中；纹理、比例和流动效果仍在调整。

[h3]多组分信息卡片[/h3]
[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/CycleTrim/docs/images/experimental-species-card.png[/img]
历史实验界面展示了各组分。截图中的分压 0 是旧版未计算占位；当前接口用可用性标记报告未知压力。

[h2]正在开发[/h2]
[list]
[*]三种及以上物质在同一格共存，贯通流动、扩散、交换和显示。
[*]气液管道一格多物：存储、输送、建筑输入输出、显示和存读档。目前尚未完成，也没有可展示的实机截图。
[*]扩展混合存档的实机验收，尤其是每格三种及以上组分、长期运行和格式迁移。
[*]GPU 热扩散、压力和多组分计算，以及减少 CPU 与显卡之间的数据传输。
[*]多组分纹理、信息卡片、密度分层和气泡上浮的实机视觉与物理验收。
[/list]
上述目标按具体构建与场景逐项验收。GPU 加速和帧率需要在相同硬件、存档与设置下测量；本轮整机对照尚待完成。

[h2]安装与反馈[/h2]
需要前置模组 [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]Jlib[/url] 0.3.10 或更新的兼容版本：订阅并启用 Jlib 和 CycleTrim，然后重启游戏。设置可在模组选项中查看。

遇到崩溃、保存或物质异常时，欢迎在 [url=https://github.com/LIghtJUNction/OniMods/issues]GitHub Issues[/url] 报告，并附上游戏版本、复现步骤和不含私人信息的日志。

[url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/CycleTrim]查看源码与开发进度[/url]
