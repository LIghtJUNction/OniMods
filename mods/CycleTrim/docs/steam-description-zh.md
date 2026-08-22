[h1]CycleTrim[/h1]

[b]轻量级性能优化 Mod（缺氧 / Oxygen Not Included）[/b]

[h2]v0.3.1 修复[/h2]
CycleTrim 之前只用 `currentChore != null` 判断复制人是否忙碌。ONI 空闲时仍会保留一个非空的 `IdleChore`，所以拾取和差事刷新也受到了节流。

v0.3.1 排除 `IdleChore`。空闲复制人恢复原版即时刷新；真正忙碌的复制人继续使用节流。

最新 ONI 或其他 Mod 改写 `Manager.TickFrame()` 时，CycleTrim 现在只跳过异步路径探针配额优化，不再抛异常并禁用整个 Mod。

[h2]这个 Mod 改了什么[/h2]
CycleTrim 减少六条高频模拟路径中的重复计算。它不改复制人优先级、日程、路径规则或差事条件。

[h2]主要优化[/h2]
[list]
[*] 智能储液罐与气库：减少重复 automation 信号发送频率。
[*] 拾取候选计算：复用路径成本与候选筛选结果，降低重复查找开销。
[*] 忙碌复制人：高开销刷新做节流处理；`IdleChore` 明确绕过节流，空闲复制人维持原版即时响应。
[*] 静止小动物导航：降低重复导航探测调用，减少导航热点压力。
[*] 普通生物 Brain 调度：高帧率下限制原版 Creature 的普通调度频率；Dupe、自定义 Brain 组和 priority 路径保持原版。
[*] AsyncPathProber：仅对原版 Creature 做已应用快照去重，并按 worker/in-flight 动态背压；Minion、Robot、自定义 abilities 回退原版。
[/list]

[h2]兼容性[/h2]
支持本体和 DLC。复制人、Robot、自定义 Brain 组和优先级路径在补丁不适用时直接走原版代码。

[h2]性能说明[/h2]
固定场景基准实测（非普适结论）：
[list]
[*] 忙碌复制人节流：FPS 中位数 70.6 → 85.1（+20.6%）；Brain 总耗时 5.087 s → 2.449 s（-51.9%）。
[*] 静止小动物导航节流：FPS 中位数 81.23 → 109.28（+34.5%）；Navigator 热点总耗时 826.852 ms → 47.970 ms（-94.2%）。
[*] UpdatePickups 缓存：平均耗时 41.279 µs → 35.708 µs（-13.5%）。
[*] 普通 Creature Brain 合成基准：总调度调用 43,200 → 19,200（-55.56%）。
[/list]

[b]注意：Brain 数据来自 240 FPS × 30 秒的独立函数级合成基准，不是游戏内 FPS。[/b]
其中 Creature 调用为 36,000 → 12,000（-66.67%），本机中位耗时为 255.580 ms → 113.289 ms（-55.67%，2.26x）；实际收益取决于存档和真实 Brain 工作量。

[b]PathProbe 合成矩阵也不是 FPS：[/b] 0/25/50/75/90% 目标命中率下，10,000 请求实际执行 10000/7701/5501/3301/2000 次；理论工作下降 0/22.99/44.99/66.99/80.00%。缓存最多连续跳过 8 次后强制刷新。

[h2]详情与下载[/h2]
[url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md]查看详情（中文）[/url]
