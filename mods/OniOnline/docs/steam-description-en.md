OniOnline 0.5.0: Multiplayer Framework Development Preview

Join a lobby and chat with friends. The built-in gameplay does not register planets or move colonies.

OniOnline is a framework for multiplayer gameplay mods. Chat is the only built-in mode. A separate mod can register its own rules and reuse the lobby, session and state channels. This does not automatically synchronize arbitrary mod internals or the entire simulation.

Getting started:
• Browse Lobbies in the main menu finds, joins and creates rooms.
• Once a colony is loaded, open Communications from the native management-bar bubble, or press Ctrl+T.
• Planet registration and the star map open only for a gameplay mod that declares onionline.worlds.v1.

The preview includes Steam public lobbies, LAN discovery, lobby codes, chat, reconnects and host moderation. Remote construction and duplicant commands stay disabled. Full colony transfers, rocket visits, resource shipping and offline hosting are not implemented.

Before gameplay admission, required mods must match in source, version, controlled content digest and actual loaded state. Public Workshop content can be downloaded after confirmation. A completed Steam download does not establish ONI installation or loading: use the native mod menu, enable the required content, restart and rejoin. Local/Dev packages are not automatically distributed. Runtime state synchronization covers only channels explicitly registered and authorized through the SDK.

CycleTrim and Spaced Out! are not fixed prerequisites. Basic sessions negotiate protocol, gameplay and feature contracts rather than requiring equal game-build or DLC labels. The current native adapter is registered for build 744825: planet identity and observation sampling cover the base game, while the native star map requires Spaced Out! Unknown builds cannot use unadapted native features; arbitrary game-version compatibility is not promised. Other required mods enabled by the host must still match the manifest.

The current source candidate is 0.5.0, protocol 11, and remains a development preview. This text does not mean it has been published to Steam. Visual interaction, real Workshop download and native restart, and complete testing between two actual game clients still need acceptance. Focused host tests do not replace game evidence. Please use copies of your saves for testing.

Source, SDK and installation: https://github.com/LIghtJUNction/OniMods/tree/main/mods/OniOnline
