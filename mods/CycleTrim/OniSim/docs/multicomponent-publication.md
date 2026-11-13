# OniSim multicomponent publication bridge

**Status:** architecture gate; not a production implementation or runtime acceptance receipt.

## Decision

The multicomponent compatibility path must be published from the same completed simulation batch that owns the corresponding legacy grid image. The first implementation should extend the existing two-buffer `GameData` handoff. It should not introduce an unrelated global pointer ring whose lifetime can drift from `Prepare`.

The intended path is:

```text
GPU multicomponent state
  -> validated host-visible publication data
  -> completed Engine batch
  -> GameData output buffer
  -> versioned native view ABI
  -> C# OniSim provider
  -> fail-closed Harmony shims
```

## Current implementation status (2026-11-13)

The native production crate directly depends on `onisim-gpu`. The worker uses
multicomponent gas transport, liquid transport and publication, combining
resident CPU operators with GPU batches; operator selection is not equivalent
to moving the entire simulation onto the GPU.

- Completed publication advertises `MULTICOMPONENT_COEXISTENCE` and the capability
  string `multicomponent-coexistence-v1`.
- `ONISIM_AcquireMulticomponentUpdate` / `ONISIM_ReleaseMulticomponentUpdate` keep
  the native State mutex as a thread-affine read lease while managed code copies
  the records. Acquire and release must run on the same thread.
- Legacy Grid fields remain a dominant-element display adapter. The species
  records carry coexistence; the old single-element pressure/gas reference
  algorithms are test-only under `native/src/reference/`.
- These source facts do not by themselves satisfy every acceptance gate below
  or establish a whole-game performance improvement.

## Superseded design assumptions

The following assumptions belong to the original design draft and do not describe the current production path. They are retained as context for the acceptance gates; use the current status above and source for implementation details.

1. `OniSim/native` is a separate `oni-sim-core` crate. Its dependencies currently contain Rayon, not wgpu.
2. `OniSim/gpu` is a separate `onisim-gpu` crate with its own Cargo workspace. It is not currently linked into the native production core.
3. `gpu/src/multicomponent.rs` now models per-cell species mass and sensible enthalpy, cell velocity X/Y, an obstacle mask, and per-channel gas metadata (canonical element ID, gas phase, specific heat capacity and vertical drift). Mass and enthalpy use the same conservative upwind face flux; temperature is derived as `H/(m*cp)`. Its readback maps a GPU buffer and copies results into caller-owned CPU slices.
4. This remains an isolated gas-only operator. It rejects liquid/solid channels; the cell obstacle mask cannot represent coexisting phases. It has no equation-of-state pressure, liquid pressure/volume fraction, phase change, latent heat, or production native integration.
5. Production `native/src/world_cells.rs` still stores one `element`, one `temperature` and one `mass` per cell. `PhysicsWorld` uses this single-element CellSOA.
6. `GameData` already has two alternating publication allocations. Each contains a Grid, events, visibility, presentation buffers and retired command slots. The worker does not reuse a ready allocation until the `Prepare` handoff returns it.
7. The existing 496-byte `OniSimGameDataUpdate` descriptor is an established ABI. Its pointers are borrowed and external reads must be serialized with mutation. The existing descriptor must not be silently resized or reinterpreted.

These facts are not evidence of a production GPU simulation, multi-phase correctness, game compatibility or a measured speedup.

## Required corrections to the proposed ABI

### Do not cap a cell at 16 species without a measured invariant

The current element catalogue is not limited to 16 possible element IDs. A dense per-cell species pool should use a checked offset and a count wide enough for the validated pool. If a practical per-cell limit is later introduced, it must be an explicit engine invariant with overflow rejection and tests, not an undocumented ABI assumption.

### Distinguish phase metadata from thermodynamic state

Each species record needs a canonical element identifier and phase, plus the mass and thermal state needed by the solver. Pressure and volume fraction must be derived from a documented equation of state, cell volume, phase density and temperature. Gas partial pressures can be defined by a validated gas mixture model. Liquid pressure is not obtained by blindly applying the ideal-gas equation to liquid mass; it needs a defined liquid/EOS or hydrostatic model.

A publication must not expose plausible-looking pressure fields that are merely placeholders. Unsupported derived fields should make the relevant capability unavailable and cause the C# adapter to fall back.

### Preserve the legacy ABI

Keep the 496-byte official-compatible descriptor unchanged. Add a versioned extension view with explicit size/version fields, cell count, species count, generation and pointers/offsets. Reject unknown versions and invalid lengths. Keep the existing save image unchanged until a separately versioned save-format migration is designed and accepted.

## Publication ownership and synchronization

The authoritative publication should live in the output `GameData` allocation for the completed batch:

- The worker constructs all cell headers and species slices before marking that batch ready.
- The ready allocation is immutable while exposed to readers.
- `Prepare` swaps the two `GameData` roles using the existing worker barrier.
- The next writer may reuse an allocation only after the handoff returns it.
- A view carries both simulation generation and publication generation. A stale view is rejected.
- Shutdown, reinitialization and worker failure invalidate views before storage is released.

A generation counter alone does not prevent a data race: it detects that a version changed only if the memory read itself was safe. The ABI therefore needs an explicit read-lifetime rule. Either (a) all C# reads are serialized with `Prepare`/mutation by the existing session lock, with a tested game-thread ownership rule, or (b) add an acquire/release read lease that pins the immutable publication allocation until release. Do not return an unpinned pointer and rely on checking the generation after reading it.

A GPU device buffer is not automatically zero-copy CPU memory. The current wgpu implementation uses a MAP_READ readback buffer and copies into a host slice. The pipeline must account for that transfer, its synchronization point and its memory cost. “Zero-copy” may only describe a separately measured host-side access path after GPU readback has completed.

## Implementation sequence

### Gate A — complete the physical data model

Before connecting the GPU operator to the game tick:

- define the species catalogue mapping from canonical game element IDs to tensor channels;
- represent phase per species, not only one solid flag per cell;
- define per-species thermal state (prefer conserved enthalpy or another documented conservative quantity);
- define cell volume, gas equation of state, liquid pressure/density model and units;
- specify channel capacity, sparse/dense conversion, overflow behavior and mass/energy conservation tests.

The existing GPU operator is a transport prototype, not yet a complete gas/liquid/solid thermodynamic model.

### Gate B — integrate the solver with the production worker

Link or deliberately embed the GPU implementation into the native production crate. It must consume the actual simulation state and produce the state that the worker publishes. The feature remains unavailable unless adapter selection, shader compilation, resource allocation and synchronization all succeed. The fallback path must retain the current native solver without partially mutating its state.

Validate a small deterministic case on CPU and Vulkan, compare every species and aggregate, then test 32-step resident dispatch with explicit output equivalence and transfer accounting. Do not infer RTX 4060 performance from a CPU-only reference or a shader that merely compiles.

### Gate C — publish with the completed GameData batch

Add the multicomponent view to the existing double-buffered publication lifetime. Build an independent extension descriptor; preserve the legacy 496-byte structure. Add ABI layout assertions in Rust and C, generation/lifetime tests, invalid-offset tests, empty-cell tests and shutdown/reinitialize tests.

### Gate D — implement the C# provider

Implement `IMulticomponentCellProvider` using the versioned native view. Validate every count, offset, element ID, phase, finite value and aggregate. The provider must never read beyond the acquired view. If the view is unavailable, stale, unsupported or inconsistent, return false and leave original game logic in control.

The current `MulticomponentCellSnapshot` stores a managed array. Avoid allocating a new array for every hot-path cell read; define a synchronous reusable-buffer or copy contract before enabling high-frequency Harmony patches.

### Gate E — enable gameplay shims individually

Only after the provider passes host and pinned-reference tests should individual patches be enabled. Keep each patch fail-closed and independently configurable. Do not enable exposure, plant, navigation, building or hover behavior merely because breathing works.

## Required evidence

| Gate | Required evidence | Does not establish |
|---|---|---|
| Data model | deterministic CPU tests for phase, units, conservation and overflow | GPU correctness |
| GPU integration | Vulkan output compared to CPU reference over 32 steps | ONI gameplay compatibility |
| Native publication | ABI layout and lifetime tests under concurrent publish/read/shutdown | Unity/Mono safety |
| C# provider | host tests against a native fixture and pinned reference build | actual game behavior |
| Gameplay patches | Steam-launched ONI scenarios on the pinned build | untested saves/mod combinations |

## Current acceptance state

- GPU prototype connected to production native tick: **No**
- Multi-phase species thermodynamics: **Not established**
- Native per-cell species publication ABI: **Not implemented**
- C# native provider: **Not implemented**
- Harmony runtime activation: **Not enabled**
- ONI runtime validation: **Not performed**

Keep PR #571 in Draft until the data model, production solver integration, publication lifetime, provider and runtime gates have independent evidence.


## ABI v1 implementation status (2026-11-07)

A copy-based OMC1 v1 snapshot has now been implemented on the follow-up branch.
It serializes the currently published native Grid under the native state mutex
into caller-owned memory, and the managed facade validates its header, offsets,
phase codes, mass aggregates and species ranges.

**Important limitation:** OMC1 v1 is explicitly flagged `LEGACY_SINGLE_ELEMENT`.
The current production `Grid`/CellSOA is still single-element, so this is a
real and testable ABI transport baseline, not a multicomponent physics producer.
It deliberately publishes no partial pressure or liquid volume fraction. The
managed reader does not register this snapshot as an active multicomponent
provider. Coexisting-species support remains blocked on integrating a genuine
multi-phase tensor state into the production solver and its GameData publication.


## GPU operator update (2026-11-07)

The experimental Vulkan gas-mixture operator now transports sensible enthalpy
alongside species mass and carries canonical per-channel gas metadata. Its CPU
reference tests per-species mass and enthalpy conservation, nonnegative state,
and impermeable obstacle cells. The Vulkan benchmark separately reports
upload/dispatch/readback and resident 32-step timings.

This improves the experimental gas transport model but does not change the
publication gate: the GPU crate remains separate from `oni-sim-core`, and the
operator is not connected to the production simulation worker or GameData.
Liquid/solid coexistence and pressure/EOS remain unimplemented.
