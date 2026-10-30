# CycleTrim 后续优化：轨道查找与动画重复更新

日期：2026-10-30。增量基线：`a8e21b65b71519f00e6fca4386199cafe0b640d6`。

本轮把反复逐项搜索变成一次调用内的临时索引，并在动画数据完全相同时省掉重复的版本更新和缓冲区复制。

报告按三个问题组织：哪些重复工作可以省、原行为如何保留、测试实际覆盖到哪里。实现与测试都位于原 CycleTrim 项目；两个新增选项分别控制，默认关闭，修改后需要重启。

## 1. 输送轨道：搜索足够重复时才建表

`SolidConduitFlow.TryAdd` 的末段对每条保留路径反复搜索新路径中的轨道。一次线性查找最多检查整条路径；许多查询重复扫描同一个列表时，可以先建立“轨道编号 → 首次出现位置”的表。

本轮只替换该方法第三处 `List<Conduit>.FindIndex` 调用。前两段包含判断、路径顺序、倒序删除、方向读取和短路求值仍由原方法执行。尤其保留首次端点匹配及逆序端点的原始处理方式，不把它们重写成通常的子串匹配。

索引采用固定准入规则，而不是看到列表就建表：

```text
列表长度在 32..4096 之间
且原始查找已经累计检查至少 2 × 列表长度个元素
且预计至少还剩 4 次查询
    → 建立首次位置索引
否则
    → 继续原 FindIndex
```

早期命中只按实际检查的 `返回位置 + 1` 累计；未命中按列表长度累计。短列表、只查一次、很快命中、过长列表和正在被裁剪的新路径自身都会保留直接搜索。索引最多保存 4096 个整数键值，结束后清空。

索引只在一次调用、一个线程内有效。换路径、进入或退出嵌套调用、异常退出都会清理。复用前复制保存的公开列表枚举器并调用一次 `MoveNext()`，借助列表的版本检查发现修改；因此 `list[i] = value` 这种长度不变的替换也会失效。内部发现失效后重新执行原查询。

补丁要求找到恰好三处搜索，最后一处紧邻已验证的委托构造；委托只能读取两个轨道编号并比较相等。第一次建表前检查相关方法的 Harmony 补丁，包含 `FindIndex` 本身。结构不符或存在冲突时保留原搜索。

## 2. 动画：值没变就不重复通知复制

动画覆盖信息由两个 setter 写入。它们每次写入十个浮点字段，然后增加版本号；显示端按对象身份和版本号决定是否复制整片覆盖数据。相同字段被反复写入时，增加版本号会重复触发这次复制。

本轮保留原循环，在原字段写入位置比较新旧位模式，并在调用内部记录是否发生变化。正常完成且至少访问了一个字段、所有字段都相同时，省去版本递增；有变化或没有访问字段时仍调用原 `MarkDirty`。两个保留字段 `unused1`、`unused2` 原样保留。

比较使用浮点的 32 位表示，因此 `+0` 与 `-0`、不同 NaN 负载和非常小的数值变化都能区分。比较不使用误差容限，也不预先扫描整个范围。原参数求值、索引访问、部分写入和异常位置继续按原顺序执行。

异常恢复单独处理：如果一次写入在完成部分字段后失败，版本号可能仍是旧值。该实例用弱键记录，后续调用保留原来的 dirty 更新，即使下一次写入相同数据，也会重新通知显示端。标记值不持有实例，实例销毁后可被回收。

安装时核对 setter、版本访问器和 `MarkDirty` 的指令契约，并要求两个写入入口没有已发布的补丁。任一入口随后被重新编织，两个入口共同关闭版本合并；包括外部代码预先改字段，或吞掉部分写入异常后改走另一重载的情况。完成清理并重新安装整组补丁，或重启游戏后，才能开始新的优化会话。

版本号减少递增次数是这项优化的明确行为变化，接受条件是显示消费者拿到的所有字节保持一致。依赖每次 setter 调用都增加版本号的模组，需要保留原更新机制。

## 3. 验证设计与结果

性能测试前固定的问题、准入规则和测量方法记录在 [measurement-plan.md](cycletrim-moono-followup-validation/measurement-plan.md)。正确性使用生产 helper、生产补丁及真实 Harmony，夹具为独立编写的合成模型。

宿主回归和编译结果如下。测试运行中的“跳过优化”日志包含故意注入的失配及冲突场景。

| 验证 | 结果 |
| --- | --- |
| 新增轨道回归 | 2285 条断言通过，含 2059 组原方法/真实补丁差分 |
| 新增动画回归 | 1013 条断言通过，含逐位输出、部分失败恢复及版本消费者 |
| 上轮存档与电力回归 | 375 + 109 条断言通过 |
| 设置及现有探针回归 | 通过，包含 70 条设置断言 |
| 完整托管 net48 参考构建 | 0 警告、0 错误 |

### 3.1 轨道查找

每个场景使用 3 次同函数预热、7 对交错基线/候选样本。计时包含原方法、作用域、兼容检查、建表和清理，补丁安装在计时外。下表为每批耗时中位数；配对加速比是各对基线/候选比值的中位数。

| 场景 | 每批调用数 | 原实现 ms ↓ | 候选 ms ↓ | 配对加速比 ↑ |
| --- | ---: | ---: | ---: | ---: |
| 长路径，查找多次未命中 | 100 | 89.614 | 74.466 | 1.184× |
| 长路径，含共享轨道 | 100 | 82.102 | 68.597 | 1.182× |
| 长路径，多数很早命中 | 1,000 | 11.778 | 16.287 | 0.751× |
| 长路径，只查询一次 | 1,000 | 7.607 | 7.539 | 0.990× |
| 短路径 | 1,000 | 6.262 | 9.431 | 0.703× |
| 包含判断提前返回 | 100,000 | 24.188 | 30.086 | 0.783× |

长路径两组采用 32 条保留路径、每条 256 个轨道、新路径 128 个轨道。早期命中与单次查询使用一条 1024 长路径；短路径使用 8 条 16 长路径。所有配对的结果校验和及方向读取次数一致。生产 helper 的独立计数见证还确认：20 次长度 64 的未命中查找，逐项谓词比较由 1280 次降到 128 次，之后复用索引。

首轮实现的 `Delegate.Method` 访问造成约 96 字节/查询的额外分配。修正后，普通扫描不用读取委托元数据；已建表查询也直接使用安装时已验证的捕获类型。早命中、单次查询及短路径对照的候选分配量与基线相同。长路径每 100 轮仍有约 1.29 MB 额外分配，包含兼容检查成本。

两轮之间原始基线耗时变化明显，因此不按跨轮中位数相除推导修正幅度。最终配对结果支持长路径重复扫描场景的收益；短路径和早期命中场景仍有作用域及查表管理开销，选项继续默认关闭。

### 3.2 动画字段与版本消费者

每组固定 256 个符号、每次更新调用一个 setter、每批 30,000 次更新，使用 3 次预热和 7 对交错样本。首次上传在计时外。消费者仅模拟按身份与版本执行托管缓冲区复制，每次复制 12,288 字节。

| 含版本消费者的场景 | 原实现 ms ↓ | 候选 ms ↓ | 配对加速比 ↑ | 每批复制次数：原 → 候选 |
| --- | ---: | ---: | ---: | ---: |
| 重复同一符号 | 4.479 | 0.549 | 8.105× | 30,000 → 0 |
| 每次改变一个符号 | 4.551 | 5.023 | 0.928× | 30,000 → 30,000 |
| 重复写入 4 个符号 | 5.491 | 2.351 | 2.355× | 30,000 → 0 |
| 重复写入全部 256 个符号 | 60.911 | 119.438 | 0.514× | 30,000 → 0 |

重复写入场景省下每批 368,640,000 字节的复制，所有最终缓冲区字节一致；每次改变数据的对照仍复制 30,000 次。两种实现所有正式动画样本的计时区间分配量均为 0。

单独比较 setter 可以看清代价来源：读取并比较十个位模式本身要花时间。内联提示减少小函数调用成本，但大量范围比较仍可能比直接写入并复制更慢。

| 仅 setter 的场景 | 原实现 ms ↓ | 候选 ms ↓ | 配对加速比 ↑ |
| --- | ---: | ---: | ---: |
| 重复同一符号 | 0.237 | 0.438 | 0.541× |
| 每次改变一个符号 | 0.275 | 0.452 | 0.616× |
| 重复写入 4 个符号 | 1.206 | 2.400 | 0.535× |
| 重复写入全部 256 个符号 | 58.278 | 122.358 | 0.461× |

这组结果支持“少量重复字段写入、随后消费整片数据”的合成场景；每次变化以及大范围比较的退化同样保留。托管复制没有模拟 GPU 驱动、纹理提交或真实游戏里的重复率，因此按需开启并在实际存档测量。

### 3.3 复现与原始证据

环境为 Linux x64、.NET SDK 10.0.401 / runtime 10.0.12，固定 CPU 0，宿主 CPU 为 AMD EPYC 9V74。宿主为共享虚拟环境。每个样本的时间、分配、校验和、耗时范围及源文件 SHA256 均记录在 [receipt.json](cycletrim-moono-followup-validation/receipt.json) 与 [benchmarks.json](cycletrim-moono-followup-validation/benchmarks.json)。

首轮样本也已保留。对最终源码使用 `git apply -R --unidiff-zero` 反向应用 `conveyor-hotpath.patch` 和 `animation-hotpath.patch` 可恢复首轮对应代码；两份补丁已通过反向应用检查，生产准入阈值和基准场景在两轮之间保持一致。

```bash
dotnet run --project tests/CycleTrim.Optimizations.Tests -c Release \
  -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true
dotnet run --project tests/CycleTrim.PerformanceProbe.Tests -c Release \
  -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true \
  -- --skip-synthetic-performance

# 每个基准使用单独进程，并依次运行。
taskset -c 0 dotnet tests/CycleTrim.Optimizations.Tests/bin/Release/net10.0/CycleTrim.Optimizations.Tests.dll --benchmark-conveyor
taskset -c 0 dotnet tests/CycleTrim.Optimizations.Tests/bin/Release/net10.0/CycleTrim.Optimizations.Tests.dll --benchmark-animation

dotnet build mods/CycleTrim/CycleTrim.csproj -c Release \
  -p:OniManagedPath=/path/to/verified/reference-assemblies \
  -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true \
  -p:IncludeOniSim=false

python3 docs/cycletrim-moono-followup-validation/summarize.py
```

`IncludeOniSim=false` 仅限定这次本地参考构建的范围，原生仿真核心的默认配置保持原值。复现时可将 CPU 0 改成当前进程允许的固定 CPU。

## 4. 证据范围与实机验收

参考程序集清单声明游戏构建 **737790**，方法源码固定到声明 **744825** 的另一修订。静态检查发现参考 DLL 已剥离目标方法体，因此它们用于核对接口和编译；运行时指令匹配由补丁启动时检查。[签名核对记录](cycletrim-moono-followup-validation/reference-signatures.json)

本轮执行环境为 .NET 宿主；Steam、Unity/Mono、真实存档及游戏帧时间仍需本地验收。保持仓库 `docs/autonomous-iteration.md` 的实机要求，并在同一存档副本上逐项开关比较：

1. 轨道：记录首次安装是否匹配，重建长轨道、增删分支、闭环、共享路段和跨世界切换，比较路径与物品流向。
2. 动画：检查批量覆盖、单帧覆盖、角色装备/外观切换、世界切换和纹理刷新；记录实际重复写入率及数据复制次数。
3. 记录游戏版本、提交、其他模组、设置、首次使用耗时和交错样本；同时检查更新延迟和内存。

普通列表的并发修改、非 Harmony 改写及直接操作底层存储没有完整覆盖。同一调用期间在已加载程序集上安装新补丁，也可能晚于本次兼容性检查。动画的私有 dirty 依赖若在初始化后被单独打补丁，需要重新启动并确认回退。

## 5. 其他方向的取舍

| 方向 | 本轮判断 | 后续需要先解决的问题 |
| --- | --- | --- |
| 场景分区清理 | 暂不加句柄查询缓存：原查询已经是少量数组访问 | 若只扫描待删除句柄，需要追踪释放、复用和节点移动，并按原位置倒序删除以保持交换删除结果 |
| 拾取任务工作偏移 | 保留原更新机制 | 双缓冲脏集合需要覆盖移动、地形、标签及重算期间再次发生的变化 |
| 轨道委托分配 | 本轮保留原委托构造，主要减少比较次数 | 只有同时证明闭包捕获、异常和补丁相互作用一致后，才能进一步消除分配 |
| 动画脏对象集合 | 本轮只合并同值覆盖数据的版本更新 | 减少批次遍历前，应证明每种外观变化都会进入脏集合，避免显示滞后 |

## 6. 来源

- 机制研究：[oni-moono 固定修订](https://github.com/tigin-backwards/oni-moono/tree/2d4aca2aeebe2351752d24737230339e0abfbb5e)。生产实现与测试独立编写；研究材料保留在临时目录。
- 轨道逻辑：[SolidConduitFlow.cs](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/SolidConduitFlow.cs)。
- 动画字段写入：[SymbolOverrideInfoGpuData.cs](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp-firstpass/SymbolOverrideInfoGpuData.cs)；版本消费者：[KAnimBatch.cs](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp-firstpass/KAnimBatch.cs)。
- 列表版本语义：[Microsoft 文档](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.list-1.enumerator?view=netframework-4.8.1)、[.NET Framework 参考实现](https://github.com/microsoft/referencesource/blob/main/mscorlib/system/collections/generic/list.cs)、[.NET 10 实现](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/List.cs)。
- Harmony 安装顺序：[2.4.2 PatchProcessor.cs](https://github.com/pardeike/Harmony/blob/v2.4.2.0/Harmony/Public/PatchProcessor.cs)。
- 报告按用户指定的 [Research Paper Writing 技能](https://github.com/Master-cai/Research-Paper-Writing-Skills/tree/main/research-paper-writing)组织问题、方法、对照及结论依据。

## 7. 结论核对

| 核对维度 | 主要判断 | 证据与结论 |
| --- | --- | --- |
| 改进内容 | 临时索引和版本合并能减少指定的重复工作 | 生产补丁实际建表见证、1280 → 128 比较计数，以及消费者复制计数支持 |
| 解释与复现 | 能从原始样本重新计算结论 | 固定规则、完整命令、源文件哈希和可运行汇总脚本已提供 |
| 性能依据 | 收益随工作负载变化 | 同一方案同时报告重复、变化、小输入、早期命中和大范围退化对照 |
| 覆盖完整性 | 宿主行为、接口和游戏运行分别评价 | 正确性及参考构建通过；Steam/Unity/Mono 和真实模组组合需继续实机验证 |
| 方法边界 | 不用扩大缓存生命周期换取数字 | 轨道仅单次调用；动画只在核对过的 dirty 契约下合并，失败实例和冲突入口保留原更新 |
