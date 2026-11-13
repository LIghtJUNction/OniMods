# Resident disease diffusion, 2026-11-13

Whole-game speedup is **not yet established**. The candidate has passed native
tests, been deployed, and advanced the real save through its resident CPU path.
The first candidate runtime series suffered severe concurrent-build paging and
failed the graphical sampler's comparison checks. A quiet-host repeat is pending.

## Change

The world state already lives in CPU arrays. Disease diffusion now uses the
existing ordered CPU sweep for every region, retaining the sterile-region scan.
It no longer packs two snapshots, uploads them, waits for a GPU dispatch, and
copies four disease fields back. The former GPU operator is available only to
native tests, rather than as a runtime fallback. Its pipeline is not constructed
in production.

The comparison also exposed a GPU disease mutation bug: when an incoming disease
is stronger and the active resident count is nonnegative, the original scalar
rule retains the resident ID. The shader instead replaced that ID. The shader
reference is fixed and a two-cell golden regression checks both implementations.
Mixed-disease floating-point arithmetic can still produce different integer
rounding on the GPU; this is not a claim of universal GPU/CPU bit equivalence.
Production uses the original CPU arithmetic.

## Native validation and local cost

`cargo test --release --lib -- --test-threads=1`: **215 passed, 0 failed,
16 ignored**. The manual disease benchmark also passed separately.

The new differential test covers 18 single-disease/sterile combinations of
rectangle size, density and inset, each with three repeated updates. It compares
all four disease fields, including growth float bits and the right/top halos.
It includes signed integer extremes and an active buffer already changed by an
earlier stage. Mixed-disease resident-ID behavior has a separate golden test.

Manual benchmark: RTX 4060 Laptop GPU, Vulkan, 160 x 274 region, five warmups and
100 calls per backend, immutable old snapshot with independently evolving active
buffers. Timings include the GPU host packing and round trip. These fixtures
measure an operator, not a complete frame or game-speed limit.

| Fixture | Resident CPU ms/call | GPU round trip ms/call |
| --- | ---: | ---: |
| Sterile | 0.022094 | 0.021374 |
| Sparse single disease | 0.062708 | 5.566861 |
| Dense single disease | 0.558428 | 5.318613 |

## Graphical runtime evidence

The disposable save starts at native time 984406.875 with 15 duplicants. Both
recorded sessions used normal OpenGL graphics, 1225 x 1526, the same camera and
zoom, speed 3, frame cap -1 and vSync 0. Detailed CycleTrim probes were disabled.

The initial baseline's five measured active windows, excluding one warmup, had
median camera cadence **23.80 frames/s** and native advancement **1.924 seconds
per wall second**. This is callback cadence, not compositor presentation timing,
and the native advancement remains constrained by the requested speed.

The first candidate series is retained as an unsuccessful measurement. During
that short series, the host swapped approximately 6.2 GiB in and 6.7 GiB out.
One sample needed 12.81 seconds to verify its final pause, and distinct running
window boundaries were missing. Its approximately 11.9 frames/s median is
**not accepted as a controlled A/B result**. The raw guarded-step latency and
cadence remain in the JSON rather than being removed as outliers.

The loaded native mapping was checked against `/proc/<pid>/maps` and hashed;
`Player.log` confirmed `Disease diffusion uses resident CPU arrays (no GPU round
trip)`. Native time advanced, the population remained 15, and the companion
reported no game error. Those observations verify execution, not a speedup.

| Artifact | SHA-256 |
| --- | --- |
| Baseline native binary | `79fa302a4d6243a1d2577474a4993446c3f1784e7305e9f8d65ac310981d749e` |
| Candidate native binary | `f6e7e26655fb3a97d7891a7bb70fc9bb0cbdc933d0c45cb24461b078265e82ed` |
| Candidate native source | `60fe537a0ca5b9722ca92bdba3f5af7add835407df8a251c3581fda146b26e61` |

## Reproduction

After building with `onim -m CycleTrim dev`, launch ONI through the Steam-backed
Harness runtime with `--graphics normal`. Load a disposable copy of the same
save through OniMcp, pause it, and verify that loading and the population have
stabilized. Record the camera, resolution, loaded assembly receipts and native
mapping before comparing builds.

```sh
python3 scripts/benchmark_cycletrim_graphics.py \
  --output /tmp/cycletrim-runtime --label disease-cpu \
  --speed 3 --seconds 5 --warmup 1 --samples 5
```

The sampler delegates bounded execution and final pause verification to
OniHarness. It excludes transitions and warmups, retains every guarded-step
sample, records host swap counters and competing Rust compilers, and returns
exit code 2 when its comparison checks fail. Inspect host contention as well as
the validity flag. Reanalyze saved telemetry with `--analyze-only`.

For the isolated operator:

```sh
cargo test --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml \
  --release --lib gpu_sim::disease_tests::benchmark_resident_disease_sweep \
  -- --ignored --nocapture
```

Raw sanitized measurements are in [resident-disease-20261113.json](resident-disease-20261113.json).
