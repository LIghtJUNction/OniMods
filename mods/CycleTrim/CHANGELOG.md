# Changelog / 更新日志

Generated from `git log -- mods/CycleTrim`.

## 2026-09-12 — 0.3.2

- Invalidate path-probe results when replacement work is queued, discarded, unsupported, or fails; invalidate cached navigation state when the `NavGrid` object changes.
- Preserve immediate fallback for unsupported critter abilities and invalid cells, and avoid integer overflow when calculating worker admission limits.
- Add six executable policy regression groups. Game assembly compatibility and in-game performance still require release validation.

## 2026-08-23

- CycleTrim 现在识别 ONI 的非空回退差事 `IdleChore`，空闲复制人的拾取和差事刷新直接走原版即时路径。
- `Manager.TickFrame()` 的 IL 不匹配时，CycleTrim 只跳过异步路径探针配额优化，不再让整个 Mod 加载失败。
- CycleTrim now recognizes ONI's non-null fallback `IdleChore`; idle pickup and chore refreshes use the vanilla immediate path.
- When `Manager.TickFrame()` IL does not match, CycleTrim skips the async path-probe quota patch instead of aborting the whole mod.

## 2026-07-17

- [a81b712](https://github.com/LIghtJUNction/OniMods/commit/a81b7122177b73a8d4866168192251c5463e9e68): `fix(cycletrim): guard reservoir spawn signal`
- [c328e0e](https://github.com/LIghtJUNction/OniMods/commit/c328e0eb009cceacdcbf2e0e1558bb660cde2bb8): `perf(cycletrim): optimize busy duplicant chore updates`
