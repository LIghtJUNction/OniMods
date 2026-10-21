# Pressure parallelism: model and implementation boundary

These are proposed-model proofs. The production pressure phase is still serial.
They supplement the existing diamond traversal proof, without changing its
34-theorem audit or claiming a whole-engine proof.

```sh
rtk proxy env LEAN_BIN=/path/to/lean-4.19.0/bin/lean python3 \
  mods/CycleTrim/OniSim/lean-lab/pressure-proof/verify.py
```

The verifier compiles all five modules (26 theorems) with warnings as errors, checks every theorem's
axiom report, rejects incomplete proofs, and binds exact source hashes. It emits
no compiled proof objects. The compiler/runtime is a local tool dependency and
is not bundled in CycleTrim.

## Disease mutation

`DiseaseMerge.lean` models signed wrapping counts with 32-bit vectors, disease
and infestation bytes, and an opaque growth bit pattern. It proves the proposed
same-species shortcut equal to the general update for every modeled state and
delta, including wrapping and nonpositive cleanup. The different-species
resolver is abstract and unchanged. This does not prove its floating-point
formula or compiled Rust.

The private Rust proposal passed 225,792 state combinations and both 224-case
official differential suites. It remains unadopted: the independent CPU16 timing
holdout did not meet the predeclared improvement threshold for ordinary flow.
See `../../conformance/results/linux-744825-same-species-experiment.json`.

## Pressure cell dependency model

Normalize x to the current scan direction; side is then x+1. The first pressure
loop reads/writes cell, side, upper and upper-side. Its candidate mutable
footprint is a two-by-two square. The old snapshot, tables and scan direction
are immutable inputs; mutation events must be buffered separately.

The ten scheduling theorems establish:

- Lexicographically ordered overlapping tasks strictly increase `x + 2*y`.
- Distinct tasks with equal `x + 2*y` have disjoint modeled footprints.
- A task cannot change another disjoint task's restricted input view.
- Arbitrary pure transitions restricted to those views commute when disjoint.
- Stable insertion sorting of a finite lexicographically ordered task list by
  wave preserves the complete execution result for those local transitions.

`PressureEnumeration.lean` adds four arithmetic theorems: the row window selects
exactly the valid coordinates, every cell's wave is visited, and emitted
coordinates reconstruct a unique wave. The Rust proposal implements the bounded
window without allocating a task list. The equivalence between this generated
sequence and the stable sorting model is not yet proved in Lean.

These are cell-level models. They do not prove the complete tiled executor, the current
Rust kernel's actual access footprint, event ordering, machine overflow, parallel
memory safety or full-engine equivalence. The private traversal proposal still
executes one cell at a time; pressure is not yet parallel in production.

The retained [serial traversal proposal](../../conformance/proposals/pressure-wavefront-order.patch)
passes 30 release unit tests and both complete 224-case official differential
suites, comparing 8440 publications each. A four-worker setting exercises
existing parallel subsystems; the proposed pressure traversal itself remains
serial. Unmasked floating-point exceptions select the original traversal to
retain trap sequencing. See the [source-bound receipt](../../conformance/results/linux-744825-pressure-wavefront-order.json).

## Proposed implementation units

1. Factor the pair calculation into a kernel receiving immutable old values and
   separate mutable cell-field views. Keep checked, disjoint borrows; avoid
   simultaneous mutable references to an entire `PhysicsWorld`.
2. Keep the current serial traversal as the reference while validating the
   factored kernel against every public field and ordered event list.
3. Skew tiles by `(x + 2*y, y)` so each job owns bounded scratch storage rather
   than a full world clone. Prove tile dependencies before parallel execution.
4. Preserve each tile's original local operation order and each region's barrier.
   Keep ordered event queues unchanged. Pressure substance notifications may
   merge by membership only after checking their final canonicalizer; all other
   event queues retain their order. Displacement loops stay serial.
5. Cap scratch memory by worker count and tile size; measure event-buffer peaks.
   Preserve a serial fallback for small regions and resource exhaustion.
6. Compare serial/parallel results on shifted/reversed/overlapping regions,
   mixed diseases, lifecycle/save cases and floating-point boundary inputs.
   Measure complete native frames and actual colonies separately.

No parallel speedup or memory reduction is established by these model proofs.

## Fixed skew tiles

`PressureTiles.lean` models 32-wave bands and 16-row bands. Four theorems prove
that overlapping tasks advance monotonically in both tile coordinates, distinct
dependent tiles strictly advance their combined level, and different tiles in
the same level cannot overlap. The source-cell horizontal span is at most 62
cells; a one-cell halo fits a 63 by 17 patch.

The first private executor proposal gives each job an owned fixed-size scratch world,
copies only its task footprint, runs independent same-level jobs through Rayon,
then merges on the simulation thread. It reuses at most eight patches. No worker
receives a mutable reference to the global world, and no full-world clone is
created. Serial fallback preserves original order for one worker, small work,
unmasked exceptions or preflight allocation failure. These dependency proofs do
not establish the Rust copying/ownership contract, notification merge, arithmetic
overflow, complete executor equivalence, performance or actual game acceptance.

The retained [first executor patch](../../conformance/proposals/pressure-tiles.patch)
passes 31 release unit tests, including 576 serial/parallel comparisons across
16 floating-point control combinations and two/four workers, plus both complete
224-case official differential suites. Buffer/object capacities account for
151272 bytes per worker, excluding allocator and thread overhead. Its performance
run was interrupted by another task launching ONI; the per-child guard refused
to produce a successful summary. Exploratory samples show substantial CPU
overhead, so this candidate is not adopted. The [receipt](../../conformance/results/linux-744825-pressure-tiles.json)
retains the partial measurements and explicit limitations.

The [second executor patch](../../conformance/proposals/pressure-tiles-v2.patch)
enters the pool once per phase and skips immutable solid/property exclusions
before copying footprints. One exclusive coordinator merges after parallel joins;
other jobs still receive only owned patches. It passes 32 release tests and both
224-case/8440-publication official differential suites. Its buffer bound is
unchanged. [V2 timing and game acceptance remain pending](../../conformance/results/linux-744825-pressure-tiles-v2.json).
These proposals are alternatives against their recorded baseline, not a patch
stack; combining candidates needs fresh validation.

The [V3 proposal](../../conformance/proposals/pressure-tiles-v3.patch) removes
global level joins with scoped workers and a bounded grid-DAG coordinator.
It passes 36 release tests and both full official differential suites. Its
complete eight-pair timing still regresses, so it is rejected. A separate
[copy-cost premise check](../../conformance/results/linux-744825-pressure-copy-cost.json)
confirms that transporting complete CellSOA patches is not cheap on the tested
uniform fixture. The next design must remove cell-data copies; the dependency
proofs remain useful, but do not imply that the copied executor is fast.

The [borrowed-row proposal](../../conformance/proposals/pressure-borrowed.patch)
removes physics-cell copying using safe disjoint mutable slice fragments of the
original arrays. Its 33 release tests and both 224-case official differential
suites pass. However, the interrupted timing run still shows substantial CPU
overhead in completed pairs; it remains unadopted. See the
[source-bound partial receipt](../../conformance/results/linux-744825-pressure-borrowed.json).
The abstract footprint proofs cover the schedule model, not the concrete borrow
planner, compiled arithmetic or notification merge.

## Notification membership

`NotificationMembership.lean` proves five properties of an abstract seen-set:
clearing is sound, recording preserves membership and soundness, raw appends
preserve soundness, and repeated recording retains exactly the union of old
and incoming cell IDs. This justifies suppressing duplicates only while a
retained record for that cell is known to exist. It does not prove Rust bit
indexing, allocation, aliasing, validation errors, or final sorting. The private
Rust candidate is not adopted on the strength of this model alone.
