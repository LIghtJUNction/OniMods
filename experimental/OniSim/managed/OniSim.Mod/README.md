# OniSim.Mod experimental loader

This is a default-disabled source prototype, not an accepted ONI integration.
It has not been installed or run in the game. `gameReady=false` still applies.
Building the project does not load Mono, the native core, or the game and does
not copy anything into the ONI installation or mods directory.

The `KMod.UserMod2.OnLoad` entry keeps the official `Sim`, `SimMessages`, and Mod
call surfaces intact. It installs no Harmony patches. When explicitly enabled,
it attempts one assembly-scoped mapping of the official `SimDLL` import name
to the caller's absolute native library path. No private Mono layout, detour,
custom message protocol, or managed Grid mirror is used.

Default behavior reads `ONISIM_EXPERIMENTAL_ENABLE`; unless its value is exactly
`1`, the loader returns without opening native modules or calling Mono APIs.
An enabled attempt also requires `ONISIM_EXPERIMENTAL_NATIVE_PATH` to name an
explicit absolute library file. These switches are intended for a later,
separately authorized runtime investigation. They are not evidence that gameplay
is safe or supported.

The preflight requires Linux x64 Mono and the exact inspected game assembly and
Unity Mono fingerprints in `OfficialProfile.cs`. It requires no `Game` instance,
gets the existing Mono module with `RTLD_NOLOAD`, and enumerates loaded assemblies
to locate the exact `Assembly-CSharp` MonoImage by filename. It rejects missing
images, unsupported profiles, missing exports, and non-v1 native ABI before the
single mapping insertion. All 16 official `SimDLL` imports are required, including
the seven conduit-temperature functions. The shared objects remain resident.

Mono documents `mono_dllmap_insert` as accepting a specific image or a global
mapping; this implementation always requires a nonnull image. Mono's resolver
also caches a resolved P/Invoke address and returns it before consulting a new
mapping. Therefore the loader cannot repair an already resolved import. These
behaviors are visible in the [official Mono native-library source](https://github.com/mono/mono/blob/main/mono/metadata/native-library.c).

The supplied ONI Mono ELF exports a nonstub `mono_dllmap_insert`, but that static
evidence does not prove `OnLoad` runs before all relevant P/Invoke resolutions,
that an insertion affects this Unity build as expected, or that native behavior
satisfies the game's complete call sequence. The `Game`-instance check is only
a startup guard; it does not prove an empty P/Invoke cache. Loader output says
`MappedUnverified`, never ready or validated. Removing or changing a mapping
requires restarting the process; there is no hot unload or rollback operation.
Repeated configuration reports the existing mapping even if the enable or path
environment variables have since changed.

Required runtime gates remain: fresh-process import timing, all 16 import
destinations, initialization and shutdown, real official element/disease tables,
frame handoff and event lifetimes, save/load, conduit behavior, supported DLCs,
and failure handling. Until those are exercised, this project is source and
build evidence only. Other Mods with their own assembly-level `SimDLL` imports
are outside this mapping; calls they make through official managed methods
retain that managed surface.

The mapped official `SIM_Initialize` keeps the game's reset/reinitialization
semantics. The standalone facade acquires an exclusive session through the
separate `ONISIM_TryInitialize` extension. Do not open a standalone session in
the same process as a mapped game session; both would address the same native
global state. Loader preflight requires the extension's presence but never
calls it to take ownership away from the game.

Compile against local official references, for example by setting
`OniSimGameAssemblyPath` and `OniSimHarmonyPath` when building this project. Both
references have `Private=false`; game and Harmony assemblies are not packaged.
The native core is not copied or replaced by this project. No Steam upload or
installation target is defined.
