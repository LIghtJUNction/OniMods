# Jlib consumer bootstrap

Build one bootstrap per game Mod:

```sh
dotnet build sdk/Jlib.Bootstrap/Jlib.Bootstrap.csproj -c Release \
  -p:ConsumerAssemblyName=OniOnline -p:OniManagedPath=/path/to/legitimate/Managed
```

The output is `bin/OniOnline/Release/net48/OniOnline.Bootstrap.dll`. Package that
file as the root `OniOnline.dll`, preserving its assembly identity, and put the
original `OniOnline.dll` in `implementation/`. Other consumers substitute their
original assembly name. Consumer-specific `obj/` and `bin/` paths avoid sharing
generated state between those builds. Consumers do not package Jlib or PLib.

The dependency gate reads the native Mod catalog, selects the single enabled,
installed `LIghtJUNction.Jlib`, and loads only that provider's `Jlib.dll`. It
requires the expected assembly name, source path, ABI 1 and static ID. It also
requires `public static void Jlib.Runtime.JlibApi.RegisterConsumer(Assembly,
KMod.Mod)` and calls it before discovering or constructing implementation types.
The public API must use the consumer's native Mod root for configuration/assets.

The wrapper forwards the original `OnLoad` timing and native owner/path/assembly.
It forwards `OnAllModsLoaded` once and records the implementation in the native
loaded-assembly set without changing the callback dictionary being enumerated.
Missing, disabled, ambiguous or incompatible dependencies leave the implementation
unloaded. A shared BCL queue shows a native confirmation after the main menu exists.
The bootstrap itself has no Jlib reference and does not call `PatchAll`.

The independent host suite runs with API doubles and owned fixture DLLs:

```sh
dotnet run --project tests/OniMods.JlibBootstrap.Tests -c Release
```

It never loads game DLLs. Passing host fixtures and a net48 reference build does
not establish Unity/Mono loading, native UI behavior or a Steam installation.

After building the host executable, inspect a consumer ZIP without loading any
of its assemblies:

```sh
dotnet tests/OniMods.JlibBootstrap.Tests/bin/Release/net10.0/FixtureConsumer.Bootstrap.dll \
  --check-package dist/OniMcp.zip OniMcp
```

This checks root/payload assembly identities, the bootstrap's conditional gate,
external Jlib references, embedded PUtil and bundled shared/game libraries.
The template may omit a direct implementation reference but still needs its gate.
Startup logs contain sanitized stages, exception types and HRESULTs; exception
messages and stack traces are deliberately excluded.
