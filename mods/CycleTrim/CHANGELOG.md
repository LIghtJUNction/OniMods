# Changelog / 更新日志

Generated from `git log -- mods/CycleTrim`.

## Unreleased — 2026-10-30

- Guard transformer circuit-resolution callbacks before reusing power-overlay
  answers. Real Harmony dependency patches now retain original per-query
  results and callback counts, including physical and virtual circuits.
- Stop comparing animation fields after a setter has already found a change;
  preserve subsequent writes, exact payload bits and partial-failure behavior.
- Skip top-level conveyor lookup scopes for candidates too short to meet the
  existing index rule, retaining nested-call isolation and invalidation.

- Add opt-in conveyor membership indexing scoped to one path-construction call.
  Build only after repeated scans, retain first positions, and discard the index
  on list edits, nested calls and exceptional exits.
- Add opt-in animation override field comparison using exact float bits. Keep
  original writes and errors in order, coalesce redundant version increments,
  and preserve original dirty behavior after a partial-write failure.
- Extend the real-Harmony host suite and paired benchmarks with short/early-hit
  conveyor controls and changing/unchanged animation workloads. The new options
  require a restart; runtime acceptance remains separate from host evidence.

- Independently implement save type metadata reuse inspired by oni-moono: cache
  only immutable CLR type metadata, preserve other reflection queries, and use
  weak keys so collectible types are not retained.
- Reuse circuit demand only inside each power overlay update, with manager
  identity, thread-local scopes, nested-update invalidation, exception cleanup
  and bounded storage. Unknown electricity interface implementations and
  conflicting Harmony callbacks retain original queries.
- Add separate restart-required settings (metadata on, power reuse opt-in), real Harmony host regressions, exact
  synthetic output checks and paired performance measurements. These do not
  establish Unity/Mono save/load, DLC compatibility or game FPS improvements.

## 2026-10-22 — 0.4.0

- Bundle the OniSim Rust core for Windows x64, macOS x64 and Linux x64 in one
  Workshop ZIP. The PLib option defaults to enabled on each platform; restart is
  required after changing it. Unsupported game profiles retain the original core.
- Build and test each native target on its own CI runner, including native export
  binding, initialization and shutdown through the production platform loader.
  CI/host tests do not establish Windows or macOS ONI runtime compatibility.
- Require all three binaries, matching source fingerprints and matching binary
  checksums before the Steam publisher accepts a CycleTrim release.

- Retain the queued navigation stamp when a worker dequeues just
  before the main thread advances its tick. Linked production-patch regressions
  cover that interleaving and late callbacks after unregistering a navigator.
- Store validated OniSim thermal commands inline and append energy batches
  directly, removing per-command payload allocations while preserving ABI bytes
  and processing order. The Release native suite passes 47 tests; whole-game and
  four-worker performance acceptance remain separate.
- Install Harmony patches once through the native mod loader. The previous extra
  `PatchAll` duplicated callbacks, probe samples and invalidation work, and tried
  to rewrite the already-rewritten path-probe queue limit.
- Accept explicit `--cycletrim-perf-probe` for Steam-launched diagnostics, keeping
  profiling off by default. Use OniHarness for bounded headless steps and inspect
  native progress, CPU cost and paging separately from rendering/control latency.

## 2026-09-19 — compatibility correction

- Stop patching `SmartReservoir.OnSpawn` and `SmartReservoir.UpdateLogicCircuit(object)`. Pinned 744825 source shows repeated equal-value `SendSignal` calls remain observable through `LogicEventSender.SetValue` / `LogicValueChanged`, so suppressing them is not behavior-equivalent.
- Restore vanilla `SmartReservoir` signal/event behavior for stock reservoirs and third-party buildings that reuse the component. This is a correctness/compatibility correction, not a performance gain; real ONI runtime acceptance is still required before merge.

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
