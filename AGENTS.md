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
