# CPU 气体输运实测：2026-11-13

气体在当前格和四个开放邻格都没有质量、焓时，其通量严格为零。
稀疏路径跳过这种组分；带符号零和非零焓仍执行原计算。
已有的输入扫描统计开放格组分占用率，低于四分之一时选择稀疏路径。
密集路径通过常量泛型移除内层占用检查，避免密集混合物的分支开销。

## 原生基准

基线是 `670e92db` 的生产算法，加上与候选相同的测试基准。
固定 160×96 格、16 种气体、8 线程；每轮预热 5 次、计时 20 次。
性能核心固定为 CPU `0,2,4,6,8,10,12,14`，按 A-B-B-A-B-A-A-B 执行。
表中是每个版本四轮中位数的中位数，单位为毫秒；耗时变化负值表示更快。

| 输入 | 扩散 | 基线 ms | 候选 ms | 耗时变化 |
|---|---|---:|---:|---:|
| 密集 | 关闭 | 1.409 | 1.463 | +3.8% |
| 密集 | 开启 | 2.038 | 2.042 | +0.2% |
| 稀疏 | 关闭 | 1.509 | 0.900 | -40.3% |
| 稀疏 | 开启 | 2.245 | 1.588 | -29.3% |

所有对照运行的质量与温度输出校验值一致，基准另检查质量守恒与有限温度。
这些结果只描述该气体链工作负载，不是完整游戏 FPS。
原始样本、源文件与测试程序哈希见 [JSON 记录](cpu-gas-transport-20261113.json)。

复跑候选基准（从 `mods/CycleTrim/OniSim/native` 执行）：

```sh
rtk proxy env CARGO_BUILD_JOBS=1 RUST_TEST_THREADS=1 cargo test --locked --release --lib benchmark_cpu_gas_chain -- --ignored --nocapture
rtk proxy env CARGO_BUILD_JOBS=1 RUST_TEST_THREADS=1 CYCLETRIM_GAS_BENCH_SPARSE=1 cargo test --locked --release --lib benchmark_cpu_gas_chain -- --ignored --nocapture
```

## 验证范围

- 气体链测试：6 项通过，含扩展后的 1、2、3、16 组分密集/稀疏 CPU/GPU 对照。
- 相关材质/区域集成测试：13 项通过，2 项手动计时用例忽略。
- `onim -m CycleTrim dev` 构建部署通过；原生核心是 Release，托管 Mod 是 Debug。
- 通过 Steam 启动真实 ONI，构建 U59-744825-SCRPAN、Unity 6000.3.5f2；无头模式，Null 图形设备、零相机渲染回调。
- 使用一次性存档副本；确认游戏映射的原生库 SHA256 与本轮打包产物一致，日志出现非零 `sparse_worlds`。
- 3 次短步均有正向仿真推进、最终暂停，15 名复制人仍在；没有异常或游戏崩溃。
- 换页量和主机负载不同，真实存档短步结果不能据此换算整体提速或 FPS。
- `cargo doc --no-deps` 通过。全库 `cargo fmt --check` 与严格 Clippy 仍被已有格式及告警问题阻挡；本次没有清理无关文件或放宽告警。
