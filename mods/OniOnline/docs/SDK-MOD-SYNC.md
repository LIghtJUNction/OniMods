# Mod synchronization SDK

`OniOnline.Framework` contains the game-independent contracts in `OniOnline.Sdk`.
They have no Unity, DLC, Steam, or native simulation dependency. The native adapter
supplies the loaded mod catalog; the room admission service exchanges it before
planet admission and gameplay traffic are enabled.

## Align the loaded mod set

Build a `ModManifest` from `ModManifestEntry` objects. Each entry contains:

| Field | Meaning |
| --- | --- |
| `Id` | Native stable mod ID, compared ordinally; Unicode and spaces are supported |
| `Version` | Package version label |
| `Policy` | `Required`, `ClientOnly`, or `Optional` |
| `Source` / `WorkshopId` | Local installation or the exact Steam Workshop item |
| `ContentSha256` | SHA-256 of the artifact actually loaded by the native adapter |
| `Enabled` | Whether this artifact participates in the current game |

IDs are limited to 128 UTF-16 code units, versions to 64, and manifests to 256
unique IDs. Control characters and malformed Unicode are rejected. Required mods
need a 64-character SHA-256. Digests are normalized to lowercase. Local entries
must have Workshop ID zero; Workshop entries must have a nonzero ID. Paths,
account credentials, and Steam session information do not belong in this manifest.

`ModSyncPlanner.Compare(authority, installed)` defaults to strict comparison:

- Every enabled authority `Required` entry must match the enabled guest artifact,
  source/item, version, and content hash. A guest `ClientOnly` declaration cannot
  override an authority requirement.
- Disabled authority entries are not network requirements.
- Extra enabled guest `Required` entries produce `Unexpected`, including an ID
  the authority only declared as `Optional` or `ClientOnly`.
- Extra guest `ClientOnly`/`Optional` entries do not block entry. Policy therefore
  belongs to trusted native/SDK configuration, not arbitrary remote claims.
- The explicit `AuthorityRequirementsOnly` mode omits the extra-mod check. Use it
  only when the gameplay has a deliberate compatibility policy.

Differences are returned in ordinal ID order. Each `ModSyncChange` has `Expected`,
`Installed`, and a `Kind`. `Expected` is null for `Unexpected`; `Installed` is null
for `Missing`. `CanDownload` means a known Workshop source exists, not that Steam
or ONI has completed an installation. The SDK never deletes an unexpected mod.

`ModSyncPlan` preserves both immutable manifests. `Fingerprint` binds both catalog
fingerprints and the comparison mode. Any changed catalog invalidates an existing
approval. Recompute the plan against the original `RequiredManifest` after native
installation, enable/disable, restart, and load. Download completion alone cannot
make `CanJoin` true. Local content requires an explicit local installation path;
the SDK does not invent a remote download URL.

`ModManifestCodec.Encode/Decode` is a deterministic, versioned, bounded binary
format. It rejects duplicate IDs, future wire versions, malformed UTF-8,
truncation, and trailing data. Its whole-manifest limit is 256 KiB; room admission
must use its bounded fragmentation service rather than one application packet.

## Register a state stream

State synchronization is opt-in per mod/channel. A mod must implement
`IModStateReducer.ValidateSnapshot` and `ApplyDelta`. Reducers calculate bytes;
they must not mutate Unity objects or perform business operations. The SDK gives
reducers cloned inputs, validates cloned output, and only then publishes a new
revision. A thrown reducer or invalid output preserves the last good checkpoint.

Create `ModStateSession` on the same thread as `RoomSession.Pump`:

```csharp
// IsAdmitted is your framework's verified mod-admission predicate.
var state = new ModStateSession(room, IsAdmitted, AdmissionGenerationFor);
// Call state.Pump(seconds) after room.Pump(seconds), and Dispose on shutdown.
```

The mandatory predicate is checked for the local player and every sending/target
player. It must return false until the room's mod-alignment barrier is satisfied.
A permissive predicate would deliberately remove that barrier.

`AdmissionGenerationFor(playerId)` returns a positive local authorization stamp,
or zero while entry is unavailable. Supply the production mod barrier's getter.
A changed stamp retires old publishers, subscriptions, leases and relay routes
even when the endpoint never observed `IsAdmitted == false`. Stamps are scoped to
this room and endpoint; do not compare them between players or treat them as
identity. The two-argument overload is retained for controlled integrations that
only have a Boolean barrier; it cannot provide this stronger retirement rule.

A publisher owns a stable stream and a fresh authority incarnation (`Epoch`):

```csharp
var binding = new ModStateBinding(
    room.RoomId, "author.mod", "weather", room.LocalPlayerId,
    Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), schema: 1);
var publisher = new ModStatePublisher(binding, reducer);
publisher.PublishSnapshot(initialBytes);
var registration = state.RegisterPublisher(publisher, playerId => CanObserve(playerId));
publisher.PublishDelta(deltaBytes);
```

`RegisterPublisher` requires the real local room/player identity and a mandatory
owner-side observation predicate. The host relay does not grant observation
permission. Recheck permissions in the predicate whenever ownership or visibility
changes. A new publisher incarnation must use a fresh epoch. Dispose its
registration when the stream disappears.

Distribute the publisher's mod/channel/authority/epoch/schema descriptor through
an authenticated gameplay contract. An observer subscribes to that verified
descriptor; incoming state cannot self-assign a new authority or epoch:

```csharp
var watch = state.Subscribe(modId, channel, authorityId, epoch, schema, reducer);
watch.Changed += () => {
    if (watch.Status == ModStateSubscriptionStatus.Active) {
        byte[] accepted = watch.Replica.Current;
        // Validate/apply to your own native adapter as a separate transaction.
    }
};
// Dispose watch to stop observing.
```

The convenience overload creates a fresh subscription generation. The explicit
`Subscribe(ModStateBinding, reducer)` overload is available to trusted framework
adapters. It must also use a fresh generation. A monotonically increasing room
request number binds transport messages to the exact subscription independently
of that nonce. Reusing a recent nonce/epoch is rejected; bounded recent-identity
guards do not impose a lifetime subscription limit.

## Authority, recovery, and unknown schemas

`ModStateBinding` binds session, mod ID, channel, authenticated authority, epoch,
subscription generation, and schema. Session/player/epoch/generation identifiers
are nonempty GUIDs in `N` format. A snapshot has revision at least one and base
revision zero. A delta must advance exactly one revision from a positive baseline.
Snapshot payloads are limited to 64 KiB and deltas to 3072 bytes.

`ModStateReplica.Accept(authenticatedSender, frame)` rejects foreign authorities
and scopes before running a reducer. Duplicate frames do not replay reducers;
stale frames do not roll back state. A gap retains the last good bytes and marks
`NeedsSnapshot`. Contiguous replay or a valid full snapshot repairs it. Conflicting
same-revision data never replaces committed bytes; a matching authoritative full
snapshot can clear that recovery requirement.

`ModStatePublisher.Recover(knownRevision)` returns contiguous retained deltas when
available, otherwise a full current snapshot. Retained deltas are bounded by 32
frames and 64 KiB. `CaptureSnapshot` does not increment the revision. `Restore`
accepts a locally trusted persisted binding/revision/snapshot; it is not a network
authority negotiation API. Revision overflow fails without changing the checkpoint.

Unknown payload schemas are handled explicitly by the pure replica. The first
unsupported frame of a generation is preserved in `Quarantined`, and that
generation stops accepting state. `QuarantinedFrames` retains up to four such
frames across explicit rebindings. Further unknown generations return
`QuarantineFull` without overwriting old evidence. A quarantined delta may lack its
baseline; it must not be treated as a restorable native save. Installing an adapter
requires a fresh trusted binding and a full authoritative snapshot.

The transport rejects mismatched registered schemas with `SchemaMismatch` before
sending native state. It never creates an empty fallback state. To persist a
checkpoint or quarantine, use `ModStateCodec` and an appropriate native save
adapter. Quarantine is held in memory until the caller persists it. The existing
16-KiB `GameplayStateStore` slot cannot hold every legal 64-KiB state frame; use a
suitably bounded store rather than truncating it.

`Disconnect` and session lifecycle invalidation terminate the active baseline.
`Checkpoint` preserves the last accepted full bytes together with their original
binding and revision for display/export; `Current != null` alone does not mean
the current subscription is active. Old generations cannot affect a new handle.
Rejoining the room requires an explicit new subscription and fresh generation.
No business operation is replayed by recovery.

## Transport and resource limits

`ModStateSession` uses reliable `RoomSession` application messages. Guest-owned
streams travel through the host with the authenticated original sender preserved.
Host-local observation runs through the same validation. State data is split into
3072-byte chunks, with a transfer nonce, whole-frame SHA-256, exact length/count,
and the complete subscription scope. Only complete matching data reaches a reducer.

The session bounds concurrent publishers/subscriptions to 16 each, remote leases
to 64, incoming transfers to 8 globally and 2 per sender, reserved encoded bytes
to 512 KiB globally and 128 KiB + 2048 per sender. Chunk zero must start an assembly;
refused transfer tails cannot occupy newly freed capacity. Senders allow at most
two transfers per observer, rotate leases, and limit data to 96 chunks/second.
Control and data receive limits are 256 messages and 1 MiB per peer/second.

Each unique accepted chunk renews a 10-second idle deadline. Duplicate chunks do
not extend it; every assembly also has a 60-second hard deadline. Incomplete data
is discarded and requests a read-only snapshot recovery. Lease renewal reports
the observer's **accepted byte revision**, not an acknowledgment that Unity/native
objects were committed. Native application success remains the adapter's job.

Callbacks and malformed module packets are isolated from room authentication.
`IncompleteAssemblyCount`, `IncompleteAssemblyBytes`, `RejectedPackets`, and
subscription status provide bounded diagnostics without claiming a Steam download,
native mod activation, simulation determinism, or cross-version native-save support.

## Validation

`ModSynchronizationTests.Run` exercises production planners/codecs/reducers and
real in-memory `RoomSession` peers. Coverage includes invalid inputs, exact
artifact differences, authority and generation rejection, reducer rollback,
loss/duplicate/reorder recovery, schema isolation, reconnect, 64-KiB relayed
snapshots, concurrent streams, global/per-peer assembly contention, slow progress,
permission revocation, cancellation tombstones, and long-lived subscriptions.
These host tests do not establish ONI rendering or two-client native simulation
acceptance; those require the separate Steam-launched game test.
