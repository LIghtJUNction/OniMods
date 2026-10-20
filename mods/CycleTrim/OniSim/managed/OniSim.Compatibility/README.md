# OniSim.Compatibility

An experimental .NET Standard 2.0 facade over this project's native core. It
binds the official `SIM_*` and `SYSINFO_*` C ABI plus `ONISIM_*` extensions
directly with cdecl delegates. It does not load game assemblies or the official
native simulator. It is not installed in ONI and has no runtime acceptance yet.
Only Linux x64 little-endian is supported. `Open` requires the caller's explicit
absolute path to `liboni_sim_core.so`.

This standalone facade does not currently take over the game's `Sim` or
`SimMessages` calls. CycleTrim's separate
[OniSim.Mod loader](../OniSim.Mod/README.md) remains default-disabled. Mapping
requires both an explicit PLib opt-in and `CYCLETRIM_ONISIM_ENABLE=1` before
startup; the latter is also required for the standalone loader. Neither path
has passed real-game lifecycle or save acceptance. Disable mapping and restart
to return to the official SimDLL.

`SendMessage` forwards any message ID and owned byte payload to native validation.
Managed checks cover nulls, buffer lengths, and allocation bounds; the native
endpoint decides wire format, numeric validity, lifecycle, and supported IDs.
This avoids a second managed whitelist blocking newly implemented official
messages. `OfficialMessage` copies its input and exposes copied payloads. A
pointer embedded inside a payload is not owned or extended by that copy; its
validity and lifetime follow the native message contract.

The typed helpers serialize official messages:

| Method | Native operation |
| --- | --- |
| `AllocateCells` | Allocate public Grid storage after element registration. |
| `InitializeFromCells` | Initialize from the official `14 + 40*N` cell image. |
| `Start` | Start the initialized world and return its public Grid view. |
| `ReplaceCell` | Queue a 28-byte absolute replacement without a callback. |
| `NewFrame` | Submit `28*N` bytes of official active-region records. |
| `Prepare` | Submit one raw visibility byte per public cell and return a view. |
| `Save` | Copy the raw blob held by `SIM_BeginSave`, then call `SIM_EndSave`. |
| `Load` | Send a raw save blob through official Load. |
| `Snapshot` | Explicitly copy the last published public Grid cell image. |

Register complete official element metadata with `SendMessage` before allocation
or initialization; the native world requires its special elements. Register
matching disease metadata before the native phases that require it. The facade
does not invent element tables, disease tables, or physical meaning for indices.

Replacement and component messages queue work. `NewFrame` seals/submits a frame;
it does not imply that public Grid arrays have changed. `Prepare` performs the
native publication handoff and supplies the next visibility input. Internal
FrameSync and double GameData buffering belong to the native scheduler: a
Prepare result can report zero completed frames or only a completed prefix.
The facade neither drains pending frames nor repeats a physics step to manufacture
an expected result. Inspect the returned descriptor and native capabilities.

`ActiveRegion` serializes `elapsedSeconds:f32`, `minX/minY/maxX/maxY:i32`, then
sunlight and cosmic radiation `f32`, in that order. Coordinates are public Grid
coordinates and are passed without clamping. `Prepare` preserves visibility
bytes without normalization.

`Save(x, y)` copies the native save bytes before ending the save lease, including
when copying fails. The result starts with the native `SIMSAVE` header and does
not include the outer i32 length that official managed save containers write.
`Load` accepts that raw blob and leaves placement, version, element mapping, and
the allocated/unstarted lifecycle checks to native code. If native save cleanup
is unknown, ordinary operations are blocked until `Dispose` cleans up the session.

There is no managed Grid mirror, transport envelope, response kind, or patch
application. `GridView.GetDescriptor()` copies only the official 496-byte x64
Pack=4 descriptor. Its 12 array pointers refer directly to Rust-owned SoA storage.
`DescriptorPointer` borrows the native descriptor itself. A view expires on
disposal or generation change; each facade access checks that boundary. Already
returned pointers cannot be revoked. Callers must synchronize their access and
never dereference them after that boundary. Publication can replace descriptor
event arrays without changing the Grid generation; consume event pointers before
the next native publication call. The descriptor is not proof of ONI integration.

The native API has one process-wide simulation. Each loaded facade has a static
lease and lock; only one facade loading context is supported. A second `Open`
fails while a session owns the lease. `Open` calls `ONISIM_TryInitialize`, whose
exclusive initialization rejects an already active simulation; a rejected
`Open` never shuts down that existing simulation. The official `SIM_Initialize`
entry deliberately retains the game's reset/reinitialization semantics and is
not used to acquire the standalone facade's lease. The shared object stays
resident for the process lifetime.

Failed construction releases its lease only after the documented native
`InvalidState`/`ResourceLimit` rejection or confirmed shutdown. `InternalFailure`
and unknown initialization outcomes retain the root and lease; restart the process
before opening another session. Shutdown statuses 0 and 4 confirm cleanup. Other
shutdown failures retain the lease and callback root, and an existing session can
retry `Dispose`. There is no finalizer or native call from garbage collection.
A forgotten `Dispose` retains the lease. Do not mix the facade with independent
direct calls into the same native global state.

The optional `Open(path, callback)` roots the cdecl callback until shutdown.
Reentry into the facade is rejected, including on the same thread. Callback
exceptions are reported after the native call and never unwind across native
code. Callback registration and published event records are separate contracts;
read native capabilities for actual callback delivery support.

`SendBatch` checks all managed buffers, then calls `SIM_HandleMessage` for each
record in order under one lock. Native rejection can occur after a prefix has
executed. The batch is not atomic and has no rollback. `BatchResult` and operation
exceptions preserve `ExecutedRecords`; exceptions also copy signed native status
and UTF-8 error text before another export can overwrite them. Snapshot returns
published state, so queued work may require the normal frame/publication flow
before its effects are visible.

The bound official homogeneous `SIM_HandleMessages(id, len, count, payload)` uses
`len` as each record's stride and returns the final single-message pointer.
Heterogeneous managed batches deliberately use ordered single-message calls.

`OniSim.Host` now exposes only `describe`. The previous `grid-demo` and
`batch-demo` fixtures assumed missing tables and immediate replacement, so they
were retired. Building the projects does not execute the host or load native
code. Capability flags describe implementation support, not measured correctness
or game readiness.
