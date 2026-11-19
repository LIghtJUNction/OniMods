[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/.agents/skills/oni-workshop-description/assets/ai-generated-congratulations.png[/img]
[h1]CONGRATULATIONS![/h1]
[b]THIS MOD IS ENTIRELY AI-GENERATED[/b]
This statement describes this project's AI development. Third-party libraries and original game assets retain their authorship and licenses.

[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/mods/OniOnline/preview.png[/img]

[h1]OniOnline · Lobbies, chat and a multiplayer framework[/h1]
[b]The default gameplay is a chat lobby, not a complete shared colony simulation.[/b]
OniOnline 0.5.3 version notes; protocol 11. [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]Jlib[/url] is required; this iteration uses 0.3.10. Basic sessions do not require CycleTrim or DLC. Check your installed package for the actual Workshop version.

[h2]Where to start[/h2]
Subscribe to and enable Jlib and OniOnline in the Mods menu, then restart.
[list]
[*]Choose Browse lobbies from the main menu. Select Steam / LAN or enter a lobby code.
[*]Choose Create lobby to open the connection form. Enter a nickname and lobby name; choose transport, port and an optional password in Network settings, then create.
[*]Copy the lobby code from the room. A participant joining the chat gameplay reaches the room chat interface.
[*]Once connected and in a loaded colony, use the communications bubble or default Ctrl+T for chat; Esc closes it. Bindings are configurable.
[/list]
[b]The new creation entry and post-join room transition are connected in source. This iteration's U60 UI and two-client game acceptance remain pending.[/b] The chat HUD stays hidden when disconnected.

[h2]What the framework provides[/h2]
[list]
[*]Lobbies, player lists, text chat and reconnection use an independent session layer, without the 744825 native-adapter gate.
[*]Status distinguishes connection attempts, retry waits and final failure while retaining the cause. Failed chat sends preserve the input.
[*]Connection checks mod manifests and compatibility. A Workshop download is not the same as enabling or loading a mod; confirmation and a restart may still be needed.
[*]The SDK carries explicitly registered messages and state channels. It does not copy arbitrary mod memory or automatically upload local development mods.
[/list]
A successful SDK send means the transport accepted the message, not that a remote client processed it. Broadcast success does not mean every participant received it.

[h2]What is unavailable in U60[/h2]
[b]Native planets, world observation and native star-map collaboration are unavailable on U60 (763796).[/b] The native adapter whitelist still contains only build 744825; star-map integration also requires Spaced Out!. Even on that build, another gameplay mod must register, select and enable world services.

World/observation controls are disabled with a reason when unavailable. The U60 message is “This feature is not supported on this game version.” with code native_adapter_unavailable.

This package alone does not provide complete colony migration, duplicant or resource transport, rocket transport or offline catch-up. Default ChatOnly does not start world services. Ctrl+V voice remains a development feature; microphone and two-client behavior have not been accepted. Keep a save copy when trying unreviewed gameplay mods.

[h2]Validation boundary[/h2]
The standalone host passed 4,808 assertions, including real loopback TCP, three separate client processes, throttling/delay and reconnection. The game-world/time boundary uses test doubles.
[b]This is not multiplayer acceptance on two U60 devices or two actual game clients.[/b]
Steam content downloads, voice and the new UI paths still require separate game checks.

LAN defaults are TCP 27140 and UDP 27141. Direct addresses use oni://IP:27140; Steam codes use oni://steam/lobby-ID. LAN transport is for trusted networks; a lobby password does not encrypt TCP traffic.

[h2]Framework illustration and documentation[/h2]
The hand-drawn illustration explains the framework and toolchain. It does not show a completed multiplayer gameplay mode.
[img]https://raw.githubusercontent.com/LIghtJUNction/OniMods/main/.agents/skills/oni-workshop-description/assets/onim-illustrated-onionline.gif[/img]
[img]https://media.githubusercontent.com/media/LIghtJUNction/OniMods/main/mods/OniOnline/assets/onionline-logo-v1.png[/img]

[url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniOnline/README.md]Entry points and full limitations[/url] · [url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniOnline/docs/SDK.md]Gameplay SDK[/url] · [url=https://github.com/LIghtJUNction/OniMods/issues]Report an issue[/url]
Include both participants' versions, gameplay, transport and error code. Remove passwords, private addresses and account details.
