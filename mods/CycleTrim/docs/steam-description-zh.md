[h1]CycleTrim[/h1]

[b]轻量级性能优化 Mod（缺氧 / Oxygen Not Included）[/b]

[h2]用途与定位[/h2]
[list]
[*] 减少高频模拟路径中的重复计算，降低 CPU 压力。
[*] 保持行为兼容，降低副作用风险，优先关注长期存档稳定性。
[*] 目标是“可见的效率提升 + 可控的行为影响”。
[/list]

[h2]主要优化[/h2]
[list]
[*] 智能储液罐与气库：减少重复 automation 信号发送频率。
[*] 拾取候选计算：复用路径成本与候选筛选结果，降低重复查找开销。
[*] 忙碌复制人：高开销刷新做节流处理；空闲复制人维持即时响应。
[*] 静止小动物导航：降低重复导航探测调用，减少导航热点压力。
[*] 普通生物 Brain 调度：高帧率下限制原版 Creature 的普通调度频率；Dupe、自定义 Brain 组和 priority 路径保持原版。
[*] AsyncPathProber：仅对原版 Creature 做已应用快照去重，并按 worker/in-flight 动态背压；Minion、Robot、自定义 abilities 回退原版。
[/list]

[h2]兼容性[/h2]
[list]
[*] 支持 DLC 与主线内容。
[*] 设计上优先保持原版核心行为稳定。
[/list]

[h2]性能说明[/h2]
固定场景基准实测（非普适结论）：
[table]
[tr]
[th]场景[/th][th]指标[/th][th]优化前[/th][th]优化后[/th][th]变化[/th]
[/tr]
[tr][td]忙碌复制人节流[/td][td]FPS 中位数（CPU 热点场景）[/td][td]70.6[/td][td]85.1[/td][td]+20.6%[/td][/tr]
[tr][td]忙碌复制人节流[/td][td]Brain 总耗时[/td][td]5.087 s[/td][td]2.449 s[/td][td]-51.9%[/td][/tr]
[tr][td]静止小动物导航节流[/td][td]FPS 中位数[/td][td]81.23[/td][td]109.28[/td][td]+34.5%[/td][/tr]
[tr][td]静止小动物导航节流[/td][td]Navigator 热点总耗时[/td][td]826.852 ms[/td][td]47.970 ms[/td][td]-94.2%[/td][/tr]
[tr][td]UpdatePickups 缓存[/td][td]平均耗时[/td][td]41.279 µs[/td][td]35.708 µs[/td][td]-13.5%[/td][/tr]
[tr][td]普通 Creature Brain（合成）[/td][td]总调度调用[/td][td]43,200[/td][td]19,200[/td][td]-55.56%[/td][/tr]
[/table]

[b]注意：Brain 数据来自 240 FPS × 30 秒的独立函数级合成基准，不是游戏内 FPS。[/b]
其中 Creature 调用为 36,000 → 12,000（-66.67%），本机中位耗时为 255.580 ms → 113.289 ms（-55.67%，2.26x）；实际收益取决于存档和真实 Brain 工作量。

[b]PathProbe 合成矩阵也不是 FPS：[/b] 0/25/50/75/90% 目标命中率下，10,000 请求实际执行 10000/7701/5501/3301/2000 次；理论工作下降 0/22.99/44.99/66.99/80.00%。缓存最多连续跳过 8 次后强制刷新。

可使用 `benchmark` 技能，让 Agent 自动跑完整基准测试并返回标准化 JSON 结果。

[h2]详情与下载[/h2]
[url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md]查看详情（中文）[/url]
