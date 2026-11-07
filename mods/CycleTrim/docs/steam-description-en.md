[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/.agents/skills/oni-workshop-description/assets/ai-generated-warning.png[/img]
[h1]WARNING[/h1]
[b]THIS MOD IS ENTIRELY AI-GENERATED[/b]

[h1]⚠ HIGH-RISK EXPERIMENTAL MOD ⚠[/h1]
[b]For experimentation and early previews only. Not suitable for long-term, stable play.[/b]
CycleTrim is rebuilding core simulation behavior. Crashes, missing materials, unusual flows, corrupted saves and mod conflicts are possible. Back up your original saves and use disposable test saves only.
[b]Future experimental saves may not open in vanilla or older versions of the mod. Do not assume you can remove the mod and continue playing.[/b]

[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/mods/CycleTrim/docs/images/handdrawn-gpu-explainer-en.gif[/img]
[i]Hand-drawn-style animation: the planned architecture, still in development.[/i]

[h2]More than one material in a cell[/h2]
CycleTrim is exploring a different simulation for Oxygen Not Included: gases and liquids sharing, flowing and mixing within a cell, with GPU acceleration where possible. The following are [b]actual in-game screenshots from development builds[/b]; they do not mean these features are fully available in the current Workshop release.

[h2]Why move beyond one material per cell?[/h2]
The stock grid's common interfaces primarily expose one element and one mass per cell. When materials meet, the simulation and game logic must handle occupancy, displacement and neighboring cells. CycleTrim is exploring cells that record the mass and state of multiple materials, allowing oxygen and carbon dioxide, or water and polluted water, to share space.

This changes more than fluid equations: breathing, building consumption, sensors, visuals and saves must understand the individual components. These integrations are being developed and cannot be assumed to work automatically.

[h2]How can this run on the GPU?[/h2]
The CPU handles game events, buildings and complex control flow well; GPUs can perform the same calculation across many cells in parallel. The experimental design arranges material data into regular arrays so GPU kernels can calculate flow, pressure and heat transfer. Shared-face exchange is computed before each cell collects its changes, reducing concurrent writes. Mass, energy and gameplay behavior still need to be checked.

[h2]How do we reduce transfers?[/h2]
Sending the entire map to the GPU and immediately reading it back after every operation can erase the benefit of acceleration. The goal is to keep mass, velocity and temperature in GPU memory so flow, pressure and heat kernels can pass buffers directly to one another; upload changed inputs and read back results when game logic or rendering needs them. Device residency does not make GPU memory directly accessible to all game code, and the synchronization boundaries are still being developed.

[h2]Current progress[/h2]
[list]
[*]Development builds have shown two gases and two liquids sharing cells. Three or more components and complete save/load are not yet integrated end to end.
[*]GPU thermal diffusion, multicomponent transport and pressure operators have standalone tests. Experimental tests chain GPU buffers between operators and run consecutive heat steps without an intermediate readback.
[*]In the current working tree, only GPU thermal diffusion is connected to the experimental production timestep, and it still uploads state and synchronously reads temperature each step. GPU transport and pressure are not yet connected to that production chain.
[*]Published pressure, partial pressure and liquid volume fractions are unfinished; breathing, plants, pipes and other interactions still need end-to-end validation.
[/list]
Standalone tests, development builds and the Workshop release are different stages. An in-game frame-rate improvement has not yet been fully verified.

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
[*]Multiple substances in one gas or liquid pipe cell, including storage, flow, building input/output, display and save/load. This is unfinished; no verified in-game screenshot is available yet.
[*]Saving and restoring every component and migrating experimental save formats.
[*]GPU thermal diffusion, pressure and multicomponent computation, with fewer transfers between CPU and GPU.
[*]In-game visual and physics validation of textures, information cards, density stratification and rising bubbles.
[/list]
These are development goals, not promises that they are released or work in your save. GPU acceleration and frame rate depend on hardware, scene and build; no specific frame rate is guaranteed.

[h2]Installation and feedback[/h2]
Requires [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]Jlib[/url]. Subscribe to and enable Jlib and CycleTrim, then restart the game. Settings are available in the mod options.

Report crashes, save problems or material anomalies on [url=https://github.com/LIghtJUNction/OniMods/issues]GitHub Issues[/url], with your game version, reproduction steps and logs without private information.

[url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/CycleTrim]Source code and development progress[/url]
