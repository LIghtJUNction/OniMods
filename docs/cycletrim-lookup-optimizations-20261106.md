# CycleTrim：减少重复元数据查询与状态机扫描

日期：2026-11-06。实现基线：`a6cfb9bfc581e48da6e14d23e8a5c1941bfd74e4`。
提交与审查入口：[PR #566](https://github.com/LIghtJUNction/OniMods/pull/566)。

本轮已经实现并测试两项改动：存档在一次调用内复用类型信息；状态机查询在列表不变时复用剩余搜索结果。
存档重复类型夹具相对既有优化为 2.047×，状态机长列表重复末项命中为 15.357×。
状态机冷启动、短列表和高变更率场景存在退化，因此新增状态机选项默认关闭。
这些比例只对应下文列明的合成工作负载。真实游戏验收尚未执行。

实际性能测试版本分别是存档提交
[`9b06e51`](https://github.com/LIghtJUNction/OniMods/commit/9b06e51d69901aea5f0d83a08dff724df95da7a3)
和状态机提交
[`29fa09c`](https://github.com/LIghtJUNction/OniMods/commit/29fa09cb352f4bb45f32bc03ee1f326b0e8d15f5)。
源码状态与已执行基准 DLL 的 SHA-256 见
[source-receipt.json](cycletrim-lookup-validation-20261106/source-receipt.json)。

## 研究来源与范围

本轮以 [oni-moono@6ac665c](https://github.com/tigin-backwards/oni-moono/tree/6ac665ce05e97ad27bbb106b4de91c69263863e5)
为机制参考，在已有存档、电力、轨道和动画优化之上减少重复查询。
Moono 的整体运行时改造由其项目自行提供；本轮 CycleTrim 改动保留现有游戏运行时和仿真更新频率。
上游的 [许可说明](https://github.com/tigin-backwards/oni-moono/blob/6ac665ce05e97ad27bbb106b4de91c69263863e5/docs/LICENSING.md)
声明 All Rights Reserved；本轮代码独立实现，没有复制或打包 Moono 源码。

已有电力缓存 PR #563 和测试工具重构 PR #565 不在本轮修改范围内。
游戏方法语义参考固定到
[Kupie/ONI_Decomp@8d4ace1](https://github.com/Kupie/ONI_Decomp/tree/8d4ace10c2b6a84cee394653a91b990a5122e636)。
该来源声明构建 744825；用于编译的引用程序集声明构建 737790。11 个引用 DLL 的长度和 SHA-256
均与仓库清单一致，且仅用于编译和静态元数据读取。

## 存档：在一次调用内复用刚查到的类型信息

原有优化已经缓存了类型的固定标记和名称，但保存过程仍会多次进入全局弱引用表。
计数组件时查一次标记，写组件时再查标记和名称；相邻步骤经常使用同一个类型。
本轮在 `SaveWithoutTransform` 中加入一个方法局部值，只保留最近查询的类型及其不可变元数据。

局部值随调用开始初始化，正常结束或抛异常后随栈帧释放。嵌套存档和其他线程各用自己的局部值。
输出顺序、组件回调、写入字节和异常传播继续由原方法完成。自定义反射、其他属性查询、继承查询和
尚未完成的 `TypeBuilder` 保留原来的处理路径。

生产实现：[SaveTypeMetadataCache.cs](../mods/CycleTrim/Core/SaveTypeMetadataCache.cs)、
[SaveTypeMetadataPatch.cs](../mods/CycleTrim/Patches/SaveTypeMetadataPatch.cs)。
该组沿用既有的 `OptimizeSaveMetadata` 设置及默认值。

### 存档性能结果

下表比较此前已经优化过的存档补丁与本轮补丁。测量执行同一个完整的两遍保存夹具，包含字符串和整数写入。
每种运行时模式启动 3 个独立进程，每个进程预热后交替执行 7 对旧／新版本。
表内速度比先对每个进程的 7 对“旧耗时 ÷ 新耗时”取中位数，再对 3 个进程中位数取中位数。

| 合成保存场景 | 默认运行时速度比 ↑ | 关闭 tiering 速度比 ↑ | 默认模式改善配对 |
| --- | ---: | ---: | ---: |
| 64 个同类型组件 | 2.047× | 1.994× | 21/21 |
| 64 个组件，4 种类型轮换 | 1.469× | 1.177× | 21/21 |
| 64 个组件，7/8 跳过保存 | 1.971× | 1.871× | 21/21 |
| 单组件保存 | 1.423× | 1.442× | 21/21 |
| 空保存 | 1.014× | 1.028× | 14/21 |

空保存没有稳定收益：默认模式的三个进程中位数为 0.935×、1.091×、1.014×。
关闭 tiering 的混合场景有 3/21 对退化，配对范围为 0.715–1.722×，三个进程中位数均大于 1。
这些波动保留在完整样本中。420 个测量窗口均为 0 B 分配，且所有配对的输出字节和长度一致。
首次元数据创建、测试缓冲区和 writer 初始化在预热或计时窗口外。

证据：[全部存档原始样本与执行记录](cycletrim-lookup-validation-20261106/save/)、
[逐进程和汇总数据](cycletrim-lookup-validation-20261106/save-summary.json)。

此结果度量合成夹具中的完整两遍保存。实际存档还包含对象遍历、组件载荷和磁盘操作，收益比例取决于这些工作量。

## 状态机：首项保留原检查，其余查询按列表版本复用

[Moono 的状态机查询优化](https://github.com/tigin-backwards/oni-moono/blob/6ac665ce05e97ad27bbb106b4de91c69263863e5/src/OniMoono.GameTweaks/FastSmiLookups.cs)
利用一个稳定条件：列表没有变化时，同一类型的首次可赋值对象也不变。
CycleTrim 每次先执行原来的首项检查。首项匹配或列表为空就原样返回；首项不匹配时才考虑复用，
需要继续搜索时从索引 1 进入原循环，保留首次匹配次序和自定义类型回调的调用次数。

缓存以实际列表为弱引用键，存储版本、查询类型和结果索引。列表增删、清空、替换元素、重排时，
`List` 版本使旧答案失效；整个列表被替换时自然使用另一个弱键。空结果也缓存。
缓存值不引用控制器、列表或状态机实例，因此已删除实例和被替换列表可以回收。

第一条答案直接保存在值对象中，第二种查询才创建字典；总共最多缓存 16 种类型，超出后执行原搜索。
表中最多强持有 16 个查询 `Type`，直到后续查询发现列表版本变化或列表被回收。
只有安装补丁的主线程可以使用缓存；工作线程、短列表及自定义反射类型继续完成原搜索。
短列表并非完全没有新增开销：首项不匹配后仍进入缓存 helper，helper 判断长度不超过 4，再继续原循环。

安装时核对完整的搜索指令、成员身份、局部类型和分支目的地。缺失接口或不支持的指令形状保留原方法。
之后其他 Harmony 前缀、后缀、最终器或转译器导致目标包装方法重建时，立即停止复用，直到重启游戏。
这些检查发生在安装或包装方法重建时。

实现：[StateMachineLookupCache.cs](../mods/CycleTrim/Core/StateMachineLookupCache.cs)、
[StateMachineLookupPatch.cs](../mods/CycleTrim/Patches/StateMachineLookupPatch.cs)。
`OptimizeStateMachineLookups` 默认关闭，通过 CycleTrim 设置开启后需要重启。

### 状态机性能与代价

基线是通过 Harmony reverse patch 保留的原始顺序搜索；候选是实际生产转译器与 helper。
每种模式使用三个新进程，每进程七对交替采样，汇总方法与存档相同。
普通场景每个窗口查询 60,000 次，冷启动场景查询 10,000 次。

| 合成查询场景 | 默认运行时速度比 ↑ | 关闭 tiering 速度比 ↑ | 默认模式改善配对 |
| --- | ---: | ---: | ---: |
| 64 项，末项命中，列表稳定 | 15.357× | 16.844× | 21/21 |
| 64 项，无匹配，列表稳定 | 16.184× | 16.452× | 21/21 |
| 64 项，首项命中 | 0.938× | 0.960× | 3/21 |
| 4 项，末项命中 | 0.799× | 0.777× | 1/21 |
| 每次查询都修改列表 | 0.939× | 0.990× | 4/21 |
| 每次查询都新建控制器与列表 | 0.448× | 0.422× | 0/21 |
| 17 种查询轮换，超出 16 条容量 | 6.313× | 7.471× | 21/21 |

小于 1 表示候选更慢。默认模式的冷启动耗时约为基线的 2.23 倍；短列表约增加 25.1% 耗时，
首项命中约增加 6.6%。首项命中虽没有缓存 helper 调用，实测仍有退化，不能据此声称完全没有开销。
每次变更时必须重新搜索，缓存也不能摊薄其管理成本。上述不利对照全部保留。
由这些结果推断，状态机缓存适合较长、稳定且被反复查询的列表；实际游戏中这类查询的占比仍需测量。

默认模式末项命中的三个进程中位数为 12.675×、15.357×、16.396×，全部 21 对范围为
10.478–18.354×；无匹配的配对范围为 10.129–21.913×。环境是共享宿主，精确倍率具有波动，
但这两个目标场景在两种模式下的全部配对均有改善。

588 个正式测量窗口包含 504 个暖态窗口和 84 个冷启动窗口。暖态全部为 0 B 分配；
294 对结果 checksum 全部一致。冷启动中，基线每次查询分配 592 B，候选为
648.3944–664.7888 B，即每次额外约 56.4–72.8 B。这里是每批累计托管分配除以查询次数，
包含两边共同的控制器及列表创建成本，不代表单个对象的大小，也不代表进程常驻内存。

独立的未计时工作量记录验证了减少搜索的来源：60,000 次稳定长列表查询仍执行 60,000 次首项检查，
但剩余循环为零；原搜索则执行 3,840,000 次类型比较。17 种查询轮换时命中缓存 56,471 次，
其余 3,529 次继续原循环。helper 探测、命中及剩余返回次数来自实际插桩，比较总数由固定夹具推导；
插桩在正式预热前已卸载。

证据：[全部状态机原始样本与执行记录](cycletrim-lookup-validation-20261106/state-machine/)、
[逐进程和汇总数据](cycletrim-lookup-validation-20261106/state-machine-summary.json)。

## 正确性与构建验证

| 验证 | 结果 |
| --- | --- |
| 全部优化宿主断言 | 20,633 条通过；基线为 7,999 条 |
| 其中存档 | 492 条，新增 117 条；包含真实 Harmony 和字节比较 |
| 其中状态机 | 12,517 条；9 个独立进程场景 |
| 设置加载及开关 | 77 条设置断言通过；含启动前配置与重启语义 |
| 既有探针、刷新及 fetch 控制 | 通过 |
| 实际引用程序集接口检查 | 通过；ilspycmd 11.0.0.9375 静态读取 |
| 接口检查器反例 | 缺失 GetSMI、字段改成 IList 均按预期拒绝 |
| 完整托管 net48 参考构建 | 0 警告、0 错误；`IncludeOniSim=false` |

断言数量包含确定性序列的多次比较。状态机用例覆盖 1,200 轮列表变更的身份对照、首个匹配、负查询、
同长度替换和重排、共享与独立列表、容量溢出、异常、自定义类型首项回调替换列表、工作线程和弱生命周期。
独立进程还验证关闭选项、既有补丁及后加四类 Harmony 补丁的回退。存档用例保留所有原覆盖，并增加
跨调用、嵌套、两遍之间更换组件、部分输出和异常后回收等测试。

原始验证记录见 [checks 目录](cycletrim-lookup-validation-20261106/checks/)。
引用程序集来源、哈希与构建号见
[reference-receipt.json](cycletrim-lookup-validation-20261106/reference-receipt.json)。
代码提交 `29fa09c` 的 GitHub
[Mod quality](https://github.com/LIghtJUNction/OniMods/actions/runs/42185939786)
和 [ONI reference compatibility](https://github.com/LIghtJUNction/OniMods/actions/runs/42185939784)
均已通过；机器可读记录见
[ci-code-receipt.json](cycletrim-lookup-validation-20261106/ci-code-receipt.json)。

## 测试方法与复现

测量环境为 Linux x64、AMD EPYC 9V74 共享宿主，固定 CPU 0，SDK 10.0.401、MSBuild 18.9.11、
.NET 10.0.12、Release、C# 7.3。三个独立进程是每种模式的重复单位，同进程内的七对属于内部重复。
完整方案见 [measurement-plan.md](cycletrim-lookup-validation-20261106/measurement-plan.md)。

### 预热协议的诊断与修正

开发试跑先后在候选和原始基线的测量窗口中观察到 160 B 分配。逐阶段诊断在约第 9,997 次循环回边处
观察到同样大小的事件，生产查询阶段为零分配，缓存值和字典的构造计数也为零；仅关闭 OSR 后该事件消失。
这些证据将问题限定为与 OSR（长循环在运行中切换编译版本）相关的测量／运行时转换，
没有捕获原始失败样本的分配栈，也没有确认具体的 160 B 对象类型。

原每批预热仅有普通 5,000 次、冷启动 500 次，未覆盖正式窗口的长循环路径。
正式状态机矩阵改为验证、预热和测量共用一个非内联循环，每批预热直接取该场景的正式次数
60,000／10,000；20 批、7 对、所有场景、4／16 缓存限制和严格 0 B 暖态门槛均保持不变。
这项修订在正式矩阵前写入测量方案。关闭 OSR 只用于诊断，最终矩阵仍使用默认及关闭 tiering 两种模式。

早期失败、两种诊断及试跑全部保留在
[diagnostics 目录](cycletrim-lookup-validation-20261106/diagnostics/)，
[诊断 receipt](cycletrim-lookup-validation-20261106/diagnostics/receipt.json) 记录实际命令、环境覆盖及未捕获的信息。
这些样本未混入正式结果。存档协议没有变更，其历史 receipt 保留了当时实际使用的 inline driver。

### 复现命令

```bash
dotnet run --project tests/CycleTrim.Optimizations.Tests -c Release \
  -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true
dotnet run --project tests/CycleTrim.PerformanceProbe.Tests -c Release \
  -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true \
  -- --skip-synthetic-performance
dotnet build mods/CycleTrim/CycleTrim.csproj -c Release \
  -p:OniManagedPath=/path/to/verified/reference-assemblies \
  -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true \
  -p:IncludeOniSim=false

python3 docs/cycletrim-lookup-validation-20261106/run_matrix.py \
  --dotnet /path/to/dotnet \
  --assembly tests/CycleTrim.Optimizations.Tests/bin/Release/net10.0/CycleTrim.Optimizations.Tests.dll \
  --benchmark save-scoped --revision YOUR_TESTED_COMMIT --output /tmp/save-matrix
```

状态机矩阵将 `--benchmark` 改为 `state-machine` 并使用另一个空输出目录。
运行器记录每个子进程的命令、退出码和运行时环境覆盖；`null` 环境值表示启动前移除了该变量。
它同时记录基准 DLL 的 SHA-256，清除 12 个可能影响编译行为的环境变量，并保留超时失败记录。
全部样本可以重新汇总：

```bash
python3 docs/cycletrim-lookup-validation-20261106/summarize_matrix.py \
  docs/cycletrim-lookup-validation-20261106/state-machine \
  --benchmark state-machine --output /tmp/state-machine-summary.json

python3 docs/cycletrim-lookup-validation-20261106/summarize_matrix.py \
  docs/cycletrim-lookup-validation-20261106/save \
  --benchmark save-scoped --output /tmp/save-summary.json
```

汇总器核对六个进程、所有相邻配对、交替顺序、checksum、窗口数量及零分配门槛，再计算逐进程中位数。

## 后续值得实施的切片

**先用真实游戏采样判断状态机缓存的适用范围。**
当前冷启动和短列表对照明确退化。下一步应记录列表长度、重复查询率和变更频率，决定是否需要更早的短列表回退，
或只让有重复使用证据的列表进入缓存。任何准入规则都要计入自身查询和内存成本，保持本轮固定负对照后再比较。

**空间分区的无删除周期可以省掉失效句柄扫描。**
[FastPartitionerCleanup](https://github.com/tigin-backwards/oni-moono/blob/6ac665ce05e97ad27bbb106b4de91c69263863e5/src/OniMoono.GameTweaks/FastPartitionerCleanup.cs)
的关键观察是插入和移动也会标记节点为脏。可先实现较小的版本：首次完整扫描；之后某周期没有发生删除时，
仅保留原来的脏标记清理。发生删除仍完整扫描。验收需要覆盖句柄版本复用、删除顺序和跨节点移动。

**拾取偏移表可以按实际变更更新。**
[FastOffsetTables](https://github.com/tigin-backwards/oni-moono/blob/6ac665ce05e97ad27bbb106b4de91c69263863e5/src/OniMoono.GameTweaks/FastOffsetTables.cs)
把每轮全量检查转为变更事件驱动。CycleTrim 可用相同原则，但必须覆盖移动、地形变化、入库出库及重新注册等事件，
并维持原分组和物品顺序。该方向需要真实游戏事件覆盖后再实施。

## 游戏验收边界

本环境未找到 Steam 启动器和标准路径的 ONI 安装，未运行游戏。当前数据来自实际执行的宿主夹具、
真实 Harmony 补丁和静态引用编译；Unity/Mono、真实存档往返、跨世界及模组组合仍需要游戏验证。
按 [仓库验收规则](autonomous-iteration.md)，PR 保持 draft，未发布 Steam Workshop。
后续实机记录应包含游戏构建、存档、模组清单、选项、帧时间及进程内存，使用同一存档交替比较。

## 结论与证据核对

| 核对维度 | 当前证据 | 仍有的限制 |
| --- | --- | --- |
| 机制 | 存档执行路径见证；状态机实际 helper 计数 | 类型比较总数是依据夹具推导 |
| 语义 | 20,633 条宿主断言、字节及对象身份对照 | 不能覆盖所有真实存档和模组组合 |
| 性能与内存 | 全部固定场景、冷启动及退化样本公开 | 共享宿主倍率；未测游戏帧率或 RSS |
| 可复现性 | 固定提交、环境、DLL 哈希、原始样本与汇总脚本 | .NET 10 宿主不能代表 Unity/Mono |
| 发布状态 | 设置接线、参考构建及回退路径验证 | 状态机默认关闭，PR 等待 Steam 实机验收 |

## 交付位置

代码和测试位于 [PR #566](https://github.com/LIghtJUNction/OniMods/pull/566)。
报告副本、证据归档与源码补丁的外部目录为
[Google Drive：CycleTrim-lookup-validation-20261106](https://drive.google.com/drive/folders/1SDTj__C7KfWdvOsp9PqzjKyjTzUDG8K5)。
