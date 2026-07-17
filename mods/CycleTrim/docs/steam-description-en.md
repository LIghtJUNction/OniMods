[h1]CycleTrim[/h1]

[b]Lightweight performance optimization mod for Oxygen Not Included[/b]

[h2]Purpose[/h2]
[list]
[*] Reduce duplicated work on high-frequency simulation paths to lower CPU pressure.
[*] Keep behavior compatibility as the default priority and focus on long-run stability.
[*] Deliver measurable performance gains with minimal gameplay side effects.
[/list]

[h2]Key optimizations[/h2]
[list]
[*] Smart reservoirs: reduce repeated automation signal emissions.
[*] Pickup candidate lookup: cache candidate cost computation and candidate ranking prep.
[*] Busy duplicants: throttle heavy refresh work for active workers; idle workers remain responsive.
[*] Stationary critters: reduce repeated `Navigator` probe work and navigation hotspot pressure.
[/list]

[h2]Compatibility[/h2]
[list]
[*] Supports base game and DLC content.
[*] Focused on preserving vanilla behavior for critical gameplay systems.
[/list]

[h2]Performance[/h2]
[table]
[tr]
[th]Scenario[/th][th]Metric[/th][th]Before[/th][th]After[/th][th]Improvement[/th]
[/tr]
[tr][td]Busy duplicant throttle[/td][td]FPS median (CPU hot-path scene)[/td][td]70.6[/td][td]85.1[/td][td]+20.6%[/td][/tr]
[tr][td]Busy duplicant throttle[/td][td]Brain total time[/td][td]5.087 s[/td][td]2.449 s[/td][td]-51.9%[/td][/tr]
[tr][td]Stationary critter throttle[/td][td]FPS median[/td][td]81.23[/td][td]109.28[/td][td]+34.5%[/td][/tr]
[tr][td]Stationary critter throttle[/td][td]Navigator hotspot total[/td][td]826.852 ms[/td][td]47.970 ms[/td][td]-94.2%[/td][/tr]
[tr][td]UpdatePickups cache[/td][td]Average cost[/td][td]41.279 µs[/td][td]35.708 µs[/td][td]-13.5%[/td][/tr]
[/table]

You can run `benchmark` skill to let an agent execute the full benchmark and return standardized JSON results.

[h2]Details[/h2]
[url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md]Full details (English)[/url]
