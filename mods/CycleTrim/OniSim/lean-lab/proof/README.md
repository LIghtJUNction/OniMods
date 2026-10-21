# Lean proof for the disease-emitter search

The proof checks that a direct row scan visits the same cells as a finite
breadth-first reference model on an unobstructed rectangle. It also checks
that both traversals produce the same result for arbitrary cell-local updates.

## Reproduce

Use the pinned Lean version in `lean-toolchain`: **Lean 4.19.0**. Only the
bundled `Std` library is required; Mathlib and network access are not required
for checking the proof once Lean is installed.

```sh
bash verify.sh
```

If Lean is installed outside PATH:

```sh
LEAN_BIN=/absolute/path/to/lean bash verify.sh
```

The script checks the version, compiles the proof with warnings treated as
errors, prints the logical dependencies of every theorem, and checks that
the report covers all theorem declarations. It writes the actual outputs to
`lean-version.txt`, `compilation.log`, `axioms.log`, and `verification.json`.
An empty `compilation.log` means the compiler emitted no warnings or errors.

The proof source is `SimDiamond.lean`; `ProofChecks.lean` prints the audit
information. `lake build` was also checked successfully. The `lakefile.toml`
contains no external package dependencies.

## Model and parameter mapping

| Proof value | Rust value or condition |
|---|---|
| `r : Nat` | Original `max_depth`; visits require `depth < r` |
| Geometric radius | `r - 1` when `r > 0`; zero depth visits no cells |
| `c.x`, `c.y` | Start coordinates after the original `as u16` conversion |
| `b.xmax` | `min(stride, 65536) - 1` as a mathematical integer |
| `b.ymax` | `min(padded_height, 65536) - 1` as a mathematical integer |
| `Legal b p` | Both coordinates are at least 1 and at most their inclusive upper bounds |
| `checkedScan` | Reject an illegal converted center, then enumerate clipped row intervals |

For positive `r`, row `y` has half-width `r - 1 - abs(y - c.y)`. Its inclusive
column interval is clipped to `[1, b.xmax]`. Rows are clipped to `[1, b.ymax]`.
The Rust implementation uses the equivalent half-open upper bounds.

The coordinate graph has four edges per interior cell and no obstacles. The
zero-coordinate border is rejected. In the implementation, `65535 + 1`
wraps to zero in `u16` arithmetic and is rejected too; the proof therefore
models an ordinary rectangle, not a wraparound grid. The integer-to-`u16`
conversion itself is a separate implementation bridge, checked by the Rust
boundary and differential tests.

## What is proved

1. **Shortest paths.** Every legal walk has length at least its Manhattan
   distance. For legal endpoints, a walk of exactly that distance exists and
   stays inside the rectangle. The proof constructs a legal step towards the
   target and inducts on the remaining distance.
2. **Reachability.** A cell is reachable in fewer than `r` steps exactly when
   it is legal and its Manhattan distance from a legal center is below `r`.
3. **Row intervals.** The clipped row-interval predicate is equivalent to
   the distance predicate. Actual list membership in the executable `scan`
   definition is proved equivalent to those intervals.
4. **Uniqueness.** Every integer range, row, and completed scan is duplicate
   free. `checkedScan` is also duplicate free and returns an empty list for
   an illegal center or a zero depth bound.
5. **Breadth-first reference coverage.** `frontier` recursively enumerates
   endpoints at exactly one depth. `bfs` concatenates all depths below `r`
   and keeps first occurrences. Coverage and uniqueness are proved from
   these definitions. No BFS-coverage statement is assumed.
6. **Same updates.** Both visit lists give the same final state for every
   function of type `Cell → Value → Value`, with arbitrary per-cell values.
   This covers changing a tuple of fields in one cell while reading fixed
   emitter/table data. It does not cover functions that read or write other
   cells or a shared mutable accumulator. Emitter order remains unchanged
   in the Rust change.
7. **Index uniqueness.** If both x coordinates lie in `[0, stride)`, equality
   of `y * stride + x` implies equality of both coordinates. This rules out
   index aliasing within the mathematical model.

The three principal declarations are `reach_iff_diamond`,
`bfs_checkedScan_same_cells`, and `bfs_checkedScan_same_updates`.

## Proof boundary

This is a proof of an explicit algorithm model. It is **not** a proof of the
proprietary Klei SimDLL, the Rust compiler, the original Rust queue's memory
operations, floating-point physics, or safe `usize` multiplication under
arbitrary invalid allocations. The reference BFS deliberately retains all
paths until deduplication to keep the proof simple. It is a finite,
executable specification, not the performance baseline used in measurements.

The connection to the implementation consists of the source review, the
documented coordinate mapping, and actual Rust differential tests. Measured
timings must come from the compiled Rust baseline and candidate, not from
the Lean reference enumerator.

## Logical dependencies

The proof uses no incomplete proof placeholders, no new axioms, and no
native evaluation tactic. Its recorded dependencies are Lean's standard
`propext`, `Classical.choice`, and `Quot.sound`; individual theorems may use
fewer or none. `axioms.log` records each theorem separately. This is a
machine-checked proof under Lean's usual logical foundations, not an
assertion that the proof is independent of every logical axiom.
