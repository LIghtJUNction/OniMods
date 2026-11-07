[h1]CycleTrim[/h1]

[b]Lightweight Performance & Frame-Time Optimizer for Oxygen Not Included[/b]

Surgically targets Oxygen Not Included's heaviest late-game simulation bottlenecks to eliminate redundant work and reduce stutter, while keeping 100% of vanilla gameplay and simulation rules intact.

[h2]Required Prerequisite: Jlib[/h2]
This mod requires [b]Jlib[/b] (Steam Workshop ID: [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]3835083839[/url]).
Please subscribe to Jlib and place it above CycleTrim in your in-game Mod Manager.

[h2]Core Optimizations[/h2]
[list]
[*] [b]Busy Duplicant Chore Throttling[/b]: When duplicants are engaged in long, uninterruptible tasks (such as researching, long digs, or operating machines), CycleTrim suspends redundant every-tick scans of the global chore list.
[*] [b]Stationary Critter Navigation Pruning[/b]: Significantly reduces full-colony NavGrid probes for idling ranch critters, alleviating pathing hotspots in high-density stables.
[*] [b]Debris Pickup Optimization[/b]: Optimizes spatial filtering and path cost reuse when tens of thousands of items litter the floor, mitigating pickup evaluation spikes.
[*] [b]Save Metadata Caching[/b]: Uses fast local cursors during autosaves instead of repeated reflection lookups, markedly reducing morning save freeze times.
[*] [b]Native OniSim Engine Core[/b]: Ships with a clean-room Rust simulation engine for Windows, Linux, and macOS, utilizing modern SIMD vectorization.
[/list]

[h2]Design & Compatibility Guarantees[/h2]
[list]
[*] [b]100% Vanilla Behavior[/b]: Never alters dupe priorities, schedules, stats, speeds, or game balance.
[*] [b]Ecosystem Friendly[/b]: Supports the Base Game and Spaced Out! DLC. Gracefully detects and coexists with popular mods such as FastTrack and Plib.
[*] [b]Zero-Allocation Hotpaths[/b]: Hot execution paths are engineered to eliminate managed heap allocations, reducing GC pause spikes.
[/list]

[h2]Configuration[/h2]
Individual optimization subsystems can be configured in the in-game Mod Options menu. Changes apply on next launch.

[h2]AI Disclosure[/h2]
Algorithms, transpiler patches, and benchmark suites were developed with assistance from frontier AI models including GPT-6, Astra, and Opus 5.5. Documentation refined by Gemini.

[h2]Source Code[/h2]
[url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/CycleTrim]GitHub Repository[/url]
