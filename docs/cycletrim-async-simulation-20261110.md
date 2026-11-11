# CycleTrim 0.4.15 / Jlib 0.3.5 synchronization investigation

## Measured 0.4.14 baseline

A disposable copy of the same 15-duplicant, eight-world save was run through Steam and OniMcp. Normal graphics, every-frame rendering, RTX 4060, 1220x1521, camera centre (98.53,223.5), zoom 35 or 160. Each running window was bounded to ten wall seconds and paused afterward. The save was not overwritten.

| Scene | Render FPS | Simulation seconds / wall second |
|---|---:|---:|
| Normal view, 1x | 23.81 | 0.92 |
| Full map, 1x | 14.74 | 1.00 |
| Full map, 3x | 7.04 | 1.40 |
| Full map, paused | 27.56 | 0 |

FPS uses fresh native frame counts and their captured timestamps. Whole-frame-chain intervals within the full-map 3x sample average 144.04 ms. Self time per frame includes 46.72 ms in `Sim.HandleMessage(PrepareGameData)`, 17.86 ms in managed simulation sub-ticks, 8.18 ms in SimDebugView jobs, and 7.27 ms in navigation-grid updates. Inclusive parent timings must not be added to these.

The prepare call waits for the native worker's previous batch. One sampled worker batch took 114.03 ms for simulation plus 2.28 ms grid, 2.84 ms textures and 9.39 ms species publication. Native gas regions also perform synchronous GPU readbacks; this is both CPU work and pipeline waiting, not proof of insufficient GPU compute throughput.

A separate 99 Hz, ten-second worker CPU sample collected 241 samples without lost events. Pressure preparation, species traversal/staging/publication, GPU upload preparation and integer-key hashing were among the largest named leaves. These percentages describe executing CPU samples, not wall-time shares. A large libc leaf remains unresolved and is not labelled as a particular memory operation.

The host also paged during this session (20,427 ONI major faults in a separate eight-second observation). Other processes contributed load. These are diagnostic observations, not a controlled old/new benchmark.

## Changes

- Native worker lifecycle events update one atomic completion signal. The render-thread query neither acquires the native state/mailbox locks nor waits for computation.
- Jlib schedules the original game timing loop. Managed sub-ticks which can use the current publication overlap native work; a positive or zero-time handoff is deferred until completion. Each call can consume at most one ready publication. Deferred input stays within the original 0.2 second limit, clears on pause, and belongs to one game instance.
- Rendering continues from the already published data. Existing buffer ownership and publication leases remain in force; worker faults are propagated rather than hidden behind a permanently busy state.
- Sparse extra-component storage uses a bounded cell-index array and dense occupied entries instead of hashing on every cell visit. No species are dropped or preferred.
- Settled snapshots cover all current worlds instead of clearing a four-entry cache while eight worlds are visited. Solid temperatures which are not gas-transport inputs no longer wake sleeping gas.
- Space exposure requires the native space zone (255) and no backwall (Vacuum or 65535). Gas/liquid loss keeps the previous absolute ceilings and adds a 25%/second fractional ceiling (5% per 0.2 second step), applied equally within each phase. Only phase totals at or below 1e-6 kg are cleared as traces. Ordinary interior vacuum does not leak.

This is a nonblocking native/managed handoff, not a claim that all game simulation has moved to another thread. Unity-dependent gameplay and snapshot acceptance still execute on the main thread. Native fixed steps are submitted by the gameplay clock, not an unrelated wall-clock loop that could run ahead of duplicants/buildings. The existing two-buffer ownership barrier remains bounded.

## Validation

- 145 native tests passed, ten manual benchmarks ignored, including real GPU pipeline tests and busy/ready/failure/shutdown signal coverage. An additional space transport-plus-decay test subsequently passed with the two existing space tests.
- 2238 Jlib host assertions passed, including real Harmony interception of a vanilla-shaped timing loop, all twelve starting sub-ticks, irregular render intervals, readiness changing during a call, pause/resume, separate game instances and visible worker errors.
- Jlib and CycleTrim Release builds passed. Linux loader initialization/shutdown and the new signal export passed.
- Initial candidate runtime measurement completed; see below. Throughput work follows that measurement.

Raw local evidence: `.cache/cpu-rewrite-20261110/release-test2/`. No saves, raw game code or profiler recordings are committed.

## Initial nonblocking candidate: rendering improved, throughput did not

The same input save and camera sequence were measured at 1219x1521 (one pixel narrower than the baseline), with every-frame rendering. The game was then closed without saving. This remains an observational comparison, with host paging and different warm-up/evolution as confounders.

| Scene | Render FPS | Simulation seconds / wall second |
|---|---:|---:|
| Normal view, 1x | 29.72 | 0.88 |
| Full map, 1x | 16.44 | 0.99 |
| Full map, 3x | 15.24 | 1.27 |
| Full map, paused | 32.81 | 0 |

Full-map 3x prepare self time fell from 46.72 to 0.41 ms/frame. However, a sampled native batch still took **131.614 ms: 125.259 simulation, 1.404 grid, 2.098 textures, 2.852 species publication**. The corresponding baseline running sample was 138.058 ms, including 123.700 ms simulation. Thus the expensive simulation did not improve; freeing rendering must not be presented as solving throughput.

Evidence: `.cache/cpu-rewrite-20261110/async-test/`.

## Removing work in the measured simulation path

Gas transport generated a density-gradient velocity, applied an incompressible projection, submitted it, synchronously reduced/read back raw velocity, then submitted transport and heat and read back again. The production face flux already limits both advection and diffusion: each face's donor coefficient is at most `dt * (min(1, .125/dt) + min(2, .125/dt))`; four faces sum to at most one. Raw-velocity CFL substeps were therefore redundant for this bounded scheme. Incompressible projection also removes the divergence of the expansion velocity, whereas gas must expand into vacuum.

The production gas path now uses density-driven bounded transport and heat in one submission. Device-generated velocities are still checked for finiteness with the final readback before any host output is exposed. All species remain separate and equal. The standalone pressure-projection and strict-CFL APIs retain their original contracts.

Release-mode local gas timings use five warm-up runs and twenty measured runs per fixed synthetic input. They include host staging inside the GPU chain, submissions and readback, but exclude world staging/publication and Unity. These separate runs show substantial timing spread; they are not whole-game measurements.

| Region / species | Old median ms | Direct median ms |
|---|---:|---:|
| 32x32 / 1 | 0.490 | 0.279 |
| 64x96 / 5 | 1.424 | 0.857 |
| 80x174 / 2 | 1.147 | 0.779 |
| 128x153 / 7 | 4.361 | 3.007 |
| 160x274 / 6 | 7.089 | 4.990 |

Radiation's GPU gather checked up to 25 neighbors twice per destination, including areas without emitters, and packed/read back each world. The direct CPU traversal visits cells and scatters only actual emitters. Paired tests restore the same input before each backend, compare every output cell, and record ten samples after five warm-ups:

| Region | Emitters | CPU median ms | GPU median ms |
|---|---|---:|---:|
| 32x32 | none / sparse / dense | 0.009 / 0.017 / 0.032 | 0.142 / 0.248 / 0.211 |
| 80x174 | none / sparse / dense | 0.132 / 0.116 / 0.393 | 2.367 / 1.062 / 0.862 |
| 160x274 | none / sparse / dense | 0.345 / 0.366 / 1.360 | 2.052 / 1.993 / 2.617 |

Production environmental radiation now uses the CPU traversal. The GPU reference remains test-only in the native integration. The region profiler now samples all regions in the same step and separates radiation, disease, building components and postprocessing instead of hiding these under `remaining`.

Validation: 146 native tests and 49 GPU tests passed in Release mode; eleven/six manual benchmarks were ignored in their respective full suites. The radiation comparison was run separately. New tests cover bounded high-velocity flux, non-finite-output rejection, gas expansion into vacuum at low/high concentration, wall isolation, decreasing density norm, species and heat retention. The benchmark's first fixture attempt had an invalid sentinel in its halo; its corrected fixture passed all paired comparisons. Release build passed.

Whole-game throughput retest is deferred at the user's request. The new build was deployed and launched through Steam, but the user closed it at the main menu before loading the disposable save. Logs show normal shutdown, no game crash, and no resource-guard abort. The user requested that it remain closed. No new gameplay FPS or complete-batch speedup is claimed.

Local timing evidence is in `async-test/gas-chain-{before,bounded,direct}.log`, `radiation-backends.log`, `local-gpu-bounded.log` and `local-native-throughput.log` under the directory above. The later build/deployment and main-menu attempt are in `throughput-test/` (build log: `async-test/build-throughput.log`). CycleTrim 0.4.15 / Jlib 0.3.5 are installed locally; these candidate versions have not been published to Steam or NuGet by this task.

## Next acceptance step

Keep the game closed until the user resumes testing. Load `throughput-0415-acceptance-20261110.sav`, whose input SHA-256 is `a58963ba5195996e701f2123a2f84bdda874f18a92071c4b66aefc3ce3c38067`, through Steam/OniMcp. Repeat the previous normal/full-map 1x and full-map 3x windows with the same warm-up, camera, normal rendering and bounded pause/resume loop. Measure complete native batches and simulation seconds per wall second alongside rendered FPS. Inspect the new same-step region breakdown to identify the next dominant phase. The previous 125.259 ms simulation / 131.614 ms batch and 1.27x actual advancement are the relevant initial-candidate comparison; local gas/radiation gains alone do not establish the complete result.

## 2026-11-11 runtime acceptance

The user resumed testing. Installed CycleTrim files matched the previous deployment receipt, and the loaded Jlib logged version 0.3.5. Steam launched the graphical client with normal every-frame rendering at 1220x1521. OniMcp loaded the unchanged input copy, verified fifteen duplicants and pause, and set the previous world/camera coordinates. Eight wall seconds at 1x preceded the measurement sequence. Each running window used the harness deadline and OniMcp pause/resume controls.

| Scene | Running telemetry window, seconds | Render FPS | Simulation seconds / wall second |
|---|---:|---:|---:|
| Normal view, 1x | 9.209 | 28.78 | 1.004 |
| Full map, 1x | 9.571 | 14.42 | 0.980 |
| Full map, 3x | 9.067 | 12.57 | 1.096 |
| Full map, paused | 6.139 | 29.65 | 0 |
| Full map, 3x repeat | 9.301 | 16.56 | 1.411 |
| Full map, 3x during attempted CPU sampling | 11.172 | 12.17 | 1.248 |

Performance acceptance remains failed. Requested 3x operation advanced around 1.10–1.41 simulation seconds per wall second. The first full-map 3x window included a 798 ms state-machine sub-tick call and increments in all three GC counters. The repeat recorded no GC collections. Host paging continued, including 5,432 main-thread major faults in the repeat control interval, and much heavier faults during the last window. These conditions limit comparison with the previous day's measurements.

One active full-map 3x native batch measured 116.301 ms simulation and 127.502 ms total, including 2.154 ms grid publication, 3.490 ms textures and 5.555 ms species publication. Four fully captured steps, each containing all eight region profiles, gave these mean phase times:

| Phase | Mean ms per fixed step |
|---|---:|
| Regional gas transport, including staging, GPU work/readback and CPU application | 50.794 |
| Global preparation and thermal work | 20.467 |
| Liquid flow and synchronization | 13.762 |
| Disease diffusion | 9.667 |
| Regional diffusion and synchronization | 3.678 |
| Disease growth | 3.117 |
| Temperature/phase processing and synchronization | 2.164 |
| Cell postprocessing | 2.112 |
| Building/component updates | 2.029 |
| Environmental radiation | 1.947 |
| Radiation emitters | 0.718 |

These are wall-clock stage timings. The global preparation figure includes bubble handling, snapshot synchronization and gas-channel refresh as well as thermal work. In the repeat's approximately twelve-second control interval, the main thread accumulated 10.32 CPU seconds, the native physics thread 5.98, and Unity's graphics-device thread 5.37. Function-level recording failed with performance-event access errors for both hardware cycles and software `cpu-clock:u`. Task-clock counting succeeded. No function-level sample report was obtained.

The gas pipeline already encodes a region's diffusion, velocity, bounded transport and heat in one submission. Regions still run through a serial host loop with individual readbacks. Cross-region batching remains an implementation task. Independent regions can share a submission/readback batch, with per-region offsets and species metadata. Thermal, phase, transport and disease stages require their data dependencies to be represented inside that GPU computation graph. CPU-owned liquid/building operations and publication currently introduce additional host boundaries.

Evidence is under `.cache/cycletrim-acceptance-20261111/`, including live tool schemas, launch/load receipts, telemetry, frame-chain windows, the complete-region phase summary, thread counters and failed perf logs. The paused screenshot was inspected. OniMcp then quit the test client without saving, and the process was verified absent. The input save hash remained unchanged. The resource guard did not trigger. Source implementation was unchanged during this acceptance run.
