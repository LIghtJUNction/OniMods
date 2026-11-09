# CycleTrim 0.4.10：正常纹理更新路径的第二轮性能改进

日期：2026-11-09。配套前置：**Jlib 0.3.2**。交付平台：**Linux x64**。

## 结果与适用范围

本轮减少正常纹理更新中的逐格原生调用，并复用液体纹理已经算过的相邻行梯度。在固定的 .NET 10 宿主基准中，9 个气体区域的颜色更新耗时中位数下降 **79.80%–85.89%**；736 × 304、目标 10% 混合格的场景从 **123.526 ms 降到 19.470 ms**。单独的 Rust 液体纹理基准中，同尺寸纯液体场景从 **21.808 ms 降到 18.513 ms**。这两组测量对应不同函数，不能相加，也不能直接换算成游戏 FPS。

上一版 0.4.9 的悬停池化与空闲发布优化只有在对应路径触发时才节省工作。本轮选择每次实际颜色更新会执行的读取路径：旧实现对每个可绘制格子获取、释放 native publication lease；生产 reader 的 acquire/release 加上状态检查共跨越原生边界 4 次，而 native lease 持有全局 `State` mutex。连续区域批量读取将这种固定成本分摊到最多 128 格。

源码与数据依据：

- [CycleTrim 0.4.10 代码提交](https://github.com/LIghtJUNction/OniMods/commit/11ff143aafb7ed26b3881f528be5f03f54beac01)。
- [Jlib 0.3.2 连续区域批量读取](https://github.com/LIghtJUNction/Jlib/commit/22ac027a069a67813db8aa2f7a162c0e8a5327fb)。
- [液体纹理独立提交](https://github.com/LIghtJUNction/OniMods/commit/5e64a228228dcf3b8bc285b0d1d9451f88eb9528)。
- [最低 Jlib 版本检查](https://github.com/LIghtJUNction/OniMods/commit/cb19904718ef97bacf8aa90817706cbfe50ce9ef)。
- [完整统计、离散程度、源码指纹和实验门槛](cycletrim-active-performance-20261109/measurements.json)。

## 改动为何能减少工作

### 连续格子的颜色读取

Jlib 新增可选批量接口，原单格接口继续可用。视觉循环只合并连续可绘制的气体或液面格子，每段最多 128 格，遇到固体、inactive 或行尾便停止。孤立格子直接使用原单格路径。实际写入时仍重新检查每格的当前状态，原颜色公式和写像素顺序保持不变。

CycleTrim 在一次 native lease 内复制 cell headers 和 species records，随后立即释放锁。元素解析、颜色计算与 `SetBytes` 在释放后执行，batch 不保存原生指针。每个批次有独占暂存，支持嵌套租用，要求原线程消费与归还。原始暂存每实例约 83 KiB，组件数组按需增长；预热后零托管分配不代表首次无分配或不保留内存。

批次最多复制 4,096 条 species 的跨度。超过上限会回到完整单格读取，回归已验证 4,097 条记录仍能完整读取。这里的上限决定是否采用批量路径，不截断组分。对于 736 × 304 全气体区域，预期 lease 从 **223,745 次降为 1,825 次**，包含两侧共同的 `IsReady`。300 格确定性计数回归实测从 301 次降至 5 次。

首轮实现曾把整个固定区段复制下来。在每 128 格只有 1 格可绘制的控制场景里，它从 2.369 ms 退步到 4.762 ms，因此未通过预先规定的门槛。改为连续可绘制区域后，以相同 11 场景和相同门槛重新测量，稀疏控制从 2.219 ms 改善到 2.115 ms。首轮失败日志和全部样本保留在证据包中。

### 液体纹理梯度复用

原液体纹理计算会提前求下一行的梯度，下一行又求一次。本轮只在同一次更新中复用已求出的原始梯度，并从已知行列位置取得邻格索引。每次更新先清理缓存，避免跨帧复用旧状态；736 列时两个 publication buffer 合计保留 11,776 字节缓存。

需要执行幂运算的负数、负零、NaN、无穷质量输入不采用缓存；原本直接返回零的满暴露分支仍可复用零结果。取消浮点异常屏蔽时不复用梯度。回归核对三张完整图片、反复更新、输出指针稳定性、16 种 MXCSR 舍入/FTZ/DAZ 组合及异常标志。此改动不降低仿真频率或物理精度。

## 实验方法

宿主为 Linux x64，Intel Xeon Platinum 8573C，CPU affinity 固定为 CPU 0。托管实验使用 .NET 10.0.12，关闭 tiered compilation；原生实验使用 Rust 1.99.1 Release。计时串行进行，没有同时执行其他构建或基准。所有慢样本保留，未做去极值、截尾或择优选次。

托管基线冻结 Jlib `2769c7efa7285949d85366ae31d1a9739cef71bf` 的完整旧 Prefix 函数体，并经 Git 对象逐字核验。候选调用生产 Prefix，共用未修改的颜色计算和旧单格读取。两者使用同一个真实 `OniSimPublicationReader` 和 native mutex；`Grid`、`Element`、`TextureRegion` 为自编宿主替身。未执行游戏 DLL，未测 Unity/Mono 或 Harmony 派发。

为隔离托管改动，两侧固定使用 SHA-256 `052e6794292c2a9103c1fd34bea25d2eeed1e5cb4942a8ebd49cd81c1b1dba89` 的上一版自有原生库。最终交付的原生库另外包含已单独验证的液体梯度优化；最终包的 ABI、publication 与 lease 烟测另行核对，不能将两组计时写成联合整帧基准。

托管矩阵为 3 种尺寸 × 0/10/100% 目标混合比例，另加 736 × 304 的稀疏与全 inactive 控制。每变体预热 8 次，17 个交替 ABBA/BAAB 块，每侧 34 样本，每样本平均 3 次完整绘制。初始化、GC、delegate 绑定、校验和输出不计时；每次 sample 后检查 checksum。中位数来自这些样本均值，JSON 中的 p95 同样是样本均值的 p95，不是游戏帧延迟的 p95。

在观察计时前固定门槛：9 个密集场景各自耗时中位数至少降低 10%；两个控制场景各自退步不超过 3%；预热分配不能增加；全像素与每个 sample 的 checksum 必须一致。最终 **11/11 场景通过，748 条样本完整保留，两侧预热托管分配均为 0**。

原生液体实验冻结 OniMods `1f4151e39acaa6358abc186d571b0effdbc1396b` 的原函数与辅助函数，测试 4 种尺寸 × 4 种布局。3 轮预热、17 个交替 ABBA/BAAB 块，每侧 34 样本；按 17 × 19、96 × 96、256 × 384、736 × 304 的顺序，每个样本分别平均 200、108、10、4 次更新。计时覆盖 cache clear 和全部三张图片生成。预设门槛只用于后三种较大尺寸：纯液体各自至少改善 10%，同尺寸其他布局不退步超过 3%；全部通过。17 × 19 用于小尺寸正确性和性能观察，不参与硬性性能门槛。

## 托管颜色更新结果

表中单位为每次完整测试区域绘制的毫秒，中位数越小越好；降幅为正表示耗时减少。实际混合格数见 JSON，混合布局由 `cell % 100 < mixturePercent` 确定。

| 尺寸 | 目标混合比例 | Grid 布局 | 旧版 ms ↓ | 新版 ms ↓ | 耗时降幅 |
|---|---:|---|---:|---:|---:|
| 96 × 96 | 0% | 气体区 | 5.100 | 0.792 | +84.48% |
| 96 × 96 | 10% | 气体区 | 5.932 | 0.877 | +85.21% |
| 96 × 96 | 100% | 气体区 | 6.098 | 1.175 | +80.74% |
| 256 × 384 | 0% | 气体区 | 60.910 | 8.596 | +85.89% |
| 256 × 384 | 10% | 气体区 | 54.986 | 8.776 | +84.04% |
| 256 × 384 | 100% | 气体区 | 59.395 | 11.527 | +80.59% |
| 736 × 304 | 0% | 气体区 | 121.594 | 18.154 | +85.07% |
| 736 × 304 | 10% | 气体区 | 123.526 | 19.470 | +84.24% |
| 736 × 304 | 100% | 气体区 | 135.792 | 27.432 | +79.80% |
| 736 × 304 | 10% | 稀疏：每段仅 1 格气体 | 2.219 | 2.115 | +4.71% |
| 736 × 304 | 10% | 全 inactive | 0.784 | 0.802 | -2.40% |

全 inactive 场景仍有约 **2.40%** 的小幅退步，处于预先设置的 3% 控制门槛内。结果支持“连续可绘制区域显著减少读取成本”，不支持“每种区域都变快”。

## 原生液体纹理结果

单位为完整 `liquid::update` 的毫秒，中位数越小越好。各场景三张完整图片均校验一致；17 × 19 用于覆盖奇数宽度和小尺寸。

| 尺寸 | 布局 | 旧版 ms ↓ | 新版 ms ↓ | 耗时降幅 |
|---|---|---:|---:|---:|
| 17 × 19 | 纯液体 | 0.0298 | 0.0245 | +17.80% |
| 96 × 96 | 纯液体 | 0.9407 | 0.7817 | +16.90% |
| 256 × 384 | 纯液体 | 9.5892 | 8.5629 | +10.70% |
| 736 × 304 | 纯液体 | 21.8077 | 18.5131 | +15.11% |
| 17 × 19 | 液体、固体、气体混合 | 0.0167 | 0.0137 | +17.59% |
| 96 × 96 | 液体、固体、气体混合 | 0.4450 | 0.3849 | +13.51% |
| 256 × 384 | 液体、固体、气体混合 | 4.8735 | 4.2855 | +12.07% |
| 736 × 304 | 液体、固体、气体混合 | 11.6178 | 9.6791 | +16.69% |
| 17 × 19 | 无液体 | 0.0008 | 0.0005 | +36.55% |
| 96 × 96 | 无液体 | 0.0227 | 0.0174 | +23.51% |
| 256 × 384 | 无液体 | 0.2560 | 0.2110 | +17.59% |
| 736 × 304 | 无液体 | 0.5883 | 0.4907 | +16.59% |
| 17 × 19 | 每格 3 种液体组分 | 0.0428 | 0.0386 | +9.61% |
| 96 × 96 | 每格 3 种液体组分 | 1.6750 | 1.4802 | +11.63% |
| 256 × 384 | 每格 3 种液体组分 | 21.7048 | 19.7895 | +8.82% |
| 736 × 304 | 每格 3 种液体组分 | 52.9699 | 48.6320 | +8.19% |

## 功能、构建与交付验证

- 托管完整非计时套件通过，包含旧有多组分断言及 batch 边界、无效数据、4097 records 回退、释放后源内存改写、同 generation 刷新、重入、并发、异常、引用释放和全像素回归。连续区域修正额外覆盖 gap、行尾、129 格边界及回调期间 Grid 状态改变。
- Jlib `net48`、`netstandard2.1` 构建与打包通过；441 项契约和静态元数据检查通过。
- 18 个隔离 bootstrap 场景通过，旧版本在 implementation 加载前被拒绝；未声明最低版本的其他消费者保持原行为。
- presentation 测试族 12 通过、0 失败（其中 liquid 4 项通过），3 个手动基准按默认忽略。Linux 完整 native suite 为 **109 通过、11 失败、7 忽略**，没有降低测试门槛。10 个失败明确报告无可用硬件 Vulkan adapter；另一个 pathfinder FFI 初始化返回 -1。本环境的 GPU 运行验收尚未完成。
- `onim build -m CycleTrim --release` 已在代码提交 `11ff143a` 完成，0 errors、28 条继承警告。Rust 独立 Release 构建保留 36 条既有未使用项警告。
- 最终 release binary 的 16 项保护检查通过；原生加载、导出、初始化、关闭和 resident lookup 烟测通过。最终包的原生库另通过 652,315 项 publication 与跨线程 lease 状态断言。两个 ZIP 的 CRC、逐项文件一致性、版本、实际 bootstrap 最低依赖和原生库三处 SHA 均通过检查。
- 代码提交 `11ff143a` 的 [Mod quality](https://github.com/LIghtJUNction/OniMods/actions/runs/42546534074) 和 [ONI reference compatibility](https://github.com/LIghtJUNction/OniMods/actions/runs/42546534059) 均成功；Jlib `22ac027a` 的 [host regression CI](https://github.com/LIghtJUNction/Jlib/actions/runs/42546476433) 成功。原生改动提交 `5e64a228` 的 native platforms、diamond、transport 工作流仍失败；已读取对应日志，GPU adapter 不可用和 pathfinder 初始化失败的结果没有改为跳过或成功。

最终二进制检查、包内身份/版本/哈希、原生加载与 lease 烟测，以及代码提交的 CI 状态见 [验证记录](cycletrim-active-performance-20261109/build-and-validation.json) 和 [CI 快照](cycletrim-active-performance-20261109/ci.json)。参考程序集用于编译和静态检查，未作为游戏运行；本次没有真实存档、Unity/Mono、GPU 或整局 FPS 验收。

## 安装与复现

同时更新 **CycleTrim 0.4.10** 与 **Jlib 0.3.2**，启用一份 Jlib 并将它排在 CycleTrim 前面，然后重启游戏。CycleTrim bootstrap 会明确提示 Jlib 版本过旧，避免加载新增接口时失败。这里交付的是 Linux x64 本地安装包；未在本次工作中发布 Steam 或 NuGet。

安装包、完整原始记录及本报告位于 [Google Drive 交付目录](https://drive.google.com/drive/folders/1JL2xhermjB_FsFvCEgl_4iFVWDRVaf08)。[交付清单](cycletrim-active-performance-20261109/delivery.json) 记录精确版本、SHA-256 和位置。证据 ZIP 保留首轮失败与修正后的完整样本、源码指纹、分析脚本及构建/验证日志，不包含游戏程序集。

```bash
# 先按仓库说明准备编译引用；仅运行项目自有 host / native binary。
dotnet run --project tests/CycleTrim.PerformanceProbe.Tests -c Release   -p:ImportDirectoryBuildProps=false -- --skip-synthetic-performance

# 正式托管协议；替换为本地自有 native .so 路径。
CYCLETRIM_ONISIM_GPU=0 DOTNET_TieredCompilation=0 taskset -c 0 dotnet   tests/CycleTrim.PerformanceProbe.Tests/bin/Release/net10.0/CycleTrim.PerformanceProbe.Tests.dll   --benchmark-multicomponent-visual /absolute/path/liboni_sim_core.so

cargo +1.99.1 test --release --locked --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml   presentation::liquid::tests::benchmark_liquid_update -- --exact --ignored --nocapture --test-threads=1
```

托管正式比较要求两侧使用同一个库。要重现本报告的确切 fixture，应使用上述固定 SHA 的自有库和 CPU 0；换用最终库或其他机器属于另一次测量。原生基准也应将测试进程固定到 CPU 0，避免把启动编译时间计入函数样本。

## 结论与证据核对

| 主张 | 证据 | 状态 |
|---|---|---|
| 连续区域减少逐格原生锁开销 | 生产源码、确定性 lease 计数、真实 native reader 基准 | 支持 |
| 9 个气体区宿主场景耗时下降 79.80%–85.89% | 748 条完整样本及固定门槛分析 | 支持，限本协议 |
| 固体间孤立气体不再触发整段复制 | 连续区域范围断言、稀疏控制、首轮失败记录 | 支持 |
| 液体纹理复用保持输出 | 16 场景像素/图片校验与浮点回归 | 支持，限已测输入 |
| 实际游戏 FPS、存档与 GPU 效果 | 本轮没有游戏会话 | 未测，不作提升承诺 |

并发 publication 更新时，一个 batch 读取同一次取得的副本，而旧逐格读取可能跨多个 publication，因此静态像素一致不能扩大为任意并发时间序列下的历史输出逐位一致。

按 [Research-Paper-Writing-Skills](https://github.com/Master-cai/Research-Paper-Writing-Skills) 的五个维度自审：

| 审查问题 | 回答 |
|---|---|
| 贡献：究竟减少了什么工作？ | 原生 lease 次数与已经求过的相邻梯度，均有生产源码和独立测量。 |
| 表达：读者能否理解单位和复现范围？ | 表格用 ms，协议、版本、原生库 SHA 与源码指纹单独列出。 |
| 实验：是否隐去失败或慢样本？ | 首轮稀疏退步和全 inactive 的最终小幅退步均保留，原始样本不删减。 |
| 完整性：还缺什么证据？ | 缺实际 Unity/Mono、存档、GPU 与整局 FPS 验收，结果没有推广到这些范围。 |
| 方法：复制和并发是否有边界？ | 批次有界、锁先释放、孤立格单读；回退不截断组分，并发快照语义已限定。 |
