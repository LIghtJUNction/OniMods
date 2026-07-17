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

可直接使用 `benchmark` 技能让 agent 自动跑完整基准测试并返回标准化 JSON 结果。

完整场景明细见：[dist/CycleTrim/README.md](../../dist/CycleTrim/README.md)（打包产物中的实测段落）。

## 详细说明与日志

- 变更记录： [CHANGELOG.md](CHANGELOG.md)
- 运行时目标验证脚本： [verify_cycletrim_target_contract.py](../../scripts/verify_cycletrim_target_contract.py)
