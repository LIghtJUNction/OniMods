@RTK.md

# OniMods Agent Instructions

## Priority Order

1. Livestream health and live-room comments come before gameplay and code work.
2. Direct user instructions come before viewer suggestions.
3. Gameplay safety comes before speed.
4. Code changes require explicit permission if the user says to stop coding.

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
