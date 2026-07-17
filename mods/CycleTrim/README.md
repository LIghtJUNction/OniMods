# CycleTrim 0.2.0

CycleTrim is a narrowly scoped performance mod for Oxygen Not Included.

CycleTrim 是一个针对《缺氧》具体热路径的性能优化 Mod。

## Production optimizations / 生产优化

1. Smart reservoirs send their automation output on spawn and then only when the output changes.  
   智能液库与气库在生成时初始化自动化输出，之后仅在输出变化时发送信号。
2. Fetch pickup candidates reuse cached cell costs and retain the best vanilla-ranked candidate for each tag and priority group. Vanilla eligibility and final ranking are preserved.  
   拾取候选选择复用格子路径代价，并为每个标签与优先级组保留原版排序下的最佳候选；原版资格与最终排序保持不变。
3. Only busy duplicants alternate expensive pickup refresh and chore reselection. Idle duplicants always refresh immediately, and `BrainScheduler.PrioritizeBrain` forces the next refresh to run immediately. Critters are not affected.  
   仅忙碌复制人会隔次执行高开销的拾取刷新与差事重选。空闲复制人始终立即刷新，`BrainScheduler.PrioritizeBrain` 也会强制下一次立即刷新。小动物不受影响。

If `PeterHan.FastTrack.GamePatches.FetchManagerFastUpdate` is detected, CycleTrim disables the overlapping fetch and busy-duplicant patches.

如果检测到 `PeterHan.FastTrack.GamePatches.FetchManagerFastUpdate`，CycleTrim 会禁用重叠的拾取与忙碌复制人补丁。

## Fixed-scenario validation / 固定场景验证

Test scene: A01, cycle 1641, 15 duplicants, world 0, game window 665x508, camera view X35-55/Y71-91, speed 3x, using paired 30-second windows.

测试场景：A01，第 1641 周期，15 名复制人，world 0，游戏窗口 665x508，镜头 X35-55/Y71-91，3x 速度，使用成对的 30 秒窗口。

The baseline used the same Debug build with only the new busy-duplicant throttle disabled; the two existing 0.1.9 optimizations remained enabled. The candidate enabled the throttle.

基线使用同一 Debug 构建，仅关闭新的忙碌复制人节流；0.1.9 已有的两项优化仍然开启。候选组开启该节流。

- Paired FPS: `74.9 -> 84.5` (+12.9%), `78.8 -> 89.5` (+13.6%), `70.6 -> 85.1` (+20.6%); median +13.6%.
- Warm-window hot paths: Brain `5.087s -> 2.449s` (-51.9%), PickupableSensor `2.147s -> 0.104s` (-95.1%), FindNextChore `1.434s -> 0.523s` (-63.5%).

这些是该固定存档、硬件和镜头下的增量结果，不是 vanilla-vs-mod 对比，也不保证所有殖民地都获得相同 FPS。

These are incremental results for this fixed save, hardware, and camera view. They are not a vanilla-vs-mod comparison and do not guarantee the same FPS on every colony.

## Compatibility and build / 兼容与构建

- Supports base game and DLC content through `supportedContent: ALL`.
- Runtime contract checks are in `scripts/verify_cycletrim_target_contract.py`.
- 通过 `supportedContent: ALL` 支持基础游戏与 DLC 内容。
- 运行时契约检查位于 `scripts/verify_cycletrim_target_contract.py`。

```bash
onim build -m CycleTrim
onim dev -m CycleTrim
```
