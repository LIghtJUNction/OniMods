# CycleTrim flow image SIMD optimization

`PresentationBuffers::update` now calls a row-based, four-cell SSE2 helper for
the flow image. It retains the existing CPU allocation and public texture ABI.
This optimizes presentation math, not the ordered fluid/pressure solver.

Each four-cell block transposes the existing flow records into vector lanes,
performs exact packed division only where mass scaling is needed, then stores
the original two-float output layout. A block whose elements all changed skips
the mass stream and uses a shorter pairwise layout. Partial rows use the scalar
tail. Active NaN masses use the same out-of-line scalar helper as the fallback,
so LLVM cannot select different NaN comparison instructions at separate sites.

SSE2 is part of the x86-64 baseline on all three supported operating systems.
No AVX, CUDA, OpenCL or GPU driver dependency is added. The helper retains the
caller's rounding/FTZ/DAZ controls. With unmasked FP exceptions it uses the scalar
path; packed arithmetic is used only with all exception masks set. It does not
allocate new heap buffers or alter simulation scheduling.

## Local verification

The retained [receipt](../mods/CycleTrim/OniSim/conformance/results/linux-744825-flow-simd.json)
contains source/binary hashes, host context and every benchmark sample.

- Four focused tests pass, including arbitrary float words, padded layouts,
  FTZ, NaN payloads, signed zero and MXCSR effects under 16 FP control settings.
- The full native library suite passes **51/51** tests.
- Against the installed, fingerprint-pinned Linux 744825 official library,
  **287 cases / 9739 publications** match exactly at **each of one and four
  workers**, including public arrays and ordered events.
- The isolated native candidate is
  `2f91e5f531261cd322aadb17057e6205bbb75ebb5c82113cf57e264051614199`.

## Measured operator cost

The benchmark calls the production helper and two frozen previous algorithms:
the original Rust per-cell padded indexing and the previous scalar row loop. It uses
five warmups and 41 rotating triplets, pins only the benchmark to CPU0, and
allocates/touches fixtures outside the measured calls. Raw samples, ranges,
medians, p95, process RSS and the host's powersave/frequency context are retained.

On the tested 256x384, 512x512 and 1024x1024 grids:

- Relative to the previous row algorithm, median speedups range **1.19–2.67x**.
- Directly against the original algorithm, they range **2.00–5.51x**.
- All twelve colony-sized shape/workload combinations pass the preselected
  no-regression gate: no median slowdown above 3%, and at least one speedup of
  10% or more. Tiny grids are also recorded without hiding their overhead.

The four authored patterns cover mixed masses, masses below one, all changed
elements, and zero mass/flow. Element tags are synthetic, including in the
zero-flow case: these are operator fixtures, not complete colony simulations.
Earlier candidates failed the changed-element performance gate and a denormal
MXCSR check; the final implementation fixes those causes without weakening the
gates. The internal scalar fallback's call structure is not used to inflate the
benchmark baseline: the old complete row implementation is retained separately.

These numbers are not whole-frame or game FPS gains. Windows/macOS runtime,
save/mod combinations and graphical acceptance remain unverified. No Steam
Workshop package or currently running game has been updated by this change.

## Reproduce in order

From `mods/CycleTrim/OniSim/native`, run the focused test first:

```sh
rtk proxy cargo test --locked --release presentation::flow::tests
rtk proxy cargo run --locked --release --example flow_cpu_bench
rtk proxy cargo test --locked --release --lib
rtk proxy cargo build --locked --release
```

The example writes CSV to stdout and human-readable gates to stderr; exit 2 means
the performance gate failed. Run it with a stable affinity and record host load.
Then use `OniSim/conformance/run.py` with the locally installed official library
and the exact newly built candidate, once per worker configuration. A reference
fingerprint failure must not be bypassed. Full-game acceptance is a later gate.
