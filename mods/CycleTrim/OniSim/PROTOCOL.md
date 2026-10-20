# OniSim direct FFI contract

The implementation uses the official C ABI and message layouts. Rust owns the
physics state and published arrays. C# borrows pointers; there is no OSB1 envelope,
sequence protocol, patch response or managed Grid mirror. The retired protocol
is kept only in [history/OSB1.md](history/OSB1.md).

This is a developing replacement. Matching names and layouts does not establish
behavioral or Mod compatibility. `gameReady=false` and `runtimeValidated=false`
remain until their acceptance gates are satisfied.

## Official evidence

The installed Linux x64 binaries were inspected statically using `nm`, `ilspycmd`,
`objdump`, and targeted Ghidra analysis. Their fingerprints and sixteen plain C
exports are in [official-abi.json](official-abi.json). Decompiled implementation
source stays outside the repository. No community Rust simulator source, binary
or runtime dependency is used.

## Calls and status

Declarations, including the seven ConduitTemperatureManager exports, are in
[native/include/oni_sim.h](native/include/oni_sim.h). The target is little-endian
Linux x86_64. The development artifact is `liboni_sim_core.so`.

`SIM_HandleMessage(id, length, payload)` accepts one message.
`SIM_HandleMessages(id, length, count, payload)` accepts consecutive messages with
one shared id; length is the stride of each record. The official entry at
`0x31d00` calls the single-message handler repeatedly and returns its last result.
Null is not a universal failure indicator. NewGameFrame regions instead form one
payload containing consecutive 28-byte region records.

| Project extension | Result |
| --- | --- |
| ONISIM_GetAbiVersion | u32, currently 1 |
| ONISIM_TryInitialize | Exclusive acquisition; same callback shape as SIM_Initialize, result in status |
| ONISIM_GetLastStatus | Thread-local i32 status |
| ONISIM_GetLastError | Borrowed null-terminated UTF-8 error string |
| ONISIM_GetGeneration | u64 lifecycle revision |
| ONISIM_GetCapabilities | Borrowed static UTF-8 JSON capability description |
| ONISIM_GetGrid | Borrowed pointer to the 496-byte descriptor |
| ONISIM_CopyGridImage | Explicit snapshot, i32 byte count or -1 |

Statuses: 0 success, 1 invalid argument, 2 unsupported, 3 invalid lifecycle,
4 resource limit, 5 internal failure, 6 buffer too small. Check status immediately
after an operation. LastStatus/LastError preserve it; other successful exports
clear it. An error string lasts until the next operation on that thread.

Rust panics are contained at the foreign boundary. Invalid foreign addresses and
process-level allocation failure are not made safe by catching a Rust panic.
Payloads must designate readable memory for their lengths. Pointer-bearing
messages also require their nested arrays to remain valid until the call returns.
Length/address bounds and overlap checks cannot prove a foreign allocation exists.

## Ownership and synchronization

There is one active native simulation per process. The official SIM_Initialize
resets the existing simulation: the inspected game's save-load path calls it
twice. This invalidates borrowed views of the old session. The standalone C#
facade instead uses ONISIM_TryInitialize, which refuses an existing session
without resetting it or transferring ownership. Its caller must not shut down
the other session after such a refusal. Shutdown status 0/4 establishes release;
status 5 does not. C# retains its callback root and lease when ownership or cleanup
is uncertain. Generation never wraps.

External pointer reads must be serialized with mutation and shutdown. A native
lock cannot protect a pointer after it has been returned. Callers must not write
through borrowed arrays or use them as FFI input/output buffers.

Commands are being integrated as per-frame owned queues with the official stage
order. The worker's physics arrays, the two GameData batches, and a caller's
published view have distinct lifetimes. See [FRAME-SYNC.md](FRAME-SYNC.md) for
ready barriers, visibility handoff, zero-time frames and publication semantics.
Prepare returns a completed batch; it is not a request to simulate that batch again.

## Messages and serialized formats

The authoritative static catalogue is [OFFICIAL-MESSAGES.md](OFFICIAL-MESSAGES.md):
62 ids, payload sizes, pointer-bearing fields, output records and startup chains.
The runtime capability list describes implemented paths, not tested conformance.
Implementation and integration are still in progress. An unsupported operation
must report an error rather than silently acknowledge simulated progress.

The C# generic dispatcher validates memory/length boundaries. Native code owns
wire semantics. This avoids a second managed whitelist rejecting newly implemented
ids. Heterogeneous managed batches execute sequentially and have no rollback
promise; a later failure reports how many earlier calls completed. Do not infer
atomicity from the shape of a batch API.

Initialization images have a 14-byte header: width/height i32, seed u32,
radiation/headless bool8. They contain separate arrays of N Cell records (16 B),
N DiseaseCell records (12 B), and N Backwall records (12 B). Meaningful content is
14+40*N bytes. The official writer passes buffer capacity, so zero capacity padding
must be accepted. Disease runtime growth is initialized by native behavior, not
assumed to be a serialized live field.

Element tables contain count i32, count*164-byte records, then i32-length UTF-8
names. The official writer includes buffer capacity; zero trailing padding is
accepted. Disease tables contain disease/element counts, then each name, 76-byte
header and 29-byte record per element. Their writer uses GetBuffer storage with
actual stream Length; trailing bytes are not part of that message. All metadata
float bit patterns are retained, rather than imposing guessed physical limits.

Load receives a raw native save image. BeginSave returns a native-owned image
that remains borrowed until EndSave. Its format and version handling are described
in [SAVE-FORMAT.md](SAVE-FORMAT.md). Saving is not a command-queue flush.

## Published data

Linux x64 GameDataUpdate uses Pack=4 and is 496 bytes:

- i32 completed-frame count at 0.
- Twelve grid/backwall pointers at 4+8*k.
- Twenty-nine count/pointer event spans at 100+12*k.
- Accumulated flow pointer at 448.
- Five property texture pointers at 456, 464, 472, 480 and 488.

Full field and event layouts live in the catalogue and C header. Events are
coherent with their batch grid and component outputs. The property textures are
CPU buffers consumed by the game's texture upload, not guessed graphics handles.
The conduit output separately uses three Pack=4 count/pointer pairs, 36 bytes.

CopyGridImage returns the required size for null destination or zero capacity.
Negative capacity fails. An undersized nonempty destination is not written.
Snapshot does not advance physics; whole-grid copying is an explicit operation.

## Evidence boundary

Current work is static inspection and compilation under the user's instruction
to finish development before testing. No current behavior, speedup, game launch,
Mod acceptance or deployed library is claimed. Earlier build and OSB1 demo
receipts are historical and do not validate the changing source tree.
