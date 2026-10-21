# CycleTrim OniSim loader

This loader is part of CycleTrim's OniSim core integration. Linux x86_64 builds
include the native library by default, and the PLib CycleTrim Mod option enables
the mapping by default. Turn the option off to keep the vanilla SimDLL mapping;
the change requires restarting ONI. The mapping and game
behavior remain unverified; `gameReady=false` still applies. Building the project
does not load Mono, the native core, or the game and does not copy anything into
the ONI installation or mods directory.

The `KMod.UserMod2.OnLoad` entry keeps the official `Sim`, `SimMessages`, and Mod
call surfaces intact. It installs no Harmony patches. Unless explicitly disabled,
it attempts one assembly-scoped mapping of the official `SimDLL` import name
to the bundled native library or the caller's absolute override path. No private Mono layout, detour,
custom message protocol, or managed Grid mirror is used.

`CYCLETRIM_ONISIM_ENABLE` remains a startup override: set it to `0`, `false`, or
`off` to force-disable mapping; `1`, `true`, or `on` permits it when the PLib
option is enabled. Other nonempty values are refused. `CYCLETRIM_ONISIM_NATIVE_PATH` may name an absolute library override;
when unset, the loader uses `OniSim/liboni_sim_core.so` beside the Mod assembly.
These switches control mapping; they do not establish gameplay compatibility.

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
`MappedUnverified`, never ready or validated. Disabling or changing a mapping
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
