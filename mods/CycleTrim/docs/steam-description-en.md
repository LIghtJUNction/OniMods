[h1]CycleTrim[/h1]

[b]Lightweight performance optimization mod for Oxygen Not Included[/b]

[h2]v0.3.1 fix[/h2]
CycleTrim previously treated any non-null `currentChore` as active work. ONI keeps a non-null `IdleChore` while a duplicant is idle, so the mod also throttled pickup and chore refreshes for idle duplicants.

v0.3.1 excludes `IdleChore`. Idle duplicants use the vanilla immediate refresh path; active workers keep the throttle.

If a current ONI build or another mod rewrites `Manager.TickFrame()`, CycleTrim now skips the async path-probe quota patch instead of throwing and disabling the whole mod.

[h2]What this mod changes[/h2]
CycleTrim removes repeated work from six high-frequency simulation paths. It does not change duplicant priorities, schedules, path rules, or chore conditions.

[h2]Key optimizations[/h2]
[list]
[*] Smart reservoirs: reduce repeated automation signal emissions.
[*] Pickup candidate lookup: cache candidate cost computation and candidate ranking prep.
[*] Busy duplicants: throttle heavy refresh work for active workers; `IdleChore` explicitly bypasses throttling so idle workers retain vanilla responsiveness.
[*] Stationary critters: reduce repeated `Navigator` probe work and navigation hotspot pressure.
[*] Ordinary Creature Brain scheduling: cap vanilla Creature normal scheduling at high render rates; Dupe, custom Brain-group, and priority paths remain vanilla.
[*] AsyncPathProber: deduplicate successfully applied vanilla Creature snapshots and apply worker/in-flight backpressure; Minion, Robot, and custom abilities stay vanilla.
[/list]

[h2]Compatibility[/h2]
Supports the base game and DLC. Duplicants, robots, custom Brain groups, and priority paths use vanilla code whenever a patch does not apply.

[h2]Performance[/h2]
[list]
[*] Busy duplicant throttle: median FPS 70.6 → 85.1 (+20.6%); Brain total time 5.087 s → 2.449 s (-51.9%).
[*] Stationary critter throttle: median FPS 81.23 → 109.28 (+34.5%); Navigator hotspot total 826.852 ms → 47.970 ms (-94.2%).
[*] UpdatePickups cache: average cost 41.279 µs → 35.708 µs (-13.5%).
[*] Ordinary Creature Brain synthetic benchmark: scheduled calls 43,200 → 19,200 (-55.56%).
[/list]

[b]Note: the Brain figures are from a standalone 240 FPS × 30 s function-level synthetic benchmark, not an in-game FPS test.[/b]
Creature calls were 36,000 → 12,000 (-66.67%); local median elapsed time was 255.580 ms → 113.289 ms (-55.67%, 2.26x). Actual gains depend on the colony and real Brain workload.

[b]The PathProbe matrix is also synthetic, not FPS:[/b] at requested 0/25/50/75/90% hit rates, 10,000 requests executed 10000/7701/5501/3301/2000 probes, for theoretical work reductions of 0/22.99/44.99/66.99/80.00%. A refresh is forced after eight consecutive skips.

[h2]Details[/h2]
[url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md]Full details [English](/url)
