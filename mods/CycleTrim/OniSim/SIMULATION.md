# Official scheduling evidence and implementation boundary

This document records static observations of the installed Linux x64 official
library. It is not a conformance result or a performance measurement. It contains
no community simulator implementation. The official binary remains outside this
repository.

Reference SHA-256:
`85111cd0b1fc14c79c785581d6c055280b9d48ba705776f3d14eb0a7b239c12d`.
Offsets below are ELF virtual addresses in that exact binary, not source lines.
Local disassemblies are under `/tmp/re/oni-simdll/` and are temporary evidence.

## Observed stages

| Location | Static observation |
| --- | --- |
| `Sim::Main`, `0x5a147`, `0x5a1b7` | Synchronizes through `FrameSync::SimSync`, then calls `SimBase::Step`. |
| `SimBase::Step`, `0x59fe5` | Calls `BeginFrameProcessing`. |
| `0x5a026` | Calls `ProcessNextFrame` for each frame selected for processing. |
| `0x5a02b..0x5a08a` | A positive elapsed value leads to one or more `UpdateData` calls; the recovered fixed interval is 0.2 seconds and an accumulator retains the remainder. |
| `0x5a0da`, `0x5a0e2` | The nonpositive elapsed path copies updated cells and updates component data lists. It does not use the positive-time physics path. |
| `0x5a0a1`, `0x5a0c9` | Ends frame processing and publishes through `CopySimDataToGame`. |
| `ProcessFrame`, `0x4ae28..0x4af64` | Consumes the cell-energy command queue in its own stage. |
| `ProcessFrame`, `0x4bd5b` | Calls `ProcessCellModifications` after that energy stage. |
| `UpdateData`, `0x69932..0x69970` | Enqueues worker tasks and signals a condition variable. |
| `0x69ae3..0x69b1b` | Waits until the outstanding work counter reaches zero. |
| `0x69b33` | Copies updated cells after the worker barrier. |
| `GetNumWorkers`, `0x57c90` | Returns the constant `1` in this binary. |

The binary has worker-queue machinery. That fact alone does not establish useful
multicore simulation: this inspected worker-count method returns one. Nor does a
worker queue establish that all stages can safely run independently.

Commands have type-specific queues and stage order. A single global FIFO of all
received commands would not reproduce the observed energy-before-cell-edit order.
Per-stage FIFO and the order between stages are separate requirements.

## Cell-energy command

`ModifyEnergy` has message id `818320644` (`0x30c69504`) and a 16-byte payload:

| Offset | Field |
| --- | --- |
| 0 | `cellIdx: i32` |
| 4 | `kilojoules: f32` |
| 8 | `maxTemperature: f32` |
| 12 | `sourceId: i32`; an energy source identifier, not a callback handle |

The message handler queues a copy; it does not immediately change a cell.
`ProcessFrame` iterates this queue in order. Each item reads the current cell
temperature, so two writes to the same cell observe each other's effects.

Static arithmetic at `0x4aea9..0x4af4f`:

1. Convert the public cell index into the padded simulation grid using
   `(cellIdx / (width - 2) + 1) * width + cellIdx % (width - 2) + 1`.
   Here `width` is the internal padded width; this is not a public-grid stride.
2. Look up the element's temperature data. Skip when its state byte masked by
   `3` is zero, mass is `<= 0.001f`, or the requested temperature limit is nonpositive.
3. Compute `delta = kilojoules / (specificHeatCapacity * mass)` and add it to
   the current temperature.
4. Limit that candidate to `max(maxTemperature, currentTemperature)`. The requested
   limit therefore constrains heating without forcing a hot cell to cool to it.
5. For positive mass, the native branch reports a candidate `<= 0` or `> 10000`,
   clamps it into `[1, 10000]`, then still writes the result. For mass in
   nonpositive/unordered mass, a separate machine-code branch skips a nonpositive
   candidate and otherwise writes it without that same hard-range check
   (`0x4af24`, `0x4af40..0x4af56`). Finite nonpositive masses have already been
   skipped by the earlier `0.001f` comparison, so this is not a normal finite-input path.
   Constants were read from ELF
   addresses `0xa5328` (`10000`), `0xa5334` (`1`), `0xa5338` (`0.001f`).
6. After writing, call `DoStateTransition` at `0x4ae82`.

These branches, transition kernels and event records now have independently
authored implementations; their command-stage integration is being completed.
Replacing the unusual observed mass condition with a general `mass > 0` rule
would change behavior. Ignoring the state-transition call would drop required
work. Static recovery and compilation still do not establish executed equivalence.

## State-transition boundary

`DoStateTransition` starts at `0x55400` and occupies `0x983` bytes. A bounded
static inspection establishes the following, not a complete transition algorithm:

- Mass `<= 0` returns immediately (`0x55439..0x5543c`).
- A lower transition requires a valid target index (not `65535`) and temperature
  strictly below the threshold at temperature-data offset `0x28` minus `3`.
- Otherwise an upper transition requires a valid target and temperature strictly
  above the threshold at offset `0x2c` plus `3` (`0x55660..0x55676`).
- The `3.0f` constant is at ELF `0xa5348`; it agrees with the managed transition
  buffer constant.
- Transition paths inspect neighbor conditions, change element/temperature/mass,
  mark dirty state, and may append substance, ore, melting and falling-liquid
  events. Some paths clear the cell (`0x5595d`).
- Disease indices and counts are read and split/transferred with mass; clearing
  an exhausted source can set its disease index to `255`.

Transition, partial melting, sublimation, disease transfer and downstream event
modules have since been added from further static inspection. They remain subject
to later conformance checks. A rewrite that only changes the element id would omit
observed work. Frame-batch output ownership is documented in [FRAME-SYNC.md](FRAME-SYNC.md).

## Parallel execution constraints

The implementation currently serializes native mutations. It does not create a
pool of eight workers or infer independence from a cell's chunk number.

The future execution boundary must retain:

- The observed order between command, thermal, fluid, component and publication
  stages, once each stage has been recovered. The table above is incomplete.
- In-place versus separate-input/output semantics for each kernel. A read-only
  snapshot is not automatically equivalent to the official update.
- Exclusive ownership of each write target, including shared tanks, events,
  callbacks, counters and random-number state. Cell coordinates alone do not
  identify all shared state.
- Stable ordering of conflicting updates and event delivery. Parallel calculation
  followed by an arbitrary completion-order merge is insufficient.
- A barrier before publishing Grid pointers and completed-frame events.

Only kernels with established read sets, write sets and reduction order can be
scheduled concurrently. Unknown dependencies keep the relevant stage serial.
Chunk boundaries and halo cells describe data exchange; they do not prove that a
chunk is a closed simulation. Persistent workers, minimum task size and serial
fallback can be selected after correctness and workload measurements.

## Deferred acceptance

The original 04:02 paused snapshot had not run a simulator library, host demo,
game, test, or benchmark. The subsequent user-authorized Lean experiment isolates
the disease-emitter search and its local disease updates. Its proof and native
component measurements are documented in lean-lab/README.md;
they do not complete the full-core acceptance listed below.

After implementation is complete, acceptance still needs official-versus-rewrite
comparisons for boundary flow, conservation including explicit sources/sinks,
temperature transitions, callbacks, repeated runs, save/load and mod combinations.
Performance comparisons need the same workload at several worker counts and frame
time distributions; average FPS alone does not establish smoother execution.
