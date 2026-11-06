# Transport 实验与当前生产内核的整合

## 采用范围

当前生产代码只采用质量搜索的 FIFO 入队去重。整合以
[`6adc57f`](https://github.com/LIghtJUNction/OniMods/commit/6adc57fdd827f189a7e53467705ef7d6ace01ec3)
为 main 基线，以
[`905d955`](https://github.com/LIghtJUNction/OniMods/commit/905d955623a1ea46ea2aa4f700bf8c8aa9f6426e)
为历史实验来源。去重仍按下、左、右、上的顺序第一次到达格子；消耗、疾病、浮点计算和事件继续调用当前生产辅助方法。
修改位于 [mass_components_flood.rs](../native/src/mass_components_flood.rs)。

main 的热传输和气体实现保留原样。热传输在历史实验之后修复了背墙步长、热平衡限制、
NaN 操作数顺序及浮点环境处理；气体压力更新已经迁入 `pressure_sweep.rs`。
历史 64 项热缓存与无菌气体捷径未移入当前生产代码。它们的源文件、证明和不利结果继续作为历史证据保存。

## 当前生产验证

[差分测试](../native/src/mass_components_flood_tests.rs) 将当前 FIFO 实现与
[冻结的整合前实现](../native/src/mass_components_flood_baseline.rs) 编译到同一个 native 测试程序中。
两边共同使用当前 `PhysicsWorld`、材料表、病菌表、数值函数和事件队列。
覆盖全部 512 个 3×3 障碍布局的 89,600 次有序搜索，另有 1,000 个确定性随机世界、
每个世界 5 次质量消耗；比较访问顺序、提前停止、全部 CellSOA 字段的浮点位模式、
消耗汇总、脏标记和事件次序。额外检查代数回绕、边界和已分配队列容量的复用。

当前 native 验收沿用现有 `onisim-native.yml` 的 Rust 1.99.1、Release 和 locked 依赖设置：

```sh
cargo +1.99.1 test --release --locked --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml
```

本地运行结果为 58 项通过、0 项失败、2 项原有性能测试忽略；新加入的 3 项 FIFO 回归全部执行。
另一次额外的 `-Dwarnings` 编译因未修改文件中的 17 项 dead-code 诊断停止，日志保留。
当前 native 验收没有引入该额外编译选项；历史组件原有的 `-Dwarnings` 设置继续保留。

它与历史组件实验分开运行。整合没有重新计时，没有修改历史性能门槛，也没有取得本版本的 Steam 游戏运行证据。
队列所需容量减少属于局部存储变化，不能据此推断整帧时间、游戏吞吐量或进程常驻内存。

## 历史实验复现

`historical-source/` 保存 PR 原 head 的完整相关源文件，并用
[sha256.json](historical-source/sha256.json) 固定来源和内容。
`prepare.py` 保留 baseline 哈希、共享辅助源码一致性及事件／世界适配器方法体检查，
额外核对历史候选清单。生成的组件程序执行当时的算法。它不读取当前 `native/src`，
所以它通过只能证明历史实验在指定编译器下仍可复现。

```sh
cd mods/CycleTrim/OniSim/transport-lab
python3 prepare.py
RUSTFLAGS=-Dwarnings cargo +1.90.0 build --release --locked --offline
./target/release/onisim-transport-lab verify
python3 summarize.py
python3 proof/verify.py  # Lean 4.19.0
```

原 640 个计时批次、40 个场景、失败及退化结果均保留。汇总重算没有追加或替换计时样本。
Lean 文件和 27 个定理保持原样；证明的模型边界仍以历史 README 为准。
CI 将历史组件、当前 native 回归和 Lean 证明列为独立任务。

本地已用 Rust 1.90.0 完成历史候选的格式检查、严格构建及两种 FTZ 模式的差分验证。
原始 summary 和源码身份记录重算后逐字不变。Lean 4.19.0 未在本地安装，
未将旧证明日志冒充本次执行；新提交的 Lean 检查由 CI 单独运行。
本次命令、输出和源码指纹见 [integration-validation/receipt.json](integration-validation/receipt.json)。

## 证据自检

贡献是将可独立验证的队列去重移入当前内核。当前行为结论由生产差分测试支持；
历史性能数字只对应固定旧快照；真实游戏收益仍缺少证据。设计依赖当前搜索期间不会从其他格子
改变未处理格子的可通行性，未来若改变这个调用约定，必须重新检查入队去重是否仍保持顺序。
