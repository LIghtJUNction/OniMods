# Clean-cell dispatch diagnostic

Two rejected proposals assumed that an uninfected region had too little useful
work to justify parallel disease post-processing. Both retained correct states
but failed the original full-frame elapsed-time gates. The second proposal and
its extra cleanup test are archived in `proposals/disease-cleanup-slices.patch`;
it is not part of the production kernel.

Before another dispatch-policy change, the diagnostic patch calls production
`post_process_cells` serially and with four workers. A third role retains the
same chunk partitioning, Rayon scheduling and floating-environment installation,
but omits cell visits. Cells begin with no disease, zero count/growth and
infestation 1, making cleanup idempotent. Every warmup and timed role is checked
against the original complete cell state, including floating-point words.

Six permutations balance role order. Each sample runs 1,000 calls after 128
warmup calls per role. The local run used CPU 0/2/4/6, one logical CPU from each
of four different physical cores. Median elapsed microseconds per call were:

| Grid | Production serial | Production parallel | Empty dispatch |
| --- | ---: | ---: | ---: |
| 96×96 | 46.14 | 27.64 | 11.06 |
| 192×192 | 167.41 | 59.00 | 13.63 |

The premise is contradicted for these fixtures: useful cleanup benefits from
parallel execution even without disease growth. The parallel role spends more
aggregate CPU but finishes sooner. Empty dispatch is an approximate scheduling
control; its ratio to parallel time is not an additive cost attribution.
These short component samples share a live host and do not establish game FPS,
whole-frame gains or concurrent-game capacity. Full dispersion and CPU times are
in `results/linux-744825-disease-dispatch-premise.json`.

## Reproduce in a disposable source copy

Use new temporary directories. The diagnostic only adds an ignored Rust test;
it does not replace production disease processing. From the repository root:

```sh
rtk proxy mkdir /tmp/oni-clean-diag
rtk proxy git archive --output=/tmp/oni-clean-diag/source.tar 8f8cc4150ff9f85ee8c6110662144038bad7e12f mods/CycleTrim/OniSim/native mods/CycleTrim/OniSim/rust-toolchain.toml LICENSE
rtk proxy tar -xf /tmp/oni-clean-diag/source.tar -C /tmp/oni-clean-diag
rtk proxy git -C /tmp/oni-clean-diag apply --directory=mods/CycleTrim/OniSim/native /absolute/repo/mods/CycleTrim/OniSim/conformance/proposals/disease-dispatch-diagnostic.patch
rtk proxy env RUSTC_WRAPPER= RUSTC_WORKSPACE_WRAPPER= CARGO_BUILD_JOBS=1 taskset -c 0,2,4,6 cargo +1.99.1 test --release --locked --manifest-path /tmp/oni-clean-diag/mods/CycleTrim/OniSim/native/Cargo.toml disease::tests::diagnose_clean_region_dispatch -- --exact --ignored --nocapture --test-threads=1
```

Choose four allowed physical cores on another host; do not select SMT siblings
as four independent cores. Cargo may fetch the locked dependencies on the first
build. The diagnostic emits 36 `CLEAN_DIAG` JSON rows and requires one passing
test. Compilation, state checks and cell construction are outside each measured
loop. Retain failed runs and all samples. No game or Steam launch is involved.
