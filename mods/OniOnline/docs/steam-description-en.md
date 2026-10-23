OniOnline 0.4.0: Multiplayer Framework Development Preview

Join a star system with friends and manage one planet per player. Each player keeps their own colony, save and authoritative simulation. Pausing and changing speed affect only that player's game. Owners can share explored regions for observation and chat.

OniOnline also provides an independent SDK. The built-in one-player-per-planet mode uses the same extension interfaces as third-party modes. Developers can add rules, authenticated messages and explicitly registered mod-state channels. This does not automatically synchronize arbitrary mod internals or the entire simulation.

Getting started:
• Browse Star Systems in the main menu is for browsing and joining.
• New Game → Survival → Online creates a system through the native opening flow.
• Once the colony is ready, open Communications from the native management-bar bubble or the in-game Options menu, or press Ctrl+T.

The preview includes Steam public systems, LAN discovery, system codes, chat, reconnects and host moderation. The ordinary package provides permission-based read-only observation; native remote work and duplicant commands are disabled. Full colony transfers, rocket visits, resource shipping and offline hosting are not implemented.

Before gameplay admission, required mods must match in source, version, controlled content digest and actual loaded state. Public Workshop content can be downloaded after confirmation. A completed Steam download does not establish ONI installation or loading: use the native mod menu, enable the required content, restart and rejoin. Local/Dev packages are not automatically distributed. Runtime state synchronization covers only channels explicitly registered and authorized through the SDK.

CycleTrim and Spaced Out! are not fixed prerequisites. Basic sessions negotiate protocol, gameplay and feature contracts rather than requiring equal game-build or DLC labels. The current native adapter is registered for build 744825: planet identity and observation sampling cover the base game, while the native star map requires Spaced Out! Unknown builds cannot use unadapted native features; arbitrary game-version compatibility is not promised. Other required mods enabled by the host must still match the manifest.

The current source candidate is 0.4.0, protocol 10, and remains a development preview. This text does not mean it has been published to Steam. Visual interaction, real Workshop download and native restart, and complete testing between two actual game clients still need acceptance. Focused host tests do not replace game evidence. Please use copies of your saves for testing.

Source, SDK and installation: https://github.com/LIghtJUNction/OniMods/tree/main/mods/OniOnline
