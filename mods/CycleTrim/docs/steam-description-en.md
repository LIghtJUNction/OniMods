[b]Required mod: Jlib.[/b] Subscribe to and enable Jlib, then restart ONI. This mod uses the shared prerequisite instead of embedding its own copy.

[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/CycleTrim/preview.png[/img]

Welcome to CycleTrim! A disruptive, experimental performance mod for Oxygen Not Included.
With this mod, [b]the low-level algorithms get rebuilt, and as much of the math as possible moves onto the GPU.[/b]

This is still early development. The order is fixed: speed first, compatibility later.

[h2]Current Stage: v0.4.0 Early[/h2]
[i]This is an experiment. Speed comes first. Save safety, other mods, and the rest of the game come after.[/i]
[i]Use a disposable test save. Do not treat this as a finished late-game optimization patch.[/i]

[h3]Supported DLC:[/h3]
[img]https://raw.githubusercontent.com/Sgt-Imalas/Sgt_Imalas-Oni-Mods/refs/heads/master/Compat_All.png[/img]

[h3]What this mod will do[/h3]
Rebuild the underlying algorithms instead of only patching vanilla hot paths.
Move as much computation as possible onto the GPU. The current GPU experiment starts with the flow presentation image. The ordered fluid and thermal solver has not moved across as a whole.
Cut frame time first, then come back for compatibility. Matching vanilla behavior is not a promise at this stage.

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
