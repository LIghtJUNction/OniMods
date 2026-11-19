[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/.agents/skills/oni-workshop-description/assets/ai-generated-congratulations.png[/img]
[h1]CONGRATULATIONS![/h1]
[b]THIS MOD IS ENTIRELY AI-GENERATED[/b]

[h1]⚠ HIGH-RISK EXPERIMENTAL MOD ⚠[/h1]
[b]For experimentation and early previews only. Not suitable for long-term, stable play.[/b]
CycleTrim is rebuilding core simulation behavior. Crashes, missing materials, unusual flows, corrupted saves and mod conflicts are possible. Back up your original saves and use disposable test saves only.
[b]Mixed saves in the current development build already use a dedicated format: version 0x4f4d0001 with a mandatory ONIMIX extension. They need a supporting OniSim reader; the tested older OniSim rejects them. Vanilla readability is not established, and removal or downgrade is not a supported continuation path. These changes have not been published to Workshop.[/b]

[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/mods/CycleTrim/docs/images/handdrawn-gpu-explainer-en.gif[/img]
[i]Hand-drawn-style animation: the planned architecture, still in development.[/i]

[h2]More than one material in a cell[/h2]
CycleTrim explores gas and liquid mixtures with GPU acceleration. The development screenshots below do not establish complete gameplay support.

[h2]Architecture[/h2]
Cells store per-species mass. GPU kernels handle thermal and eligible regional fluid calculations; the CPU coordinates game logic. Regional gas operators share buffers, but uploads, waits and readback remain. Buildings, breathing, sensors and saves need separate integration.

[h2]Current progress[/h2]
0.4.22 overlaps CPU gas tasks with GPU liquid readback through single-process coroutines, preserving result order. It fixes trace-gas depletion and reuses staging buffers. All gas species participate; changed inputs are recomputed. Requires Jlib 0.3.8. Paired native benchmarks passed; game FPS still needs controlled verification.
[list]
[*]The save extension preserves primary, secondary and additional inventories. Worlds without additional positive inventory retain ordinary SIMSAVE v15; mixed worlds need the new reader. Exact 22-species round trips have host/FFI evidence; broader game acceptance remains open.
[*]GPU thermal diffusion covers the grid. Eligible gas regions use diffusion, velocity, bounded transport and heat kernels in one submission; eligible liquids use multicomponent diffusion. Other conditions still use CPU processing or exclude regions.
[*]Phase changes, bubbles, some liquid flow, disease, radiation, buildings and publication still use the CPU. Building interactions, liquids and persistence need end-to-end validation.
[*]Pressure, partial pressure and liquid volume fractions are unfinished. Breathing, plants and pipes need end-to-end validation.
[/list]
The 2026-11-20 development checkpoint 8db2654 passed strict profile, runtime and ABI checks for Linux x64 U60/763796. It ran through Steam with normal graphics on a three-duplicant save copy. Six naturally formed oxygen/carbon-dioxide cells retained complete inventories, mass, temperature and total-mass binary32 bits after a unique checkpoint and same-process reload, then continued advancing. See the [url=https://github.com/LIghtJUNction/OniMods/blob/main/docs/cycletrim-u60-multicomponent-live-20261120.json]live evidence[/url] and [url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/OniSim/docs/save-format.md]save-format and old-reader boundaries[/url].

The live scene covers two positive species per cell; 22-species coverage remains host/FFI evidence. Windows/macOS native profiles remain limited to 744825. Whole-game performance A/B, additional species and long-duration/DLC acceptance remain pending. Later liquid-fill, pressure-availability and background-run changes are outside this receipt. These changes have not been published to Workshop.

[h3]Gas mixing: oxygen and carbon dioxide in one cell[/h3]
[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/CycleTrim/docs/images/experimental-gas-mix.png[/img]
This test shows two gases and their proportions in one cell. Diffusion, stratification and mass conservation still need complete in-game validation.

[h3]Liquid mixing: water and polluted water[/h3]
[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/CycleTrim/docs/images/experimental-liquid-mix.png[/img]
This test shows water and polluted water in one cell. Textures, proportions and flow are still being refined.

[h3]Multicomponent information cards[/h3]
[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/CycleTrim/docs/images/experimental-species-card.png[/img]
The experimental interface displays individual substances. Partial pressures are zero in this screenshot: pressure data is unfinished and should not be treated as physically valid.

[h2]In development[/h2]
[list]
[*]Three or more substances per cell throughout transport, diffusion, exchange and rendering.
[*]Multicomponent pipes, including building input/output and save/load; no verified screenshot yet.
[*]Broader mixed-save game acceptance, especially three or more species per cell, long runs and format migration.
[*]GPU thermal diffusion, pressure and multicomponent computation, with fewer transfers between CPU and GPU.
[*]In-game visual and physics validation of textures, information cards, density stratification and rising bubbles.
[/list]
These are development goals, not promises that they are released or work in your save. GPU acceleration and frame rate depend on hardware, scene and build; no specific frame rate is guaranteed.

[h2]Installation and feedback[/h2]
Requires [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]Jlib[/url] 0.3.8 or newer. Subscribe to and enable Jlib and CycleTrim, then restart the game.

Report crashes, save problems or material anomalies on [url=https://github.com/LIghtJUNction/OniMods/issues]GitHub Issues[/url], with your game version, reproduction steps and logs without private information.

[url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/CycleTrim]Source code and development progress[/url]
