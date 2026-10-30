# 管道显示分级刷新：实现与验证

本质是复用已经画好的管道网格，减少两次真实模拟更新之间重复生成显示数据的次数。

本轮以 PR #558 的 `62067913a5aea4daa4a8991689c86aad261b1269` 为基础，在独立分支增加
气体、液体管道的显示刷新选项。真实管道流动继续执行原方法；同一时间戳内多次模拟调用也会分别
使显示缓存失效。已有的动画、轨道退化修复由另一个并行任务处理，本报告只归属此次显示改动。

**交付状态：候选实现，默认 Full。** 正确性宿主回归和严格引用构建通过；选定版本在大工作量
稳定显示中有净收益，但 Half 的中等工作量仍未通过预先设定的耗时门槛。Steam 实机验收也尚未
完成，因此这份报告不把它认定为已完成性能验收或可直接合并的优化。

## 1. 从实际机制选择优化位置

固定 744825 方法体中，`ConduitFlow.Sim200ms` 累计传入的时间并推进流动；
`ConduitFlowVisualizer.RenderMesh` 则在绘制时扫描可见管道、生成插值几何并上传网格。
真实流动和每帧显示是两条不同路径。因此，本次优化选择减少显示重建，保持模拟调用、
物质数量、温度、疾病数据和回调交付的原有执行路径。见[固定的可视化方法体](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/ConduitFlowVisualizer.cs)
及[管道流动方法体](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/ConduitFlow.cs)。

FastTrack 的实现也区分具体系统：管道动画有独立刷新选项，植物相关优化则包括属性和组件
查询复用。已核实源码并不支持将它概括成一个通用的 Critical / Secondary / Cosmetic
三级模拟调度器。本次使用独立实现，借鉴的是显示复用与及时失效的思路。对应的
[FastTrack 管道显示源码](https://github.com/peterhaneve/ONIMods/blob/38dfc8c9aa2e3263c5bb033a8eafa64c20785485/FastTrack/ConduitPatches/ConduitFlowVisualizerPatches.cs)
与[植物相关源码](https://github.com/peterhaneve/ONIMods/blob/38dfc8c9aa2e3263c5bb033a8eafa64c20785485/FastTrack/CritterPatches/PlantElementPatches.cs)分别记录了具体实现。

直接把植物生长或流体更新合并成更大的时间步，需要另外证明中间事件顺序。例如在两次更新
之间资源耗尽又补充，合并后的计算可能漏掉原本应发生的状态转换。一般情况下：

`advance(dt); advance(dt)` 与 `advance(2 * dt)` 并不自动等价。

## 2. 已实现的行为

在「模组 → CycleTrim → 设置 → 管道动画刷新」选择模式，修改后重启。

| 模式 | 稳定输入下的重建频率 | 已完成网格的绘制 |
| --- | --- | --- |
| Full，默认 | 保持原版每次调用 | 保持原版 |
| Half | 每 2 个渲染帧重建 | 中间帧继续绘制 |
| Quarter | 每 4 个渲染帧重建 | 中间帧继续绘制 |

刷新还受一个未缩放时间期限约束：距上次成功构建达到 100 毫秒时，下次绘制调用立即重建。
低帧率下不会为了凑满四帧继续等待；没有绘制调用时，也不会凭空产生额外帧。时间读取使用
`Time.unscaledTimeAsDouble`，避免先读取单精度时间再转换造成长会话精度损失。

```text
查看内容、需要音效、输入变化或期限已到：执行原网格构建
否则：绘制上次完整构建的两张网格
只有完整成功的构建，才能成为下一次可复用的结果
```

**变化会唤醒显示，而不等待固定周期。** 以下依赖都参与失效或输入比较：

- 原 `Sim200ms` 调用和网络重建的前后边界；同一时间戳及同数量拓扑变化都被覆盖。
- 原实时可见范围查询、当前世界、网格尺寸。
- 高亮、覆盖层、导热类型着色、相关样式和纹理参数。
- 流管理器、网格、材质及其所属对象的身份和生命周期。

覆盖层查看管道内容、需要收集音效时执行完整原路径。外层 `Render` 的动画时钟和音频处理
持续执行。低频模式会降低几何插值和精灵相位的采样频率，这是该显示选项的实际视觉取舍。

## 3. 缓存有效性比计时器更重要

**空视野不能被误认成新画面。** 原方法发现没有可见管道时直接返回，旧网格内容仍可能存在。
补丁严格核对正常分支依次完成 moving/static 两次 `End` 调用，并在收尾后标记成功。
空返回会撤销旧缓存，之后继续原路径，不会把之前视野的管道重新画出。

**异常和重入不会留下半成品。** 第一次绘制后、第二次绘制后及收尾阶段的异常都清除缓存
资格并保留原异常。缓存重绘失败时，不回放已经执行过的绘制。同一实例重入后使用原路径；
不同实例的状态分别保存。

**失效覆盖读取和构建的整个区间。** 捕获输入之后会再次检查模拟版本；完成构建后还要检查
版本和输入一致性。高亮或覆盖层在构建过程中改变又恢复，也会留下失效记录。
每帧重复设置同一覆盖层状态不会无条件破坏缓存。

**资源保持在原线程处理。** 补丁在游戏创建 visualizer 后安装，保存安装线程身份，
所有 Unity 网格和材质调用仍在原绘制路径执行。[Unity 官方说明](https://docs.unity3d.com/cn/2022.3/Manual/overview-of-dot-net-in-unity.html)
指出多数 Unity API 要求主线程调用；增加后台线程并不能免除数据快照、同步和资源提交的成本。

## 4. 兼容范围

发现 FastTrack 时让出这组显示优化。安装时检查 visualizer、mesh、可见上下文、工作收尾
和相关读取依赖上的外部 Harmony 补丁；不兼容的结构保持原方法。
原方法和回调顺序由现有方法体执行，补丁没有另写一套管道传输算法。

设置要求重启。之后给 `Render` 或 `RenderMesh` 新增补丁会撤销复用；仅在依赖方法上迟装补丁、绕过
Harmony 的改写或任意直接改写私有存储，仍需要重启后重新检测。不能将安装期检测描述为
对任意运行时改写的完整监控。

外层 `Render` 内唯一的原调用被分派到普通 C# 函数；该函数通过缓存的 open delegate 调用
当前 `RenderMesh` 入口，保留原有回调链。后续 prefix 跳过原方法、postfix、抛异常的 finalizer
及卸载行为已在 .NET 10 的真实 Harmony 夹具中验证。引用和宿主的 Harmony 都是 2.4.2；
Unity/Mono 下现有委托对后续 detour 的响应仍需实机验证。

## 5. 实验与复现

测量计划先于计时固定在 [measurement-plan.md](cycletrim-visual-refresh-validation/measurement-plan.md)。
基准使用同一原始宿主夹具的 reverse patch 作为基线，候选执行真实 Harmony 补丁。
输入捕获、失效观察、网格模拟工作、绘制和完成检查都计入候选耗时。
夹具执行独立整数工作，不执行游戏程序集或 Unity 渲染器。

固定协议为 .NET 10.0.12 / SDK 10.0.401、Release、Linux x64、共享宿主 CPU 2，
每场景 3 组预热、7 组原版/候选交替配对，每个样本 6000 帧。每轮覆盖 12 场景，
有 84 组正式配对；正式样本的当前线程分配均为 0 B。初次预热曾因把零分配门槛错误地
施加于预热阶段而停止；随后只对正式样本执行该门槛，原始预热日志也保留。

四轮完整结果均保留；最终选用 **inplace** 轮对应的较简单实现：

| 轮次 | 结构变化 | 预定的四项中/大工作量耗时门槛 |
| --- | --- | --- |
| initial | Harmony prefix/finalizer 传递大状态 | Half medium、Quarter medium 失败 |
| dispatch | 普通函数分派，保留当前原方法入口 | Half medium 失败 |
| inplace，选定 | 快照按引用比较、直接填写 out、必重建时不比较旧输入 | Half medium 失败 |
| aggregate，弃用 | 初始化时聚合私有字段读取 | Half medium 失败 |

聚合读取候选没有提供足够证据来支持新增的动态表达式与 Mono 私有访问复杂性，因此已撤回。
它起初遇到游戏 `Expression` 类型与 LINQ 同名的引用构建错误，限定命名空间后构建通过；
该候选的测量、两个源码差异和构建记录均保留。不同轮次的宿主负载会变化，表中各轮的
比值只能解释该轮配对结果，不能据此宣称某一步单独带来了多少增量收益。

### 选定版本的完整结果

下表耗时是 **6000 帧的中位毫秒数**；加速比是 7 组 `原版 / 候选` 配对比值的中位数，
不等同于两个独立中位数相除。比值低于 1 表示退化。[完整样本](cycletrim-visual-refresh-validation/inplace-benchmark.log)
与[四轮范围及中位数](cycletrim-visual-refresh-validation/benchmarks.json)可逐项复核。

| 场景 | 原版 ms | 候选 ms | 配对加速比 |
| --- | ---: | ---: | ---: |
| 稳定 Half，中等工作量 256 | 3.432 | 3.806 | 0.939× |
| 稳定 Quarter，中等工作量 256 | 3.160 | 2.614 | 1.168× |
| 稳定 Half，大工作量 4096 | 40.830 | 22.248 | 1.854× |
| 稳定 Quarter，大工作量 4096 | 40.714 | 12.305 | 3.203× |
| 稳定 Half，零整数工作控制 | 0.918 | 2.245 | 0.378× |
| 稳定 Quarter，零整数工作控制 | 0.838 | 1.883 | 0.445× |
| 每帧改变高亮 | 3.472 | 8.293 | 0.430× |
| 内容覆盖层 | 3.351 | 3.826 | 0.930× |
| 每帧音效请求 | 3.516 | 3.805 | 0.932× |
| 每帧模拟更新 | 3.535 | 8.093 | 0.441× |
| 低帧率，125 ms/帧 | 3.814 | 8.928 | 0.405× |
| 每三帧模拟更新 | 3.651 | 5.347 | 0.657× |

稳定场景中 Half 的构建次数为 **6000 → 3000**，Quarter 为 **6000 → 1500**；
所有场景均保留 **12000 次绘制**。每帧模拟场景仍调用模拟 6000 次，每三帧模拟场景仍调用
2000 次且随即刷新。音效场景保留 6000 次重置和触发。测试分别比较 moving/static 的
payload、顺序 checksum 和动画时间，不使用会掩盖错误的两个互补值合计。

**性能结论是有条件的。** 重建较贵而输入稳定时，显示复用能净提速；工作很小或每帧都变化时，
检查成本可能超过收益。本版本没有通过全部预定耗时门槛，基准对此返回非零退出码。
汇总脚本对数据一致性返回成功不会改变这个事实。宿主夹具没有真实 Unity 相机、网格上传、
GPU 或官方物质模拟，不能从以上比值推算整局 FPS。

### 正确性与可复现性

- 优化宿主套件 **4566 条断言**通过：既有 4080 条，以及本轮 74 条门闩和 412 条可视化断言。
- 设置/诊断套件通过，其中设置断言 **76 条**。
- 三个负向门闩变体均被原测试捕获：遗漏撤销旧缓存、只比较完成时钟、使用单精度时钟。
- 全部 **13 个引用文件**哈希匹配；新补丁 **49 项反射接口签名**核验通过。
- 选定源码严格 net48 托管构建 **0 警告、0 错误**，`IncludeOniSim=false`。

命令、源码/二进制指纹、各轮差异和日志索引见[验证目录说明](cycletrim-visual-refresh-validation/README.md)
及[验证记录](cycletrim-visual-refresh-validation/receipt.json)。只有独立夹具被执行；下载的游戏引用
仅用于编译和静态元数据检查，未执行或随证据提交。

## 6. 实机验收与后续范围

仓库 `docs/autonomous-iteration.md` 要求通过 Steam 启动游戏的验收后再合并此类生命周期与
渲染补丁。当前环境只有编译引用和宿主测试设施。待验收项目包括气液管道、覆盖层、音效、
镜头缩放与移动、跨星球、建造拆除、暂停倍速、存档重载和退出清理。
Full 保持默认；Half/Quarter 是已实现且可测试的候选选项。当前既有未通过的合成耗时门槛，
也缺少游戏运行证据。后续应先在真实热点中确认收益范围，再决定是否保留两个低频档位；
本轮不靠改变工作量、提高门槛或隐藏慢场景来宣布通过。

植物生长、反应堆、呼吸和疾病传播的低频积分，以及任意把 Unity 对象操作移入后台线程，
不属于这次已完成的实现。下一步若优化这些系统，应先查明实际热点和依赖，再选择等价的
局部缓存、脏区域、批处理或纯计算并行，并单独建立对照证据。

## 7. 证据对应与自检

| 结论 | 对应证据 | 范围 |
| --- | --- | --- |
| 稳态减少 50% / 75% 的显示重建 | 门闩测试、真实 Harmony 夹具计数 | 固定帧率、输入稳定 |
| 每帧绘制、时钟、音效和模拟调用保留 | 逐帧 payload/次数、故障和重入回归 | 独立宿主夹具 |
| 接口可编译 | 固定引用哈希与严格 net48 构建 | 引用声明构建 737790 |
| 原方法的空返回、两个 End 和模拟顺序 | 固定 744825 方法体来源 | 源码契约 |
| 游戏内画面和整局 FPS | 待 Steam 实机验收 | 当前未取得证据 |

自检：贡献是明确的显示采样边界；说明给出了失效条件和复现入口；实验保留慢场景；
验证区分源码、引用、宿主和实机；设计没有把低质量物质或缓慢变化误当作可以删除的模拟。

## 一手资料

- [744825 ConduitFlowVisualizer](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/ConduitFlowVisualizer.cs)
- [744825 ConduitFlow](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/ConduitFlow.cs)
- [744825 GridVisibleArea](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/GridVisibleArea.cs)
- [744825 ClusterManager](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/ClusterManager.cs)
- [FastTrack 管道显示](https://github.com/peterhaneve/ONIMods/blob/38dfc8c9aa2e3263c5bb033a8eafa64c20785485/FastTrack/ConduitPatches/ConduitFlowVisualizerPatches.cs)
- [FastTrack 植物相关优化](https://github.com/peterhaneve/ONIMods/blob/38dfc8c9aa2e3263c5bb033a8eafa64c20785485/FastTrack/CritterPatches/PlantElementPatches.cs)
- [Unity 官方线程说明](https://docs.unity3d.com/cn/2022.3/Manual/overview-of-dot-net-in-unity.html)

文档按 Research-Paper-Writing-Skills 的实验与审阅流程组织，区分实现、证据和仍待验证的范围。
