[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/OniOnline/assets/onionline-logo-v1.png[/img]

[b]Required mod: Jlib.[/b] Subscribe to and enable Jlib, then restart ONI. This mod uses the shared prerequisite instead of embedding its own copy.

[h1]OniOnline[/h1]

Welcome to OniOnline! A multiplayer framework for Oxygen Not Included.
With this mod, [b]friends share a lobby, a chat channel, and a session. Gameplay rules arrive as separate mods.[/b]

See who is in the room, talk while the colony loads, and let a gameplay mod register the worlds it actually wants to share.

[h2]Current Stage: v0.5.0 Preview[/h2]
[i]This is a development preview. Expect bugs, incomplete graphics, and sessions that still need a backup save.[/i]
[i]CycleTrim and Spaced Out! are not fixed prerequisites. Mods you do not declare are treated as required, so guests may still need the same list.[/i]

[h3]Supported DLC:[/h3]
[img]https://raw.githubusercontent.com/Sgt-Imalas/Sgt_Imalas-Oni-Mods/refs/heads/master/Compat_All.png[/img]

[h3]MODDING API[/h3]
OniOnline.Framework is a .NET Standard 2.0 SDK. It does not reference Unity, the game, or PLib.
Call GameplayFramework.Register from OnLoad, then Select the gameplay before connecting.
Declare onionline.worlds.v1 only when the mod needs planet registration and world sync. Without that contract the session stays on lobby, chat, and mod alignment.
Do not ship a second copy of the framework DLL inside a gameplay mod.
SDK notes: [url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniOnline/docs/SDK.md]SDK.md[/url]
Example project: [url=https://github.com/LIghtJUNction/OniMods/tree/main/examples/OniOnline.PlanetSignals]OniOnline.PlanetSignals[/url]

[h3]What is synchronized?[/h3]
The built-in gameplay is lobby chat. Steam public rooms, LAN discovery, and room codes can host it.
Before launch, both sides compare required mods, versions, content hashes, and load state. A mismatch opens the game's own confirm dialog, with a path to download public Workshop files. Download finished does not mean the mod is enabled.
Reconnect must confirm the list again. Local and Dev mods are not uploaded for you.
Registered state channels can sync the snapshots a gameplay mod explicitly defines. Private configs and the whole native simulation are not mirrored.
Rockets, material shipping, shared construction, and offline catch-up are not in this preview. Closing the simulating client stops that colony.

[h3]Controls and Quality of Life[/h3]
The management bar stays quiet on menus, during loads, and on the printing pod selection screen.

* Main menu: Browse Lobbies to find, join, or host. The default gameplay is chat.
* In a colony: the management-bar bubble opens Comms. Ctrl+T opens chat. Escape closes it. Both shortcuts can be rebound.
* Push-to-talk is in the development candidate on Ctrl+V. Microphone, focus-loss, and two-client audio are not accepted yet.
* LAN listens on TCP 27140 and discovers on UDP 27141. A direct address looks like oni://IP:27140. Steam room links look like oni://steam/<lobby id>.
* LAN passwords are not transport encryption. Use them on a network you trust.

[h3]Built with onim[/h3]
This mod comes from [url=https://github.com/LIghtJUNction/OniMods]OniMods[/url]. onim is the toolchain in that repo. It takes an Oxygen Not Included mod straight through development, testing, and Workshop publishing.

onim dev tries it in game. onim build packs it. onim publish sends it to Steam.

[h3]Contributions[/h3]
Contributions are welcome!
If you have improvements, fixes, or new features, feel free to open a Pull Request.

Please make sure your changes are clear and well-documented where necessary.

You can find the [url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/OniOnline]GitHub repository here[/url]

[h3]Disclaimer[/h3]
Please remember that this mod is under active development and will receive regular updates and improvements.
Your feedback and suggestions are highly appreciated.
Use a test save. Protocol 11 and build 744825 are the current adapter target. An unknown game build keeps the basic session and refuses native world reads.

[h3]Contact[/h3]
Questions and bug reports go to the [url=https://github.com/LIghtJUNction/OniMods/issues]GitHub issue tracker[/url].

Have fun out there, and remember: when together, oxygen is still not included.
[img]https://i.ibb.co/LXdJf9K9/I3P8A6f.gif[/img]
