# OniMods 结构整理与验证报告

日期：2026-11-06。对应 [PR #564](https://github.com/LIghtJUNction/OniMods/pull/564)。

## 结论与范围

本轮按 [untangle-codebase](https://github.com/caldoai/skills/blob/main/skills/untangle-codebase/SKILL.md)
完成一次全仓库结构审查：先确认依赖和行为，再分批修改，最后交叉审查。
Comments、Tests 均纳入范围。审查覆盖 Rust CLI、OniMcp、OniOnline、
CycleTrim/OniSim、测试入口、构建脚本及 CI；保留承担真实游戏和协议约束的边界。

最直接的修复是 Core 测试的失败传播：原入口丢弃了测试返回值，导致断言失败后
进程仍可能退出为成功。其他修改把项目路径、构建产物、可见工具、探针收据和
共享协议字段各自交给一个明确的所有者，减少修改一处时必须同步理解的隐含关系。

审查基线为 `90c31fa9826161caa5382edf4e870f6c07adb317`；本报告的本地集成验证
对应代码提交 `ebc9ff661245bd84a29aa6b837b2ea607c807768`。
随后的报告提交只增加文档和验证记录。代码批次累计涉及 92 个文件，新增 1,138 行、
删除 3,800 行；该数字不含本报告及其 JSON 记录，也不作为质量改善的独立证据。

## 决策与实现

### 1. 项目路径和构建产物随调用传递

原来的配置加载器通过进程级 `ONI_CLI_REPO_ROOT` 传递仓库位置。加载第二份配置，
会改变其他命令隐式读取到的上下文。现在绝对根目录由 `Config` 持有，且不写回
用户配置文件；配置加载和 doctor 共用定位规则。

构建返回 `BuiltMod`，明确携带程序集名称、输出目录和归档路径。dev、install、
publish 使用这一结果，避免再次猜测已经生成的文件位置。setup、config 和 doctor
共用平台 Managed 路径规则，纠正原先不一致的 macOS 路径。
相关实现见 [config.rs](../src/config.rs)、[build.rs](../src/build.rs) 和
[CLI 集成测试](../tests/build_cli.rs)。

验证包含两份配置互不干扰、配置加载不修改全局环境、从其他工作目录使用相对或
绝对配置路径，以及现有安装和 setup 行为。Linux 本地运行通过；平台 CI 结果单独
记录，不把 Linux 上的路径断言描述成 macOS 游戏运行验收。

### 2. 测试入口明确注册，并向进程传播失败

Core、Tools 和 Server 原先通过反射寻找私有 `Main`，或用多个包装项目选择入口。
现在每个测试组提供 `Run`，由所属宿主显式调用，保留已有断言与执行次序。
Core 的单一入口返回真实测试结果。

对基线和候选分别在隔离副本中加入相同的故意失败断言，得到如下结果。该变异没有
进入最终源码；它验证的是失败是否能到达 CI，而不仅是成功路径能否运行。

| 输入 | 基线 | 候选 |
| --- | --- | --- |
| 一个测试组包含 `Require(false, ...)` | 报告一个失败组，进程退出码 **0** | 报告同一个失败组，进程退出码 **1** |

OniOnline 的同机网络测试改为绑定回环地址。所有客户端本就运行在同一主机，
不需要枚举外部网卡。真实 TCP、UDP、三个独立客户端进程、限速、断连和恢复测试
继续执行；这也消除了受限环境禁止网卡枚举时的无关失败。

### 3. 同一协议字段和可见工具筛选由一处负责

[CommandWireText](../mods/OniOnline/Multiplayer/CommandWireText.cs) 统一 DupeCommand
和 RemoteWork 中相同的文本编码合同：严格 UTF-8、小端 ushort 字节长度前缀、
最多 128 个 UTF-16 代码单元及 384 字节。调用方继续提供原有 `dupe_text` 和
`work_text` 错误标记。其他网络字段的长度、空值或控制字符政策不同，继续独立实现。

测试使用固定字节报文和畸形输入，覆盖字节兼容性与边界拒绝行为。
OniMcp 的可见工具筛选统一由缓存层负责，移除调用方重复构造的子集和无调用者的
测试专用快照入口。保留可变 Group/Name/Hidden 的效果、返回列表隔离，以及先读取
可见列表时初始化全量排序缓存的既有时机。

### 4. URI 只解析一次，保留既有路由区别

资源入口将已解析的 URI 传入后续路由，去掉重复解析。特征测试同时运行在基线和
候选上，确认查询选择器保护、非法或外部 URI 拒绝，以及已停用 mechanics 路由。

审查发现，两个资源在带端口或用户信息的 authority 变体上存在历史回退行为。
因此本轮保留这些分支；直接合并目录和回退表会改变可观察行为。

| 资源 | 规范 URI 的 action | 已有 authority 变体的回退 action |
| --- | --- | --- |
| `oni://buildings/configurables` | `list` | `state_list` |
| `oni://controls/state` | `state_list` | `list` |

这些断言位于 [Core 回归宿主](../tests/OniMcp.Core.Tests/Program.cs)。

### 5. 探针构建和收据共享同一个实现

内存和纹理探针原先各自复制编译参数、旧输出清理、源码摘要和收据校验逻辑。
现在由不可变的 [ProbeBuild](../mods/CycleTrim/OniSim/conformance/probe_build.py)
描述各自输入，共享构建实现，保留原有 CLI 路径和选项。

共享构建器自身也进入源码摘要。修改它会使旧收据无法证明当前源码，必须重新产生
适用的收据；历史收据仍作为历史证据保存。测试覆盖两个探针的成功、失败、缺失或
陈旧输出，以及构建前后源码变化。测试编译器是替身，不代表真实游戏探针已编译或运行。

四个 conformance 脚本直接启动所需的 Python 或 dotnet 子进程，去掉没有声明的
`rtk proxy` 运行依赖；调用参数、超时和返回码检查保留。`OfficialProfile.MonoPath` 的单纯转发
也被移除，平台路径继续由 `NativePlatform` 负责。

### 6. Harmony 只合并初始化检查

通用快照和 Conveyor 快照重复检查 Harmony 版本、发布锁、注册表布局和提供者程序集。
现在这些初始化阶段的规则集中于
[HarmonyPatchProfile](../mods/CycleTrim/Core/HarmonyPatchProfile.cs)。

两个快照仍各自持有状态、订阅生命周期和缓存策略。Conveyor 的构造历史保护及
较小注册表优化保留；频繁执行的 `Check()`、`Matches()` 函数体与基线一致。
独立审查核对了事件订阅先于程序集枚举、提供者状态的保持、动态程序集身份和未知
提供者拒绝。已有差分、竞态及第二提供者测试继续运行。

### 7. 测试覆盖由可执行行为证明

四个 Modern/ResourceRead 项目编译相同的退休协议测试与服务器代码，另有 13 个
历史测试源码早已被项目配置排除。现在四组仍然有效的拒绝、资源与会话断言统一在
Server.Tests 执行，每组创建独立服务器；删除重复包装项目、未编译源码和只要求这些
文件包含特定文字的旧映射检查。历史要求保留为
[不可变提交链接](mcp-protocol-retirement.md)。

公共文档示例改为服务器实际支持的 `2025-11-25` initialize/session 协议。
文档检查对照生产协议常量验证当前示例，同时允许历史说明保留退休版本。

[check_mods.py](../scripts/check_mods.py) 独立发现 Python 脚本、目录测试和 C#
宿主。新增发现器测试验证真实子进程失败、缺少 SDK、参数路由及排除项，避免测试
发现器递归运行自身。Linux conformance 明确要求 `cc` 和固定版本 OniHarness；
两个 CI 工作流初始化子模块并追踪 gitlink 变化。

注释只随废弃实现或重复说明删除。解释 FFI 所有权、游戏回退、协议区别和发布状态
转换的注释保留；没有因文件相似而合并不同的发布、恢复或游戏生命周期。

## 验证结果

本地环境为 Linux、Python 3.12.14、.NET SDK 10.0.401、Rust 1.99.1。
OniHarness 固定于 `7261e65299c72c86d370caa921d5c1407876e1f7`，指针未修改。
下表的不同层级不相加成一个“总测试数”，子套件已包含在集成检查中。

| 检查 | 结果 | 能证明的范围 |
| --- | --- | --- |
| `check_mods.py --skip-synthetic-performance` | **73/73**，退出码 0 | 55 个独立 Python 脚本、5 个 Python 目录套件、12 个 C# 项目及 1 个 adapter 变体 |
| Rust CLI `cargo +1.99.1 test --locked` | **50/50** | 30 单元、15 CLI 集成、5 setup；基线为 48 |
| OniSim native Cargo | **55 通过、2 显式忽略** | native 行为；两项忽略的是独立微基准 |
| OniMcp Core | **19 组通过** | 入口、可见性缓存、URI 等行为，包含于 73 项 |
| OniOnline 主宿主 | **4,615 条断言通过** | 生产协议及同机真实网络，包含于 73 项 |
| native-origin adapter | **92 条断言通过** | 生产适配逻辑与显式替身，包含于 73 项 |
| 五个 Python 目录套件 | **142 个测试通过** | conformance 65、memory 12、multiplayer 47、native bundle 6、tooling 12，包含于 73 项 |
| 三个 Mod Release 参考编译 | **CycleTrim、OniMcp、OniOnline 均成功** | 对固定参考程序集编译及打包；不证明游戏内运行 |
| 合成性能检查 | **PASS** | 原有统计记录开销预算，详见下文 |
| 完整差异空白检查 | **通过** | 基线至候选的 `git diff --check` |

固定报文测试另行确认 DupeCommand 72、RemoteWork 343、Journal 43 条断言通过。
Harmony 回归保留 Conveyor 2,377 条断言及 2,059 次差分比较，其他优化宿主也通过。
独立交叉审查覆盖 CLI/探针、运行时/Harmony 和测试/CI 三组改动；已处理 macOS
测试夹具路径与可见列表首次读取时机的问题，复查未留下阻塞发现。

### 可复现命令

在仓库根目录执行。参考编译需先按仓库清单准备参考文件，并将
`ONIMODS_REFERENCE_DIR` 设置为对应绝对路径；额外的 OniOnline 编译输入见下一节。

```sh
git submodule update --init tools/OniHarness
cargo +1.99.1 test --locked
python3 scripts/check_mods.py --skip-synthetic-performance
cargo +1.99.1 test --locked --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml
python3 scripts/run_cycletrim_synthetic_performance.py
dotnet build mods/CycleTrim/CycleTrim.csproj -c Release -p:OniManagedPath="$ONIMODS_REFERENCE_DIR" -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true
dotnet build mods/OniMcp/OniMcp.csproj -c Release -p:OniManagedPath="$ONIMODS_REFERENCE_DIR" -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true
dotnet build mods/OniOnline/OniOnline.csproj -c Release -p:OniManagedPath="$ONIMODS_REFERENCE_DIR" -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true
git diff --check 90c31fa9826161caa5382edf4e870f6c07adb317 HEAD
```

CycleTrim 构建的 native 子进程也需使用 Rust 1.99.1。`.NET` 构建的 MSBuild 汇总为
零警告、零错误；native Rust 编译仍有原有警告，不能将此表述为整个工具链零警告。

### 参考编译和性能的证据边界

[参考清单](../ci/oni-reference-assemblies.json) 固定镜像提交为
`Sgt-Imalas/Sgt_Imalas-Oni-Mods@be4a70d8f335fdd8ac7f3b1ccf91b50bb6ddb46d`。
该镜像声明 ONI **737790**，晚于此版本的 **744825** 方法体来源是另一组证据。
本次没有修改清单；参考编译通过不能称为 744825 二进制兼容验收。

OniOnline 额外需要同一镜像中的 `com.rlabrecque.steamworks.net.dll`，以及清单已固定的
UnityEngine.Modules 2021.3.33 包中的 `UnityEngine.AudioModule.dll`。
前者核对 Git blob 身份与 SHA-256，后者核对包和 DLL 的 SHA-256；摘要记录在
[验证 JSON](untangle-codebase-validation.json)。这些文件仅用于编译，没有运行或随报告分发。

合成检查得到串行统计记录开销比 min/median/max **1.183/1.988/2.505x**，
四写者为 **0.899/1.308/2.753x**；一百万次串行记录的托管分配为 **0 B**。
这是已有插桩开销检查，原阈值不变；这些比值不表示本次重构带来的加速，也不证明游戏 FPS。

三个运行包均未携带测试源码。CycleTrim 包仍按既有规则包含 conformance README
与 94 份历史 JSON 收据；存在该目录不等于夹带探针或测试源码。

### 明确未执行的项目

73 项集成检查明确列出当前环境未运行的真实游戏目标检查、Release 二进制反编译检查、
在线 NavGrid 方法体检查、Debug 重启打包检查和完整 Steam publisher 打包预检。
合成性能在集成检查中跳过，已单独执行。loader 测试没有执行真实 native bundle
加载冒烟检查；conformance 中编译和加载的 C 时钟夹具是自行编写的测试输入。

未进行 ONI/Unity 游戏内运行、多设备游戏联机、真实探针运行或 Steam 发布。
已有 gameplay PR 的运行验收要求继续适用。源码合同、宿主替身、参考编译和实际游戏
运行分别记录，避免把较低层级的成功扩展成未经执行的结论。

## 提交与追踪

每批通过针对性验证后即提交并推送，保留以下代码提交顺序：

| 提交 | 内容 |
| --- | --- |
| `907959d` | 显式回归入口与失败传播 |
| `7a1dba6` | CLI 项目根目录与构建产物 |
| `b46882e` | 探针构建收据与直接子进程依赖 |
| `b57fc5b` | 工具可见性与命令文本合同 |
| `dfaa6ff` | Harmony 初始化规则 |
| `8fca169` | 退休协议测试合并、Python 发现与文档 |
| `ebc9ff6` | URI 单次解析与缓存初始化时机 |

报告与机器可读验证记录单独提交。合并状态、最终提交身份及其全部 CI 结果见
[PR #564](https://github.com/LIghtJUNction/OniMods/pull/564)；只有相应提交的检查
完成后才用于合并判断。新增功能或扩大游戏运行证据仍需独立验证。
