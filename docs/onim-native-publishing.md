# onim 原生发布开发进度（2026-11-02）

当前源码候选为 `onim 0.2.0`。用户已要求先提交推送，后续开发与实际发布留待下次。
本轮没有创建 Jlib 工坊条目，也没有更新已有条目的内容或依赖关系；安装中的 onim 未升级。

## 已实现

- `onim publish` 默认走 Rust 原生 Legacy ZIP 上传，`--gui` 保留为显式入口。
- 首次创建需 `--create`；先创建 Private 条目，核对下载字节后再按 `--visibility` 设置可见性。
- `--preflight` 只读检查账号、上传器与游戏上下文；`--dry-run` 准备不可变发布计划。
- `--resume <plan.json>` 从持久日志回读。上传意图、首次创建保护和文件锁防止不确定请求被重复发送。
- `workshop_owner` 固定账号；`workshop_dependencies = ["Jlib"]` 已写入三个消费 Mod 的配置。
- 包含中英文元数据、游戏上下文 UGC 下载与 SHA256 核对，以及保留已有前置关系的添加流程。
- 已有消费条目在更新内容前关联前置；新条目在公开前关联前置。这部分流程尚未进行真实写入验收。

## 本轮证据

已见：官方 Linux `OniUploader64` 的 SHA256 为
`0e8497f9903877ed2dbadac046dc0af271dbdca473fbed9fac254c6ea405c2de`。
`PublishMod` 字符串位于文件偏移 `0x5a4be8`，发布状态机入口为 `0x1d7510`。
`0x1d77c2` 调用 RemoteStorage vtable `+0x38`（FileShare），
`0x1d7e1f` 调用 `+0xf8`（CreatePublishedFileUpdateRequest）；
标题、说明、标签和可见性更新也出现在该状态机中。
官方 `libsteam_api.so` 的对应 flat 导出把 PublishWorkshopFile 和
CommitPublishedFileUpdate 分别分派到 `+0xf0`、`+0x130`。
这支持复用 Legacy 文件发布协议；尚未通过本轮真实上传观察完整执行链。

原生封装使用游戏自带的版本化 Steam API，当前支持 RemoteStorage v016、
UGC v016、User v021、Utils v010。回调布局依据
[Valve 的 SDK 1.53a 头文件](https://github.com/ValveSoftware/Proton/tree/proton_10.0/lsteamclient/steamworks_sdk_153a)，
并以 C++ 编译器核对 Linux 的尺寸及关键偏移。

Rust 单元与 CLI 回归、严格 clippy、真实账号的 Creator `636750` 与 Consumer `457140`
只读查询已通过。四个消费工程已构建，包检查确认共享 Jlib、未内嵌 PLib 或游戏 DLL。
这些检查不代表 Unity/Mono 实机验收。

## 下次继续

1. 审查原生发布的写入边界与恢复路径，补充所需回归；补齐公开 Web API 回读。
2. 用原生 `onim publish` 验收 Jlib 首次上传、双语元数据、下载 SHA256、公开状态。
3. 登记实际 Jlib Workshop ID，更新 README/工坊说明链接，再更新 OniMcp、CycleTrim。
4. 核对自动前置关系的实际回调与读回；OniOnline 仍是未上架源码候选。
5. 更新发布文档与 AGENTS 中的正式入口。保留禁止 SteamCMD/SetItemContent 目录上传的规则。
6. 静态检查表明独立 `onim init` 项目还需携带 Jlib 引导层与构建支持；尚未完成独立工程验收。

Jlib 仓库的代码范围清理、README 更新与历史重写强推已完成。普通完整克隆已从约
218.61 MiB 压缩至 1.14 MiB；GitHub 关闭 PR 的隐藏引用仍可能保留旧对象。
