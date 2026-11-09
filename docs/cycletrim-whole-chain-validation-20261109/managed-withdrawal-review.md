# 未验收上传 gate 的托管撤回

## 范围提纲

- 撤回生产上传 gate 及可选 presentation metadata 接线。
- 保留原多组分读取、lerper 和 22 阶段诊断。
- 用实际程序集负控和剩余 host 回归验证撤回边界。
- 原候选保持固定源码、二进制与全部失败证据，可独立复现。

## 结论与改动

**当前托管源码已经完整移除未验收的上传 gate，不再通过空 reader 留下包装成本。** `PresentationUploadGate`、`PropertyTexturePublicationPatch` 及其生产注册均已移除。Compatibility 只撤回新增的可选 presentation delegate、绑定、查询方法及结构；`NativeLibrary.cs` 和 `OniSimPublicationReader.cs` 已逐字节核对等同 f408，因此原 OMC acquire/release、线程归属和错误检查保留。[冻结源码与哈希收据](managed-withdrawal-review.json)。

**帧诊断和 OMC/lerper 的生产接线保留。** FrameRate、22 阶段 `FrameStageProbe`、已有多组分 provider 和 PerformanceProbe 项目接线均与撤回前 d374 字节相同。原候选 fixture 中被帧诊断共用的 PropertyTextures 模型抽成 `PropertyTextureFixture.cs`，仅用于既有 hook 回归的目标类型；候选的缓存、revision、异常注入和基准双路径没有带入新 fixture。该 stub 不是生产上传行为的证明，实际撤回证据来自编译程序集的类型和 IL 检查。[共享 fixture](../OniMods/tests/CycleTrim.Optimizations.Tests/PropertyTextureFixture.cs)、[收据中的 retained source 哈希](managed-withdrawal-review.json)。

**候选专用测试从当前生产验证入口移出，原断言没有改弱。** 六个 `PresentationUpload*` 测试、fixture、benchmark 和 diagnostic 文件，以及其 CLI、project reference、Compile 接线均从当前项目移出。它们原封不动保留在 Git `d374cff01fd2df4b958ef3c08beabdbe15605b33`；固定二进制在 [rejected-candidate-d374](rejected-candidate-d374/README.md)。当前 host 对旧候选 family/benchmark 参数明确返回错误，不会静默执行另一组测试并返回成功。[CLI 检查](managed-withdrawal-cli-absence.json)。

## 验证结果

| 验证 | 实际结果 | 能证明的边界 |
| --- | --- | --- |
| Optimizations Release build | exit 0，0 warnings，0 errors | 剩余测试及生产链接编译通过。 |
| PerformanceProbe Release build | exit 0，0 warnings，0 errors | 恢复的 Compatibility、ModInfo 和 probe 接线编译通过。 |
| Optimizations 全部剩余家族 | 11 家族 PASS | 包含实际 Harmony host 上的 22 阶段 hook；不是游戏运行，也不把 stub 计数当生产上传证据。 |
| PerformanceProbe 功能回归 | `--skip-synthetic-performance`，exit 0 | provider、lease、重入、并发、options、probe 等功能测试通过；没有新性能采样。 |
| FrameChain analyzer 回归 | 10/10 PASS | 解析、算术、时序、缺失 hook 等验证规则保留。 |
| 旧候选程序集负控 | 4 个预期 FAIL、18 PASS，exit 1 | 新 guard 确实检测到真实旧程序集中的 gate、patch、注册和上传包装。 |
| 最终交付程序集正控 | 待主任务最终 onim build | 本轮没有自行构建或覆盖 dist，不预先宣称通过。 |

构建及功能日志：

- [最终 Optimizations build](managed-withdrawal-final-optimization-build.log)、[最终完整回归](managed-withdrawal-final-optimizations.log)、[最终执行收据](managed-withdrawal-final-execution.json)。
- [PerformanceProbe build](managed-withdrawal-probe-build.log)、[功能回归](managed-withdrawal-probe.log)。
- [Analyzer 十项回归](managed-withdrawal-frame-analyzer.log)，该解析器及其测试源码没有随最终调整改变。
- [编译程序集负控](managed-withdrawal-release-guard-negative.log)、[负控收据](managed-withdrawal-release-guard-negative.json)。

**新增发布 guard 检查的是编译后的 CycleTrim.dll。** 它要求 gate/patch 类型缺席、ModInfo 无上传 reader 注册、程序集没有原始外部上传包装，同时正向确认 OMC provider 和既有 TextureLerperOptimization 仍接入。旧候选 DLL 的 SHA256 为 `49685163479fa89847c7cace3a057b6f175e05116cd212ec327469ae7953c35d`，负控时先核对其与归档副本完全一致，再运行 guard。[发布 guard](../OniMods/scripts/verify_cycletrim_release_binary.py)。

**独立复审删除了一项只验证 stub 自身的新断言。** 最终代码没有 `PropertyTextureDirectUploadRegression`，Program 的 frame-rate 家族恢复为原 `FrameRateDiagnosticsRegression`。删除后仅重新构建 Optimizations host 并重跑原 11 家族；原 PerformanceProbe 和 analyzer 源码未变，没有重复执行。初次源码收据与日志保留为中间记录，不作为最终生产上传证据。[中间收据](managed-withdrawal-initial-review.json)。

## 固定候选的独立复现

**以下命令针对被拒绝的 d374 候选，不能当成最终交付版本。** 归档目录已经保存原 host 及其 Compatibility/Harmony 依赖、原 native 库和原测试 ELF；使用该目录内的二进制可避免引用撤回后的 API。先按 [manifest](rejected-candidate-d374/manifest.json) 核对输入；原正式计划和全部样本仍独立保留。本次撤回没有再次执行这些候选命令。

```bash
CYCLETRIM_REJECTED=/workspace/scratch/725bb75bc42e/chain-validation/rejected-candidate-d374
env DOTNET_TieredCompilation=0 CYCLETRIM_ONISIM_GPU=0 CYCLETRIM_ONISIM_WORKERS=1 \
  /workspace/scratch/6c1cf53f37e8/toolchain/dotnet/dotnet \
  "$CYCLETRIM_REJECTED/managed-host/CycleTrim.Optimizations.Tests.dll" \
  --regression-presentation-native "$CYCLETRIM_REJECTED/liboni_sim_core.so"
```

若将来要单独复现候选基准，使用同一固定输入和原 [presentation 计划](plan-presentation-chain.md)，将上面的入口换成 `--benchmark-presentation-native`，保留新的完整原始输出，勿覆盖 r1—r4，也勿与最终交付测试混用。源码重建必须使用固定 d374 及其依赖，不得将当前撤回后的 Compatibility 与旧 gate 拼接。

## 主张与证据边界

| 主张 | 证据 | 状态 |
| --- | --- | --- |
| 托管生产接线已撤回，原 OMC API 保持。 | 源码差异、两个 Compatibility 文件与 f408 的字节相等。 | 支持。 |
| 22 阶段诊断没有随 gate 被删掉，OMC/lerper 保留接线。 | 保留文件哈希、最终实际 hook 回归、编译 guard 的 OMC/lerper 正向检查。 | Host 与编译接线范围支持。 |
| 发布 guard 能拒绝仍含 gate 的旧产物。 | 固定旧 DLL 负控，恰好四项目标失败。 | 支持。 |
| 最终分发包没有 gate。 | 需要最终 onim 构建后的 22 项正控。 | 当前待主任务完成。 |
| 撤回带来新的 FPS 或性能收益。 | 没有新性能测量或 ONI/Unity 会话。 | 不作此主张。 |

## 五维自检

| 维度 | 回答 |
| --- | --- |
| 贡献 | 撤回未达到原门槛的生产路径，保留可用于定位真实瓶颈的诊断。 |
| 清晰性 | 明确区分当前生产源码、共享 host 模型和固定拒绝候选。 |
| 实验强度 | 源码/构建/功能/负控证据充分支持撤回；不将其写成新性能结果。 |
| 评价完整性 | 原失败矩阵和候选断言不改；最终产物正控单列待办。 |
| 方法可靠性 | 不新增关闭功能的 flag，不保留空 reader 包装，不改变 OMC 与诊断合同。 |
