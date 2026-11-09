# 表现发布链路：首次计时前固定计划

日期：2026-11-09。验收阈值由 root 在首次运行本基准前确认；不得根据结果改动阈值、场景或取样规则。当前还没有本基准的性能结果。

## 要验证的变化

原生端为已经完成的五张表现图发布 session generation 与 derived revision。现有 `reuse_derived` 判定在两个 GameData 缓冲区预热后证明内容可复用；缓冲区地址交替不会产生新的内容身份。托管端只对已经成功上传、内容身份相同的 Texture2D 跳过 `LoadRawTextureData` 与 `Apply`，保留原来的 LateUpdate、模拟消息与纹理插值。

生产调用依据：固定 `Kupie/ONI_Decomp@8d4ace10c2b6a84cee394653a91b990a5122e636`（声明 build 744825）的 `Game.Update → SimEveryTick → UnsafeSim200ms → StepTheSim → Sim.HandleMessage(PrepareGameData)`；`PropertyTextures.LateUpdate → UpdateProperty` 中的五个 external load 与一个共享 Apply。来源文件的 SHA256 与 Git blob 校验见 `managed-chain-sources.json`。

## 对照及测量边界

执行入口：`PresentationUploadNativeRegression.Run(nativePath, benchmark: true)`，CLI `--benchmark-presentation-native <absolute-so-path>`。

同一候选原生库用于两种托管对照。基线执行冻结的、未被 gate 替换的五图上传调用；候选执行真实 Harmony transpiler 与生产 gate。两侧 host 的更新循环、条件、调用顺序、输入和纹理存储相同。这是上传去重的增量对照，不是两个完整游戏版本的 FPS 对照。

`zero_prepare` 和 `command_two_prepares` 的计时包含真实 NewGameFrame/PrepareGameData 导出、worker 等待、原生生成或复用、稳定 Grid 发布、descriptor 指针赋值、生产 gate 和 host 纹理 CPU 复制。host 的两个原生状态检查也包含在两侧。`render_repeat` 计时期间不调用 Prepare，测量同一已发布结果的重复渲染更新。

Texture2D 是 instrumented host：Load 使用 Marshal.Copy，Apply 使用 Array.Copy，并记录次数及字节。它不执行 Unity、图形驱动、GPU 上传、相机或显示提交。因此所有耗时只可标为 host 链路耗时。不得换算或称作 ONI FPS、真实 GPU 上传成本或实际存档收益。

## 固定矩阵与取样

| 维度 | 固定值 |
| --- | --- |
| 网格 | 96×96、256×384、736×304 |
| 场景 | `render_repeat`、`zero_prepare`、`command_two_prepares` |
| 原生设置 | `CYCLETRIM_ONISIM_GPU=0`、`CYCLETRIM_ONISIM_WORKERS=1` |
| 托管运行设置 | `DOTNET_TieredCompilation=0`；两种对照使用同一进程、同一设置 |
| 构建 | Release；保存实际 .NET、native 路径与二进制哈希 |
| 场景初始预热 | 8 次真实零时间步发布与渲染 |
| 每 sample 预热 | 4 次，与该 sample 使用相同场景和 variant |
| 每 sample 计时 | 4 次更新 |
| 顺序 | 17 blocks；ABBA 与 BAAB 交替 |
| 样本数 | 每场景每 variant 34；总计 612 samples、2448 次计时更新 |

`command_two_prepares` 每次提交一条真实替换格子命令，然后执行两个 handover，以保证拿到包含该命令的结果。此场景是持续变化的控制，不将两次 handover 描述成游戏每帧的固定次数。

保留逐 sample ticks、Load/Apply 次数、输入字节、当前线程托管分配、revision、五图合并 SHA256。每个 sample 计时外逐字节核对 native 当前 descriptor 指向的五图与 host 已上传图。计时外计算 SHA256；同一场景所有基线和候选 sample 必须一致。

## 固定验收门槛

1. 九个场景的每个 sample 都必须满足五图逐字节与 SHA256 一致。
2. 每四次更新，基线必须执行 20 Load 和 20 Apply；候选 `render_repeat`、`zero_prepare` 必须为 0，`command_two_prepares` 必须保留 20。
3. 256×384 和 736×304 的 `render_repeat`、`zero_prepare`：候选整段中位数相对基线至少下降 10%。
4. 三个尺寸的 `command_two_prepares`：候选整段中位数回归不得超过 3%。
5. 96×96 的两个重复场景只作规模控制，照常报告结果，不增加事后门槛。

P95 是 sample 均值的 P95，不是游戏帧时间 P95。分配数仅覆盖测量线程的托管分配，不包含原生 worker、驱动或其他线程。必须获得独占计时窗口后运行；编译、测试和其他 agent 的计算不得重叠。

首次正式计时前补充固定：禁用 .NET 分层编译，避免对照在不同 JIT 层级采样。保存 .NET runtime 版本、Stopwatch.Frequency、host DLL、实际 native 库及相关生产/测试源码 SHA256；保留全部 612 条原始 sample。此补充发生在首次计时之前，不改变场景、顺序、样本数或门槛。

## 正确性预检和证据限制

先执行 native revision 测试、真实 Harmony host 生命周期/异常/重入测试，以及真实 native → descriptor → managed gate → Texture2D stub 回归。另在单独进程用旧 native 库验证缺少可选导出时保留全部上传。

原版 OnReset 创建新的 Texture2D，对象身份改变要求第一次上传。大小、image identity、generation、revision、Unity updateCount、cache epoch 共同限制复用。根据 Unity 的 [Texture.updateCount 合同](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Texture-updateCount.html)，外部 GPU 写入需要调用方增加该计数；没有标记的 GPU 写入不在本次检测承诺内。同尺寸 Reinitialize 的 counter 变化仅由 host 模型演练，尚无真实 Unity 运行证明。[Reinitialize 文档](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Texture2D.Reinitialize.html)说明会清除 CPU/GPU 像素，但没有单独规定其 counter 行为。

## 五项复核

问题与实际调用对应；对照只改变已明确的上传路径；输出采用字节与哈希双重核对；阈值先固定再测；结论限制在真实原生端连接 instrumented host 的范围。游戏运行、图形硬件与真实帧时间留待有对应会话时验证。

## 正式 r2 之前的追加记录

首轮完整九场景、612 samples 的正确性与上传计数全部通过，但性能总门槛 **FAIL**：96×96 `command_two_prepares` 中位数增加 8.869%，超过原定 3%。首轮计划的逐字节快照另存 `plan-presentation-chain-r1.md`，其 SHA256 与首轮 `presentation-run-metadata.json` 一致。首轮 `presentation-benchmark-final.log`、`presentation-benchmark-results.json` 和 `presentation-run-metadata.json` 原样保留；文件名中的 final 不代表候选最终被接受。

r2 对应实际源码修订：managed 变更图路径复用一次查得的 CWT 记录，省去重复查找并延迟不必要的 updateCount 读取；native 在整批零时间命令成功后保留已经建立的 read 同步证明及双缓冲预热进度；首次 derived reuse 必须对两缓冲的五张图作真实字节相等证明，只有相同才共享 revision。动态 world_offsets 的负控已经证明旧共享身份可能错误：sun 图会保留未覆盖范围的不同历史。不相同的缓冲继续携带各自生成时的身份，不修改原图；身份耗尽仍强制为 0。

`presentation-dirty-diagnostic-before.*` 与 `presentation-dirty-diagnostic-after.*` 是独立归因诊断，使用另一组迭代数和分段计时，且没有测得稳定的墙钟节省。它们既不替代首轮失败，也不作为 r2 性能验收。

r2 保持以上原定九场景、每侧每场景 34 samples、17 个 ABBA/BAAB blocks、每 sample 4 次预热与 4 次计时、所有字节/SHA/调用次数条件及全部性能阈值。两种 variant 在同一新运行中使用同一最终 native 库和相同环境，重新采集完整 612 samples；不复用、筛选、合并或重新标注首轮数据。初始 8 次预热与 benchmark 入口保持不变。

新证据路径固定为 `presentation-benchmark-r2.log`、`presentation-benchmark-r2-results.json`、`presentation-run-r2-metadata.json`；保存最终 native、host 和相关源码 SHA256、.NET 版本、Stopwatch.Frequency 及运行设置。先完成最终库真实 FFI 和九场景无计时预检，再在独占窗口一次执行完整矩阵。增加的身份比较成本必须留在真实链路中接受同一门槛。
