# CycleTrim follow-up measurement plan

Recorded before running the new benchmarks on 2026-10-30.
Baseline: `a8e21b65b71519f00e6fca4386199cafe0b640d6`.

## Questions

1. Does a temporary conveyor index reduce repeated rail comparisons while
   preserving the original path order, first index, removals and query ordering?
2. Does animation field comparison preserve all output bits and partial-write
   errors while reducing redundant version-triggered buffer copies?
3. What do the extra checks cost when there is little or no redundant work?

## Correctness gates

- Run the production helpers and installed Harmony patches, using independent
  synthetic fixtures with the required public signatures.
- Compare original and candidate outputs, exception types and relevant side
  effects. Animation version suppression is an explicit intended change; compare
  bytes delivered to a consumer that tracks object identity and version.
- Include malformed/empty inputs, same-length edits, duplicates, nested calls,
  failure recovery, floating-point bit patterns and patch incompatibility.
- Keep reference compilation, source contracts, host execution and unavailable
  Steam execution as separate forms of evidence.

## Measurement protocol

- .NET SDK 10.0.401 / runtime 10.0.12, Release, Linux x64.
- Run one benchmark process at a time, pinned to one allowed CPU; record that
  this is a shared virtual host rather than an isolated physical machine.
- At least three warmups of the exact measuring method. Measure seven pairs,
  alternating baseline/candidate order by pair; exclude patch installation.
- Record each elapsed time, allocation count and result checksum, plus the
  workload's deterministic work counts. Reject pairs with unequal outputs.
- Report medians, ranges and the median of paired baseline/candidate ratios.
  Do not equate these synthetic times with game FPS or actual rebuild time.
- Conveyor controls include long paths with repeated searches, early matches,
  a single query and short paths. Preserve the predefined index admission rule:
  32..4096 entries, at least two path lengths already scanned, at least four
  remaining queries. Do not tune these constants to hosted-runner noise.
- Animation controls include unchanged and changing fields, one-symbol and
  range setters. Separate setter-only cost from version-consumer buffer-copy
  cost; use no invented integer busy loop or synthetic query allocation.

## Adoption decision

Require exact results and a deterministic reduction of the targeted redundant
work. Timing is supporting evidence, not a flaky pass/fail threshold. Retain
small/no-reuse controls and disclose regressions. Both new groups remain
independent, restart-required opt-ins until representative in-game measurements
establish their practical benefit.
