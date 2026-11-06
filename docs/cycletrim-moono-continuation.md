# PR #558 续改：电路兼容性与无效比较

日期：2026-10-30。增量基线：`7aa5003c23cd41a385664e684c3debf918a798a2`。

本轮先修复「别的模组改变了电路查询，却仍复用旧答案」的问题，再减少已经确定没有必要的动画比较和轨道作用域管理。

## 1. 电力视图：补齐缓存答案的依赖

一次电力需求查询会沿变压器的 `Generator.CircuitID` 继续解析物理线路或虚拟线路。原兼容检查覆盖了耗电和额定功率，却漏掉这条线路解析链。其他模组即使通过 Harmony 改写其中的方法，原候选仍可能复用第一次答案。

现在除原有检查外，还检查 `ICircuitConnected` 的三个 getter、`Generator.CircuitID`、`Game.Instance`、`CircuitManager` 的两个 `GetCircuitID` 重载和 `GetVirtualCircuitID`，以及网络查找和 `Grid.IsValidCell`。方法不存在或签名不匹配时保留原查询。实现位于 [PowerOverlayCompatibility.cs](../mods/CycleTrim/Patches/PowerOverlayCompatibility.cs)。

新增测试分别给 11 个依赖安装 prefix 和 postfix，共 22 组。补丁每执行一次就改变线路，测试同时比较每次输出、原查询次数和补丁调用次数；另有未打补丁的物理、虚拟线路控制，确认正常情况下仍实际复用。测试先预热，再确认程序集版本没有变化，避免偶发程序集加载掩盖漏查。

负向验证只把兼容检查换回 `7aa5003`，保留新测试，执行在 `Generator.get_CircuitID prefix preserves every changed answer` 处失败；修复后完整通过。这个结果证明新测试能抓住原缺陷，而不只是核对一个配置标志。[失败记录](cycletrim-moono-continuation-validation/negative-power.log)、[修复后记录](cycletrim-moono-continuation-validation/candidate-host.log)

调用链的来源固定到 [CircuitManager.cs](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/CircuitManager.cs)、[Generator.cs](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/Generator.cs) 和 [UtilityNetworkManager.cs](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/UtilityNetworkManager.cs)。其方法体资料声明构建 744825；本轮静态核对的参考程序集声明 737790。参考编译验证的是接口，两者各自的证据范围保持分开。

## 2. 动画：已经要更新，就停止比较旧值

字段比较只为回答一个问题：这次调用是否需要增加版本、通知消费者复制数据？发现第一处变化后，答案已经确定。现在 `state == 3` 时，后续字段直接执行原赋值。调用状态仍只有「未访问」「全相同」「已变化」三种，没有引入新的范围阈值或跨调用缓存。[生产 helper](../mods/CycleTrim/Core/AnimationSymbolValueComparer.cs)

新增真实 Harmony 测试让第一个 atlas 字段发生变化，随后写入包含正负零、NaN 负载及其他浮点模式的所有标量和向量。正常结束、末尾越界、部分写入、版本值和同值重试后的消费者字节均与原行为对照。原有动画场景全部保留，额外测量 4/256 符号从第一个字段开始变化，以及 256 符号直到最后一个字段才变化的情况。

完全相同的范围仍须逐字段比较；直到最后才变化也几乎不能提前结束比较。因此这次改动减少了有条件的无效工作，大范围动画开销仍需后续处理，不能据此默认开启整组优化。

## 3. 轨道：不可能建表时省去顶层作用域

原规则要求已经扫描至少两倍路径长度，而且预计还有至少四次查询。每次查询最多扫描一条完整路径，因此至少需要六个候选轨道才可能建表。现在顶层候选不足六项时直接使用原查找；原有 32..4096 长度限制和建表规则保持原值。[补丁](../mods/CycleTrim/Patches/ConveyorPathLookupPatch.cs)

嵌套短调用仍建立禁用索引的隔离作用域，进入和退出都会使父索引失效。新增测试先建立父索引，再让短子调用修改父路径的一个元素并抛异常，验证返回父调用后能读到新位置。原有 2,059 组差分继续覆盖路径顺序、删除、方向回调、空值和别名。

## 4. 执行结果

[测量计划](cycletrim-moono-continuation-validation/measurement-plan.md) 写于本轮候选测量前。生产源文件、测试及证据哈希见 [receipt.json](cycletrim-moono-continuation-validation/receipt.json)。

| 验证 | 结果 |
| --- | --- |
| 电力视图 | 233 条断言通过，新增 124 条 |
| 存档元数据 | 375 条断言通过 |
| 轨道 | 2,305 条断言通过，保留 2,059 组差分 |
| 动画 | 1,167 条断言通过，新增 154 条 |
| 优化宿主合计 | 4,080 条断言通过 |
| 设置及既有探针 | 通过，含 70 条设置断言 |
| 旧电力检查负向验证 | 按预期失败，具体错误见上述记录 |
| 完整托管 net48 参考构建 | 0 警告、0 错误；`IncludeOniSim=false` |
| 完整 PR 范围空白检查 | 通过 |

### 4.1 当前候选的配对性能

下表速度比均为同一轮「未优化合成夹具耗时 ÷ 当前生产 helper 耗时」的七对中位数。大于 1 表示该负载下更快，小于 1 表示更慢。固定 CPU 0，运行时 .NET 10.0.12，SDK 10.0.401；共享虚拟宿主存在明显波动，全部原始样本与范围均保留。

| 场景 | 原夹具 ms 中位数 ↓ | 候选 ms 中位数 ↓ | 配对速度比 ↑ |
| --- | ---: | ---: | ---: |
| 电力：重复电路 | 24.646 | 16.002 | 1.579× |
| 电力：全部不同电路 | 23.511 | 32.697 | 0.719× |
| 轨道：长路径多次未命中 | 121.514 | 103.717 | 1.424× |
| 轨道：长路径共享轨道 | 113.808 | 107.077 | 1.129× |
| 轨道：多数早命中 | 12.660 | 22.828 | 0.606× |
| 轨道：单次查询 | 10.936 | 11.032 | 0.991× |
| 轨道：短路径 | 9.827 | 13.959 | 0.726× |
| 轨道：包含判断提前返回 | 34.528 | 43.755 | 0.754× |
| 动画含消费者：重复单符号 | 4.728 | 0.640 | 6.735× |
| 动画含消费者：变化单符号 | 7.184 | 8.002 | 0.912× |
| 动画含消费者：重复 4 符号 | 8.888 | 3.287 | 2.666× |
| 动画含消费者：重复 256 符号 | 79.338 | 217.478 | 0.380× |
| 动画含消费者：变化 4 符号 | 7.991 | 9.101 | 0.945× |
| 动画含消费者：变化 256 符号 | 65.590 | 89.207 | 0.766× |
| 动画含消费者：最后字段变化 | 68.580 | 211.454 | 0.324× |

电力查询每批从 51,200 次降至 3,200 次时，额外兼容检查后仍有合成负载收益；全部不同电路仍执行 51,200 次查询并承担缓存管理成本。动画重复场景每批省掉 30,000 次、每次 12,288 字节的托管复制；变化场景保持 30,000 次复制。动画计时区间分配量均为零，全部结果和消费者字节一致。

早发现变化的动画范围省去了后续比较，但整个 setter 及消费者仍比未优化夹具慢。完全相同的大范围和最后字段变化是当前明显的退化场景。轨道早命中与短路径的包装成本也仍在。这些数据继续支持三个选项默认关闭、分别验证后按需开启。

保留了提交基线的完整测量，以及「新扩展基准 + 旧动画 helper」消融。不同轮次没有交错测量两个生产 helper，因此不将其耗时或速度比相除宣称本轮提升。汇总脚本校验了 52 组、共 364 对计时样本，包含各轮完整正反对照；[benchmarks.json](cycletrim-moono-continuation-validation/benchmarks.json) 保存分布并指向原始日志。

### 4.2 复现

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

# 每个基准依次运行，各自使用独立进程；CPU 改成当前允许的固定编号。
taskset -c 0 dotnet tests/CycleTrim.Optimizations.Tests/bin/Release/net10.0/CycleTrim.Optimizations.Tests.dll --benchmark-power
taskset -c 0 dotnet tests/CycleTrim.Optimizations.Tests/bin/Release/net10.0/CycleTrim.Optimizations.Tests.dll --benchmark-conveyor
taskset -c 0 dotnet tests/CycleTrim.Optimizations.Tests/bin/Release/net10.0/CycleTrim.Optimizations.Tests.dll --benchmark-animation
python3 docs/cycletrim-moono-continuation-validation/summarize.py
```

负向验证和旧 helper 消融在独立临时源码副本执行：保留本轮测试，分别将 `PowerOverlayCompatibility.cs` 或 `AnimationSymbolValueComparer.cs` 替换为 `git show 7aa5003:<path>` 的内容，再构建。前者运行完整测试应出现已记录的失败；后者运行扩展动画基准。原始游戏程序集仅用于静态签名检查和参考编译。

## 5. 验收边界与结论核对

PR 保持开放，游戏运行验收继续按 [autonomous-iteration.md](autonomous-iteration.md) 执行。需要用 Steam 启动游戏，在实际提交上记录游戏构建、存档、其他模组和设置，检查保存/加载、电力视图、轨道构建删除与嵌套更新、角色外观和世界切换。对重复率、首次扫描成本、实际帧时间及内存采样，才能判断哪些选项适合开启。

本轮宿主执行覆盖上述合成行为；Unity/Mono、GPU 上传、真实存档和组合模组场景仍待实机证据。动画 dirty 依赖在安装后被单独改写、非 Harmony 修改、同一查询已完成兼容检查后才装新补丁，以及并发底层存储修改，仍属于此前记录的边界。

| 核对问题 | 依据与结论 |
| --- | --- |
| 贡献是否具体？ | 旧电力检查在新增真实 Harmony 用例下失败，修复后通过；动画和轨道各省掉一类条件明确的无效工作。 |
| 机制是否可复现？ | 三种修改分别对应生产文件、差分测试、固定计划及完整命令。 |
| 性能证据是否足够？ | 现有及新增正反场景全部保留；共享宿主数据支持特定负载判断，未建立跨轮增量加速结论。 |
| 评价是否完整？ | 宿主、参考编译和实机证据分开；游戏运行仍是合并门禁。 |
| 方法是否稳健？ | 无调参、无扩大缓存寿命；嵌套和部分失败保留原语义，大范围退化明确记录。 |

文档按用户指定的 [Research Paper Writing 技能](https://github.com/Master-cai/Research-Paper-Writing-Skills/tree/main/research-paper-writing)组织问题、方法、对照和证据核对。
