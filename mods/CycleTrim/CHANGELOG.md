## 2026-11-09 — 0.4.10

- Read multicomponent texture data in bounded batches of up to 128 contiguous cells. Copy native records under one publication lease, release it, then resolve elements and compute the existing colors. This removes per-cell native lock acquisition from the batch path without holding the lock across game callbacks.
- Reuse liquid gradients already calculated for the next row, preserving the original image bytes and exceptional floating-point behavior. Scratch storage is bounded by the world width and is reused after warmup.
- Require Jlib 0.3.2 for the optional batch-provider interface. The standalone dependency bootstrap rejects older providers before loading the CycleTrim implementation and tells the player to update Jlib.
- Preserve the existing single-cell path for other providers and batch failures. Host tests cover copied-data lifetime, invalid data, nested reads, exceptions and concurrent texture workers; Unity/Mono and save-specific FPS remain separate acceptance work.

## 2026-11-09 — 0.4.9

- Reuse the existing SIMD element-ID validator before texture publication.
- Reuse unchanged grid fields after both idle publication buffers are warm, while copying accumulated flow before its periodic reset.
- Pool resource-hover traversal collections and clear object references on success or failure. Measurements and verification are recorded in [the 0.4.9 report](../../docs/cycletrim-performance-20261109.md).

## Unreleased — simulation time and complete frame measurements (2026-11-08)

- Preserve the original `SimEveryTick` input and accumulated simulation time; remove the frame patch's unconditional `2/60` second truncation.
- Measure consecutive `Game.Update` entry intervals with a monotonic clock, including the Update-to-LateUpdate gap. Derive FPS, mean, p50/p95/p99 and maximum from the same bounded window; label callback cadence and unattributed time explicitly.
- Keep inclusive method timings separate from the frame total, close re-entrant and exceptional scopes safely, reset captures across game lifetimes and frame discontinuities, and report GC collection-count deltas. Timing-only hooks remain absent when diagnostics are disabled.
- Extend real Harmony host regressions for time preservation and capture behavior. All optimization host families and the managed onim reference build pass; Steam liquid and graphical FPS acceptance remain pending.

## Unreleased — source review fixes (2026-11-08)

- Preserve the game's evaluated chore costs by removing the automatic GPU recosting patch and its unused managed bridge. The standalone jump-flood API is documented as a geometric-distance experiment; its estimates can cross walls and must not replace navigation costs.
- Accumulate the actual elapsed time of deferred far-zoom animation updates per controller. Consume it once when an update runs, including zoom and movement transitions; clear it when a stationary animation is culled.
- Make frame timing, GC sampling and periodic FPS reports obey the existing performance-diagnostics option. Timing-only patches and stopwatches are absent when diagnostics are off; the existing frame and simulation policies remain active.
- Remove the ignored target-frame-rate option. Rendering retains the existing uncapped policy instead of presenting a setting with no effect.
- Add host regressions for animation timing and diagnostic patch behavior, and refresh the multicomponent hover fixtures against the pinned Jlib implementation. Release binary checks reject the removed automatic GPU recosting types.
- Repair the pinned-reference build by supplying Jlib's asset/audio modules from the existing verified Unity package, and return the GPU submission index through the public context API instead of accessing its private queue across crates.
- Update the performance-probe source contract for synchronous reporting while retaining scheduler-ownership and generation checks.
- Restore element, disease and backwall ID validation in release simulation steps. Reuse the existing batched validators and cover invalid writes without activity flags, plus the supported empty-value sentinels.

## Unreleased — GPU simulation data flow (2026-11-08)

Committed on `main`:

- Wire wgpu/Vulkan `GpuSimulation` into the OniSim tick, on by default; `CYCLETRIM_ONISIM_GPU=0` disables it and initialization failures fall back to CPU with a log line.
- Whole-grid GPU thermal diffusion and species sensible-enthalpy conversion; reuse resident temperatures across steps, compress mixture-presence uploads and profile upload block density.
- Chain regional gas pressure projection, transport and thermal work between GPU buffers; optimize multicomponent stencil isolation.
- Sparse GPU liquid multicomponent diffusion overlapped with the CPU gas-pressure pass; limit diffusion snapshots to the active region.
- Bubbles skip empty spans, liquid droplets settle, and both share one sparse scan.
- Reuse the previous publication for idle zero-time frames (with debug-update diagnostics).
- `--cycletrim-gpu-profile` Steam launch argument sets `CYCLETRIM_GPU_PROFILE=1` for sampled GPU timing logs.
- Far-zoom render throttling retuned (static-object animation stride up to 30 frames, overlay update interval up to 30 frames; duplicants stay at stride 1–2); Jlib texture-lerper and timelapse-camera optimizations applied; frame-setting repair logs once.
- Patch installation failures are written to the console before rethrowing.

Working tree (uncommitted; compiles with `cargo check --release`, no in-game acceptance yet):

- New WGSL operators wired behind the GPU path: bubble/droplet movement, phase-transition selection, gas pressure faces with liquid hydraulic head, liquid flow, disease diffusion and growth, radiation regions, inactive-cell mass decay; shared resident SoA buffers for operator chaining.

Still pending: per-step upload/readback remains, pressure/partial-pressure publication, three or more species, full save/load, and whole-game FPS acceptance.

## Unreleased — pickup and liquid-overlay hot paths

- Preserve resource-panel semantics: enumerate the same `WorldInventory.GetPickupables(tag)` collection as vanilla, but cache `Pickupable` → `KAnimControllerBase` references with weak keys and clear only controllers highlighted by the current UI element.
- If the optimized hover pass fails after partial work, roll back its partial highlights and let vanilla own both pointer-enter and pointer-exit, preventing stuck highlights.
- Cache `KBatchedAnimController` lookup in `OverlayModes.Mode.UpdateHighlightTypeOverlay`; unsupported IL shapes retain the original call.
- Optimize `PropertyTextures.UpdateLiquid` without changing cell order or output bytes: read invariant `LiquidMaxMass` once per update and remove the overwritten `LiquidAmountOffset` calculation.
- The original `HighlightColour` setter already suppresses unchanged values; no duplicate equality guard was added.
- Pinned-reference compile and release safety checks are required; ONI runtime and before/after colony benchmarks remain pending.

## 2026-11-07 — 0.4.8

- Promote the GPU multicomponent operator's CPU upwind flux into the native tick: dense 8-channel gas tensor, pack/unpack through legacy Grid + mixture, run after `gas_phase`.
- Still no in-process Vulkan handoff (upload/readback was slower than CPU on the flow bench); Poisson pressure remains research-only. Save ABI still stores dominant element + runtime mixture overlay.

## 2026-11-07 — 0.4.7

- Aggressive multicomponent: publish `FLAG_MULTICOMPONENT_COEXISTENCE`, allow a second gas species per cell when classic `displace_gas` fails, lower parallel gate to 1024 cells / up to 16 workers. Managed shims can leave the LEGACY fail-closed path.
- Does not yet replace the full Grid ABI or GPU Poisson solver; save format still stores the dominant element only.

## 2026-11-07 — 0.4.6

- Force Unity VSync off and uncapped `targetFrameRate` at load / Game.OnSpawn / graphics apply; re-assert if something restores the default. Default `CYCLETRIM_ONISIM_WORKERS=8` when unset.

## 2026-11-07 — 0.4.5

- Always map OniSim at startup. Remove the PLib `EnableOniSimExperimental` toggle and the official-SimDLL runtime fallback. `CYCLETRIM_ONISIM_ENABLE=0` remains an emergency kill switch only.

## 2026-11-07 — 0.4.4

- Package `OniSim.Compatibility.dll` beside `implementation/CycleTrim.dll`. Without it the Jlib bootstrap failed with FileNotFoundException and the dialog misleadingly pointed at Jlib.
- Merge the Rust SimDLL toggle and GPU multicomponent acceleration toggle into a single unified option ("OniSim 仿真与 GPU 加速 / OniSim simulation & GPU acceleration").

## 2026-11-07 — 0.4.3

- Fix severe frame drop (1-2 FPS) in late-game saves caused by AsyncPathProber.Manager loop starvation: reset penalty score on throttled navigators (navigators[nav] = 0) to prevent permanent ordering priority monopolization, and bound per-tick skips.
- Optimize stationary critter navigation probe check by directly querying cached CreaturePathFinderAbilities.canTraverseSubmered, eliminating repeated database and attribute lookups.
- Disable experimental animation symbol optimization by default to avoid Mono JIT unaligned read overhead on Linux x64.
- Disable OniSim runtime mapping by default in CycleTrim and the standalone
  loader. The official SimDLL remains active unless the experimental option is
  explicitly enabled.
- Rename the PLib opt-in key to `EnableOniSimExperimental`; do not reuse the
  old persisted `EnableOniSim` key, preventing upgrades from silently
  retaining the previous default-true takeover.
- Keep the native library packaged for opt-in testing; this changes runtime
  activation policy, not the shipped native artifact.

# Changelog / 更新日志

Generated from `git log -- mods/CycleTrim`.

## Unreleased — 2026-10-30

- Add restart-required Full / Half / Quarter conduit display refresh. Full remains
  the default; reduced modes sample geometry and sprite phase while preserving
  simulation calls, the outer animation clock, audio and every-frame drawing.
- Invalidate display reuse on simulation/topology changes, visible bounds, world,
  style and resource changes. Empty returns, partial failures, reentry and cleanup
  cannot publish old or incomplete meshes; incompatible rendering patches fall back.
- Validate real Harmony dispatch, late patches and failure paths. Retain complete
  paired synthetic measurements, including slower controls and failed iterations;
  game performance and Unity/Mono acceptance still require Steam-launched testing.

- Guard transformer circuit-resolution callbacks before reusing power-overlay
  answers. Real Harmony dependency patches now retain original per-query
  results and callback counts, including physical and virtual circuits.
- Create conveyor index scopes lazily, pass the lease through the rewritten
  call site, and preserve nested cleanup with an injected finally. Short and
  early-return paths avoid the former unconditional Harmony wrappers; nested
  calls retain an isolated barrier and invalidate their parent's index.
- Stop animation comparisons after the first changed field and retain the
  original remaining stores. Use an exact stack-local float bitcast to avoid
  explicit-layout union temporaries; pointer access is limited to value parameters.
- Add paired prior/current regression-recovery measurements, exact-bit property
  checks, and exception/control-flow regressions. Preserve unsuccessful trials
  and distinguish controlled JIT measurements from default-runtime checks.
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
