# OniOnline.Framework

.NET Standard 2.0 multiplayer framework and SDK. This assembly has no Unity, ONI, PLib or DLC reference.

The `OniOnline` game adapter supplies native UI, save/load, Steam and world access. The bundled `OnePlanetPerPlayer` gameplay uses the same public interfaces as third-party gameplay extensions.

API documentation and current limits are included in `docs/SDK.md`, `docs/SDK-MOD-SYNC.md`, and `docs/FRAMEWORK-COMPATIBILITY.md` in the package, or in the [repository documentation](https://github.com/LIghtJUNction/OniMods/tree/main/mods/OniOnline/docs). The API is a development preview.

Gameplay mods that ship custom package resources can register their extensions
with `ModSyncDeclarations.RegisterContentExtensions` before the first manifest
snapshot. The declaration is local, immutable after startup, and does not grant
access to user configuration or save directories.

Gameplay rules may optionally implement `IGameplayCapabilityContract` to add
directional, versioned capability IDs to the session handshake. The adapter
merges those IDs with its native and mod-sync capabilities; a gameplay package
must still perform its own admission and permission checks.

Subscribe to `GameplayFramework.SessionChanged` and reacquire `GameplayFramework.Current` on every callback. The adapter disposes the previous session before publishing a replacement, so a plugin must release its old channel subscriptions instead of keeping a stale `GameplaySession` reference.

`GameplaySession.LocalEnvironment` and `HostEnvironment` expose immutable
game-build, DLC, CycleTrim and capability descriptions for choosing a
graceful fallback. They describe an endpoint; they do not grant native access
or replace capability and mod-synchronization checks.

Build without installed game files: `dotnet build sdk/OniOnline.Framework -c Release`. Package locally with `dotnet pack sdk/OniOnline.Framework -c Release -o dist/sdk`.

Use the framework supplied by OniOnline at runtime; other mods must not bundle their own competing copy.
