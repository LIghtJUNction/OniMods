# OniOnline.Framework

.NET Standard 2.0 multiplayer framework and SDK. This assembly has no Unity, ONI, PLib or DLC reference.

The `OniOnline` game adapter supplies native UI, save/load, Steam and world access. The bundled `OnePlanetPerPlayer` gameplay uses the same public interfaces as third-party gameplay extensions.

API documentation and current limits are included in `docs/SDK.md`, `docs/SDK-MOD-SYNC.md`, and `docs/FRAMEWORK-COMPATIBILITY.md` in the package, or in the [repository documentation](https://github.com/LIghtJUNction/OniMods/tree/main/mods/OniOnline/docs). The API is a development preview.

Build without installed game files: `dotnet build sdk/OniOnline.Framework -c Release`. Package locally with `dotnet pack sdk/OniOnline.Framework -c Release -o dist/sdk`.

Use the framework supplied by OniOnline at runtime; other mods must not bundle their own competing copy.
