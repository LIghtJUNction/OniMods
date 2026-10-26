# Jlib 的 Steam 首次发布

`scripts/publish_jlib_steam.sh` 使用本仓库的单文件 Legacy ZIP 发布器。
上传器 App 为 `636750`，游戏 App 为 `457140`，首次目标 ID 为 `0`，
包内必须包含 `Jlib.dll`、`mod.yaml`、`mod_info.yaml`，且 staticID 为
`LIghtJUNction.Jlib`。Jlib 不带 Spaced Out! 标签。

不能使用 SteamCMD、`workshop_build_item` 或 `SteamUGC.SetItemContent`。
创建内容仍通过 `SteamRemoteStorage.PublishWorkshopFile`；公开阶段只修改元数据。

## 包与离线计划

先在 `onim.toml` 登记 Jlib 源码路径，并完成 Jlib 自身与消费者的相关测试。
通用发布器测试不验证 Jlib API 或实际游戏加载。

```sh
rtk proxy dotnet build tools/OniMods.SteamPublisher -c Release --disable-build-servers
rtk proxy dotnet run --project tests/OniMods.SteamPublisher.Tests -c Release
rtk proxy scripts/publish_jlib_steam.sh --prepare-first-item \
  --plan "$PWD/dist/Jlib.first-publication.json"
```

准备步骤不初始化 Steam API。它构建 Release 包，检查双语说明和预览图，
生成不能覆盖的计划文件，并输出 `planSha256`。计划绑定目标、ZIP 字节、
预览图和元数据。首次创建日志独立保存在 onim 状态目录的
`workshop-create/jlib-first-publication.jsonl`，不会与 OniOnline 共用日志。

核对计划内 DLL、版本、许可文件、文案、预览图和哈希后，才进入发布阶段。
计划或包改变时重新准备；已有计划文件不能覆盖。

## 创建、验证、公开

保持 Steam 客户端已登录。以下两条命令会写入 Steam；不要作为离线测试执行。

```sh
rtk proxy scripts/publish_jlib_steam.sh --create-first-item \
  --plan "$PWD/dist/Jlib.first-publication.json" \
  --expected-plan-sha256 REVIEWED_SHA256
rtk proxy scripts/publish_jlib_steam.sh --publish-first-item \
  --plan "$PWD/dist/Jlib.first-publication.json" \
  --expected-plan-sha256 REVIEWED_SHA256
```

创建前先查询当前账号的已有条目，拒绝重复 Jlib 条目；创建请求先落地一次性日志。
私有条目创建后，由独立 Consumer 进程下载并核对 Legacy 状态、文件路径及 ZIP 字节。
公开阶段再次验证私有条目，写入双语页面和 Public 可见性，再执行 Consumer 下载和
公开 Web API 回读。回调超时或结果不确定时检查日志和已有条目，不自动再次创建。

这两个首次阶段期间，`onim.toml` 的 `publishedfileid` 保持未设置或 `0`。
全部公开验证成功后记录最终 Workshop ID，并为后续更新固定该 ID；当前包装脚本
专门限制为首次发布，不能拿它自动更新任意配置 ID。

## 消费者的 Workshop 依赖

Jlib 有经过验证的最终 ID 后，依赖方向为：

| 父条目 | 子条目 |
| --- | --- |
| OniMcp `3807166922` | 已验证的 Jlib ID |
| CycleTrim `3807166689` | 已验证的 Jlib ID |
| OniOnline 的实际发布 ID | 已验证的 Jlib ID |

依赖命令使用独立的 Consumer `457140` 上下文，只调用查询和
`SteamUGC.AddDependency(parent, child)`，不会经过内容上传分支。
子项 ID 从 Jlib 首发计划及已完成 Public 验证的日志读取，不接受任意子项 ID。

```sh
rtk proxy scripts/publish_jlib_steam.sh --add-jlib-dependency \
  --parent CycleTrim --jlib-plan "$PWD/dist/Jlib.first-publication.json" \
  --expected-plan-sha256 REVIEWED_SHA256
rtk proxy scripts/publish_jlib_steam.sh --verify-jlib-dependency \
  --parent CycleTrim --jlib-plan "$PWD/dist/Jlib.first-publication.json" \
  --expected-plan-sha256 REVIEWED_SHA256
```

OniMcp 使用 `--parent OniMcp`。OniOnline 发布完成后使用
`--parent OniOnline --parent-workshop-id ACTUAL_ONIONLINE_ID`。
父项也须通过当前所有者、App、标题、Public、非封禁及 Community 类型检查。
Steam 上仍为集合项、缺少 Legacy 文件句柄或文件为空时不写入。
Jlib 已依赖该父项时拒绝形成直接循环。

命令输出逐阶段 JSON。已存在关系时只读返回；缺失时先落地固定父子关系的
一次性写入日志，再发送一次 API 请求。已有写入意图且关系仍未回读成功时，
再次运行 add 会拒绝写入；应使用 verify 检查，并保留日志调查结果。
其他依赖不会被删除。

等待 `AddUGCDependencyResult_t` 回调，要求 `EResultOK` 且父、子 ID 都匹配。
随后创建禁用缓存的新 UGC 查询，启用 `SetReturnChildren`，通过
`GetQueryUGCChildren` 确认新增关系、原有关系未减少，并复查父子项当前身份、
公开状态、Legacy 文件句柄与文件大小。回调失败、超时或回读不符时，尽可能
额外采集一次只读结果，返回非零状态并保留写入日志，不自动重复 API 请求。
Consumer 下载的实际 ZIP 哈希另由首次发布验证，不把文件句柄/大小当成哈希验证。

Steam 将普通条目的依赖定义为 soft dependency：工坊关系不证明自动订阅、启用，
也不证明 ONI 的程序集加载顺序。仍须验证 Jlib 缺失时消费者的提示与停用行为，
以及启用后实际加载的共享 Jlib 路径、版本和哈希。
依据：[Steam AddDependency](https://partner.steamgames.com/doc/api/ISteamUGC#AddDependency)、
[查询依赖子项](https://partner.steamgames.com/doc/api/ISteamUGC#GetQueryUGCChildren)。
