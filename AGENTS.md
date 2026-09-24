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

Read `docs/autonomous-iteration.md` before scheduled research, implementation or
CI work. Park runtime-blocked PRs instead of repeatedly rebasing them; report
reference builds, source contracts, host tests and actual ONI runs separately.
