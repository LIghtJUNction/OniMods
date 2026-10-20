# Disease emitter comparison protocol

This protocol is written before collecting performance results.

## Scope and evidence boundary

Both sides compile the real `disease_emitters.rs`: the unchanged file from
`OniSim-source-20261020-040228.zip` and the current modified project file.
The original package does not compile as a whole (36 errors in the recorded
baseline check). Therefore this executable supplies only a small `PhysicsWorld`
container. Its `Cells`, disease tables, numeric conversion, and status code are
the actual archived project files. The three disease mutation functions are
extracted verbatim from the actual `fluid_helpers.rs`; their source and function
hashes are recorded. No substitute disease arithmetic is used.

This is an executed native component comparison. It is not an ONI game run,
a binary replacement compatibility test, or a measurement of game FPS.

## Correctness gate

All tested old/new searches must return the same cell set, without duplicates.
All tested updates must produce identical disease ID, count, infestation byte,
growth float bits, and emitter timer bits. The other cell fields must remain
unchanged. Tests cover small exhaustive grids, reproducible random grids, invalid
starts, unsigned 16-bit coordinate conversion, dimensions at and above 65536,
depth 0/1/255, inclusive region limits, overlapping emitters, positive and negative
counts, different diseases, overflow, and special floating point values.

The equivalence claim assumes valid allocated world storage and successful
baseline allocation. The old allocation failure path is not part of normal
successful update equivalence. There are no walls in this specific search:
element IDs are varied independently of the cell set.

## Workloads

- Public grids: 256 x 256, 512 x 512, and 1024 x 1024. Each has two extra cells
  per dimension, matching the project world's padding.
- `max_depth`: 1, 4, 16, 64, 255. The mathematical radius is `max_depth - 1`.
- Search: one centered emitter. Both implementations consume every returned
  index through the same non-linear integer checksum function.
- Complete update: 1, 8, or 32 spatially distributed emitters. Every emitter
  has disease 0, a positive count of 1, and zero interval/dt. Initial disease
  counts are positive. This keeps the executed disease branch stable during
  repeated measurement. It measures a common same-disease update path; different
  diseases and negative emissions are covered by correctness tests.
- Each case records the actual number of visited cells, including clipping.

## Timing protocol

- Rust release defaults, without fast-math flags or custom target CPU flags.
- One CPU affinity is selected from the process's allowed CPUs when supported.
  The selected CPU and actual compiler version are recorded.
- Each algorithm has warmup and calibration blocks. Calibration targets 20 ms;
  valid measured blocks must be at least 10 ms.
- Eight paired repetitions use AB, BA, AB, BA, AB, BA, AB, BA order.
- Independent repetition counts account for large speed differences. Results
  are normalized per search or per complete update call.
- Every update block starts from a clone of the same seed world and emitter
  state. Cloning and the final complete-state checksum are outside the timer.
- `std::time::Instant` supplies elapsed time. Outputs are consumed. Results
  include every measured block; any block below 10 ms is retained as `short`
  and rerun with a larger iteration count.
- Raw CSV includes warmup, calibration, short, and measured blocks. Summary
  statistics use measured blocks and report medians, dispersion, and paired
  ratios. No failed or slow measured block is removed.
- Allocation statistics use a separate build with an allocation-counting
  feature. That executable is never used to report timing. The normal release
  build has no counting allocator or atomic counter on its allocation path.

## Preset success criterion

1. No mismatch in the full correctness suite.
2. Median paired improvement greater than 5% at `max_depth` 1, 4, and 16 for
   every tested grid, both search and full update (all three emitter counts).
3. Report all deeper-radius results, including any regression. Do not generalize
   these selected component measurements to the complete simulation or game.

The threshold and workload grid must not be changed after observing results.
