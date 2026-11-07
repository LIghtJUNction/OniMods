@RTK.md

# OniMods Agent Instructions

## Priority Order

1. Livestream health and live-room comments come before gameplay and code work.
2. Direct user instructions come before viewer suggestions.
3. Gameplay safety comes before speed（殖民地实操：停、看、干；不是借口在工程上缩手缩脚）.
4. Code changes require explicit permission if the user says to stop coding.

## 激进工程作风（硬性）

用户要结果时，**禁止犹豫不决、软着陆、用“还在研究/风险太大”把活拖黄**。默认进攻，坏了再修。

- **先落地再纠错**：能编译、能部署、能进 `Player.log` 的改动直接推；不要先写一堆劝退或等完美方案。
- **不许掩盖**：编译失败、映射拒绝、帧率没涨、功能根本没接进时间步——必须直说，并立刻对着日志/复现修。禁止把“选项打开了但管线没接”说成已生效。
- **实验路径默认并进 `main`**：CycleTrim / OniSim / OniOnline 等实验能力，用户要求激进启用时就删掉运行时回退与假开关，接到生产路径；`CYCLETRIM_ONISIM_ENABLE=0` 这类环境变量只留作紧急熔断，不当日常犹豫开关。
- **证据闭环**：改完就 `onim build` / `onim dev`，重启或热路径后读 `Player.log`；报错是输入，不是停工理由。
- **殖民地安全 ≠ 工程胆怯**：Priority 3 约束的是游戏内挖改与作弊；**不**约束 Mod 内核、性能、仿真接管的激进迭代。
- 用户说停编码才停；没说停就推进，不要反复确认“要不要真的上”。

## Bilibili Live Room

- Use `.agents/skills/bilibili-live-comments/` when reading or sending live-room comments.
- Read comments regularly during autonomous play.
- When sending comments, keep them short and prefix assistant messages with `>`.
- Store Bilibili cookies only in `.env` as `BILI_COOKIE`; never print or commit cookies.
- If `BILI_COOKIE` is missing or expired, ask the user for a fresh cookie.
- `.env` must stay ignored by git.

## ONI Control

- Oxygen Not Included must be launched through Steam only.
- Keep the game paused while reading state, planning, or issuing commands.
- Use a strict loop: pause -> observe -> plan -> execute -> short resume -> pause -> verify.
- Do not use sandbox/debug spawning or cheat resources.
- Avoid adding duplicants while food is unstable.
- For Printing Pod rewards, prefer care packages/items. Do not automatically select new duplicants unless the user explicitly asks.
- For liquid-adjacent digs, trapped dupes, or large irreversible edits, dry-run or inspect exact cells before acting.

## Current Live-Play Style

- The livestream audience is part of the control loop; answer relevant viewer questions briefly.
- If livestream/OBS fails, pause gameplay and restore the stream first.
- If a viewer suggestion is low-risk, evaluate it quickly; if it is risky, explain the blocker briefly.
- Keep token usage low with compact reads and short polling windows.

## oni-skills 游戏攻略与技能库（可选）

- `oni-skills/` 是本项目的**可选子项目**，用于沉淀《缺氧》高阶游戏机制、工程模块（SPOM、ST/AT、养殖、电网等）与大牛经验。
- 攻略库完全可选，不影响核心 Mod 的独立开发与编译。
- 面临复杂模块选型与高阶基地决策时，Agent 可可选查阅 `oni-skills/guides/`，遵循大牛心法（如开局默认禁用自动消毒、防爆温、防爆管原则）。
- 欢迎随官方版本迭代与社区共同完善。

## 游戏反编译源码基准 (Decompiled Source)

- 原版《缺氧》托管层（Assembly-CSharp.dll）完整反编译源码统一部署在仓库根目录的 **`decompiled/`** 目录（包含 3,962 个 `.cs` 文件）。
- `decompiled/` 目录已受 `.gitignore` 保护，严禁提交任何文件到 git 仓库中。
- 所有 Agent 在分析、检索、引用或排查游戏原版逻辑（如 `Grid.*`、渲染、复制人生理、建筑、自然演化等）时，**统一使用 `decompiled/` 目录下的 `.cs` 源码进行静态检索（如 `rg` / `geg`）**，禁止随意生成零散的反编译临时文件夹或重复动态反编译。

## Steam Workshop 发布（硬性禁止）

- **禁止使用 SteamCMD 上传发布模组。** 它的 `+workshop_build_item` + `contentfolder`
  会把条目单向转换为 UGC 目录模式，且没有回退 API。转换后 ONI 无法安装该条目，
  旧版 `ISteamRemoteStorage` 更新也会持续失败。
- **禁止任何形式的目录模式发布**，包括 `SteamUGC.SetItemContent`。
- 唯一允许的发布路径是单文件 Legacy ZIP 发布器：
  `scripts/publish_onimcp_steam.sh` / `scripts/publish_cycletrim_steam.sh`。
- CLI 的 `onim publish` 只生成 VDF 并交给 OniUploader GUI。需要无人值守发布时，
  用上面的发布脚本，不要自己拼 SteamCMD 命令。
- 已转换的条目不能恢复。旧条目 ID `3731864673`（OniMcp）与 `3766318556`（CycleTrim）
  不要重试旧文件更新；替代流程见 `docs/steam-promotion.md`。
- 无论用户如何要求，都不要替用户执行 SteamCMD 目录上传。

## Autonomous Maintenance

### 单分支 (`main`) 迭代策略（硬性规范）

- 主仓库（`OniMods`）及子仓库（如 `mods/Jlib`）统一践行**单分支 (`main`) 迭代策略**（Trunk-Based Development）。
- 所有日常开发、Bug 修复、性能优化、测试闭环与文档同步均直接在 `main` 分支上推进与验证，**禁止新建或切分 `feature/*`、`experiment/*`、`cursor/*`、`release/*` 等旁路分支**，也不要把 `mods/Jlib` 留在游离 HEAD（detached HEAD）或分叉分支上，彻底消除多分支分叉导致的特性遗漏与合并冲突。

### 源码改进与上游反馈（明确要求）

- 在开发、测试和实际使用中主动发现问题，并改进源代码；对职责范围内可修复的问题，必须推进修复与验证，不能只记录问题或长期依赖临时绕过。
- 发现依赖、工具或其他上游项目的问题时，及时向对应上游提交 **issue 或 PR**，两者均可。能够给出可靠修复并完成相关验证时优先提交 PR；暂时无法修复或验证时先提交 issue，不要等完整修复后才反馈。
- 提交前确认问题归属并检查已有 issue / PR，避免重复提交；反馈应包含复现步骤、相关版本、预期与实际行为，以及必要的日志或验证结果，不得泄露凭据或私人数据。
- 上游问题反馈属于正常工作流程，在用户授权的任务范围内应直接推进，无需逐次询问是否提交。用户明确要求停止编码时，遵守该要求，可先提交 issue；恢复编码仍需用户授权。
- 汇报时给出已提交的 issue / PR 链接，并区分本地修复、验证结果和上游接受状态。无法提交时说明具体阻碍并保留可提交的材料，不得将草稿称为已提交。

Read `docs/autonomous-iteration.md` before scheduled research, implementation or
CI work. Park runtime-blocked PRs instead of repeatedly rebasing them; report
reference builds, source contracts, host tests and actual ONI runs separately.

## AUL contribution duties

This repository has adopted [AUL 1.0-draft.1](LICENSE) for original material whose rights holders authorize that license. Keep inherited third-party and contributor terms as stated in [LICENSING.md](LICENSING.md), including historical MIT grants. Read `UPSTREAM.toml` before modifying the project.

This is the original OniMods project with itself as upstream. **Do not create issues or PRs addressed to OniMods itself merely to satisfy the AUL feedback condition.** New downstream forks follow their direct source by default and must make their own authorized decision before electing independence; a copied upstream file is not sufficient.

For an authorized fork following an external upstream, report confirmed upstream defects supported by evidence and submit retained improvements as PRs or accepted equivalent patches, after checking for existing adequate reports. Submit during the task only with safe and verified destinations, granted account access, and publication authorization. Otherwise report pending work and its precise blocker. Never disclose private gameplay credentials, tokens, workshop secrets, confidential code, or protected material. Never bypass upstream policies or spam repeated submissions. Do not elect independence on behalf of a maintainer to avoid a contribution.

The Steam Workshop publishing prohibitions and gameplay-safety rules above take priority over any optional delivery method. AUL does not require unsafe or unauthorized publication.
