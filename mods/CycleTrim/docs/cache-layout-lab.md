# Experimental fetch candidate layout

The opt-in lab compares a dictionary holding full pickup values against a compact
key-to-index dictionary that writes winners into the existing result list. It
keeps ranking, first-wins ties and per-call lifetime. No cross-tick cache or
game-state spawning is added. Ordinary launches select baseline.

Launch through OniHarness with `--game-arg=--cycletrim-layout-control=/absolute/layout.ctrl`.
The control contains `baseline REQUEST_ID` or `compact REQUEST_ID`; `.ack` contains
PID, layout, request ID and both call counts. The opt-in controller polls at 4 Hz
on the game thread. No controller is created for normal launches.

The generic production helper is linked into the existing BrainBenchmarks project.
It checks selected identities, ties/NaN freshness, colliding hash keys and reuse.
`--candidate-layout-benchmark baseline|compact KEYS ITERATIONS` has READY/GO and
DONE/REPORT handshakes so external PMU measurement excludes warmup and reporting.

Use OniHarness's `measure_candidate_layout.py` for alternating synthetic runs and
`measure_fetch_layout.py` for guarded native steps. Source/host measurements are
not game FPS. The native continuation changes state and only its main thread's
user-space generic hardware events are counted. A small three-duplicant fixture
does not establish a late-game speedup. Keep the compact variant experimental
until representative runtime evidence supports adoption.
