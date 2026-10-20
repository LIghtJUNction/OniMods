# Historical OniSim OSB1 protocol, version 1

This is the retired prototype contract associated with `../first-run.json`.
The current implementation uses direct FFI; see `../PROTOCOL.md`. The historic
demo results do not validate the replacement implementation.

This is an independently written experiment based on the installed official ONI
managed/native interface. Do not read or incorporate community Rust SimDLL source.
The experiment is not installed into ONI and is not yet a replacement simulator.
Its first milestone is native Rust grid storage and ordered official-message
transport through a separate C# DLL. Physics stepping, callbacks, save/load and
game/mod acceptance remain required before any game integration.

## Evidence boundary

Official Linux x64 binaries inspected with file/nm/ilspycmd:

- `libSimDLL.so`: SHA-256 `85111cd0b1fc14c79c785581d6c055280b9d48ba705776f3d14eb0a7b239c12d`.
- `Assembly-CSharp.dll`: SHA-256 `6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1`.
- The managed `Sim` boundary uses Cdecl `SIM_HandleMessage(i32 id, i32 len,
  byte* payload) -> void*`, initialization with a reverse callback, shutdown,
  and a Pack=4 `GameDataUpdate` pointing to Grid arrays.

These are static interface observations, not observed execution. Official binaries
and decompiled implementation source stay outside the repository. No community
Rust simulator is a build or runtime dependency.

## Native transport

The new transport is deliberately named separately from the official exports:

```c
uint64_t osb_create(void);
int32_t osb_exchange(uint64_t handle, const uint8_t* request, uint32_t request_len,
                     uint8_t* response, uint32_t response_capacity,
                     uint32_t* response_len);
int32_t osb_destroy(uint64_t handle);
uint32_t osb_protocol_version(void);
```

Handles are monotonically allocated integers, never exposed Rust addresses.
Calls for a session are serialized. Request/response pointers are borrowed only
for the call; Rust and C# never retain each other's movable managed pointers.
The transport contains no pointer-bearing official messages.

Native return: 0 = a response was written (inspect its application status), -1 =
invalid handle, -2 = response too small, -3 = invalid pointer/length, -4 = internal
panic/poisoned state. On -2, response_len is the required capacity and **no command
has executed**. A null response with capacity zero is a non-mutating size query.
Caller buffers must be valid, non-overlapping regions for their stated lengths.

## Frame

All fields and payload scalars are little endian. Header is exactly 40 bytes:

| Offset | Type | Field |
| --- | --- | --- |
| 0 | u32 | magic `0x3142534f` (ASCII OSB1) |
| 4 | u16 | version = 1 |
| 6 | u16 | headerBytes = 40 |
| 8 | u64 | sequence, nonzero and increasing for accepted requests |
| 16 | u32 | operation |
| 20 | u32 | payloadBytes, must equal the remaining frame length |
| 24 | u32 | recordCount |
| 28 | u32 | flags: request 0, response 1 |
| 32 | u64 | expected generation; response contains current generation |

Each new session starts at generation 1. Describe permits generation 0 for initial
discovery; other operations require the current generation. Allocating/resetting
storage increments it. Responses repeat request sequence and operation. A rejected
request and a size query do not advance sequence or generation.

Operations: 1 Describe, 2 SingleMessage, 3 MessageBatch, 4 Snapshot, 5 Shutdown.
Describe/Snapshot/Shutdown have no payload and recordCount=0. SingleMessage has
one record. A batch has 1..4096 records. Each record is i32 officialMessageId,
u32 payloadBytes, followed by that many bytes, without implicit padding.
Frame payload is limited to 64 MiB; a grid is limited to 1,048,576 cells.

Response payload begins with i32 application status, u32 executedRecords,
u32 resultKind. Statuses: 0 OK, 1 bad frame, 2 unsupported operation/message,
3 bad lifecycle state, 4 stale generation, 5 invalid payload, 6 stale sequence,
7 configured limit. Result kinds: 0 no data; 1 UTF-8 capability JSON; 2 grid image;
3 cell patches; 4 UTF-8 error text. Native errors never count as success.

All record framing, unknown message IDs and unsupported flags are checked before
executing a batch. Batches are ordered, not rollback transactions. Other execution
errors can return a partial executedRecords count; clients must not blindly replay.
The initial implementation may reject an unsupported batch before any work.

## Official message subset

- AllocateCells = `1092408308`: width:i32, height:i32, radiation:u8,
  headless:u8; exactly 10 bytes, positive bounded dimensions, boolean bytes 0/1.
- SimData_InitializeFromCells = `2062421945`: the grid image below.
- Start = `-931446686`: empty. Locks grid dimensions for this session until shutdown.
- NewGameFrame = `-775326397`: its 28-byte region payload is known, but **must return
  unsupported** until independently recovered physics and scheduling are implemented.
- ModifyCell = `-1252920804`: 28 bytes: cellIdx:i32@0, callbackIdx:i32@4,
  temperature:f32@8, mass:f32@12, diseaseCount:i32@16, elementIdx:u16@20,
  replaceType:u8@22, diseaseIdx:u8@23, addSubType:u8@24, padding@25..27.
  Initially support explicit absolute replacement only: callbackIdx=-1,
  replaceType=1, addSubType=1, elementIdx!=65535, finite nonnegative mass and
  temperature. Reject special/default-temperature and displacement behavior.
  This subset follows official ReplaceElement/InvalidCallback metadata;
  element-specific clamping and displaced matter are not implemented.
- Unknown messages, pointer payloads, save/load and callbacks remain unsupported.

Describe reports the supported IDs, capabilities and current generation. It must
report gameReady=false and simulationStep=false for this milestone. No call may
silently acknowledge a missing physics operation.

## Grid image and patches

Use the official InitializeFromCells serialized layout: width:i32, height:i32,
seed:u32, radiation:u8, headless:u8 (14-byte prefix), followed by N Cell records,
N DiseaseCell records, then N Backwall records, where N=width*height. Total size is
14+40*N. Do not interleave the three arrays.

Cell: u16 elementIdx@0, u8 properties@2, u8 insulation@3, u8 strengthInfo@4,
padding bytes@5..7,
f32 temperature@8, f32 mass@12 (16 bytes). DiseaseCell: u8 diseaseIdx@0,
reserved bytes@1..3, i32 diseaseCount@4, f32 reservedError@8 (12 bytes).
Backwall: u16 elementIdx@0, reserved bytes@2..3, f32 mass@4, f32 temperature@8
(12 bytes). Official Cell.Write serializes properties and strengthInfo as zero;
the transport preserves supplied bytes rather than inventing those flag semantics.
Preserve reserved bytes through a round trip. Floating-point values
must be finite; validate physical bounds only where official semantics are known.

Cell patches are u32 patchCount followed by patchCount records of u32 cellIndex,
16 Cell bytes, 12 DiseaseCell bytes, 12 Backwall bytes (44 bytes per patch).
Allocation/initialization/Start return a full image; isolated supported cell writes
can return patches. Snapshot returns a full image and never advances simulation.

## Managed compatibility boundary

Build a game-independent `OniSim.Compatibility.dll` targeting netstandard2.0.
It owns the native handle, sequence and generation, checks all response envelopes,
and exposes official AllocateCells/InitializeFromCells/Start operations over OSB1.
A native load must use the caller's explicit library path, not replace SimDLL files.
Dispose shuts down then releases its own native handle. No finalizer calls into an
already-unloaded library. Session failure must not be reported as successful work.

C# owns stable unmanaged Grid mirrors and a 496-byte Linux x64 Pack=4 descriptor;
populate confirmed Grid fields only and zero unsupported event/texture fields.
Never present the incomplete descriptor as a game-ready replacement. Do not let
client code use a disposed or superseded view. Integration with Harmony/official
Mod entry points is gated on completed ABI, event and simulation behavior.
