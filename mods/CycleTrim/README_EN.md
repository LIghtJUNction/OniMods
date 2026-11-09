# CycleTrim

![CycleTrim](preview.gif)

**Prerequisite:** install and enable [Jlib](https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839) ([source](https://github.com/LIghtJUNction/Jlib)) version **0.3.2 or later**, then restart the game. CycleTrim has used the batch-read interface since 0.4.10; its bootstrap checks the dependency version before loading the implementation. Consumer packages no longer embed Jlib.

A disruptive, high-risk experimental performance mod, still in early development. The direction is to rebuild the underlying algorithms and move as much computation as possible onto the GPU. Speed comes first, compatibility later. **Use it only with disposable test saves.**

The current build (0.4.12) has started removing one-element-per-cell flow: OniSim lets a second gas species share a cell (`FLAG_MULTICOMPONENT_COEXISTENCE`), and multicomponent gas flux plus GPU thermal diffusion run inside the native tick. Saves still store the dominant element plus a runtime mixture overlay; three or more species and full save/load are not done. The original argument and limits are in [next cell representation](docs/next-cell-representation.md); the current GPU wiring is in [GPU progress](#gpu-progress-2026-11-08).

The hot-path cuts listed below are what the current build already does. They are not the final shape, and they are not a promise of vanilla behavior.

This mod comes from [OniMods](https://github.com/LIghtJUNction/OniMods). onim is the toolchain in that repo. It takes an Oxygen Not Included mod straight through development, testing, and Workshop publishing: `onim dev` tries it in game, `onim build` packs it, and `onim publish` sends it to Steam.

## Index (summary page)

- [Whole-game update chain and diagnostics (0.4.12)](#whole-game-update-chain-and-diagnostics-0412)
- [Complete experiments and acceptance limits](../../docs/cycletrim-whole-chain-20261109.md)
- [Next version: replace one element per cell](docs/next-cell-representation.md)
- [GPU progress (2026-11-08)](#gpu-progress-2026-11-08)
- [Multicomponent assumptions audit](docs/multicomponent-assumptions-audit.md)
- [Algorithm evolution and mathematical models (Stage 2/2.1)](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md#algorithm-evolution-from-per-cell-displacement-to-gpu-poisson-pressure-projection-stage-221)
- [What it does](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md#what-it-does)
- [Usage](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md#usage)
- [Compatibility](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md#compatibility)
- [Benchmark summary](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md#benchmark-summary)
- [Detailed docs and logs](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md#detailed-docs-and-notes)

## Whole-game update chain and diagnostics (0.4.12)

A game update includes native simulation, publication, building and object state machines, pathfinding queues, property textures, animation, UI, camera and audio. Faster GPU kernels can still leave the main thread computing or waiting at those boundaries. Version 0.4.12 extends diagnostics to 22 phases and measures the complete interval between consecutive `Game.Update` entries, including independent Unity callbacks in the review. See the [call-chain evidence](../../docs/cycletrim-whole-chain-20261109.md).

Add `--cycletrim-perf-probe` to the Steam launch arguments to enable both managed phase and native batch diagnostics. `[CycleTrim][FrameChain]` JSON in `Player.log` records complete intervals, median, P95/P99, inclusive and self phase time, and unattributed time. Parse the log with:

```bash
python3 scripts/analyze_cycletrim_frame_chain.py /absolute/path/Player.log --output frame-chain.json
```

Do not add parent and child inclusive times. `PrepareGameData` and `JobManager.Run` can include both work and waits, and unattributed time is not a GPU timer. Update-entry cadence is not presented FPS. Diagnostics are off by default; their timing-only patches are absent when disabled. The [validation report](../../docs/cycletrim-whole-chain-20261109.md) defines the measurements and their limits.

This iteration repairs idle publication reuse across alternating empty/zero worker handovers, evaluates native publication revisions for texture-upload reuse, and keeps both publication payloads in fixed allocations so their owning pointers move through the handover. The report preserves failed candidates, every control scenario and tail timings. Function or host improvements do not establish whole-game FPS; this environment has no new ONI or hardware GPU frame-rate acceptance.

## Algorithm evolution: from per-cell displacement to GPU Poisson pressure projection (Stage 2/2.1)

As a core tenet of CycleTrim's simulation rebuild, the fluid pipeline is evolving from vanilla's **discrete spatial displacement** to **GPU-resident continuous Poisson pressure projection**.

### 1. Legacy Vanilla Sweep (serial grid sweep + arbitration displacement)
- **Serial donor-receiver mass exchange** (governed by `ElementRecord.flow`):
  $$\Delta m = \text{clamp}\left(k \cdot (m_a - m_b), \, -0.125 \cdot m_b, \, 0.125 \cdot m_a\right)$$
- **Displacement arbitration**: When incoming mass targets an occupied cell, `displace_gas` / `liquid_displacement` executes recursive multi-directional spatial searches.
- **Coupled state updates**: Clamps single-step transfers to $12.5\%$ donor mass, transfers discrete temperature and disease counts, alters cell element identity, and handles Vacuum / Sink logic.
- **Hardware constraints**: Heavy branching and sequential cell order inhibit wide vectorization (AVX) on CPUs and cause catastrophic warp divergence and memory thrashing on GPUs.

### 2. New GPU Continuous Poisson Projection (Stage 2/2.1 WGSL Kernels)
The new formulation treats fluid as a variable-density finite-volume continuum executed entirely in GPU compute passes:

- **Step 1: Multicomponent Conservative Face-Flux Transport (Transport Kernel)**
  $$M_c(t + \Delta t) = M_c(t) - \Delta t \cdot \nabla \cdot F_c$$
  Identical fused multiply-accumulates (MAC) on every cell without empty-cell searches.
- **Step 2: Finite-Volume Divergence & RHS (Divergence Kernel)**
  $$\nabla \cdot u^* = \frac{u(y, x+1) - u(y, x)}{\Delta x} + \frac{v(y+1, x) - v(y, x)}{\Delta y}, \quad \text{RHS} = \frac{\nabla \cdot u^*}{\Delta t}$$
- **Step 3: Variable-Density Poisson Pressure Solve (RBGS Pressure Kernel)**
  $$\nabla \cdot \left(\beta \nabla p\right) = \frac{\nabla \cdot u^*}{\Delta t}, \quad \text{where face permeability } \beta_f = \frac{1}{\rho_f} = \frac{2}{\rho_1 + \rho_2}$$
  Iterated via Red-Black Gauss-Seidel with Successive Over-Relaxation (SOR):
  $$p_i^{(k+1)} = p_i^{(k)} + \omega \left( \frac{\sum_{j \in \mathcal{N}(i)} \beta_{ij} p_j - \text{RHS}_i}{\sum_{j \in \mathcal{N}(i)} \beta_{ij}} - p_i^{(k)} \right)$$
  Dispatched in alternating Red Passes $((x+y) \bmod 2 = 0)$ and Black Passes $((x+y) \bmod 2 = 1)$ separated by pipeline memory barriers.
- **Step 4: Divergence-Free Velocity Projection (Velocity Projection Kernel)**
  $$u_f^{n+1} = u_f^* - \Delta t \cdot \beta_u \cdot \frac{p_{right} - p_{left}}{\Delta x}$$
  $$v_f^{n+1} = v_f^* - \Delta t \cdot \beta_v \cdot \frac{p_{bottom} - p_{top}}{\Delta y}$$
- **Boundary conditions**: Impermeable solid and outer boundaries strictly enforce normal velocity $u_n = 0$, Neumann zero-gradient $\frac{\partial p}{\partial n} = 0 \iff \beta = 0$.

### 3. GPU Closed-Loop Pipeline Vision
$$\boxed{ \text{Transport} \longrightarrow \text{Divergence} \longrightarrow \text{Poisson} \longrightarrow \text{Projection} \longrightarrow \text{Transport} }$$
The goal is to keep the complete physical loop in GPU memory while the CPU handles entity interactions, publication and gameplay queries. The execution boundaries below describe the current implementation, which has not reached that complete resident loop.

> **Status**: The Stage 2 CPU mathematical reference (residual $10^{-7}$, divergence decay, zero-velocity fixed point, and semantic comparators) and Stage 2.1 WGSL kernels (7 GPU operator tests and benchmark harness) are completed. Regional gas pressure projection, transport and thermal work now chain between GPU buffers inside the native tick; a global Poisson solver has not replaced the vanilla displacement path.

## GPU progress (2026-11-08)

OniSim creates a `GpuSimulation` on wgpu 30 (Vulkan backend) at startup, enabled by default. `CYCLETRIM_ONISIM_GPU=0` disables it before launch; an initialization failure is logged and the CPU path is used. `CYCLETRIM_GPU_PROFILE=1`, or the Steam launch argument `--cycletrim-gpu-profile`, enables sampled timing logs.

**Committed on `main` and wired into the tick:**

- Whole-grid GPU thermal diffusion (variable-coefficient finite volume) and species sensible-enthalpy conversion. Temperatures stay resident across steps; species-presence masks are compressed and uploads are block-incremental.
- Eligible gas regions: multicomponent concentration diffusion, density-gradient velocity, pressure projection, transport and thermal work chained between GPU buffers.
- Eligible liquid regions: sparse GPU multicomponent diffusion, overlapped with the CPU gas-pressure pass; diffusion snapshots cover only the active region.
- CPU work includes region eligibility and packing, applying phase-transition results, events and building components. Stages use their CPU path when no GPU is available or a region is ineligible. Idle zero-time frames can reuse already generated publication data.

**Whole-chain source review, 2026-11-09:** bubbles, thermal work, phase-transition selection, eligible gas pressure/transport, disease and radiation have GPU call paths, interleaved with uploads, waits, readback and CPU application. Multicomponent liquid diffusion has a GPU path, while production `gpu_liquid_flow` remains `false`: liquid pressure, flow and ordered displacement still execute on the CPU. WGSL operators and `resident_cells` in the repository do not establish that the entire production update has moved to the GPU. The [native-stage review](../../docs/cycletrim-whole-chain-20261109.md) records each boundary and its source.

**Still open:** every step still uploads, waits on the GPU and reads back — this is not a fully device-resident pipeline. Event application, building interaction and publication stay on the CPU. Pressure, partial-pressure and liquid-volume-fraction publication, three or more species and full save/load are incomplete, and whole-game FPS gains are not fully validated. The latest `Player.log` (2026-11-08 00:26) shows GPU acceleration active on an RTX 4060 Laptop; later commits have no new in-game log yet. Details: [OniSim/gpu/README.md](OniSim/gpu/README.md), [OniSim/docs/progress.md](OniSim/docs/progress.md).

## What it does

- Reuses fetch candidate lookup data for cheaper selection.
- Throttles expensive refresh work for busy duplicants only; idle duplicants remain responsive.
- Throttles stationary-critter navigation probing.
- Reuses immutable component type metadata during saves.
- Reuses repeated circuit demand queries within each power overlay update.
- Optionally samples gas/liquid conduit display mesh rebuilding while retaining simulation calls, every-frame drawing and audio.
- At far zoom levels, time-slices animation updates for static objects and throttles material/conduit overlay updates by zoom (duplicants keep a higher refresh rate); texture-lerper and timelapse-camera optimizations come from Jlib.
- Forces VSync off and an uncapped frame rate, re-applying (and logging once) if something restores the defaults.

## Usage

- Subscribe and enable as normal.
- No extra configuration is required in most cases.

**Save type metadata** is enabled by default and reuses adjacent type queries
within each save invocation. **Power overlay queries** is opt-in
because updates with few repeated circuits can cost more; both settings require
restarting ONI. Power query reuse falls back for unknown electricity interface implementations or
conflicting patches. The [implementation and validation notes](../../docs/cycletrim-moono-optimizations.md)
separate host tests and synthetic measurements from pending ONI runtime acceptance.
These are independent implementations informed by oni-moono's mechanisms, not its
engine/runtime replacement or its reported whole-game speedup.

**State machine lookups** is opt-in and requires restarting ONI. It reuses query
results while the state-machine list is unchanged. List changes invalidate the
answers; short lists and worker-thread queries keep the original path.

**Conduit animation refresh** defaults to Full. Half / Quarter rebuild the display
every 2 / 4 rendered frames when inputs are stable and continue drawing the completed
meshes between rebuilds. Simulation, camera, world and style changes refresh immediately;
a 100 ms age limit is checked on the next render call. Content overlays and audio
requests retain full updates. Restart is required; FastTrack or incompatible rendering
patches disable this group. Small workloads can cost more because of cache validation.
See the [implementation, complete measurements and acceptance scope](../../docs/cycletrim-visual-refresh.md).

## Compatibility

- Supports base game and DLC content, but OniSim takeover is experimental: use disposable saves.
- CycleTrim no longer intercepts `SmartReservoir` automation signaling; vanilla repeated notifications are preserved for stock reservoirs and third-party buildings that reuse the component.

### OniSim native simulation core

OniSim is CycleTrim's experimental native simulation core, with source under [`OniSim/`](OniSim/). Windows x64, macOS x64 and Linux x64 packages include the native library, and since 0.4.5 it is **always mapped at startup**, replacing the official SimDLL. The former PLib `EnableOniSimExperimental` toggle and the official-SimDLL runtime fallback were removed. Mapping cannot be hot-reverted. `CYCLETRIM_ONISIM_ENABLE=0`, `false`, or `off` remains only as an emergency kill switch before startup.

Use only with a disposable test save. All 62 official message IDs are now accounted for in source routing, but that is not proof of full semantic equivalence. Gas-interaction rules are parsed and stored, while the helper has no Rust fixed-step caller; static inspection also found no incoming call to the legacy helper in the pinned official control flow, so its required role remains unresolved rather than a proven missing phase. Limited save/load receipts apply only to their exact historical binaries, not automatically to the current source. DLC, other-Mod, graphical, long-duration, large-colony, and whole-game performance acceptance remain open; OniSim is not a complete replacement yet. Linux keeps the inspected `Assembly-CSharp` and Mono whole-file fingerprint checks; Windows/macOS require the ONI 744825 release with the same native interface and message layouts. Other versions are refused with a log message.

Builds include OniSim by default; use `IncludeOniSim=false` to create a package without it. `CYCLETRIM_ONISIM_WORKERS` defaults to 8 (set `1` to disable kernel parallelism). `CYCLETRIM_ONISIM_NATIVE_PATH` may override the bundled library with an absolute path; when unset, the loader uses the `.dll`, `.dylib` or `.so` under `OniSim/<platform>/` in the Mod folder. macOS needs an x64 game process (Rosetta on Apple Silicon). Three-platform CI builds and host loader tests are not in-game acceptance on every platform.

## Benchmark summary

| Scenario | Metric | Before -> After | Improvement |
|---|---|---|---|
| Busy duplicant throttle | FPS median (CPU hot-path scene) | `70.6 → 85.1` | `+20.6%` |
| Busy duplicant throttle | Brain total time | `5.087s -> 2.449s` | `-51.9%` |
| Stationary critter navigation throttle | FPS median | `81.23 -> 109.28` | `+34.5%` |
| Stationary critter navigation throttle | Navigator hot-path total | `826.852ms -> 47.970ms` | `-94.2%` |
| UpdatePickups cache | Hit rate (median) | `24.37%` | `24.37% of lookups hit cache` |
| UpdatePickups cache | Average cost | `41.279us -> 35.708us` | `-13.5%` |
| UpdatePickups cache | Estimated hits per second (30s window) | `~6.29 /s` | `cache-hit reuse` |

### Synthetic function-level benchmark for Brain scheduling (integrated, ordinary Creatures only)

The Brain rate cap is integrated into the production Harmony patch, but it applies only to the exact vanilla `CreatureBrainGroup`. Duplicants and custom Brain groups keep vanilla scheduling. Priority Brains keep the vanilla queue-selection and immediate-execution path and are not charged against the ordinary Creature allowance.

> **This is a standalone synthetic function benchmark, not an in-game FPS test, and it cannot be converted directly into whole-colony performance.**

- Environment: standalone `.NET 10` Release benchmark; ONI is not launched.
- Scenario: simulated `240` render FPS for `30` seconds, with `512` deterministic CPU-work iterations per normal Brain call.
- Method: `2` warmups and `7` paired, alternating baseline/candidate samples; medians are reported, and every sample verifies stable call counts and checksums.
- Call accounting: total calls fell from `43,200` to `19,200` (`-55.56%`). Ordinary Creature calls fell from `36,000` to `12,000` (`-66.67%`), while all `7,200` Dupe calls remained vanilla.
- Representative local result: elapsed time fell from `255.580ms` to `113.289ms` (`-55.67%`, `2.26x`).
- Scope: excludes priority workload, real Sensors/PathProber cost, and in-game Harmony scheduling overhead; it does not represent actual FPS.
- Safety difference: the current game DLL uses a dynamic `i != brains.Count` boundary. CycleTrim also re-reads Count so Brains added during an update can join the scan, but terminates when the list shrinks behind the scanned position to avoid the vanilla condition's potential failure to converge.

### AsyncPathProber deduplication and backpressure

- Supports only the exact vanilla `CreaturePathFinderAbilities`; Minion, Robot, and custom abilities fall back to vanilla because their dynamic permission/equipment semantics cannot be proven complete.
- A cache entry exists only after `Navigator.TakeResult` returns successfully. Cleared queues, unregisters, unapplied/stale results, and exceptions never complete an entry. Eight consecutive skips force a refresh.
- Startup verifies that Creature sentinel recycling is still an empty override; if a game update changes that contract, the entire Async optimization disables itself instead of running unsafely.
- Queue quota follows worker and in-flight counts within `1..4`; one worker uses quota 2 while idle and 1 while busy.
- 10,000-request synthetic matrix (**not in-game FPS**): requested hit rates `0/25/50/75/90%` produced `10000/7701/5501/3301/2000` PathProbe executions and avoided `0/2299/4499/6699/8000` clones, a theoretical probe-work reduction of `0/22.99/44.99/66.99/80.00%`. Bounded fallback makes actual reduction lower than nominal hits.

The standalone Brain benchmark project has been retired. The results above are historical measurements; they are not results from the current source. Run the current host checks with `python3 scripts/check_mods.py --skip-synthetic-performance`.

Full scenario notes are in [dist/CycleTrim/README.md](../../dist/CycleTrim/README.md) for reproducible detail.

## Source layout

```text
mods/CycleTrim/
├── ModInfo.cs          # KMod entry
├── OniSim/             # native simulation core, C ABI and loader
│   ├── native/         # Rust SimDLL (tick, multicomponent flux, publication)
│   └── gpu/            # wgpu/WGSL GPU operators and benchmarks
├── Core/               # non-patch core logic (e.g. BrainRateCap)
├── Patches/            # Harmony patches
├── docs/               # Steam descriptions, design/audit notes, screenshots
└── CycleTrim.csproj
```

Same monorepo convention as the other mods: keep only the entrypoint and project/metadata files at the mod root; patches live under `Patches/`, shared logic under `Core/`.

## Detailed docs and notes

- Changelog: [CHANGELOG.md](CHANGELOG.md)
- Runtime verification script: [verify_cycletrim_target_contract.py](../../scripts/verify_cycletrim_target_contract.py)
