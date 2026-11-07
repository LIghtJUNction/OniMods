[h1]CycleTrim[/h1]

[b]Conquer Late-Game Lag! Lightweight Performance & FPS Optimizer for Oxygen Not Included[/b]

As your colony reaches hundreds or thousands of cycles, does your FPS tank and every morning autosave freeze your screen?
[b]CycleTrim[/b] performs surgical optimizations on ONI's heaviest simulation bottlenecks. It eliminates redundant computations and significantly improves FPS while [b]keeping 100% of vanilla mechanics and gameplay intact[/b].

[h2]⚠️ Required Prerequisite[/h2]
This mod requires [b]Jlib[/b] (Steam Workshop ID: [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]3835083839[/url]).
[b]Please subscribe to Jlib and ensure it is placed ABOVE CycleTrim in your in-game Mod Manager.[/b]

[h2]⚡ Major Optimizations[/h2]
[list]
[*] 🏃 [b]Busy Duplicant Chore Throttling[/b]: When dupes are locked into long, uninterruptible chores (researching, digging, operating machinery), CycleTrim suppresses the redundant every-tick scan of the global chore list. Saves up to 50%+ chore CPU time!
[*] 🐾 [b]Stationary Critter Navigation Pruning[/b]: Ranch critters idling in pens no longer trigger excessive full-colony NavGrid probes. Cuts pathing hotspots by up to 90%+ in high-population ranches!
[*] 📦 [b]Debris Pickup Acceleration[/b]: Late-game colonies often have tens of thousands of items on the floor. Optimizes candidate filtering and path cost reuse to eliminate pickup micro-stutters.
[*] 💾 [b]Lightning-Fast Save Metadata[/b]: Replaces repeated reflection queries during autosaves with ultra-fast local cursors, slashing daily autosave lag by 1.5×~2×!
[*] 🦀 [b]Embedded OniSim Native Rust Core[/b]: Ships with a clean-room native Rust simulation accelerator for Windows, Linux, and macOS, unlocking SIMD parallel processing power.
[/list]

[h2]🛡️ Safety & Compatibility[/h2]
[list]
[*] [b]Pure Vanilla Behavior[/b]: Never alters dupe priorities, schedules, stats, speeds, or game balance.
[*] [b]Great Mod Compatibility[/b]: Fully supports both the Base Game and the Spaced Out! DLC. Actively detects popular mods like FastTrack and Plib, gracefully backing off if conflicts are detected.
[*] [b]Zero-Allocation Hotpaths[/b]: Carefully engineered to prevent heap allocations on critical paths, eliminating GC pause spikes.
[/list]

[h2]⚙️ Configurable Options[/h2]
All optimization submodules can be tailored to your liking in the in-game Mod Options menu. Changes take effect on next game launch.

[h2]Source Code & Community[/h2]
100% open-source and transparent:
[url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/CycleTrim]GitHub Repository[/url]
