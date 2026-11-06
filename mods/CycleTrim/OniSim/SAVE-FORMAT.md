# Official native save contract

This interface and binary-format description was originally derived by static
inspection of the local official Linux x64 binaries. Those observations alone
were not runtime evidence. Subsequent authored native fixtures and disposable
colony tests are recorded separately in [conformance/README.md](conformance/README.md)
and [RUNTIME-VALIDATION.md](RUNTIME-VALIDATION.md), with exact binary identities.

Sources inspected statically on 2026-10-20:

- `/tmp/re/oni-simdll/official-libSimDLL.so`, SHA-256
  `85111cd0b1fc14c79c785581d6c055280b9d48ba705776f3d14eb0a7b239c12d`.
- `/tmp/re/oni-simdll/official-Assembly-CSharp.dll`, SHA-256
  `6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1`.

Offsets below are ELF virtual addresses in that native file. Managed observations
refer to named methods in that managed file. Decompiled official implementation
source is not part of this repository.

## API ownership and managed framing

`SIM_BeginSave(int32_t *size, int32_t x, int32_t y)` at `0x33180` returns a
native-owned byte buffer. `*size` is its byte count. A new BeginSave first releases
the previous buffer. `SIM_EndSave()` at `0x324b0` frees it and clears the retained
pointer; calling EndSave without a retained buffer has no effect. A caller must
copy the bytes before EndSave, another BeginSave, or session teardown.

The pinned writer directly traverses mutable simulation arrays; this entry
contains no FrameSync wait or worker join. Callers must synchronize simulation
before saving. The Rust writer holds its Engine mutex during serialization and
retains a separate owned image afterward; this does not make an unsynchronized
caller-held pointer valid across EndSave or Shutdown.

Authored native fixtures exercise eight consecutive BeginSave replacements
without an intervening EndSave, including absent-backwall normalization, and
three replacements with positive or negative header origins. Every image is
copied before the next BeginSave, and its size/hash is compared with the pinned
writer. Origin probes test serialized header bytes only; negative origins are
not accepted as valid load destinations. The driver releases the image in a
finally block even when reading or validating it fails.

Managed `Sim.Save(BinaryWriter, x, y)` copies that buffer, calls EndSave, then
writes an outer little-endian `i32 blobLength` followed by the blob bytes.
The outer length is **not** part of the native format. Both `Sim.Load` and
`Sim.LoadWorld` read this outer length and send exactly those bytes as message
`Load = -672538170`. Native `Load(BinaryBufferReader&)` is at `0x320b0`.
A null native return means failure to managed code; a non-null return means
success. The successful pointer identifies the native simulation state, not a
new caller-owned object or a grid descriptor.

The native blob has no compression, checksum, element table or disease table.
The complete colony file has separate managed framing, serialization metadata,
settings, optional compression, object state and streamed fields. Managed
`SaveLoader.Save(BinaryWriter)` writes `world`, serialized `SaveFileRoot`, game
settings, this length-prefixed sim blob, the save manager's objects, then game
state. A native blob alone is not an ONI colony file.

## Current writer: version 15

All scalar encodings below are little endian. Floats are IEEE 754 binary32.
Let `W` and `H` be the internal native grid dimensions and `N = W * H`.

| Blob offset | Bytes | Value |
| --- | ---: | --- |
| `0` | 8 | ASCII `SIMSAVE` followed by a zero byte |
| `8` | 4 | Signed version, currently `15` |
| `12` | 4 | Signed internal width `W` |
| `16` | 4 | Signed internal height `H` |
| `20` | 4 | Signed destination X offset, the BeginSave `x` argument |
| `24` | 4 | Signed destination Y offset, the BeginSave `y` argument |
| `28` | 1 | Saved options bit field |
| `29` | `16 * N` | Primary cell records |
| `29 + 16*N` | `8 * N` | Disease records |
| `29 + 24*N` | `12 * N` | Backwall records |

The total native blob size is `29 + 36*N`. BeginSave computes this size from the
current internal dimensions; `x` and `y` do not choose a rectangle to crop.

Each section contains all `N` cells in row order, with the X coordinate changing
first. The sections are separate; a writer must not interleave primary cell,
disease and backwall records into one per-cell structure.

Primary cell record (`CellWrite` at `0x31d60`):

| Record offset | Type | Value |
| --- | --- | --- |
| `0` | `i32` | Stable element hash from the current element table |
| `4` | `f32` | Temperature |
| `8` | `f32` | Mass |
| `12` | `f32` | Radiation |

Disease record (`DiseaseWrite` at `0x32440`):

| Record offset | Type | Value |
| --- | --- | --- |
| `0` | `u32` | Stable disease hash; zero means no disease |
| `4` | `i32` | Disease count |

Backwall record (`BackwallWrite` at `0x31f10`):

| Record offset | Type | Value |
| --- | --- | --- |
| `0` | `i32` | Stable backwall element hash |
| `4` | `f32` | Mass |
| `8` | `f32` | Temperature |

BackwallWrite handles an invalid internal element index (`65535`) by writing the
Vacuum hash and zero mass/temperature. It also clears the source backwall's mass
and temperature, while retaining its invalid element index. The primary cell
writer expects its index to address an existing element table entry.

Properties, insulation, strength, accumulated flow, native component handles,
callback registrations, queues, RNG state and frame counters are not serialized
by this native writer. This is an observation about the official format, not
permission to omit the corresponding runtime behavior from a replacement.

## Internal dimensions and placement

Managed `Sim.AllocateCells(width, height, headless)` sends a 10-byte payload:
`i32 width`, `i32 height`, `u8 radiationEnabled`, `u8 headless`.
`AllocateCells` at `0x31720` creates an internal grid of
`W = width + 2`, `H = height + 2`. The additional cells form the native boundary.
`SimData::SimData` at `0x3e470` stores those padded dimensions. BeginSave writes
all padded cells, including that boundary. Saving only the managed visible
`width*height` cells would not reproduce the official blob.

`CellSOA::CellSOA` at `0x3b2d0` initially fills the full primary storage with
element index `65535`, temperature/mass/radiation zero, properties zero,
insulation `255`, strength byte `132` (`0x84`), disease index `255`, disease count
zero and zero disease bookkeeping. ClearUnoccupiedCells subsequently replaces
the invalid primary element indices with Vacuum. The backwall constructor uses
Vacuum as its default element and initializes mass and temperature to zero.

`SimData::InitializeBoundary` at `0x3d7a0` sets both primary buffers' bottom row
and side columns to Unobtanium, and the top row to Vacuum, including its corners.
All these boundary primary cells receive mass `9999`, temperature zero, disease
index `255` and disease count zero. It sets the backwall boundary to its default
Vacuum element with zero mass and temperature. This helper does not reset the
primary boundary radiation field. Start calls it after any legacy thermal
migration and before copying the active primary buffer.

`SimData_InitializeFromCells` at `0x3f580` places the supplied external cells at
internal `(column + 1, row + 1)`, initializes primary radiation to zero, copies
the supplied properties/insulation/strength bytes, places disease and backwall
data in the same interior, settles thermal boundaries and initializes the outer
boundary. This `+1` interior placement is different from Load's explicit blob
offset placement because save blobs already include their own boundary.

Loading does not allocate or resize the target grid. For source record
coordinates `(column, row)`, native Load places the record at internal index
`(xOffset + column) + (yOffset + row) * targetInternalWidth`. It uses the existing
target width as the destination row stride. The source width/height specify how
many records to consume; they are not the target dimensions.

`SaveLoader` saves an ordinary colony using offsets `(0, 0)`. World generation's
`ProcGenGame.WorldGenSimUtil` uses each world's `data.world.offset.x/y`, then
`SaveLoader.LoadFromWorldGen` allocates the combined cluster grid and loads the
individual world blobs before Start. This explains why offsets are stored in
the blob and why multiple loads into one allocated grid are meaningful.

## Older versions read by this official build

Native Load rejects a wrong magic or a signed version greater than 15. It has no
explicit minimum-version check. The following are the observed branches; the
presence of a branch does not establish that every historical version is a
supported or meaningful external input.

| Version | Header bytes | Primary bytes/cell | Disease bytes/cell | Backwall bytes/cell | Discarded legacy bytes/cell |
| --- | ---: | ---: | ---: | ---: | ---: |
| `< 8` | 20 | 12 | 0 | 0 | 0 |
| `8` | 20 | 12 | 0 | 0 | 4 |
| `9..12` | 20 | 12 | 8 | 0 | 4 |
| `13` | 21 | 12 | 8 | 0 | 4 |
| `14` | 29 | 16 | 8 | 0 | 4 |
| `15` | 29 | 16 | 8 | 12 | 0 |

- Versions before 14 have no stored offsets; both default to zero.
- Versions before 13 have no saved-options byte; it defaults to zero.
- Versions before 14 have no primary radiation field. It remains at the
  allocated/cleared value except for later load normalization.
- Versions before 9 do not contain disease records.
- Versions 8 through 14 end with an uninterpreted `4*N` legacy block. The reader
  skips it after the cell and disease sections. Its historical meaning is not
  established by the inspected reader.
- Versions before 15 contain no backwall section.
- Versions at most 10 set a legacy thermal-boundary flag at native state offset
  `0x718`. Start uses `SimData::SettleThermalBoundaries` (`0x3d3c0`) before normal
  initialization. Ignoring the flag loses an observed migration step.

The official primitive reader advances without per-field bounds checks. Its
final Load assertion only checks that consumed bytes are at least the supplied
length. A replacement should validate size, dimensions, offset arithmetic and
destination bounds before mutation; unsafe reads and writes are not required
compatibility behavior. Exact malformed-input behavior has not been executed.

## Element and disease remapping

Cells and backwalls save stable element hashes, not their transient `u16`
runtime indices. The current element table must already exist before loading.
`GetElementIndex` at `0x36b70` maps the hash to the current table index, returning
`65535` when the hash is absent.

`CellRead` at `0x31de0` has two legacy hash remaps before table lookup:

| Stored hash | Replacement hash | Current managed name |
| --- | --- | --- |
| `0x0280cf79` (`41996153`) | `0x987dac06` (`-1736594426`) | `Cuprite` |
| `0x2391c22b` (`596754987`) | `0x4c76ae31` (`1282846257`) | `MaficRock` |

The old stored names are not present in the inspected `SimHashes` type and are
not inferred here. An unknown primary element becomes Vacuum and its
temperature, mass and radiation become zero. `BackwallRead` at `0x31fb0` performs
the ordinary element hash lookup; it does not apply these two legacy remaps or
the primary unknown-element fallback in that helper.

`DiseaseRead` at `0x32020` maps a nonzero hash through the current disease table.
Hash zero or an absent hash becomes index `255` and count zero.
`Disease::GetDiseaseIndex` at `0x343e0` searches table records and uses `255` as
the unavailable result. Merely saving runtime disease indices is incompatible
with disease table reorderings.

After each primary record, Load normalizes temperature at most zero to 293 K
and radiation at most zero to zero; Void and Vacuum have temperature/mass/
radiation cleared. It then performs `DoLoadTimeStateTransition` at `0x54f40`,
which depends on the current element transition table. Exact temperature
transition behavior therefore matters during load as well as during stepping.

Allocation caches these required special element indices:

| Name | Stable hash |
| --- | --- |
| `Void` | `-1456075980` (`0xa9360b34`) |
| `Vacuum` | `758759285` (`0x2d39bf75`) |
| `Unobtanium` | `1838482828` (`0x6d95058c`) |

## Saved options

`SetSavedOptions = 1154135737` carries two bytes: `clearBits`, `setBits`.
Native `SetSavedOptions` at `0x316b0` updates the retained byte as
`(old & ~clearBits) | setBits` when a simulation state exists.
The managed enum defines bit `1` as `ENABLE_DIAGONAL_FALLING_SAND`.

Load calls `SimData::ApplySaveSettings` at `0x3d380`. A zero current byte accepts
the loaded byte; a nonzero current byte must equal it. This matters when several
world blobs are combined into one native grid: their saved options must agree.
Unknown bits should be retained as bits rather than silently erased.

## Managed startup and component dependencies

The observed ordinary-colony load order in `SaveLoader.Load(IReader)` is:

1. Read managed save metadata/settings and set the external grid dimensions.
2. `SIM_Initialize(Sim.DLL_MessageHandler)`.
3. `Elements_CreateTable = 1108437482` from current `ElementLoader.elements`.
4. `AllocateCells = 1092408308` with external dimensions and the radiation and
   headless flags. Allocation's seed comes from current time in this path.
5. `Disease_CreateTable = 825301935` from the current database and element table.
6. `ClearUnoccupiedCells = -1836204275` with no payload. It clears the full
   padded primary buffers to Vacuum with zero mass/temperature/radiation and
   no disease before one or more blobs are placed into them.
7. Send the native Load blob. A `SaveFileRoot.streamed["Sim"]` entry, if present,
   is a separate reader containing the same managed length prefix; otherwise
   the blob is taken from the main save reader.
8. `Start = -931446686`, with no payload, returns a `Sim.GameDataUpdate`.
   Managed Sim.Start binds the grid/backwall pointers, accumulated flow and
   property texture pointers, then calls `Grid.InitializeCells()`.
9. Restore managed prefabs and save manager objects, visible/spawnable/damage
   fields, and game state.
10. `DefineWorldOffsets = -895846551` supplies the cluster's world rectangles:
    `i32 count`, then count repetitions of four `i32` values
    `(worldOffsetX, worldOffsetY, worldSizeX, worldSizeY)`.

World-generation loading defines world offsets before loading each individual
blob and calls Start only after all world blobs have loaded. World-generation
creation instead uses `SimData_InitializeFromCells` and its separate cell-image
format; that message must not be mistaken for this versioned save format.

Native component handles are absent from the save blob. `SimComponent.OnSpawn`
registers again using a callback index, moves its handle from `-1` (absent) to
`-2` (registration pending), then receives the real handle through
`Game.simComponentCallbackManager`. Managed `Sim.IsValidHandle` excludes `-1`
and `-2`; `Sim.GetHandleIndex` selects the low 24 bits. Component add/remove,
modify, callback dispatch and result-array behavior remain required after load.

Concrete managed examples confirm separate reconstruction:

- `DiseaseEmitter` serializes its emission settings, creates a fresh array of
  `-1` handles during OnSpawn, calls AddDiseaseEmitter and then modifies each
  registered emitter in its registration callback.
- `RadiationEmitter : SimComponent` adds a fresh native emitter during
  OnSimRegister and applies its configured emission state after registration.
- `SimTemperatureTransfer` holds a nonserialized handle and registers an element
  chunk from its restored PrimaryElement mass, temperature and element. Its
  temperature accessor reads `elementChunks[handle & 0xffffff]` after registration.

Consequently, complete compatibility needs all of the following: matching
padded-grid storage and Start pointer exports; current element and disease
tables and hash mappings; versioned section parsing and placement; saved
options and legacy migrations; boundary initialization; managed object restore;
native component registration and handle generations; registration callbacks;
element chunk and building thermal result arrays; frame synchronization and
normal component updates. A self-contained custom grid snapshot cannot replace
this combined contract.

## Remaining evidence boundary

The format, offsets and managed call order above are static observations of one
official build. Later differential fixtures author versions 0 through 15, load
them in each implementation, compare published data and current-version save
bytes, reset each session and reload its own current image. They also exercise
legacy thermal boundaries, discarded legacy words and retained radiation across
overlapping loads. They are not a collection of historical colony files or a
cross-implementation exchange of complete colony saves. Repeated EndSave calls
are compared with the official idempotent release. Broad historical-colony,
concurrent-save and multi-world acceptance remain open.
