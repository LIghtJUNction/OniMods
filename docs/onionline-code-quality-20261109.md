# OniOnline 代码质量审查（2026-11-09）

本轮修复了四个可复现的状态恢复问题，分三次提交到 `main`。修复代码版本为 [`5b8555e`](https://github.com/LIghtJUNction/OniMods/commit/5b8555ec73970a5a5bacc956d6c181ac43ecfc11)，审查起点为 `54678be`。四个生产文件合计新增 28 行、删除 16 行；新增代码主要用于补齐状态转换，并将聊天缓存处理归并到一个方法。

## 已修复的问题

| 问题与复现条件 | 根因与修复 | 回归证据 |
| --- | --- | --- |
| 聊天丢失旧消息，后续消息已收到；房主随后淘汰了缺失的历史。 | `HistoryReset` 只推进序号，没有释放已经连续的聊天缓存。普通消息和历史重置现在共用 `DrainReceivedChat()`，按序展示缓存并保留去重。 | `NetworkResilienceTests.ExpiredHistoryReleasesBufferedChat` 覆盖丢包、历史淘汰、重复重放和继续收消息。 |
| 访客订阅另一名访客的模组状态，发布者已注册但尚未生成首个快照。 | 主机把临时 `Unavailable` 当成终止结果，导致同一订阅的后续重试被丢弃。现在只有 `Denied`、`SchemaMismatch` 终止转发路由。 | `ModSynchronizationTests.SessionAvailability` 通过三个实际会话验证等待首个快照、自动恢复及后续增量。 |
| 研究完成后，已有副本拒收新的研究状态。 | 完成研究会清空活动科技，却保持“选择研究”的修订号；导入端错误拒绝了这个合法转换。现在允许同修订号下“活动科技变为空且原科技已解锁”，继续检查操作者、点数和解锁集合。 | `FrameworkPrimitiveTests` 覆盖合法完成、未完成时禁止清空，以及旧快照禁止回退。 |
| Steam 房主发生变化，客户端迟迟没有收到明确终止原因。 | `Poll()` 先排入 `host_changed`，随后 `Dispose()` 清空了事件队列。现在先完成清理，再保留终止事件。 | `SteamRoomTransportTests` 执行生产传输层和 `RoomSession`，检查清除旧事件、关闭两个通道、错误只报告一次以及立即终止会话。 |

四项新增回归均先在原实现上复现失败，再在修复后通过。Steam 测试使用显式的 Steam API 测试替身，传输层与会话状态机均为生产代码。

## 验证结果

验证使用 .NET SDK `10.0.401`、Release 配置。宿主回归启用 `TreatWarningsAsErrors=true`；完整套件中的专项目标不重复计入总数。

| 检查 | 结果 |
| --- | --- |
| `OniOnline.Tests` 完整宿主套件 | **4,749 个断言通过**，包含真实 TCP、三个独立客户端进程、弱网、世界同步与重连。 |
| `--resilience` | **89 个断言通过**；包含新增聊天历史恢复回归。 |
| `--online-options` | **60 个断言通过**；包含聊天历史容量与重放边界。 |
| `--mod-sync-contracts` | **91 个断言通过**。 |
| `--framework-primitives` | **96 个断言通过**。 |
| `--steam-directory` | **24 个断言通过**；包含生产 Steam 传输层回归。 |
| `OniOnline.Journal.Tests` | **43 个断言通过**。 |
| `OniOnline.TcpProxy.Tests` | **5 类真实 TCP 故障场景通过**：双向半关闭、强制断开、双向连接重置。 |
| `OriginAdapterTests=true` | **92 个原生复制人来源适配器断言通过**。 |
| `tests/OniOnline.Tests/test_*.py` | **47 个 Python 测试通过**。 |
| `onim -m OniOnline build --release` | **成功**；完整 `net48` 模组及引导器编译、打包成功，0 errors；Jlib 保留既有的 28 条警告。 |
| ZIP 与程序集元数据 | ZIP 校验通过；仅包含引导器、实现和框架三个 DLL；实现引用独立 Jlib，无内嵌 PLib、旧 PLib 引用或测试探针。 |
| `git diff --check` | 本轮完整改动范围通过。 |

宿主测试的准备与运行命令：

```sh
git submodule update --init mods/Jlib
# 按构建指南配置 Directory.Build.props 中的本机游戏路径。
dotnet pack mods/Jlib/Jlib/Jlib.csproj -c Release -p:TargetFrameworks=netstandard2.1
dotnet run --project tests/OniOnline.Tests -c Release \
  -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true
dotnet run --project tests/OniOnline.Journal.Tests -c Release \
  -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true
dotnet run --project tests/OniOnline.TcpProxy.Tests -c Release \
  -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true
python3 -m unittest discover -s tests/OniOnline.Tests -p 'test_*.py' -v
onim -m OniOnline build --release
```

本次构建采用仓库固定的 **737790 编译参考镜像**，并从同一固定提交补齐 Steamworks 参考程序集。它与项目声明的原生游戏构建 744825 分别记录；没有执行这些第三方参考 DLL，也没有运行两个真实 Steam 游戏客户端。双机 Steam 网络、Unity 生命周期和游戏内画面仍需真实运行环境验收。

## 审查范围与维护性

审查重点包括网络协议与队列、握手和重连、聊天恢复、模组状态转发、研究状态导入、Steam 清理，以及 `WorldWire`、`SnapshotAssembler`、`SnapshotTransferSender`、`WorldReplication` 的边界、所有权、共享权限、超时和预算。快照部分没有发现需要修改的已确认问题；实际来源 `NativeWorldSource` 会在共享状态改变时轮换 epoch，原有防回滚检查得以保留。

修复保留了协议格式和已有权限检查。聊天缓存使用共同的处理方法；临时不可用与终止失败的处理与订阅端行为一致；研究状态在完整校验后一次性写入；Steam 清理仍保留异常传播语义。另一个审查者复核了 SDK 与 Steam 修复的调用方、失败路径及测试，未发现阻断问题。

同步修正了[构建、安装与测试指南](../mods/OniOnline/docs/build-install-test-zh.md)：独立 Jlib 前置、子模块初始化、本地测试包生成、引导器与实现的目录布局，以及 Jlib 和 OniOnline 的安装顺序。

## 提交与 CI

| 提交 | 内容 |
| --- | --- |
| [`24818a4`](https://github.com/LIghtJUNction/OniMods/commit/24818a41d150c926c9079d19defde2cce0acfa90) | 聊天历史过期后的缓存恢复。 |
| [`f057381`](https://github.com/LIghtJUNction/OniMods/commit/f057381e9824a682b4aa2d161236e47d12180c77) | 模组状态转发恢复、研究完成同步。 |
| [`5b8555e`](https://github.com/LIghtJUNction/OniMods/commit/5b8555ec73970a5a5bacc956d6c181ac43ecfc11) | Steam 房主变化的终止通知及生产传输层测试。 |

代码提交 `5b8555e` 的远端检查均已完成并通过：[Mod quality](https://github.com/LIghtJUNction/OniMods/actions/runs/42503703674)、[ONI reference compatibility](https://github.com/LIghtJUNction/OniMods/actions/runs/42503703688)。结果于 2026-11-09 01:19（UTC+8）核对；后续构建指南与本报告的提交另行触发文档版本的 CI。

## 结果复核

| 维度 | 结论 |
| --- | --- |
| 实际贡献 | 四个正常支持路径中的恢复错误均有复现与修复。 |
| 表达清晰度 | 每项问题均对应触发条件、根因、实现和测试。 |
| 验证强度 | 新增回归在旧实现上失败；修复后定向测试和完整宿主套件通过。 |
| 覆盖范围 | 区分宿主、参考编译和真实游戏验收；没有宣称双机游戏运行通过。 |
| 设计合理性 | 沿用现有协议、权限与状态模型，避免引入新的兼容层或配置开关。 |
