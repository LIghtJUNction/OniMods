# OniSim status (2026-10-21)

## Current development source

The canonical development native is now
`2772e52680f74a953a8fd18be396e2718ea1ccba1b7dad4fa738bc9cb98dc76f`.
The pressure sweep borrows fixed-length storage once, retaining checked indices,
original floating expressions, serial transfer/notification order and padded-edge
projection. It adds no heap storage or world copies. The former transfer caller
has been migrated and removed; shared projection logic serves both notification
paths. Transfer/cell source bodies match the baseline after adapter-name changes.

All 43 release Rust tests and 50 game-free Python tests pass. Twenty-four new
raw-flow-word fixtures include signed zero, subnormals, infinities and quiet/
signaling NaNs. Both the baseline and new core pass the expanded 287 cases /
9739 publications at one and four workers. An independent build from committed
base e0e0fe7 plus the archived proposal reproduces the exact measured native.

Incremental complete-frame tests against 8bd415ce on CPU0/2/4/6 with four workers
pass the unchanged CPU/wall/RSS gates at both sizes: 96x96 has 96 samples and
192x192 has 60. CPU medians fall 2.56-16.31% in the five nonthermal 96x96 scenes
and 5.15-13.48% at 192x192. Thermal increases 0.70% / 2.50%, below the 3% limit.
All final states match official and no timed major faults occur. A separate
24-sample PMU diagnostic observes whole-process instructions -13.38% fluid /
-12.36% mixed-germs, cycles -8.19% / -8.20%; physics instructions fall about
13.7-13.8%. Host load/frequency dispersion and fixture scope still apply.

The isolated 5eb76a3 package has ZIP bd0b2823, managed d0437d55 and native
2772e526. A genuine Steam headless process loaded those exact bytes, the approved
disposable save, Null graphics and zero camera callbacks. Normal bounded progress,
save/load, clock rollback and post-reload progress passed; the original save is
unchanged. All four owned game/runtime processes, the supervisor and controller
exited normally. Actual worker-thread names were not captured, so complete
headless acceptance is still not claimed. Graphical, whole-game CPU/RAM and
four simultaneous-game acceptance remain pending. See
`conformance/results/linux-744825-pressure-sweep-{delivery,headless}.json`.

The first armed memory probe correctly refused at its freshness guard, but
investigation found a tooling defect: automatic JSON date parsing followed by
string conversion lost the UTC suffix and reparsed it in local time. The helper
now retains raw ISO strings. Forty-three production-helper checks in UTC,
Asia/Taipei and America/Los_Angeles plus 12 Python collector/mapping tests pass;
the fixed probe compiles against the pinned installed game. Its next launch was
refused by the unchanged 6 GiB single-game capacity gate. No Unity/Mono counter
capture has passed. The failure and new build remain separate, with no lowered
identity, freshness or capacity limits. See
`conformance/results/linux-744825-memory-probe-timezone.json`.

## Previous 8bd development source

The preceding development native was
`8bd415cea1129c5b7a1e94b08dc269e829f00c815d8c38d06b733437f8c2e0e0`.
Twelve native source files and the expanded thermal case registration were
integrated after checking the C301 source-archive ancestor and preserving
unrelated source bytes. An independent canonical build produces the exact
tested binary. All 41 release Rust tests and 49 game-free harness tests pass.
The canonical harness matches the frozen expanded run byte-for-byte: both one
and four workers pass 263 cases / 9571 publications, including 32 new thermal
wire-word cases. The 12 memory-collector protocol/mapping tests are separate.

The new cases exposed existing cell/backwall MINSS operand-order errors and a
backwall MINSD cap/clamp order error. C301 and intermediate candidates failed;
the corrected binary passes. A fresh one-worker direct official comparison passes all
predeclared native CPU/RSS gates. CPU median reductions are 3.452% fluid,
20.683% thermal, 9.286% sealed, 1.356% germ-flow, 1.788% mixed-germs and 35.463%
vacuum. These are authored 96x96 native fixtures, not whole-game FPS. Retained
host-load/frequency dispersion limits conclusions about exact gain magnitude.

Fresh full-frame scaling tests on four distinct physical cores do not pass the
predeclared performance gates at either 96x96 (96 samples) or 192x192 (60 samples).
All final states still match the official library. Four workers reduce some
fluid/germ wall times, but increase CPU work and regress other scenes; see
PARALLEL-VALIDATION.md. Parallel correctness and thread creation do not establish
an overall performance win.

The private active-disease dispatch experiment `7762e676…` passes all 41 release
tests and 263 cases / 9571 publications at both worker counts. It is not adopted:
the 96x96 comparison reduces CPU use but regresses thermal wall time by about
18.8%, failing the original gate. The 192x192 run also fails: thermal wall time
increases 9.2% and sealed wall time 12.5%. All 156 timed final states still match
official; no timed major faults occurred. The experiment and rejection are in
`conformance/results/linux-744825-disease-active-dispatch.json`. The canonical
native remains `8bd415ce…`.

The follow-up checked-cleanup-slice experiment `6ad189b3…` also remains rejected.
It passes 42 release tests and both complete 263-case / 9571-publication runs,
but its 156 full-frame timing samples still fail the original elapsed-time gates.
See `conformance/results/linux-744825-disease-cleanup-slices.json`.
The common dispatch-removal premise was then tested directly on production
disease post-processing. Parallel clean-cell cleanup is faster than serial at
both sizes, so uninfected regions cannot be treated as having no useful parallel
work. The next optimization should reduce cleanup work while retaining useful
parallelism. The rerunnable test and limitations are documented in
`conformance/DISEASE-DISPATCH-DIAGNOSTIC.md`.

Two further SSE2 cleanup proposals preserve the pool and exclusive row ownership.
`368df124…` and the exact all-clear/all-retained branch variant `9dbb9536…` each
pass 45 release tests and both 263-case / 9571-publication suites. Bounds, tails,
all lane masks and float words are checked; the branch variant also checks three
input classes in each of 512 MXCSR environments. Neither is adopted: the first
fails both full-frame size gates; the branch variant passes 192x192 but fails the
original 96x96 mixed-germ limits (CPU +4.37%, wall +3.83%). All 156 timed samples
per variant are retained. Public proposals are archived; the branch proposal
independently rebuilds to the exact tested native hash.

A 24-sample CPU-core PMU diagnostic then counts actual warmed physics, pool and
orchestration threads. In sealed fixtures, pool instructions fall 39.85%, but
whole-process instructions fall only 1.11%; mixed-germ whole-process instructions
fall 0.15%. The serial physics thread accounts for about 90-97% of retired
instructions in these two scenes. Prioritize its hotspots for larger gains.
Counters do not override failed elapsed-time gates or establish whole-game FPS.
Receipts are `conformance/results/linux-744825-disease-cleanup-{sse2,sse2-fastpaths,counters}.json`.

A fresh canonical 8bd415ce profile covers all six 96x96 scenes, five repeats
per scene with four workers on CPU0/2/4/6. All 30 final states match official;
13,135 CPU-clock samples contain 12,402 native IPs with no loss/corruption.
Pressure transfer takes 27.90-39.21% of native samples in the five nonthermal
scenes. This supports examining repeated pressure-storage setup, not a claim
of precise stall or actual-game attribution. Instrumented timings are excluded
from performance acceptance. See
`conformance/results/linux-744825-canonical-frame-profile.json`.

A compact immutable element-property proposal `2390ab48…` targets serial phase
and pressure lookups. It keeps raw flow words, checked indices and the original
records; a shared-only record wrapper prevents stale derived properties. Four
new tests include the full u16 index domain, state/float words, cloning,
replacement and invalid indices. All 45 release tests and both complete
263-case / 9571-publication comparisons pass. It is not adopted: 96x96 thermal
CPU increases 7.45% and 192x192 vacuum increases 13.14%, failing the unchanged
full-frame gates. All 156 exact samples and live-host dispersion are retained.
The benchmark has six authored elements, not the full actual-game table. See
`conformance/results/linux-744825-element-fluid-properties.json` and its public
proposal. A separate 24-sample hardware diagnostic finds only 0.96% fluid and
1.05% mixed-germ whole-process instruction reductions (physics reductions
1.00% / 1.16%). The lookup removes little total work and is not the next large
performance target. Counters do not override the failed timing gates. The
canonical source remains unchanged.

The dedicated OniSim CI now also runs the complete production native crate's
Release unit tests without game files, alongside the emitter differential suite.
This closes a coverage gap; official-library and actual-game runs remain local,
separate acceptance levels.

The source includes checked common-length pressure slices, integer SIMD index
validation, phase-local contact reuse, conservative identity-copy elimination,
and the consumed PreparedStep token. The contact-cache and PreparedStep Lean
model sources are now under lean-lab with their source-identical prior checker
receipts; neither model proves compiled Rust/libm or game compatibility.

This identity has not yet passed actual-game headless or graphical acceptance;
older runtime receipts below do not approve it. The distribution ZIP has not
been promoted. Whole-game performance and four simultaneous complete games
remain unverified. See `conformance/results/linux-744825-thermal-minss.json`
and `conformance/results/linux-744825-thermal-minss-official.json`.

## Earlier accepted fixture binaries

The Rust core builds and is integrated into CycleTrim's Linux package. The old
36-compiler-error snapshot is obsolete. The notification-dedup V2 source is now
landed, with 31 passing release host tests. Its original isolated binary
`4e677de1…` passed ordered headless then graphical small-colony save/load,
positive progression, full CPU/GPU texture comparisons and actual screenshot
inspection. Both owned processes exited normally; the original save is unchanged.
See [the V2 runtime receipt](conformance/results/linux-744825-notification-dedup-v2-runtime.json).

Release builds now explicitly disable host compiler wrappers. Fresh builds in
two different source roots produce the same native SHA-256:
`c301cc3ead6bfe0edebf928b901f767c144a6dc143eb75cdf260ebff4288ba42`.
This identity has separate Steam headless and graphical acceptance for the
small fixture: four actual cell workers, normal save/load with clock rollback,
positive progression and full CPU/GPU texture comparisons before/after graphical
reload. The actual screenshot was inspected and its dimensions matched the
scaled physical window. All owned games exited normally. Exact package/source identities require their
separate delivery-validation.json receipt. Whole-game performance remains
unmeasured; historical results below retain their original binary scope.

The earlier C301 [official-versus-Rust suite](conformance/README.md) passed 227 cases
with one and four workers, comparing 9103 publications per run, including empty batches. This includes
224 positive contracts and three explicitly negative diagnostic cases. Data
comparisons retain exact float bits, every public grid/flow/CPU texture field,
all 29 ordered event lists, conduit outputs and current native save bytes.
The candidate hash and harness hashes are recorded in the compact receipts.

Coverage now includes building/chunk and mass/disease-component lifecycles,
conduit heat exchange, radiation emitter modes, sunlight, active regions,
authored native save versions 0 through 15, legacy thermal boundaries,
overlapping loads, rectangular world initialization/freeing, sublimation,
liquid offgas, partial melting and boiling, and session-reset/save roundtrips.
Repeated EndSave calls match the original idempotent release.

These comparisons exposed and fixed gas-swap direction timing, cooling-cell
equilibrium, backwall transfer/sentinels, retained emitted-mass output,
sublimation's flow field and capacity limit, partial-melt mass/heat/germ branches,
and restoration of the cooled neighbour when a partial transition cannot displace
its source. Conduit failures now reach the actual managed fatal callback rather
than only the extension status. Invalid temperatures fail before mutation;
continued execution after the original's fatal report is not claimed equivalent.

The three formerly missing enum messages are absent from both dispatch registries
in the pinned official library and are explicit compatible no-ops. Batched Load
prevalidates all records and commits in order. Prepare retains a publication
barrier per record and returns the final descriptor.

Earlier Steam-launched headless runs loaded a disposable colony, created four
Rust cell workers, advanced, saved, reloaded and advanced again. They apply only
to the binary identities in [RUNTIME-VALIDATION.md](RUNTIME-VALIDATION.md).
The earlier component/phase fixes passed a Steam-launched headless smoke run: four workers, positive native progress before/after save and reload,
equal reload clock/dupe count, zero cameras/crashes, and restored original files,
mod settings and continue-save preference. The source save hash was unchanged.
An earlier launch was refused while another OniOnline test was running; its
process was untouched, and the successful run began only after the game was free.
The previous export/readiness binary (b5b0bd4d860b9451399771e9a3b6ddf527b53b22119172b1ed15cb9e3457000f) has a Steam-launched headless receipt:
an ordinary non-sandbox early colony (only OniMcp in its saved mod footprint)
loaded with three duplicants, created four cell workers, advanced 4.484130859375
native seconds, saved/reloaded at the same clock, and advanced 2.71728515625
seconds after reload. The sampled cell matched at displayed precision; this is
not whole-grid runtime equivalence. Original files/settings and the source hash
were restored. Earlier larger normal-colony trials did not pass: one had severe paging/control timeout, another
reported a missing construction-material element during restore. The same-save, same-managed-DLL Rust-off comparison reproduced the construction
material assertion with the official library mapped and no Rust mapping. The
save records 22 mods, while the isolated set has three; custom elements/prefabs
are missing. Its content dependencies must be matched before it is a usable
compatibility fixture. Original files/settings were restored; source saves were unchanged.
The current 1f56c9b2 candidate now has separate Steam headless and graphical
small-colony acceptance: four workers, save/reload clock equality, positive
progress before/after reload, and five full native/Unity/GPU texture comparisons
both before and after graphical reload. Both owned processes stopped and
recorded files/settings restored. See
[the current runtime receipt](conformance/results/linux-744825-rust2024-runtime.json).
This does not extend acceptance to larger colonies or other mod combinations.

The source archive keeps the canonical repository-relative project layout and
original license/provenance files. Its separate clean-build receipt binds the
exact archive hash to compilation of both the native core and managed mod.
Compilation does not substitute for runtime acceptance.

This remains experimental, not 100% compatibility. Uncovered interaction and
payload variants, realistic multi-world/DLC saves, concurrent save/lifecycle
stress, broader texture scenarios and long colony runs remain open. SIM_DebugCrash now matches the explicit SIGINT/assertion-report contract in isolated children; Linux SYSINFO returns null like the pinned original. No incoming call to the legacy
three-argument gas-interaction helper was found in the pinned control flow;
that does not establish every interaction's behavior. Keep gameReady=false and
runtimeValidated=false. No whole-game FPS gain is established.

The Lean-proved disease traversal and bounded persistent cell-worker pool are
implemented; their separate evidence is in [PARALLEL-VALIDATION.md](PARALLEL-VALIDATION.md)
and [lean-lab/README.md](lean-lab/README.md).

The conformance driver now fixes the actual empty/nonempty batch sequence before
comparing data and verifies equal message counts. This exposed a polling race in
the earlier driver around the fifteen-Step flow reset; failed traces remain.
Readiness observation has a host test proving that it neither consumes a result
nor releases a batch. All 62 catalogue message IDs are now exercised, with entry
coverage kept separate from full behavior coverage.

Previous accepted native SHA-256: `1f56c9b2f0dc47f0db3e24015e6d927492d61cf9c9ade4f17c6bcf3e6c897c33`. The Linux system-info export now returns
null as the original does. The intentional DebugCrash export reproduces SIGINT
followed by the pinned fatal assertion if a handler returns. Both failed against
the previous binary and passed after the change. These are not colony success
claims. The prior null-backwall binary (d45cb10e) has generated-world graphical and
headless save/reload receipts; these do not approve later binaries; the current 1f56c9b2
small-colony acceptance is recorded separately. See the top of RUNTIME-VALIDATION.md
before interpreting its historical runs.

Worker lifecycle tests additionally cover full-queue rejection before command
capture, retained frame ordering, shutdown with an outstanding publication,
runner failure wakeup, abandoned leases and capture panic containment. Sixteen
fresh worker lifecycles process 30,720 queued frames in exact submission order.
These host tests do not replace concurrent actual-game save/lifecycle stress.

The allocation-only differential fixture additionally controls its realtime
seed. Both original `time()` and Rust `SystemTime` consume the same fixed
instant in isolated fixture children; monotonic deadlines keep running. Without
this control, even the same official binary differed between separate runs.
The driver rejects missing libraries before starting children or writing cases.
Nine game-free driver tests cover these controls, probe build identity,
full-buffer observation validation, field presence and input identity changes.
The clock shim is never
loaded into the actual game or included as a production native library.

A native survival-world generation trial exposed rejection of the legitimate
0xffff absent-backwall marker. The original setter retains it, the managed
BackwallIndexer returns null, and native serialization writes Vacuum/zero for
it. The port now retains this marker through commands/publication, accepts it
in backwall preflight, and avoids material lookup for an absent wall. Three new
reference fixtures cover clear/restore, repeated null writes and save/reload
(including stale mass/temperature at zero elapsed time). All failed against the
previous Rust binary and pass after the fix. The d45cb10e candidate generated a native
survival world with three duplicants and passed one full-buffer CPU/Unity/GPU
comparison for all five property textures. A separate headless run loaded a copy,
advanced 4.849925994873047 native seconds, saved/reloaded at identical clock
55.466583251953125 with three duplicants, and advanced another 2.883289337158203
seconds. An initial invalid sample was retained as a failed check; a second
intentional reload verified a valid observed cell at displayed precision. The owned
game was stopped to release memory and all recorded test settings were restored.

Eight asymmetric thermal fixtures cover insulation bytes 0, 127, 254 and 255,
opposite temperature gradients, unequal heat capacities and surface areas, and
save/reload. All pass with one and four workers. The comparison driver now rejects
extra as well as missing array/event fields, distinguishes absent fields from
explicit nulls, and refuses to issue a summary if a harness or native library
changes during the run. A replacement run removes its previous summary before
starting children, so a failed run cannot retain an earlier green receipt.

Nine topology/lifecycle fixtures cover overlapping regions on odd-sized grids,
reversed region order, two-world rectangle release/reinitialization and eight
successive native save/reload cycles. The last fixture exposed a liquid
convection rounding bug: a weighted f32 temperature can round below the cold
endpoint, where the port incorrectly selected the hot endpoint. The corrected
clamp retains the cold endpoint, matching ELF 0x6fe48..0x6fe51. A two-water-cell
fixture fails against d45cb10e and passes against 82460a88, as does the repeated
save/reload fixture; exact comparisons and failed traces are retained. That intermediate hash has no game acceptance. The later 1f56c9b2 build
retains the fix and has separate headless/graphical small-colony acceptance.

Complete native frame bottlenecks and rejected performance candidates are now
recorded in [PERFORMANCE.md](PERFORMANCE.md). The reusable frame benchmark
compares three scenes with alternating samples and exact official publications;
the SSE numeric helper is now adopted in source after 25 release tests and
one/four-worker differential runs. Fixed-performance-core frame timing improved
by roughly 2.6–7.8% across the three fixtures. No whole-game FPS gain is claimed.

The native crate and Lean validation harness now use Rust 1.99.1 / Edition 2024,
resolver 3 and explicit unsafe operations. Both dependency resolutions are at
the latest Rust-1.99-compatible releases (zero packages changed). Native release
tests pass 28 cases; serial/four-worker component checks each pass 65042 cases.
See `conformance/results/linux-744825-rust2024.json` for source/toolchain
identities, feature checks, warning scope and upstream Clippy feedback.
The compact Load plan uses 32 rather than 56 bytes per cell; six alternating
Load-memory pairs reduce sampled extra RSS by roughly 10.3 MiB in the specified
two-record fixture. No actual-game memory or current-build FPS gain is claimed.
Current differential coverage additionally includes zero-capacity thermal
endpoints, pressure boundaries and seeded mixed-pressure fixtures; all 224 cases
were rerun after the final thermal source change and native rebuild.

Nine mixed-state soak fixtures add 96 batches followed by four save/reload
cycles per seed/material combination. Current one/four-worker full suites each
pass 224 cases and 8440 publications. A prior serial child timeout is retained
separately, with a passing isolated full-suite recheck; its cause is unproven.

Three interleaved gas/liquid/solid/vacuum soak fixtures exposed a thermal f32
multiplication-order error: the original rounds the squared-insulation scale
before multiplying conductivity. All three failed before correction and match
exactly after correction, including four native save/reload cycles per scene.
The fixed source passes 31 release tests and 227 cases / 9103 publications with
one and four workers. See `conformance/results/linux-744825-mixed-thermal-order.json`.
The new native identity is c301cc3e. Both two- and four-worker Steam headless
instances passed normal save/load with positive progress before/after reload,
clock rollback equality and final pause. Four actual cell threads were observed
in the four-worker process. Both processes stopped normally. Graphical four-worker
acceptance subsequently passed, with five complete CPU/GPU texture comparisons
before and after reload and actual screenshot inspection. These runtime results alone do not approve
a ZIP or source archive; check their separate delivery validation receipt.

A private equal-temperature pressure proposal (`f1736fbb`, not adopted) passes
33 release tests, including 3,651,072 result-word/exception-flag comparisons in
32 MXCSR environments, and both one/four-worker suites (227 cases / 9103
publications each). It retains original arithmetic when strict bounds or the
preexisting masked/sticky-inexact environment do not permit skipping it.
Eight alternating native pairs per scene against c301cc3e measured germ-flow
CPU median -4.81%, below the predeclared 5% target. Other scene CPU changes
range from -2.21% to +2.79%; peak RSS changes are within 0.20%. All 96 sample
finals match official output. CPU frequency endpoints vary 1.40–2.33 GHz and
memory-pressure some/avg10 varies 0.19–9.46%; no stable whole-game speedup is
established. The gate remains failed and canonical native sources/packages are
unchanged. See `conformance/results/linux-744825-equal-temperature.json`.

A pressure-helper inline proposal (`9a04afbd`, not adopted) removes both disease
helper calls from the compiled pressure kernel while retaining safe indexing and
all arithmetic. It passes 31 release tests and the one/four-worker differential
suites (227 cases / 9103 publications each). Its six-scene timing preflight was
refused because another task started a real ONI instance; zero timing rows were
accepted. The foreign process was left untouched. See
`conformance/results/linux-744825-pressure-inline.json`.
Own-process userspace CPU sampling is now available without installing perf or
changing host permissions. Six native germ-flow profiles collected 4250 samples,
4025 in the native libraries, with no lost/corrupt records and exact official
finals. Corrected ELF load-bias normalization was checked against actual exported
Rust function pointers. Baseline update_pressure accounts for about 23% of native
samples and its outer disease helper about 10%; inlining moves helper samples
into its caller, so symbol percentages are not a speedup comparison. This is
fixture diagnostics with sampling bias, not whole-game performance evidence.
See `conformance/results/linux-744825-pressure-sampling.json`.

Four new mixed-phase active-region soak fixtures pass 308 publications per
selected run for both c301cc3e and the private 9a04afbd proposal with one/four
workers. They exercise separated/overlapping active regions, forward/reversed
region order across the 32-cell boundary and four save/reload rounds following
24 batches. This is a four-case supplement to the recorded 227-case suites;
it does not claim a fresh complete 231-case suite or actual DLC-colony coverage.
See `conformance/results/linux-744825-multi-region-soak.json`.

The pressure-inline 9a04afbd native benchmark subsequently completed after the
foreign game exited: 96 alternating samples, exact official finals. Relative to
c301cc3e, CPU medians changed germ-flow -6.147%, mixed-germs -11.119%, fluid
-7.089%, sealed -5.774%, vacuum -10.018%, thermal +3.155%. The thermal result
exceeds the unchanged 3% other-scene limit, so the proposal remains unadopted.
The separate private phase-barrier proposal skips only identity cell copies
when gas/liquid phases reached no CellSOA-mutating path, retaining all flow/event
work. Its abstract state-equivalence premise and eight Lean theorems are separate
from actual Rust guard and runtime validation; acceptance is still pending.

Phase-barrier V1 (098d360d) passes 31 release tests and fresh full expanded suites
with one/four workers: 231 cases / 9411 publications per run. Its 96 native timing
samples match official finals; all six CPU medians decrease, but germ-flow
-4.9319% misses the fixed 5% target. Thermal is -6.4305%, fluid -3.7189%, sealed
-3.9364%, mixed-germs -7.8161%, vacuum -10.1085%. Thus V1 remains unadopted.
See `conformance/results/linux-744825-phase-barriers-v1.json`. V2 refines the
conservative pressure marker to reduce bookkeeping and retains identical gates;
its validation is separate and pending.

Phase-barrier V2 (9cddf664) passes 31 release tests and complete 231-case /
9411-publication suites with one/four workers. Its 96 paired native samples vs
c301cc3e show germ-flow CPU -6.6547%, mixed-germs -8.0404%, vacuum -10.3347%,
fluid -3.1659%, sealed -3.3749%, thermal +1.0757%. The explicit thermal -5%
target fails; source remains unadopted. A separate 96-sample direct official
comparison is more relevant to replacement cost: vacuum CPU -37.6986%, sealed
-1.8453%, mixed-germs +1.3204%, germ-flow +3.3282%, fluid +5.5145%, thermal
+13.4461%. The unchanged maximum-3%-regression official gate fails. All sampled
finals match official output, but there is no whole-game FPS/CPU/RAM claim.
See `conformance/results/linux-744825-phase-barriers-v2-official.json`.
Three additional thermal CPU profiles collect 641 samples without lost/corrupt
records and retain exact official finals. Native code contains 484 samples;
update_temperature accounts for 34.09% of native samples. libm contains 19.50%
of total samples. Sampled instruction locations also cluster after packed f64
division; software sampling cannot precisely attribute stall cycles to an opcode.
Investigate heat exchange and repeated conductivity math before more copying
micro-optimizations; actual-game CPU/RAM profiling remains required. See
`conformance/results/linux-744825-phase-barriers-v2-thermal-profile.json`.

A new phase-local single-contact cache (0631b3dd), separate from the two older
rejected 32-entry caches, passes 34 release tests and complete 231-case /
9411-publication one/four-worker suites. Actual-helper tests compare 955776
result/flag/errno observations in 32 floating environments, including 387072
cache hits. The cache is <=32 stack bytes with no extra heap or World field.
Five Lean bit/effect replay theorems pass under explicit libm/guard assumptions;
they are not a compiled floating-point proof. The first NaN code-generation
failure and unavailable-SAT proof attempt are retained separately.
Native timing versus 9cddf664 reduces thermal CPU median 22.168%, but mixed-germs
+3.526% and vacuum +3.882% fail the fixed 3% other-scene gate. Keep unadopted.
A separate three-pair CPU16 hardware-counter diagnostic, with exact official
finals and >=98% counter running/enabled, finds cold-scene retired-instruction
changes within 0.07%; cycle changes are -0.94%..+2.48%. This does not replace or
relax CPU-time acceptance, but contradicts substantial added cold-loop work.
The initial generic PMU returned zero on this hybrid CPU's atom core and was
correctly rejected; valid events are selected from the pinned CPU's sysfs PMU.
See `conformance/results/linux-744825-thermal-contact-one-entry.json`.
The frame engine already runs preflight before its fixed-tick loop, while each
simulation_step::step repeats it. Investigate that duplicate work and its
invariants before another cache/cold-loop tweak. Whole-game CPU/RAM, broader
colony compatibility and four real overlapping instances remain outstanding.

PreparedStep (7811921b) removes only the duplicate frame/first-substep preflight.
Its private borrowed, consumed token prevents mutation between validation and
execution; every subsequent mutated substep still validates anew. 37 release
tests pass, including one-check count, later invalid-state rejection and invalid
time rejection without advance. Reintroducing the duplicate fails the exact
count test (observed2 expected1); correct sources were restored and all tests
rerun. Three Lean theorems establish pure duplicate-check/time-guard/later-check
preservation without any step-preserves-valid-state assumption. Both full
231-case /9411-publication one/four-worker suites pass.
A separate short-frame workload uses 50 individually published0.2s frames per
block, two warmup and six timed blocks. Bulk pilots were rejected because native
async grouping returned30 of50 submitted frames; the corrected strict barrier
protocol matches official finals. Eight alternating pairs per scene give thermal
CPU -4.9443%, below the unchanged5% target; other scene medians remain within3%
regression. The retained original long-frame workload also completes96 exact
samples with no scene CPU median regression above3%. The proposal remains
unadopted; those native results do not establish whole-game performance. See
`conformance/results/linux-744825-prepared-first-step.json`.

PreparedStep (7811921b) subsequently passed one actual Steam-launched headless
three-duplicant save/load roundtrip. The actual managed DLL and mapped native
library hashes matched the staged inputs. Three bounded steps advanced the
native clock; reload restored the exact checkpoint clock and final pause.
Five guarded speed-1 measurements completed after one warmup, with median
0.660 CPU seconds per native second, but 1,705–5,806 major faults per sample.
There is no matched baseline or maximum-throughput/FPS result. The first
measurement attempt failed because companion telemetry had not yet published
MCP's paused state; its failure remains retained. The corrected collector waits
for the same process, save, exact native clock and paused state.
The observed owned-tree PSS plus SwapPSS peaked at 4,325,855 KiB across 123
valid samples. Swapping pages out does not reduce this footprint. The private
capacity forecast was raised from 3 to 5 GiB per complete game, plus a 1 GiB
host reserve. After a normal quit all five owned processes and the idle
controller were reclaimed; the original save hash was unchanged. Graphical
admission was refused before launch as available memory fell to about 2 GiB.
Four simultaneous complete games and graphical acceptance remain unverified.
See `conformance/results/linux-744825-prepared-first-step-runtime.json`.

An isolated SIMD validation experiment retains every per-step check and error
order, replacing only the element/backwall/disease slice predicates with
integer SIMD and scalar tails. It uses no extra heap allocation. Rust 1.99.1
release builds pass 40 host tests, including exhaustive word/count boundaries
and vector tails/unaligned slices, plus all 231 official-comparison cases with
one and four workers. The first invocation accidentally selected the host's
default Rust 1.99.0 from the repository root; its passing results remain separate.
The explicit 1.99.1 rebuild has native SHA-256
`268977a5b35f167a77c29ab5fca27b0356feaecacf00c4e167e64603de4374ba`.
The six-scene CPU16 eight-pair benchmark against 7811921b completed with all
96 final snapshots matching the official library and no timed major faults.
CPU medians fell 5.518% fluid, 2.489% thermal, 7.091% sealed, 0.374% germ-flow,
3.041% mixed-germs and 1.021% vacuum. The predeclared incremental gates pass:
one scene improves at least 5%, no scene regresses above 3%, and peak RSS does
not increase above 3%. All rows, including a late host slowdown, are retained.
This is relative to another experimental Rust variant; no source adoption,
whole-game speedup or runtime acceptance is claimed for this new variant. See
`conformance/results/linux-744825-simd-validation.json`.

The separate direct official-library comparison also completed 96 exact final
snapshots. CPU medians changed +3.235% fluid, -13.010% thermal, -7.404% sealed,
+0.373% germ-flow, +1.217% mixed-germs and -36.861% vacuum. Fluid still exceeds
the fixed 3% regression limit, so the combined proposal remains experimental.
Improvement over another Rust variant must not be presented as superiority to
the official implementation. See
`conformance/results/linux-744825-simd-validation-official.json`.

Direct userspace hardware-counter diagnostics for 268977a5 show fluid retired
instructions +8.072% and cycles +3.273% versus the official library. Five
candidate fluid profiles collect 2,871 total / 2,754 native samples without
loss/corruption; gas::update_pressure accounts for 41.939% of native samples.
The hottest sampled IP follows the weighted-temperature DIVSS. This is not
precise stall attribution or whole-game evidence. See
`conformance/results/linux-744825-fluid-pressure-diagnostic.json`.

A masked-environment paired-division experiment retains the original scalar
function body for unmasked callers. Its first intrinsic implementation produces
equal result words but an extra invalid MXCSR flag in a boundary test; that
failure and source are retained. Opaque DIVPS with neutral upper lanes passes
3,030,272 word/control-flag observations in 32 environments, all 41 release host
tests, and both 231-case official comparison suites. Nevertheless, eight-pair
CPU16 measurements regress fluid +1.817%, germ-flow +9.563% and mixed-germs
+9.752%. The proposal is rejected, not adopted. All 96 exact snapshots and noisy
host intervals are retained. See
`conformance/results/linux-744825-pressure-packed-divisions.json`.

The separate, opt-in MemoryProbe now compiles against the pinned installed game.
It reads Unity allocation/reservation, Mono heap/used bytes, managed-reported
bytes, runtime-specific GC generation counts and Linux PSS/swap counters without
requesting GC or asset unloading. Its collector binds the loaded DLL, request,
native process incarnation, run nonce, paused save and actual native clock.
Five collector protocol tests and 13 existing instance-helper tests pass;
these do not exercise Unity/Mono. A four-profile group with probe 6517b5f5 and
native 268977a5 is prepared at /tmp/ct-mem-268v2, with zero games started. The
preparation helper defaults to a 5 GiB per-game forecast plus 1 GiB reserve.
The last host snapshot remains below the resulting 6 GiB single-game budget.
Repeated-load and official-library memory attribution, graphical acceptance and
four simultaneous complete games remain open. See
`conformance/memory_probe/README.md` and
`conformance/results/linux-744825-memory-probe-preparation.json`.
