# CycleTrim 0.3.0

CycleTrim is a narrowly scoped performance mod for Oxygen Not Included.

CycleTrim 是一个针对《缺氧》具体热路径的性能优化 Mod。

## Production optimizations / 生产优化

1. Smart reservoirs send their automation output on spawn and then only when the output changes.  
   智能液库与气库在生成时初始化自动化输出，之后仅在输出变化时发送信号。
2. Fetch pickup candidates reuse cached cell costs and retain the best vanilla-ranked candidate for each tag and priority group. Vanilla eligibility and final ranking are preserved.  
   拾取候选选择复用格子路径代价，并为每个标签与优先级组保留原版排序下的最佳候选；原版资格与最终排序保持不变。
3. Only busy duplicants alternate expensive pickup refresh and chore reselection. Idle duplicants always refresh immediately, and `BrainScheduler.PrioritizeBrain` forces the next refresh to run immediately. Critters are not affected.  
   仅忙碌复制人会隔次执行高开销的拾取刷新与差事重选。空闲复制人始终立即刷新，`BrainScheduler.PrioritizeBrain` 也会强制下一次立即刷新。小动物不受影响。
4. Stationary critters alternate redundant `Navigator.UpdateProbe` calls. Movement, `forceUpdate`, occupation reporting, asynchronous path probing, all duplicants, and all non-critters retain vanilla behavior. The maximum delay is one brain update.
   静止小动物会隔次执行重复的 `Navigator.UpdateProbe`。移动、`forceUpdate`、占用报告、异步路径探测、所有复制人与所有非小动物对象都保留原版行为，最多延迟一次 Brain 更新。

If `PeterHan.FastTrack.GamePatches.FetchManagerFastUpdate` is detected, CycleTrim disables the overlapping fetch, busy-duplicant, and stationary-critter navigation patches.

如果检测到 `PeterHan.FastTrack.GamePatches.FetchManagerFastUpdate`，CycleTrim 会禁用重叠的拾取、忙碌复制人与静止小动物导航补丁。

## Fixed-scenario validation / 固定场景验证

Test scene: A01, cycle 1641, 15 duplicants, world 0, game window 665x508, camera view X35-55/Y71-91, speed 3x, using paired 30-second windows.

测试场景：A01，第 1641 周期，15 名复制人，world 0，游戏窗口 665x508，镜头 X35-55/Y71-91，3x 速度，使用成对的 30 秒窗口。

The baseline used the same Debug build with only the busy-duplicant throttle disabled; the other production optimizations for this line remained enabled. The candidate enabled the throttle.

基线使用同一 Debug 构建，仅关闭忙碌复制人节流；该路线其他已发布优化仍然开启。候选组开启该节流。

- Paired FPS: `74.9 -> 84.5` (+12.9%), `78.8 -> 89.5` (+13.6%), `70.6 -> 85.1` (+20.6%); median +13.6%.
- Warm-window hot paths: Brain `5.087s -> 2.449s` (-51.9%), PickupableSensor `2.147s -> 0.104s` (-95.1%), FindNextChore `1.434s -> 0.523s` (-63.5%).

这些是该固定存档、硬件和镜头下的增量结果，不是 vanilla-vs-mod 对比，也不保证所有殖民地都获得相同 FPS。

These are incremental results for this fixed save, hardware, and camera view. They are not a vanilla-vs-mod comparison and do not guarantee the same FPS on every colony.

## Stationary-critter throttle + diagnostics / 静止小动物节流与探针

Scenario: immutable A01 cycle 1641 auto-save, 15 duplicants, world 0, 1265x1548 game window, camera center `(45.5, 81.0)`, zoom 8, speed 3x, using Debug builds with identical aggregate instrumentation.

场景：不可变的 A01 第 1641 周期自动存档，15 名复制人，world 0，1265x1548 游戏窗口，镜头中心 `(45.5, 81.0)`，缩放 8，3x 速度，使用具有相同聚合探针的 Debug 构建。

The formal baseline was the 0.2.0 production patches plus the probe. Its 30-second windows measured FPS `92.334 / 81.230 / 80.525` (median `81.230`) and Navigator totals `868.892 / 783.839 / 826.852 ms` (median `826.852 ms`).

正式基线为 0.2.0 生产补丁加探针。30 秒窗口 FPS 为 `92.334 / 81.230 / 80.525`（中位数 `81.230`），Navigator 总耗时为 `868.892 / 783.839 / 826.852 ms`（中位数 `826.852 ms`）。

The candidate added only the stationary-critter patch. Its last three stable windows measured FPS `104.890 / 109.282 / 113.840` (median `109.282`) and Navigator totals `47.116 / 47.970 / 51.517 ms` (median `47.970 ms`). Median FPS increased by 34.5%, while the measured Navigator hotspot fell by 94.2%.

候选版仅新增静止小动物补丁。最后三个稳定窗口 FPS 为 `104.890 / 109.282 / 113.840`（中位数 `109.282`），Navigator 总耗时为 `47.116 / 47.970 / 51.517 ms`（中位数 `47.970 ms`）。中位 FPS 提升 34.5%，测得的 Navigator 热点下降 94.2%。

These scenario-specific incremental results are not a vanilla-vs-mod comparison and exclude loading and GC-spike windows. Brain remained the broader workload. The food sensor was deliberately not throttled for gameplay safety.

这些是该固定场景的增量结果，不是 vanilla-vs-mod 对比，并已排除加载与 GC 尖峰窗口。Brain 仍是更广泛的工作负载；出于游戏安全考虑，食物传感器未被节流。

To profile this path you can run the shared target-contract verification script:

要校验目标 API 合约，可执行以下脚本:

```bash
python3 scripts/verify_cycletrim_target_contract.py
python3 scripts/verify_cycletrim_target_contract.py /path/to/OxygenNotIncluded_Data/Managed/Assembly-CSharp.dll
```

## Memory and cache analysis / 内存与缓存分析

Scenario: immutable A01 cycle 1641 auto-save, 15 duplicants, world 0, 1265x1548 game window, camera center `(45.5, 81.0)`, zoom 8, speed 3x, using identical Debug probes and stable 30-second windows.

场景：不可变的 A01 第 1641 周期自动存档，15 名复制人，world 0，1265x1548 游戏窗口，镜头中心 `(45.5, 81.0)`，缩放 8，3x 速度，使用相同 Debug 探针与稳定的 30 秒窗口。

The per-`UpdatePickups` cell cache had a median logical hit rate of 24.37%, removing 24.37% of navigation calls; cache-off made 1.322x as many calls. Median `UpdatePickups` average time changed from `41.279 us` off to `35.708 us` on (-13.5%). Each update produced 6.29 hits, with an estimated `0.886 us` saved per hit. Median FPS changed from `66.964` to `80.200` (+19.8%).

每次 `UpdatePickups` 内的格子缓存逻辑命中率中位数为 24.37%，减少了 24.37% 的导航调用；关闭缓存后调用量为 1.322 倍。`UpdatePickups` 平均耗时中位数由关闭时的 `41.279 us` 降至开启时的 `35.708 us`（-13.5%）。每次更新平均命中 6.29 次，估算每次命中节省 `0.886 us`。FPS 中位数由 `66.964` 升至 `80.200`（+19.8%）。

The candidate dictionary pool reached a steady 100% hit rate. Pool-off allocated a median 9,576 dictionaries per 30 seconds (`319.2/s`) versus zero with pooling. Median `UpdatePickups` average time changed from `47.578 us` off to `35.708 us` on (-24.9%), the median collection count for each GC generation fell from 2 to 1 per 30 seconds, and median FPS changed from `73.437` to `80.200` (+9.2%).

候选字典池在稳定阶段达到 100% 命中率。关闭池化时，每 30 秒字典分配中位数为 9,576 次（`319.2/s`），开启池化时为零。`UpdatePickups` 平均耗时中位数由关闭时的 `47.578 us` 降至开启时的 `35.708 us`（-24.9%），每 30 秒每一代 GC 的收集次数中位数均由 2 降至 1，FPS 中位数由 `73.437` 升至 `80.200`（+9.2%）。

These are scenario-specific incremental results with loading excluded. Unity Mono's allocated-bytes API returned zero and was excluded; RSS/private memory varied too much for causal claims; hardware LLC counters were not measured because `perf` was unavailable. The cache remains scoped to one `UpdatePickups` call for safe invalidation and is not extended across updates.

这些是排除加载阶段后的特定场景增量结果。Unity Mono 的 allocated-bytes API 返回零，因此未纳入结论；RSS/私有内存波动过大，不用于因果声明；由于 `perf` 不可用，未测量硬件 LLC 计数器。为保证安全失效，缓存仍限定在单次 `UpdatePickups` 调用内，不跨更新扩展。

## Compatibility and build / 兼容与构建

- Supports base game and DLC content through `supportedContent: ALL`.
- Runtime contract checks are in `scripts/verify_cycletrim_target_contract.py`.
- 通过 `supportedContent: ALL` 支持基础游戏与 DLC 内容。
- 运行时契约检查位于 `scripts/verify_cycletrim_target_contract.py`。

```bash
onim build -m CycleTrim
onim dev -m CycleTrim
```

## Changelog / 更新日志

完整提交记录见: [CHANGELOG.md](CHANGELOG.md)

## 引用链接 / References

- 源码仓库: [github.com/LIghtJUNction/OniMods](https://github.com/LIghtJUNction/OniMods)
- 模组定义与构建配置: [CycleTrim.csproj](CycleTrim.csproj)
- 运行时契约校验脚本: [scripts/verify_cycletrim_target_contract.py](../../scripts/verify_cycletrim_target_contract.py)

## 参考项目 / 友链

- 性能补丁示例（ONI）: [FastTrack](https://github.com/peterhaneve/FastTrack)
- 通用补丁框架: [Harmony](https://github.com/pardeike/Harmony)
