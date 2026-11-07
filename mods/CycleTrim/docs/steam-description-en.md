[b]Required mod: Jlib.[/b] Subscribe to and enable Jlib, then restart ONI. This mod uses the shared prerequisite instead of embedding its own copy.

[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/CycleTrim/preview.png[/img]

Welcome to CycleTrim! A disruptive, experimental performance mod for Oxygen Not Included.
With this mod, [b]the low-level algorithms get rebuilt, and as much of the math as possible moves onto the GPU.[/b]

This is still early development. The order is fixed: speed first, compatibility later.

[h2]Current Stage: v0.4.0 Early[/h2]
[i]This is an experiment. Speed comes first. Save safety, other mods, and the rest of the game come after.[/i]
[i]Use a disposable test save. Do not treat this as a finished late-game optimization patch.[/i]

[h3]Supported DLC:[/h3]

[h3]Next version: experiment past one element per cell & GPU Poisson pressure projection[/h3]
The stock simulation stores one element and one mass in each cell. When two substances meet, they cannot share the cell, so the old substance has to be pushed somewhere else. That push searches neighbor cells, branches, and then decides who gets in first. Wide CPU vectors and GPUs are built for the same multiply-add on every cell. They stall on that kind of divergence.

[b]1. Legacy Vanilla Sweep (serial grid sweep + arbitration displacement):[/b]
Serial donor-receiver mass exchange hard-clamped to 12.5% of donor mass, combined with recursive displace_gas / displace_liquid neighbor searches. Heavy branching and strict order-dependence destroy CPU cache locality and cause GPU warp divergence.

[b]2. New GPU Numerical Pipeline (under research):[/b]
Variable-density continuous Poisson pressure projection (Red-Black Gauss-Seidel with SOR iterations) combined with multicomponent face-flux transport, eliminating neighbor displacement divergence via GPU memory barriers and divergence-free projection.

This build is not that algorithm yet. OniSim is still one element per cell. The pressure solve does not become a single multiply. The Stage 2 mathematical reference (residual 1e-7, divergence decay, zero-velocity fixed point, and semantic comparator tests) has passed in an isolated experimental branch. The production kernel remains segregated, and disease, buildings, and the save format do not move in the same step. Speed first, followed by strict behavioral compatibility validation.

Full note: [url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/docs/next-cell-representation.md]why one element per cell fights modern hardware[/url]

[h3]What the current build already does[/h3]
Busy duplicants, stationary critters, and pickup scans repeat less work. These are early cuts, not the final shape.
The OniSim native core ships in the package but does not take over the simulation by default. Keep the vanilla SimDLL for normal colonies; explicitly enable the experimental option only on a disposable test save.
CycleTrim still needs [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]Jlib[/url]. Enable Jlib above CycleTrim, then restart.
Options live under Mods, then CycleTrim, and apply on the next launch. OniSim is off by default; the new experimental opt-in must be enabled explicitly. CYCLETRIM_ONISIM_ENABLE=0 keeps the vanilla SimDLL mapping. The mapping cannot be hot-reverted.

[h3]Controls and Quality of Life[/h3]
The default keeps the official simulation core active. OniSim remains an experimental, opt-in test path.

* Subscribe and enable. Restart once so Jlib and the options page load together.
* Keep OniSim off for normal colonies; enable it only for a disposable test save.
* Conduit animation, power-overlay reuse, and state-machine reuse can each be narrowed in the options. Restart after changing them.

[h3]Built with onim[/h3]
This mod comes from [url=https://github.com/LIghtJUNction/OniMods]OniMods[/url]. onim is the toolchain in that repo. It takes an Oxygen Not Included mod straight through development, testing, and Workshop publishing.

onim dev tries it in game. onim build packs it. onim publish sends it to Steam.

[h3]Contributions[/h3]
Contributions are welcome!
If you have improvements, fixes, or new features, feel free to open a Pull Request.

Please make sure your changes are clear and well-documented where necessary.

You can find the [url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/CycleTrim]GitHub repository here[/url]

[h3]Disclaimer[/h3]
Please remember that this mod is an early experiment and will keep changing.
Your feedback and suggestions are highly appreciated.
Speed first, compatibility later. Report crashes, bad saves, and mod conflicts on GitHub, and use a test save.

Have fun out there. Get the cycles moving. Oxygen and compatibility are still behind them.
[img]https://i.ibb.co/LXdJf9K9/I3P8A6f.gif[/img]
