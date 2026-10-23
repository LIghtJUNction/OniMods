# OniOnline 验证矩阵

## 局部宿主

| 改动 | 优先命令 | 能证明 | 不能证明 |
| --- | --- | --- | --- |
| SDK/玩法/生命周期 | `--sdk` | 认证消息、玩法规则、存档槽、旧会话释放 | Unity 生命周期、原生 UI |
| DLC/游戏版本 | `--compatibility` | 能力合同、未知构建 fail-closed、codec 边界 | 任意版本真实游戏加载 |
| 入场模组同步 | `--mod-sync-contracts`、`--mod-sync-session` | 清单、分页、恢复、准入 generation | Steam Workshop 下载/重启后加载 |
| 星球准入 | `--gameplay-admission` | 身份、权限、撤权、授权观察边界 | 原生星球迁移 |
| 网络恢复 | `--resilience`、`--network-matrix` | TCP、弱网、重连、重复/乱序、预算 | 两台真实 ONI |
| 世界/观察 | `--world-interest`、`--world-sync` | 生产快照、观察授权、快照恢复 | 真实 Unity 渲染和 FPS |

完整宿主回归使用已构建 DLL：

```sh
rtk proxy dotnet build tests/OniOnline.Tests/OniOnline.Tests.csproj -c Release --no-restore
rtk proxy dotnet tests/OniOnline.Tests/bin/Release/net10.0/OniOnline.Tests.dll
```

## 构建和包

```sh
rtk proxy dotnet build mods/OniOnline/OniOnline.csproj -c Release --no-restore
rtk proxy dotnet pack sdk/OniOnline.Framework/OniOnline.Framework.csproj -c Release -o dist/sdk
rtk proxy onim -m OniOnline build --release
```

检查：

- `mods/OniOnline/dist/OniOnline.zip` 与 `dist/OniOnline.zip` 是否为同一候选；
- 正式 DLL 不包含 `RuntimeProbe`，不引用外部 PLib；
- SDK NuGet 包包含 DLL、XML、许可证和必要文档；
- `monodis --assemblyref sdk/OniOnline.Framework/bin/Release/netstandard2.0/OniOnline.Framework.dll` 只显示 `netstandard`；
- `git diff --check` 通过，未把无关 CycleTrim 或 `.local-tests` 加入提交。

## 真实游戏验收

需要实机时先读 `.agents/skills/oni-harness/SKILL.md` 和 `docs/autonomous-iteration.md`，再核对容量、Steam 链、当前进程、启用 Mod 和测试存档。用 Steam 启动，保存并保留一份一次性副本；不要强杀用户游戏。

最低真实场景：

1. 主菜单“浏览星系”只浏览/加入，不显示创建表单。
2. 新游戏 → 生存模式 → 联机进入创建流程，退出/取消不留下房间或 HUD。
3. 进入殖民地后，管理栏气泡和 `Ctrl+T` 直接打开通讯；重连时保留历史并显示状态。
4. 模组差异先出现摘要确认，下载后仍要求原生启用并重启；重新加入才算加载对齐。
5. 新建或加载一次性存档，验证 PlanetId/归属、暂停/倍速独立、授权观察、撤权和重连。
6. 两个真实游戏客户端分别报告 Steam 身份、实际加载 DLL、房间、聊天、保存/重启和恢复结果。

任何一项只由宿主测试、探针、截图旧候选或协议客户端证明，都只能标为“局部证据”。
