# PR #558 continuation: fixed validation plan

Recorded on 2026-10-30 before measuring the continuation candidate.
Submitted baseline: `7aa5003c23cd41a385664e684c3debf918a798a2`.

## Changes under evaluation

1. Guard the transformer circuit-resolution dependencies which the power
   overlay query actually calls. A Harmony patch on one of these dependencies
   must preserve original per-query results and callback execution by disabling
   reuse. Verify with an observable patched dependency, not only a guard flag.
2. After an animation setter has found a changed field, execute its remaining
   original writes without further old-value comparisons. Keep exact floating
   bits, partial writes, exceptions, dirtying and failure recovery.
3. Avoid a top-level conveyor scope when the candidate cannot meet the existing
   index admission rule. Two full searches plus four remaining searches require
   at least six candidate rails. Nested short calls still isolate and invalidate
   their parent. The original index admission constants are unchanged.

## Correctness and build gates

- Run the existing real-Harmony synthetic suites and extend them for the above
  observable behavior, including short nested conveyor calls which mutate a
  retained path and throw.
- Confirm the new power regression fails with the submitted compatibility
  implementation and passes with the correction.
- Preserve the 2,059 conveyor differential cases and all previous animation,
  save and power checks. Run the settings/probe suite and complete managed
  reference compilation separately.
- Run `git diff --check` over the entire PR range. Record the exact submitted
  candidate and CI evidence; an earlier SHA's success is not a new pass.

## Performance protocol

- Reuse .NET SDK 10.0.401 / runtime 10.0.12, Release, Linux x64, CPU 0 on the
  shared virtual host. Run benchmark processes sequentially.
- Retain the original conveyor scenarios and animation scenarios. Add changing
  ranges of 4 and 256 symbols and a 256-symbol range whose last field alone
  changes. These expose both early-exit benefit and the worst comparison case.
- Keep three warmups, seven alternating baseline/candidate pairs and the
  existing batch sizes. Check exact output bytes, checksum, allocation and copy
  counts. Never discard a slow or noisy sample because it is inconvenient.
- Report medians, ranges and paired ratios. Each built-in ratio compares the
  unoptimized fixture with that binary's production helper. Do not divide
  measurements from different rounds and call that a measured improvement.
- Keep all original controls, particularly fully unchanged wide ranges, where
  the extra state branch may itself cost time. Do not tune thresholds to these
  measurements. Timing supports a bounded workload claim, not a game FPS claim.

## Acceptance boundary

Power, conveyor and animation remain restart-required, default-off settings.
Reference builds validate API compatibility; host fixtures validate their own
executed behavior. Steam-launched save/load, overlay, rail and animation checks
on the candidate commit remain the runtime acceptance gate.
