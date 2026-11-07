[b]前置模组：Jlib。[/b] 在模组菜单订阅并启用 Jlib，然后重启缺氧。本模组使用共享前置，不再内嵌一份。

[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/CycleTrim/preview.png[/img]

欢迎来到 CycleTrim！一个极具颠覆性的实验性性能优化模组。
有了这个模组，[b]底层算法会被重新拆开，能搬上 GPU 的运算就尽量搬上 GPU。[/b]

现在还在早期开发。顺序写在前面：先加速，再兼容。

[h2]当前阶段: v0.4.0 早期[/h2]
[i]这是实验版本。速度优先，存档、模组和其他系统的兼容还排在后面。[/i]
[i]请只用可丢弃的测试存档。不要把它当成已经稳定的后期优化补丁。[/i]

[h3]支持的 DLC:[/h3]
[img]https://raw.githubusercontent.com/Sgt-Imalas/Sgt_Imalas-Oni-Mods/refs/heads/master/Compat_All.png[/img]

[h3]下个版本：实验拆掉一格一物与 GPU 泊松压力投影[/h3]
原版每个格子只留一个元素和一份质量。两种物质相遇时不能在格子里共存，旧物质必须被挤到别的格子。这一步要搜索邻格、走分支，还要决定谁先进入。现代 CPU 的宽向量指令和 GPU 最怕这种发散，最擅长的是每个格子做同一套浮点乘加。

[b]1. 旧版原版算法（串行网格遍历 + 仲裁排挤）：[/b]
串行供受体质量迁移（受 ElementRecord.flow 控制，单次截断12.5%），结合深度邻格排挤搜索（displace_gas / displace_liquid）。强分支与时序依赖导致 CPU 缓存失效与 GPU 线程束分化。

[b]2. 新版 GPU 算法（研发中）：[/b]
变密度连续流体泊松投影（Red-Black Gauss-Seidel with SOR 压力迭代）结合多组分界面通量输运，利用显存屏障与无散投影消除邻格分支搜索。

当前包还不是这套算法。OniSim 仍是一格一物。压力求解也不会直接变成一次简单乘法。Stage 2 数值参考（残差 1e-7、散度收敛、零速度不动点等 6 项测试）已在独立实验分支通过，生产内核保持隔离，疾病、建筑和存档格式不跟这一步一起改。先加速，再严格进行行为兼容性验收。

完整分析：[url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/docs/next-cell-representation.md]一格一物为什么在现代硬件上是负资产[/url]

[h3]眼下构建里已经有的[/h3]
忙碌复制人、静止小动物和拾取扫描会少做重复工作。这些是早期裁剪，不是最终形态。
OniSim 原生核心随包提供，但默认不接管模拟。普通殖民地继续使用官方 SimDLL；只有在可丢弃的测试存档中才显式开启实验选项。
仍需要 [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]Jlib[/url]，并把它排在 CycleTrim 前面，然后重启。
选项在模组菜单的 CycleTrim 里，下次启动才生效。OniSim 默认关闭，必须显式开启新的实验性选项；启动前设置 CYCLETRIM_ONISIM_ENABLE=0 会强制留在原版 SimDLL。映射不能在游戏中途热撤销。

[h3]操作指南与体验优化 (QoL)[/h3]
默认保留官方模拟核心；OniSim 是需要手动开启的实验路径。

* 订阅并启用。重启一次，让 Jlib 和选项页一起加载。
* 普通殖民地保持 OniSim 关闭；只在可丢弃的测试存档中开启。
* 管道动画、电网图层和状态机复用都可以在选项里单独收窄，改完要重启。

[h3]由 onim 完成[/h3]
本模组来自 [url=https://github.com/LIghtJUNction/OniMods]OniMods[/url]。onim 是这个仓库里的工具。它把缺氧 Mod 的开发、测试和创意工坊发布连成一条直线。

onim dev 放进游戏里试。onim build 打包。onim publish 发到 Steam。

[h3]开源贡献[/h3]
非常欢迎社区贡献！
如果你有优化、Bug 修复或新功能建议，欢迎提交 Pull Request。

请确保你的提交条理清晰并在必要处附带说明文档。

项目地址：[url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/CycleTrim]GitHub 仓库传送门[/url]

[h3]免责声明[/h3]
请注意本模组仍在早期实验中，更新会很频繁。
我们非常重视大家的反馈与建议！
先加速，再兼容。崩溃、存档异常和模组冲突请报到 GitHub，并请使用测试存档。

祝游戏愉快。先把周期跑起来。氧气和兼容性，都还在后面。
[img]https://i.ibb.co/LXdJf9K9/I3P8A6f.gif[/img]
