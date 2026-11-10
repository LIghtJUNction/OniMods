[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/.agents/skills/oni-workshop-description/assets/ai-generated-congratulations.png[/img]
[h1]CONGRATULATIONS![/h1]
[b]THIS MOD IS ENTIRELY AI-GENERATED[/b]

[h1]⚠ HIGH-RISK EXPERIMENTAL MOD ⚠[/h1]
[b]For experimentation and early previews only. Not suitable for long-term, stable play.[/b]
CycleTrim is rebuilding core simulation behavior. Crashes, missing materials, unusual flows, corrupted saves and mod conflicts are possible. Back up your original saves and use disposable test saves only.
[b]Future experimental saves may not open in vanilla or older versions of the mod. Do not assume you can remove the mod and continue playing.[/b]

[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/mods/CycleTrim/docs/images/handdrawn-gpu-explainer-en.gif[/img]
[i]Hand-drawn-style animation: the planned architecture, still in development.[/i]

[h2]More than one material in a cell[/h2]
CycleTrim explores gas and liquid mixtures with GPU acceleration. The development screenshots below do not establish complete gameplay support.

[h2]Architecture[/h2]
Cells store per-species mass. GPU kernels handle thermal and eligible regional fluid calculations; the CPU coordinates game logic. Regional gas operators share buffers, but uploads, waits and readback remain. Buildings, breathing, sensors and saves need separate integration.

[h2]Current progress[/h2]
[list]
[*]Version 0.4.13 transports all gas species without limiting a cell to two gas slots. Liquid species equality and complete mixture save/load remain unfinished.
[*]The development timestep uses GPU thermal diffusion over the grid. Eligible gas regions use GPU concentration diffusion, density-gradient velocity, pressure projection, transport and heat; eligible liquid regions use GPU multicomponent diffusion. Some conditions still use CPU processing or exclude a region.
[*]Host/operator tests and short game runs have been performed; full gameplay integration remains experimental.
[*]Phase changes, bubbles, some liquid flow, disease, radiation, building interactions and publication still rely on the CPU. Gas transport now retains three or more species per cell; building interactions, liquids and persistence still need end-to-end validation.
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
[*]Multicomponent pipes, including building input/output and save/load; no verified screenshot yet.
[*]Saving and restoring every component and migrating experimental save formats.
[*]GPU thermal diffusion, pressure and multicomponent computation, with fewer transfers between CPU and GPU.
[*]In-game visual and physics validation of textures, information cards, density stratification and rising bubbles.
[/list]
These are development goals, not promises that they are released or work in your save. GPU acceleration and frame rate depend on hardware, scene and build; no specific frame rate is guaranteed.

[h2]Installation and feedback[/h2]
Requires [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]Jlib[/url]. Subscribe to and enable Jlib and CycleTrim, then restart the game. Settings are available in the mod options.

Report crashes, save problems or material anomalies on [url=https://github.com/LIghtJUNction/OniMods/issues]GitHub Issues[/url], with your game version, reproduction steps and logs without private information.

[url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/CycleTrim]Source code and development progress[/url]
