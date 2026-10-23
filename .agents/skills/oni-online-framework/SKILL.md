---
name: oni-online-framework
description: "Develop, review, test, and package the OniOnline multiplayer framework, SDK, native adapter, mod synchronization, and built-in one-planet-per-player mode. Use for OniOnline code or delivery work; not for ordinary ONI gameplay advice or unrelated CycleTrim changes."
---

# OniOnline 联机框架

用于维护 `/home/lightjunction/Documents/GITHUB/OniMods` 中的 OniOnline 联机框架。它覆盖独立 SDK、ONI 原生适配、入场模组对齐、运行中注册状态同步、内置“一名玩家控制一颗星球”玩法，以及原生风格的浏览、开局和通讯界面。

## 先确认边界

- 先读仓库根目录的 `AGENTS.md`、`RTK.md`、`UPSTREAM.toml` 和 `docs/autonomous-iteration.md`。所有 shell 命令遵守仓库的 `rtk` 约定。
- 先看 `git status --short`、当前 ONI 进程和目标 Mod 配置。保留 CycleTrim、`.local-tests` 和其他未完成改动；不要 reset、clean、stash、覆盖或切换分支来制造“干净”工作树。
- 不把宿主测试、Release 构建或 ZIP 内容当成真实游戏验收。Unity/Harmony、生效的安装 DLL、图形 UI、输入法、存读档、Steam P2P 和双客户端必须分别报告。
- ONI 只能通过 Steam 启动。禁止 SteamCMD `workshop_build_item`、`SteamUGC.SetItemContent` 或目录模式 Workshop 发布；发布只能走仓库规定的单文件 Legacy ZIP 流程。
- 没有明确的实机授权、容量通过和一次性测试存档时，不启动或重启游戏，不替换用户正在玩的存档和 DLL。
- 如果当前请求只要求代码、技能或局部验证，先做只读检查并明确不启动游戏：
  `rtk proxy git status --short`、`rtk proxy pgrep -af 'OxygenNotIncluded|OxygenNotIncluded.exe' || true`。不要因为发现残留进程就自行结束它。

## 设计不变量

1. `sdk/OniOnline.Framework` 必须保持 .NET Standard 2.0，并且不能引用 Unity、ONI、DLC、PLib 或游戏 DLL。第三方玩法只引用打包后的公开 SDK，不复制框架 DLL。
2. 基础会话按协议、玩法、能力和模组合同协商。游戏构建号、DLC、CycleTrim 版本是适配信息，不是基础联机的简单相等门槛；未知原生构建必须 fail closed，只保留安全的框架能力。
3. 入场前模组同步必须同时核对模组 ID、来源、版本、受控内容摘要和实际加载状态。`Required`、`ClientOnly`、`Optional` 的用途不能混用；下载完成不等于已安装、已启用或已加载。
4. 运行中状态只能通过 SDK 显式注册的频道同步。每个频道要有权威端、schema、epoch/generation、快照/增量、权限、预算、缺口恢复和未知 schema 隔离；旧准入代次不能继续写入新会话。
5. 内置玩法保持一名玩家一颗星球。房间 ID、观察世界 ID 和跨存档永久 PlanetId 是不同身份；重连和重新绑定不能复制星球，也不能把观察权限当成迁移权限。
6. 主菜单的“浏览星系”只负责浏览和加入；“新游戏 → 生存模式 → 联机”负责创建。殖民地 HUD 的原生管理栏直接打开“通讯”，快捷键默认 `Ctrl+T`；主菜单、创建、载入和初始复制人选择期间不显示联机 HUD。
7. UI 复用 ONI 原生控件、字体、外框和声音。不要加入网页式灰框、巨大悬浮按钮、霓虹边框或把实现细节塞进玩家文案。

详细合同按需读取：

- [框架合同与源码地图](references/contracts.md)
- [局部测试、打包和实机证据矩阵](references/verification.md)
- [OniOnline 当前 README](../../../mods/OniOnline/README.md)
- [交付计划](../../../mods/OniOnline/docs/DELIVERY-PLAN.md)
- [SDK 文档](../../../mods/OniOnline/docs/SDK.md)
- [模组内容同步](../../../mods/OniOnline/docs/MOD-CONTENT-SYNC.md)
- [版本与能力协商](../../../mods/OniOnline/docs/FRAMEWORK-COMPATIBILITY.md)

## 推荐工作流

### 1. 先定位影响面

把改动归类为 Framework/SDK、网络会话、模组同步、Gameplay、原生适配、UI 或打包。优先找已有生产类型和已有测试，不在 UI 或 Harmony 补丁里复制一套协议逻辑。涉及共享状态时先写清楚权威端、准入代次、schema 和失败后保留的数据。

### 2. 先做局部验证

纯协议、状态、序列化和准入改动先跑对应专项；UI、Harmony、存读档和原生世界改动只能把专项结果当作前置检查，不能称作实机通过。常用入口：

```sh
rtk proxy dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore -- --sdk
rtk proxy dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore -- --compatibility
rtk proxy dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore -- --mod-sync-declarations
rtk proxy dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore -- --mod-sync-contracts
rtk proxy dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore -- --mod-content-sync
rtk proxy dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore -- --mod-content-digest
rtk proxy dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore -- --mod-sync-session
rtk proxy dotnet run --project tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore -- --gameplay-admission
```

上面的宿主专项会共享 `obj/bin`，必须按顺序串行执行，不能并发启动多个 `dotnet run`；逐项记录退出码和 `PASS … assertions` 输出。模组同步按“声明 → 合同 → 内容传输 → 内容摘要 → 会话准入”顺序跑。测试新增行为，不要只测内部字段。至少覆盖正常路径、断线/重连、旧 generation、权限撤销、未知 schema、预算上限和重复消息。第三方玩法还要验证 `Bind → unbind → Select → Bind` 后旧会话失效、`SessionChanged` 重新触发，插件重新从 `Current` 取会话。

### 3. 构建并检查实际产物

```sh
rtk proxy dotnet build mods/OniOnline/OniOnline.csproj -c Release --no-restore
rtk proxy dotnet pack sdk/OniOnline.Framework/OniOnline.Framework.csproj -c Release -o dist/sdk
rtk proxy onim -m OniOnline build --release
rtk proxy git diff --check
```

构建成功只证明编译和打包。检查正式包不含 RuntimeProbe、不带外部 PLib 依赖，SDK assembly reference 只允许 `netstandard`；核对 ZIP/NuGet 内容、哈希和版本。不要把 Debug/Probe 包安装到真实游戏，也不要把旧 ZIP 当成本轮候选。

### 4. 只有最后才做全局验收

局部专项通过后，再运行完整宿主回归：

```sh
rtk proxy dotnet build tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore
rtk proxy dotnet tests/OniOnline.Tests/bin/Release/net10.0/OniOnline.Tests.dll
```

实机阶段必须从 Steam 启动，并使用一次性存档副本。保持暂停，按“暂停 → 观察 → 计划 → 执行 → 短暂恢复 → 暂停 → 验证”循环。多人验收先容量预检，再一图形/一无头，最后才扩大到双设备或多实例；协议客户端不能替代完整游戏。若容量门禁失败，保留失败回执并继续做源码、包和宿主验证，不降低门槛。

## 交付报告

报告要分开写：源码修改、局部测试、完整宿主回归、SDK/Mod 包、已安装、实际加载 DLL、真实游戏场景和未验收项。明确“同机 TCP”不等于“两台真实 ONI”，“包已构建”不等于“游戏已加载”，“Steam 下载完成”不等于“模组已启用”。未通过的原生 UI、输入法、存读档、Steam P2P 或双客户端场景不能用乐观措辞覆盖。
