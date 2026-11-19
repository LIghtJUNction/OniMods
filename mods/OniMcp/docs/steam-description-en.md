[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/.agents/skills/oni-workshop-description/assets/ai-generated-congratulations.png[/img]
[h1]CONGRATULATIONS![/h1]
[b]THIS MOD IS ENTIRELY AI-GENERATED[/b]
This statement describes this project's AI development. Third-party libraries, specifications and game assets retain their original authorship and licenses.

[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/mods/OniMcp/preview.png[/img]

[h1]OniMcp · Give your client a view of the colony[/h1]
OniMcp provides a local MCP service for Oxygen Not Included. A compatible client can inspect colony state, investigate problems and request explicit game actions. You choose the model and client.

[b]0.2.9 version notes. Required: Jlib 0.3.10 or a newer compatible provider.[/b]
[url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]Subscribe to Jlib[/url], enable both mods in the game's Mods menu, then restart. Dependent mods share one Jlib installation. Check your installed package for the actual Workshop version.

[h2]Start in three steps[/h2]
[list]
[*]Load a colony. The service can start in the main menu, but map reads require a ready save.
[*]Add [code]http://localhost:8788/mcp/[/code] to your MCP client. The browser status page is [code]http://localhost:8788/[/code].
[*]Discover the tools and resources, then read the current state: [code]world_editor command=read path=/active/colony/current.oni[/code]
[/list]

[h2]What it provides[/h2]
[list]
[*]Seven public tools for state inspection, camera controls, building plans, orders and game controls.
[*]Virtual world paths through world_editor and oni:// resources through resources/read. read_control is no longer a public tool.
[*]Complete published cell component masses and temperatures, with an explicit unavailable result when no provider snapshot exists.
[*]State previews that remain valid JSON and point to the complete document.
[*]Request errors and task status, with parameters discoverable through the current session's tools/list.
[/list]
Cell resource example: [code]oni://world/cell/81/111[/code]. Replace the coordinates with a cell in your map. Public tools are world_editor, game_control, navigation_control, building_control, orders_control, server_control and benchmark.

[h2]Data and validation boundaries[/h2]
Multicomponent pressure, partial pressure and volume fractions have separate availability flags. Current OniSim does not provide calibrated pressure or partial pressure. Unavailable does not mean zero or a vacuum. Liquid occupancy comes from the solver's nominal capacity; it is not kPa.

This iteration checked public tool discovery, current-state JSON and cell reads in U60. A focused host regression passed 50 assertions, which does not validate every game action. The latest six follow-up hint corrections are committed; their game recheck is pending. OniMcp does not promise autonomous campaign completion or an FPS increase.

[h2]Connection notes[/h2]
The default listener is local. Non-loopback listening requires authentication; a Bearer token does not encrypt traffic. Configure a VPN or TLS reverse proxy for remote access. The MCP protocol version is 2025-11-25 and clients must initialize a session. Explain and confirm the scope before loading a save, restarting or making large world changes.

[h2]Retained demonstration images[/h2]
These screenshots come from historical repository demonstrations, not this iteration's U60 acceptance run.
[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/docs/images/gpt-oni-demo/mcp-workflow.jpg[/img]
[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/docs/images/gpt-oni-demo/colony-building.jpg[/img]
[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/docs/images/gpt-oni-demo/colony-overview.jpg[/img]

Hand-drawn development workflow. This is an illustration, not performance evidence.
[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/.agents/skills/oni-workshop-description/assets/onim-illustrated-onimcp.gif[/img]

[h2]Documentation and feedback[/h2]
[url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README_EN.md]Documentation and client setup[/url] · [url=https://github.com/LIghtJUNction/OniMods/issues]Report an issue[/url]
Include game/mod versions, the request, error and a minimal reproduction. Remove tokens and private account details.
[url=https://donate.lmm.best/?project=onimcp]Support development and testing[/url]
