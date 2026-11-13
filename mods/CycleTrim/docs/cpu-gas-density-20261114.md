# CPU gas density reuse — 2026-11-14

## Change

Parent revision: `b402f099`. CPU velocity generation previously reduced each cell and each open neighbor across all species. The no-diffusion packing pass now captures the same `f32::sum` in existing thermal scratch. The velocity stencil reads those totals; transport subsequently overwrites the thermal workspace. No additional buffer or executor submission is introduced. The cache is refreshed for every job, not retained as physics state. Single-channel transport is unchanged.

An initial separate parallel refresh added a barrier and produced inconsistent gains. Fusing a post-diffusion refresh also failed to establish a benefit. Neither is retained: diffusion uses the original stencil reductions. Test-only reference flags do not exist in production.

## Validation

- Release native suite: **220 passed, 17 ignored**, serial execution. Includes the new bitwise comparison over odd and single-column shapes, 2/8/16 species, signed zero and alternating diffusion settings, plus existing CPU/GPU and publication regressions.
- Manual benchmark compares the production helper with its original density reductions in the same process. Both paths use the same current transport implementation; earlier transport optimizations are excluded from this comparison.
- Release, 160×96 cells, 8 workers, 10 warmup pairs and 60 measured ABBA pairs per case. Inputs and output bits match.
- The paired mean ratio below is candidate/reference. Bootstrap 95% intervals use 5,000 pair resamples with seed 0. No outliers were filtered.

| Species | Mixture | Diffusion | Reference median ms | Candidate median ms | Paired ratio (95% interval) | Whole-command major faults |
| --- | --- | --- | --- | --- | --- | --- |
| 8 | dense | false | 0.9963 | 0.9843 | 0.9947 (0.9606–1.0331) | 16 |
| 8 | dense | true | 1.4989 | 1.4962 | 1.0190 (0.9938–1.0477) | 16 |
| 8 | sparse | false | 0.8518 | 0.8174 | 0.9794 (0.9501–1.0122) | 0 |
| 8 | sparse | true | 1.2651 | 1.2874 | 1.0007 (0.9672–1.0335) | 0 |
| 16 | dense | false | 2.2250 | 2.1848 | 0.9656 (0.9344–0.9966) | 0 |
| 16 | dense | true | 3.3502 | 3.3317 | 1.0053 (0.9806–1.0309) | 0 |
| 16 | sparse | false | 1.4788 | 1.3802 | 0.9502 (0.9216–0.9770) | 1 |
| 16 | sparse | true | 3.0472 | 3.0833 | 0.9939 (0.9664–1.0212) | 1 |

The 16-species no-diffusion cases measured about **3.4% / 5.0%** lower paired mean time. The 8-species intervals cross 1, so their speedup remains unconfirmed. Diffusion is a control case with the original computation. These are local CPU-chain measurements, not whole-game FPS or evidence that the 8 FPS problem is solved.

Fault counts cover Cargo and its children, including startup and warmup; they do not isolate measured windows. The sparse 16-species command had one fault and the dense 8-species command had 16. These samples are retained, not relabeled as fault-free.

## Reproduction

```sh
CARGO_BUILD_JOBS=1 CARGO_TARGET_DIR=/path/to/task-target RUSTUP_TOOLCHAIN=1.99.1 \
  cargo test --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml --release -- --test-threads=1

CYCLETRIM_GAS_BENCH_DENSITY=1 CYCLETRIM_GAS_BENCH_CHANNELS=16 \
  cargo test --manifest-path mods/CycleTrim/OniSim/native/Cargo.toml --release \
  benchmark_cpu_gas_chain -- --ignored --nocapture --test-threads=1
```

Set `CYCLETRIM_GAS_BENCH_SPARSE=1` for the sparse fixture. Do not run CPU/GPU benchmarks concurrently with the game or another build.

Raw local evidence: `/tmp/cycletrim-density-full-tests-20261114.log`, `/tmp/cycletrim-density-final-c{8,16}-{dense,sparse}-20261114.log`, and `/tmp/cycletrim-density-final-summary-20261114.json`. Private game data is not included.

## Delivery

`onim -m CycleTrim build --release` passed. The packaged Linux binary and build receipt were checked against the current native source digest and binary SHA256. This is a Linux development package, not a new three-platform release bundle. Existing dependency warnings remain.

The candidate is a source iteration after Workshop 0.4.22. No new Workshop upload or in-game FPS acceptance is implied by these tests.
