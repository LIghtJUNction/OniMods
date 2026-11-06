// Explicit generated-colony mutation scope, separate from readonly imported checkpoints.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using OniOnline.Core;
using OniOnline.Multiplayer;
using OniOnline.Runtime;

namespace OniOnline
{
    internal sealed partial class RuntimeProbe
    {
        private NativeBuildingBridgePermit nativeBuildingPermit;
        private NativeBuildingBridgeProbe.ScoutReceipt nativeBuildingScout;
        private string buildingProtectedPreviousPath, buildingProtectedPreviousHash;
        private static RuntimeProbe buildingGenerationObserver;
        private Game buildingGeneratedGame;
        private SaveGame buildingGeneratedSave;
        private OnlineColonyState buildingGeneratedColony;
        private MinionIdentity[] buildingGeneratedOriginals;

        private void ClearBuildingGenerationProof()
        {
            buildingGeneratedGame = null; buildingGeneratedSave = null;
            buildingGeneratedColony = null; buildingGeneratedOriginals = null;
        }

        [HarmonyPatch(typeof(OnlineColonyState), "AcceptNewGameOrigins")]
        private static class BuildingGeneratedOriginsObservation
        {
            private static void Postfix(OnlineColonyState __instance, NativeDupeLoadEntry[] entries, bool __result)
            {
                var probe = buildingGenerationObserver;
                if (probe == null) return;
                probe.ClearBuildingGenerationProof();
                if (!__result || !probe.testColony || entries == null || entries.Length != 3) return;
                try
                {
                    var game = Game.Instance; var save = SaveGame.Instance;
                    if (game == null || save == null || !game.baseAlreadyCreated || game.IsLoading() ||
                        !ReferenceEquals(__instance, OnlineColonyState.Current) || entries.Any(entry => entry == null || entry.Dupe == null)) return;
                    var originals = entries.OrderBy(entry => entry.NativeId).Select(entry => entry.Dupe).ToArray();
                    if (originals.Distinct().Count() != 3 || Components.LiveMinionIdentities.Items.Count != 3 ||
                        originals.Any(dupe => !dupe.isSpawned || Components.LiveMinionIdentities.Items.Count(other => ReferenceEquals(other, dupe)) != 1)) return;
                    probe.buildingGeneratedGame = game; probe.buildingGeneratedSave = save;
                    probe.buildingGeneratedColony = __instance; probe.buildingGeneratedOriginals = originals;
                }
                catch (Exception error)
                {
                    probe.ClearBuildingGenerationProof();
                    UnityEngine.Debug.LogWarning("[OniOnline][Probe] Generated building proof unavailable: " + error.GetType().Name);
                }
            }
        }

        [HarmonyPatch(typeof(SaveLoader), "Load", new[] { typeof(string) })]
        private static class BuildingGenerationStringLoadReset
        {
            private static void Prefix() { if (buildingGenerationObserver != null) buildingGenerationObserver.ClearBuildingGenerationProof(); }
        }

        [HarmonyPatch(typeof(SaveLoader), "Load", new[] { typeof(IReader) })]
        private static class BuildingGenerationReaderLoadReset
        {
            private static void Prefix() { if (buildingGenerationObserver != null) buildingGenerationObserver.ClearBuildingGenerationProof(); }
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(buildingGenerationObserver, this)) buildingGenerationObserver = null;
            ClearBuildingGenerationProof(); nativeBuildingPermit = null; nativeBuildingScout = null;
        }

        private void RequireGeneratedBuildingContext(OnlineRuntime runtime)
        {
            RequireNativePreflightColony();
            if (!testColony || runtime == null || runtime.Session != null || runtime.Multiplayer != null ||
                nativeWork != null || nativeDupe != null || nativeRemoteWork != null || networkWorkActive ||
                GetComponent<NativeNavigationQuiescenceProbe>() != null || GetComponent<NativeLifecycleProbe>() != null)
                throw new InvalidOperationException("Building fixture requires this process's fresh paused generated colony without a room or another native fixture.");
            if (!ReferenceEquals(buildingGeneratedGame, Game.Instance) || !ReferenceEquals(buildingGeneratedSave, SaveGame.Instance) ||
                !ReferenceEquals(buildingGeneratedColony, OnlineColonyState.Current) || buildingGeneratedOriginals == null ||
                buildingGeneratedOriginals.Length != 3 || Components.LiveMinionIdentities.Items.Count != 3 ||
                buildingGeneratedOriginals.Any(dupe => dupe == null || Components.LiveMinionIdentities.Items.Count(other => ReferenceEquals(other, dupe)) != 1))
                throw new InvalidOperationException("A successful original-three native new-game batch on this exact Game is required; loading a checkpoint clears this write scope.");
            if (buildingProtectedPreviousHash != null &&
                (!File.Exists(buildingProtectedPreviousPath) || HashNativePreflightFile(buildingProtectedPreviousPath) != buildingProtectedPreviousHash))
                throw new InvalidOperationException("The previous active save changed during the independent building fixture.");
            if (nativeBuildingPermit != null &&
                (!ReferenceEquals(Game.Instance, nativeBuildingPermit.ExpectedGame) || !ReferenceEquals(SaveGame.Instance, nativeBuildingPermit.ExpectedSave)))
                throw new InvalidOperationException("The generated building fixture's exact native game changed.");
        }

        private void PrepareNativeBuildingProbe(OnlineRuntime runtime)
        {
            RequireGeneratedBuildingContext(runtime);
            if (nativeBuildingPermit != null || GetComponent<NativeBuildingBridgeProbe>() != null)
                throw new InvalidOperationException("Use a fresh generated colony for another independent building fixture.");
            var manifest = runtime.SelectedPlanet;
            if (Components.LiveMinionIdentities.Items.Count != 3)
                throw new InvalidOperationException("Building fixture requires exactly the three generated originals.");
            var originals = Components.LiveMinionIdentities.Items.Where(dupe => dupe != null)
                .OrderBy(dupe => dupe.GetComponent<KPrefabID>().InstanceID).ToArray();
            if (!originals.Select((dupe, index) => ReferenceEquals(dupe, buildingGeneratedOriginals[index])).All(same => same))
                throw new InvalidOperationException("The generated original references changed before preparing building copies.");
            var colony = OnlineColonyState.Current;
            if (manifest == null || colony == null || originals.Length != 3)
                throw new InvalidOperationException("Building fixture requires the generated original three and their existing planet.");
            foreach (var dupe in originals)
            {
                int cell = Grid.PosToCell(dupe);
                DupeOriginRecord origin;
                if (!Grid.IsValidCell(cell) || Grid.WorldIdx[cell] != manifest.NativeWorldId ||
                    !colony.TryReadDuplicantOrigin(dupe.GetComponent<KPrefabID>().InstanceID, manifest.NativeWorldId, out origin) ||
                    origin == null || origin.Kind != DupeOriginKind.NativeLocal || origin.OriginalOwnerId != manifest.OwnerId || origin.OriginPlanetId != manifest.PlanetId)
                    throw new InvalidOperationException("Building fixture cannot adopt an unknown or externally owned duplicant.");
            }
            buildingProtectedPreviousPath = SaveLoader.GetActiveSaveFilePath();
            buildingProtectedPreviousHash = File.Exists(buildingProtectedPreviousPath) ? HashNativePreflightFile(buildingProtectedPreviousPath) : null;
            string folder = Path.GetFullPath(Path.Combine(SaveLoader.GetSavePrefixAndCreateFolder(), "OniOnlineHarness-" + Process.GetCurrentProcess().Id));
            Directory.CreateDirectory(folder);
            string original = Path.Combine(folder, "building-source-" + sequence + ".sav");
            string copy = Path.Combine(folder, "building-copy-" + sequence + ".sav");
            if (File.Exists(original) || File.Exists(copy)) throw new IOException("Building checkpoints must both be fresh.");
            double time = GameClock.Instance.GetTime();
            if (SaveLoader.Instance.Save(original, false, false) != original || !File.Exists(original))
                throw new IOException("Fresh building source checkpoint was not saved.");
            string originalHash = HashNativePreflightFile(original);
            if (SaveLoader.Instance.Save(copy, false, true) != copy || !File.Exists(copy) ||
                SaveLoader.GetActiveSaveFilePath() != copy || HashNativePreflightFile(original) != originalHash || GameClock.Instance.GetTime() != time)
                throw new IOException("Fresh active building copy was not saved without advancing the source.");
            testSavePath = copy;
            nativeBuildingPermit = new NativeBuildingBridgePermit {
                MutableCopyAuthorized = true, GeneratedThisProcess = true, CreatedPid = Process.GetCurrentProcess().Id,
                ActiveCopyPath = copy, ActiveCopySha256 = HashNativePreflightFile(copy), OwnedSaveDirectory = Path.GetFullPath(folder),
                OriginalSourcePath = original, OriginalSourceSha256 = originalHash,
                PlanetId = manifest.PlanetId, PersistentOwnerId = manifest.OwnerId, NativeWorldId = manifest.NativeWorldId,
                LoadedProbeSha256 = modHash, OriginalStarterIds = originals.Select(dupe => dupe.GetComponent<KPrefabID>().InstanceID).ToArray(),
                ExpectedGame = Game.Instance, ExpectedSave = SaveGame.Instance, ExactOriginalStarters = originals
            };
            RequireGeneratedBuildingContext(runtime);
            nativeBuildingScout = NativeBuildingBridgeProbe.Scout(nativeBuildingPermit, () => RequireGeneratedBuildingContext(runtime));
            WriteNativePreflightJson("native-building-scout-" + sequence + ".json", new {
                permit = nativeBuildingPermit, scout = nativeBuildingScout,
                previousActiveSaveUnchanged = buildingProtectedPreviousHash == null || HashNativePreflightFile(buildingProtectedPreviousPath) == buildingProtectedPreviousHash,
                scope = "Two fresh native SaveAs checkpoints for this process's generated original-three colony. No readonly-reload receipt authorizes this fixture. Blueprint execution has not begun."
            });
        }

        private void StartNativeBuildingProbe(OnlineRuntime runtime)
        {
            RequireGeneratedBuildingContext(runtime);
            if (nativeBuildingPermit == null || nativeBuildingScout == null)
                throw new InvalidOperationException("Prepare the separate generated-colony building copy and inspect its scout receipt first.");
            string receiptName = "native-building-probe-" + sequence + ".json";
            NativeBuildingBridgeProbe.Begin(gameObject, nativeBuildingPermit, nativeBuildingScout,
                CaptureNativeBuildingPhysicalFingerprint, () => RequireGeneratedBuildingContext(runtime),
                report => WriteNativePreflightJson(receiptName, report));
        }

        private static object CaptureNativeBuildingPhysicalFingerprint()
        {
            var physical = JObject.FromObject(CaptureNativePreflightFingerprint());
            // Construction metadata and ordinary Dig are the deliberate writes under test.
            // Real resources, original duplicants, clock and physical cells must stay equal.
            physical.Remove("selection");
            physical.Remove("assignments");
            physical.Remove("assignmentGroups");
            return new { colony = physical, resources = NativeBuildingBridgeProbe.CaptureResourceFingerprint() };
        }
    }
}
