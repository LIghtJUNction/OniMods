# Official frame synchronization contract

Static observations of the installed Linux x64 official library, SHA-256
`85111cd0b1fc14c79c785581d6c055280b9d48ba705776f3d14eb0a7b239c12d`.
Addresses are ELF virtual addresses. This document records behavior recovered
from that binary, not an executed conformance result or implementation source.

## Three distinct boundaries

| Boundary | Official entry | Effect |
| --- | --- | --- |
| Command submission | `ProcessFrame`, `0x4ad00` | Commands are grouped by type and run in a fixed stage order. |
| New game frame | `BeginFrameProcessing`, `0x490a0` | Selects all frames pending when the worker takes its batch. |
| Completed Step batch | `CopySimDataToGame`, `0x58600` | Produces one coherent GameData result for all selected frames. |

A received command belongs to the current unsealed frame. NewFrame seals that
frame's owned command queues. Later submissions go into a new queue. Copying a
component registry or reusing a mutable global pending list is not equivalent.

The official pending frame limit is 30. At that limit NewFrame does not enqueue
another frame, and current commands remain available for a later submission.

## Worker and Prepare handshake

The relevant functions are `Main@0x5a100`, `SimSync@0x39bc0`,
`GameSync@0x39cb0`, and `Prepare@0x32c60`.

```text
worker selects a batch → Step → output GameData complete → ready = true
                                                          │
                          waits for Prepare ◀─────────────┘
                          │
Prepare waits for ready → swaps GameData roles → releases next worker Step
```

There are two GameData objects in addition to the physics read/write cell
arrays. The previous GameData stays frozen throughout a batch. The other holds
the new grid snapshot, events, component outputs, textures and visible-cell mask.
Physics read/write arrays alone cannot supply these publication lifetimes.

Without Prepare, the worker can complete at most one Step batch before it waits.
Later NewFrame calls enqueue work. Prepare hands over a completed result; it does
not advance the same frame again. Which pending frames join a batch depends on
when the worker selects it. API call order alone does not fix that selection.
Start can also produce an empty initial batch before the first Prepare.

A replacement must let NewFrame return after submission, wait outside the
submission lock, and wake both worker and waiting caller on failure or shutdown.
Holding the global API lock for an entire simulation Step defeats that boundary.
Preserving stable external addresses with an extra public copy is a possible
adapter choice; the copy cost still needs measurement.

`Main@0x5a100` enables the thread's MXCSR flush-to-zero bit (`0x8000`), retaining
the other control bits. This is separate from denormals-are-zero or compiler
fast-math. It also releases queued conduit handles once before each Step,
including an empty batch; doing so once per enclosed NewFrame changes reuse time.

## Visibility has an additional handoff

Prepare writes its incoming mask into the old GameData. After the role swap,
that object becomes the next Step's output. At that Step's tail,
`swapVisibleGrid@0x3a1d0` exchanges its mask with SimData's active mask.

Consequently, the new Prepare mask first affects physics in the following Step,
after the next one. Assigning `world.active_mask` immediately in Prepare is early.
Keep the two GameData masks distinct from the currently active physics mask.

## Component and event reduction

`ElementChunk::Update@0x61c60` accumulates its output delta. It does not clear that
delta every NewFrame. `UpdateDataListOnly@0x61a60`, used by a zero-time frame or
an empty Step, does clear it. `CopySimDataToGame@0x58600` copies the component
outputs and only then clears the source deltas.

Thus two positive-time frames in one batch accumulate; a later zero-time frame
in the same batch clears that accumulated output. Publishing after each frame
and retaining only its last component output loses information.

Substance changes are resolved against the frozen previous GameData and final
world state at the Step tail. Repeated cell changes are consolidated before the
derived solid/liquid/navigation events are published. SpawnOre events have their
own reduction by cell and element. Dig events use a separate output vector.
All borrowed result spans must remain coherent with the grid that accompanies
them until their publication lifetime ends.

The four-component directional flow array is cleared at each Step start.
Accumulated scalar flow is different: CopySimDataToGame copies it into the
output first, increments its publication counter, and clears the source every
15 publications (`0x58600`, tail near `0x59b30`). Neither array should be cleared
once per individual NewFrame.

## Development and later acceptance

The implementation is being aligned with this contract. No current test,
benchmark, simulator library or game execution is implied by this document.
After development, acceptance must cover queued frames, empty/zero-time batches,
visibility handoff, component deltas, command isolation, publication lifetimes,
shutdown while waiting, and worker failure propagation.
