# Reproduce the disease emitter experiment

Required: Python 3.11+, Rust **1.99.1**, and a C linker for the Rust target.
There are no external Rust crates or Python packages.

After cloning OniMods, keep this directory layout:

- `mods/CycleTrim/OniSim/lean-lab/baseline/`
- `mods/CycleTrim/OniSim/native/src/disease_emitters.rs`
- `mods/CycleTrim/OniSim/native/src/diamond.rs`
- `mods/CycleTrim/OniSim/lean-lab/harness/`

From the extracted package directory:

```bash
rustup toolchain install 1.99.1 --profile minimal
RUSTUP_TOOLCHAIN=1.99.1 python3 mods/CycleTrim/OniSim/lean-lab/harness/run.py all
```

If the project requires RTK, use `rtk bypass` before Python. The runner picks
one allowed CPU by default; `--cpu N` selects another allowed CPU. Its build
directory defaults to `/tmp/onisim-diamond-build`. Use `--target-dir PATH` to
choose a different temporary directory.

The runner verifies every committed baseline source SHA256 and its recorded archive identity before regenerating exact helper functions.
It builds two executables: ordinary release for timing, and a separate feature
build for allocation statistics. It runs correctness before timing. Results are
written to `mods/CycleTrim/OniSim/lean-lab/results/`.

Individual stages are available:

```bash
RUSTUP_TOOLCHAIN=1.99.1 python3 mods/CycleTrim/OniSim/lean-lab/harness/run.py build
RUSTUP_TOOLCHAIN=1.99.1 python3 mods/CycleTrim/OniSim/lean-lab/harness/run.py verify
RUSTUP_TOOLCHAIN=1.99.1 python3 mods/CycleTrim/OniSim/lean-lab/harness/run.py benchmark
RUSTUP_TOOLCHAIN=1.99.1 python3 mods/CycleTrim/OniSim/lean-lab/harness/run.py allocations
RUSTUP_TOOLCHAIN=1.99.1 python3 mods/CycleTrim/OniSim/lean-lab/harness/run.py summary
```

For a new checkout or after changing code, use `all`. A standalone `benchmark`
stage reuses an existing binary and passing correctness file; it does not bind
that correctness file to a newly compiled binary. Individual stages are for
continuing the same unchanged build, not a substitute for a fresh full run.

## What the executable actually tests

The archived and candidate disease emitter source files are compiled directly.
The archived `Cells`, disease tables, status handling, and numeric conversion
are also used directly. `prepare_sources.py` extracts `clear_disease`,
`calculate_disease`, and `add_disease` verbatim, checks exact equality, and
records file/function SHA256 hashes in `baseline/provenance.json`.

The only type adapter is a four-field `PhysicsWorld` plus the matching `Region`
fields. It excludes unrelated subsystems that prevent the original whole crate
from compiling. Consequently, results establish native component behavior and
performance under the recorded fixtures. They do not establish ONI runtime
compatibility or game FPS.

`PROTOCOL.md` specifies the workloads and success threshold fixed before timing.
`results/environment.json` records the source hashes, compiler, CPU, affinity,
OS, build/run commands, and timing binary hash. `raw-samples.csv` and
`raw-samples.json` retain warmup, calibration, and measured blocks. `summary.csv`
and `summary.json` distinguish median paired ratios from the ratio of medians.
The recorded time unit is nanoseconds per complete component call.

Correctness compares cell sets without relying on visit order. It also compares
disease fields and emitter timers bit for bit after actual updates. The timed
full-update workload uses stable positive, same-disease emissions. Different
diseases, overlapping positive/negative emissions, unusual float bits, and
coordinate conversion edges are covered by the separate correctness suite.

The original full source ZIP is intentionally not committed. Its input SHA-256 is recorded in `baseline/provenance.json`.
