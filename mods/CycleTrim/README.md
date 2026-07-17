# CycleTrim

ONI 性能优化 Mod。该 Mod 针对缺氧的几个高频模拟路径做降频优化，目标是在保持行为兼容的前提下降低开销、提升稳定性。

## 快速索引（本页为总览）

- [核心优化](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md#核心功能简要)
- [安装与运行](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md#使用方式)
- [验证与兼容性](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md#兼容与说明)
- [性能实测摘要](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md#性能实测摘要)
- [详细说明与日志](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README.md#详细说明与日志)

## 核心功能（简要）

- 减少智能储液罐/气库的重复信号发送。
- 优化拾取候选的成本复用逻辑。
- 仅对忙碌复制人的高开销任务进行间歇刷新，空闲复制人不受影响。
- 节流静止小动物的重复导航探测。

## 使用方式

- 订阅后按常规启用即可。
- 推荐配合现有存档直接运行，无需额外配置。

## 兼容与说明

- 与 ONI DLC 与主线兼容。
- 以稳定性优先，避免影响原版关键玩法。

## 性能实测摘要

以下为固定场景增量测试（非全局通用），硬件与存档不同会有差异：

| 场景 | 指标 | 优化前 -> 优化后 | 改善 |
|---|---|---|---|
| 忙碌复制人节流 | FPS 中位数（CPU 热点场景） | `70.6 → 85.1` | `+20.6%` |
| 忙碌复制人节流 | Brain 总耗时 | `5.087s → 2.449s` | `-51.9%` |
| 静止小动物导航节流 | FPS 中位数 | `81.23 → 109.28` | `+34.5%` |
| 静止小动物导航节流 | Navigator 热点总耗时 | `826.852ms → 47.970ms` | `-94.2%` |
| UpdatePickups 缓存 | 命中率中位数 | `24.37%` | `命中率 24.37%` |
| UpdatePickups 缓存 | 平均耗时 | `41.279us → 35.708us` | `-13.5%` |
| UpdatePickups 缓存 | 每秒命中数（30s 窗口估算） | `~6.29/次` | `命中有效复用` |

### Brain 调度的函数级合成基准（已接入，仅普通 Creature）

Brain 限流已接入生产 Harmony 补丁，但只作用于原版普通 `CreatureBrainGroup`。复制人（Dupe）、自定义 Brain 组继续走原版调度；priority Brain 保留原版队列选择和即时执行路径，不受普通 Creature 限额影响。

> **这是独立的合成函数基准，不是游戏内 FPS 测试，也不能直接换算为整局性能提升。**

- 环境：独立 `.NET 10` Release 基准，不启动 ONI。
- 场景：模拟 `240` 渲染 FPS、持续 `30` 秒，每次普通 Brain 调用执行 `512` 次确定性 CPU 工作。
- 方法：`2` 次预热，`7` 组 baseline/candidate 交替配对测量，取中位数；每组校验调用数与 checksum 一致。
- 调用口径：总调用从 `43,200` 降至 `19,200`（`-55.56%`）；其中普通 Creature 调用从 `36,000` 降至 `12,000`（`-66.67%`），Dupe 的 `7,200` 次调用保持原版。
- 代表性本机结果：耗时从 `255.580ms` 降至 `113.289ms`（`-55.67%`，`2.26x`）。
- 边界：不包含 priority 工作量、真实 Sensors/PathProber 成本或游戏内 Harmony 调度开销，不能代表实际 FPS。
- 安全差异：当前游戏 DLL 使用动态 `i != brains.Count` 边界。CycleTrim 同样每轮重读 Count，让帧内新增 Brain 可纳入扫描；但在 Update 中列表收缩到已扫描位置之后时会有界终止，避免原版条件可能无法收敛。

### AsyncPathProber 去重与背压

- 仅支持精确的原版 `CreaturePathFinderAbilities`；Minion、Robot 与自定义 abilities 因动态权限/装备状态无法完整证明，全部回退原版。
- 缓存只在 `Navigator.TakeResult` 正常应用后成立；排队清空、注销、过期未应用及异常不会写入缓存。相同快照最多连续跳过 8 次，随后强制刷新。
- 启动时会验证 Creature 的 sentinel 回收方法仍为空实现；若游戏更新改变该契约，整组 Async 优化会自动禁用而不是冒险运行。
- 队列背压按 worker 与 in-flight 动态计算，范围 `1..4`；1 worker 空闲时 quota=2、忙碌时 quota=1。
- 10,000 请求合成矩阵（**不是游戏内 FPS**）：目标命中率 `0/25/50/75/90%` 时，实际 PathProbe 执行为 `10000/7701/5501/3301/2000`，避免 clone 为 `0/2299/4499/6699/8000`；理论 probe 工作下降 `0/22.99/44.99/66.99/80.00%`。8 次有界回退使实际降幅低于名义命中率。

可复现命令：

```bash
dotnet run --no-restore -c Release --project benchmarks/CycleTrim.BrainBenchmarks/CycleTrim.BrainBenchmarks.csproj
dotnet run --no-build --no-restore -c Release --project benchmarks/CycleTrim.BrainBenchmarks/CycleTrim.BrainBenchmarks.csproj -- --benchmark
```

可直接使用 `benchmark` 技能让 agent 自动跑完整基准测试并返回标准化 JSON 结果。

完整场景明细见：[dist/CycleTrim/README.md](../../dist/CycleTrim/README.md)（打包产物中的实测段落）。

## 源码目录

```text
mods/CycleTrim/
├── ModInfo.cs          # KMod 入口
├── Core/               # 与游戏补丁无关的核心逻辑（如 BrainRateCap）
├── Patches/            # Harmony 补丁
├── docs/               # Steam 描述等
└── CycleTrim.csproj
```

与仓库其它 mod 约定一致：根目录只保留入口与工程/元数据文件；补丁进 `Patches/`，可复用逻辑进 `Core/`。

## 详细说明与日志

- 变更记录： [CHANGELOG.md](CHANGELOG.md)
- 运行时目标验证脚本： [verify_cycletrim_target_contract.py](../../scripts/verify_cycletrim_target_contract.py)
