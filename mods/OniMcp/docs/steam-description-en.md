[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/OniMcp/preview.png[/img]

[b]Required mod: Jlib.[/b] Subscribe to and enable Jlib in the game mod menu, then restart ONI. This mod uses the shared prerequisite instead of embedding its own copy.

[h1]ONI MCP Server[/h1]

Welcome to ONI MCP Server! A local Model Context Protocol bridge for Oxygen Not Included.
With this mod, [b]AI clients can read the colony and act only inside the tools you expose.[/b]

Point Claude, Cursor, Codex, or Gemini at the local server. See alerts, pipes, power, and duplicants, without parking the simulation thread.

[h2]Current Stage: v0.2.6[/h2]
[i]The local server is in active use. Tool coverage and client prompts still move between releases.[/i]
[i]Other mods can change the data these tools report. Place Jlib above this mod before you enable it.[/i]

[h3]MODDING API[/h3]
Players need the shared library [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]Jlib[/url]. Subscribe to it, enable it above ONI MCP Server, and restart the game.
Developers reference [url=https://www.nuget.org/packages/LIghtJUNction.Jlib]LIghtJUNction.Jlib[/url] with ExcludeAssets="runtime". The NuGet package does not install the Workshop item.
The public surface is the MCP endpoint plus oni:// resources. Start from oni://tools/manifest.

[h3]What the client can reach[/h3]
A loaded colony exposes status, duplicant checks, a text map, building definitions, and inventory.
Aggregate tools cover world reads, camera and overlays, building plans, dig and sweep orders, and server control.
Large digs, demolitions, and save loads stay explicit. This is not an autonomous player for a whole campaign.

[h3]Controls and Quality of Life[/h3]
The server stays off the simulation thread. Colony resources appear after a save is loaded.

* Browser home: http://localhost:8788/
* MCP endpoint: http://localhost:8788/mcp/
* The service starts from the main menu. oni:// colony tools need a loaded save.
* Auth is off by default. Turn on AuthEnabled in OniMcpConfig.json before the port leaves localhost.

[h3]Built with onim[/h3]
This mod comes from [url=https://github.com/LIghtJUNction/OniMods]OniMods[/url]. onim is the toolchain in that repo. It takes an Oxygen Not Included mod straight through development, testing, and Workshop publishing.

onim dev tries it in game. onim build packs it. onim publish sends it to Steam.

[h3]Contributions[/h3]
Contributions are welcome!
If you have improvements, fixes, or new features, feel free to open a Pull Request.

Please make sure your changes are clear and well-documented where necessary.

You can find the [url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/OniMcp]GitHub repository here[/url]

[h3]Disclaimer[/h3]
Please remember that this mod is under active development and will receive regular updates and improvements.
Your feedback and suggestions are highly appreciated.
Report issues on GitHub. Keep the game paused while a client is only reading.

[h3]Contact[/h3]
Questions and bug reports go to the [url=https://github.com/LIghtJUNction/OniMods/issues]GitHub issue tracker[/url].

Have fun out there. The duplicants still need oxygen. The model is only visiting.

Like what I'm doing? You can support continued OniMcp testing:
[url=https://donate.lmm.best/?project=onimcp]Donate[/url]
