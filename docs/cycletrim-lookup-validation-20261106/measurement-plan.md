# CycleTrim lookup optimization measurement plan

Fixed before candidate measurements on 2026-11-06.

## Scope and baselines

- Repository baseline: `a6cfb9bfc581e48da6e14d23e8a5c1941bfd74e4`.
- Mechanism reference: `tigin-backwards/oni-moono@6ac665ce05e97ad27bbb106b4de91c69263863e5`.
- Production candidates: invocation-local save metadata reuse and bounded,
  mutation-aware state-machine lookup reuse. Implementations are independent;
  no Moono implementation is distributed.
- Save comparison: the existing metadata-caching transpiler versus the new
  invocation-local cache, on the same complete synthetic two-pass save method.
- State-machine comparison: the original synthetic method versus the complete
  production Harmony patch. Both use the same ordered type-assignability search.

## Correctness and memory requirements

Require identical serialized bytes or returned object identities, original
exception behavior, successful option wiring and the complete existing host
suite. Exercise list mutation/replacement, first-match ordering, negative
queries, custom reflection types, worker calls, weak lifetimes and patch changes.
Reference assemblies are only for static inspection and compilation.

Warmed save paths must not allocate more than the existing patch. Warmed cache
hits must not allocate. Separately report state-machine initialization cost and
cache capacity; cold initialization must not be hidden in a warm-memory claim.

## Fixed workload matrix

Save: 64 mixed component types (four types rotating), 64 repeated components,
64 components with seven of every eight skipped, empty and single-component saves.
Include the complete two-pass fixture and its writes. Use 20,000 saves per large
batch and 200,000 per short batch; 20 warmup batches of 2,048 saves per variant.

State machines: 64-entry last-match hit, 64-entry miss, 64-entry first-match hit,
4-entry short list, a mutation before each query and a newly created owner before
each query. Include a rotating query set larger than the fixed 16-type cache
capacity. Keep the first 16 cached types until the list changes; additional types
use the original search. Existing tiny/fast paths remain explicit adverse controls.

## Timing protocol

Use SDK 10.0.401 / .NET 10.0.12, Linux x64, Release, with the same compilation
settings for both variants. Pin each process to one allowed CPU. Run three fresh
processes in each of two modes: runtime defaults and tiered compilation disabled.
Each process uses at least 20 warmup batches followed by seven alternating
baseline/candidate pairs per workload. Check equal outputs outside timing and
retain every pair, elapsed time and allocated-byte count.

Report medians of paired elapsed-time ratios (baseline / candidate; higher is
faster), per-process results and pair ranges. Inner pairs share a process and are
not independent cross-process replications. No cross-machine or cross-round
division is used to claim incremental improvement.

Target: measured repeated/mixed save and long repeated state-machine workloads
should improve over their named baselines; all adverse controls remain visible.
Do not change workload sizes, cache thresholds or success criteria after seeing
timings. Correctness and allocation requirements are gates; shared-host timing
is reported with its variation and does not substitute for Unity/Mono testing.

## Runtime boundary

The state-machine option stays off by default pending a Steam-launched ONI run.
Reference binaries declare build 737790, while the pinned method-body source
declares 744825. Host timing is not a game FPS or save-pause measurement. Actual
Unity/Mono, save roundtrip and mod-combination acceptance remain separate.
