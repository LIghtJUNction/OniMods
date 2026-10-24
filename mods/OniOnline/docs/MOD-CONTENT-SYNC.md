# Native mod alignment

The adapter separates a Steam source download, an ONI installation, and content
already loaded in the current process. Only the final native manifest comparison
can permit joining. Download callbacks never prove that a DLL was reloaded.

## Local policy and catalog

Trusted local plugin code may call
`ModSyncDeclarations.Register(modId, ModSyncPolicy.ClientOnly)` or `Optional`
before the first `Snapshot()`. Unregistered identities default to `Required`.
The framework identity `LIghtJUNction.OniOnline` always remains `Required`.
Conflicting declarations fail. `Snapshot()` freezes declarations for the process
and returns an immutable copy; remote manifests cannot register local policy.

Create the native catalog after plugin registration:

```csharp
var catalog = NativeKModCatalogSource.Create(
    Global.Instance.modManager, ModSyncDeclarations.Snapshot());
```

`OnAllModsLoaded` runs inside the native manager's loading loop. Retry the first
`CaptureLoadedSnapshot()` on a later frame while its blocking issue is
`native_mod_loader_busy`. Keep that successful startup snapshot for the process
lifetime. Later use `CaptureInstalled(requiredManifest)`. Require `IsComplete`
before publishing or passing a manifest to the synchronization controller.
`ActiveManifest` describes loaded identities; `InstalledManifest` also includes
unambiguous disabled packages for comparison. Issue codes contain no paths.

The native factory checks the manager's actual `mod_load_in_progress` field,
tracks load/unload calls, and observes native catalog metadata for its epoch.
Missing native metadata fails closed. All calls belong on the Unity main thread.
Disk content has two hash passes and repeated inventories, but is not protected
by a filesystem transaction against hostile external writes.

`NativeModCatalogDiagnostics.Summary(snapshot)` provides a curated player-facing
cause without paths or raw exceptions. `ShouldRetryInitial(snapshot, elapsed)`
allows temporary loader/update failures for at most 30 seconds; unclassified
content and other permanent failures must leave the loading state immediately.

A unique active source wins over disabled Local/Dev/Workshop copies of the same
stable ID. Multiple active sources block. With only disabled copies, the target
source, Workshop ID and version select a unique candidate when possible;
otherwise that identity remains missing and an audit issue explains the ambiguity.
Disabling a loaded DLL in native configuration does not unload it: the catalog
preserves the loaded evidence and requires a restart.

Only bounded packaged code, common Unity resources, and known packaged-data or
localization roots contribute to the controlled digest. Text data outside those
roots is ambiguous; unclassified JSON/YAML refuses fingerprinting. Private
configuration, saves, credentials, logs and source are excluded. A mod that
ships a gameplay resource with an extension outside the built-in resource list,
or reads data outside those packaged roots, needs a reviewed content declaration
adapter before it can claim complete synchronization. No Local/Dev source or
archive is uploaded or transmitted.

## Steam controller

```csharp
var controller = new SteamModContentSync(
    new SteamWorkshopModContentApi(),
    captureCompleteInstalledManifest,
    captureCurrentAuthorityManifest);
controller.Begin(plan, explicitlyConfirmedPlanFingerprint, monotonicSeconds);
controller.Tick(monotonicSeconds);
```

The confirmation fingerprint binds both manifests and comparison policy.
Subscription/download mutations recheck current manifests; an authority change
invalidates outstanding requests. Maximum four metadata/subscription/download
jobs run concurrently. `Cancel()` releases pending SDK query/call-result handles;
Steam may finish a background download already accepted by the client.

The adapter uses the existing Steam client, queries item details, validates the
exact item and ONI consumer app ID `457140`, rejects banned/failed items, then
calls `SubscribeItem` and `DownloadItem`. Polling requires installed content,
usable install information and no update/download/pending flags. The download
folder may contain a Legacy ZIP file; it is never treated as the ONI installation
or unpacked by this adapter.

Steam download callbacks have no request generation. Their current item state
is checked, and stale callback failures cannot terminate a new accepted request.
A failure hint is reported while the bounded download timeout and native state
remain authoritative. Wrong-item/app callbacks cannot complete another job.
SDK rejection, query failure and timeouts are explicit failure states.

`AwaitingNativeInstall` means Steam has the source package. `AwaitingActivation`
means exact installed content still lacks startup-loaded evidence. Call
`RefreshNativeCatalog()` after a native refresh; the OniOnline adapter also
rechecks while a controller is waiting for native evidence, at a bounded cadence.
`CanJoin` refers to the latest complete comparison and must be refreshed
immediately before admission. A controller is discarded when the barrier is
cleared or its connection generation changes, so a later mismatch cannot reuse
an old download plan.

The status view keeps the native stage and adds a short, localized cause for
known failures (wrong Workshop item, timeout, unavailable network, local/manual
action, or changed content). Unknown error details fall back to a generic
message; paths, account data and raw SDK exceptions are never shown.

`NativeModActivationRequest.TryCreate` prepares exact enable/disable actions
without applying them. It requires the confirmed plan and unchanged manifests,
and refuses disabled content with a different source, version or digest. This
iteration has no automatic native settings writer: there is no reliable atomic
enable/restart transaction implemented here, and no current `mods.json` changes
are made. The native settings flow remains required for activation/restart.

## Evidence

Focused host tests exercise the production controller/catalog with fake SDK
failures, incomplete downloads, item/app mismatch, stale callbacks, changed
manifests and hashes, native duplicate-source selection, and activation preparation.
The native bridge is separately compiled against the installed game's reference
metadata. These checks do not establish a real Steam subscription/download,
ONI installation/restart, Windows behavior or a native settings transaction.

Filesystem-only preflight on 2026-10-24 also fingerprinted the configured enabled
Local OniHarness Runtime, Local CycleTrim, Dev OniMcp and Local OniOnline packages:
all four passed without unclassified JSON/YAML or companion-file rejection.
No private configuration contents were read and no current-process load claim
follows from this filesystem check.
