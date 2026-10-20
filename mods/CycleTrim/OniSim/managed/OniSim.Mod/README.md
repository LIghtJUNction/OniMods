# CycleTrim OniSim loader

This loader is experimental and default-disabled. The CycleTrim package does not
include its native library by default. Build an experimental Linux x86_64 package
with `IncludeOniSim=true` only when testing.

Both the CycleTrim PLib Mod option and an explicit startup
`CYCLETRIM_ONISIM_ENABLE=1` are required for mapping. A missing or disabled
setting leaves the official SimDLL intact. For an isolated standalone loader,
the environment variable is still required. Use disposable test saves only;
restarting ONI is required to return to the official library once mapped.
Building the project does not launch or install the game.

The `KMod.UserMod2.OnLoad` entry leaves official `Sim` and `SimMessages`
methods intact and installs no Harmony patches. The loader performs a single
assembly-scoped mapping only after version and native checks. No private Mono
layout, detour or extra managed Grid mirror is used.

`CYCLETRIM_ONISIM_ENABLE` accepts `1`, `true` or `on` to allow a previously
selected PLib option; `0`, `false` or `off` disables it. Unknown nonempty
values are refused. Missing values always disable mapping even if a previously
saved PLib configuration says enabled. `CYCLETRIM_ONISIM_NATIVE_PATH` can
specify an absolute override; otherwise the library is expected at
`OniSim/liboni_sim_core.so` next to the CycleTrim assembly.
These switches do not establish gameplay compatibility.

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
