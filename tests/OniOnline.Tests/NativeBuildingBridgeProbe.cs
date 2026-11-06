// Probe-only. No release gate, simulation step, native completion, save or resource spawn.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniOnline.Multiplayer;
using OniOnline.Runtime;
using UnityEngine;

namespace OniOnline
{
    // A read-only reload receipt is intentionally insufficient. The caller must create a
    // distinct normal SaveAs checkpoint in its own disposable directory before issuing this.
    internal sealed class NativeBuildingBridgePermit
    {
        public bool MutableCopyAuthorized = false;
        public bool GeneratedThisProcess = false;
        public int CreatedPid = 0;
        public string ActiveCopyPath = "", ActiveCopySha256 = "", OwnedSaveDirectory = "";
        public string OriginalSourcePath = "", OriginalSourceSha256 = "";
        public string PlanetId = "", PersistentOwnerId = "", LoadedProbeSha256 = "";
        public int NativeWorldId = -1;
        public int[] OriginalStarterIds = Array.Empty<int>();
        [JsonIgnore] public Game ExpectedGame = null;
        [JsonIgnore] public SaveGame ExpectedSave = null;
        [JsonIgnore] public MinionIdentity[] ExactOriginalStarters = Array.Empty<MinionIdentity>();
    }

    internal sealed class NativeBuildingBridgeProbe : MonoBehaviour
    {
        private const string GameHash = "6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1";
        private const string FrameworkHash = "117fcc6962eeed82b4929347b9a466fb41e807a01b5a9a7acc8bdc446da276c7";
        private const string Owner = "building-fixture-A", Builder = "building-fixture-B", Challenger = "building-fixture-C";
        private static NativeBuildingBridgeProbe active;
        private static readonly HashSet<string> usedCopies = new HashSet<string>(StringComparer.Ordinal);

        internal sealed class Site
        {
            public WorkCell Anchor;
            public WorkBuildSpec Build = new WorkBuildSpec();
            public float RequiredKg, AvailableKg;
            public int[] NativeCells = Array.Empty<int>();
        }
        internal sealed class ScoutReceipt
        {
            public bool Ready, Blocked;
            public string Reason = "", PermitSha256 = "", SelectionSha256 = "";
            public string GameAssemblySha256 = "", FrameworkAssemblySha256 = "", LoadedProbeSha256 = "";
            public int Frame, CandidateChecks;
            public double GameTime;
            public Site Locker, Outhouse;
            public WorkCell OrdinaryDig;
            public List<object> MaterialChecks = new List<object>();
            public Dictionary<string, int> Rejections = new Dictionary<string, int>();
        }
        internal sealed class Receipt
        {
            public string Phase = "starting", PermitSha256 = "", BeforePhysicalSha256 = "", AfterPhysicalSha256 = "";
            public bool Finished, Passed, CleanupAttempted, CleanupComplete, OrdinaryDigPreserved;
            public bool SourceBytesUnchanged, CopyBytesUnchanged, PhysicalFingerprintUnchanged, AuthorityBlocked;
            public bool NativeBuildingExecutionValidated = false, ReleaseGateChanged = false;
            public int StartedFrame, FinishedFrame;
            public double InitialGameTime;
            public ScoutReceipt Scout;
            public List<object> Samples = new List<object>();
            public List<string> Failures = new List<string>();
            public string Scope = "Single complete native game, paused blueprint-only production Authority/Executor fixture. Not network-client, worker completion, save/reload or release acceptance. Ordinary native Dig is deliberately preserved; discard this private process without saving.";
        }
        private sealed class Context : IWorldWorkContext
        {
            internal WorkPlanet Value;
            public bool TryGetLocalPlanet(out WorkPlanet planet) { planet = Value; return planet != null; }
        }
        private NativeBuildingBridgePermit permit;
        private ScoutReceipt scout;
        private Receipt receipt;
        private Func<object> fingerprint;
        private System.Action guard;
        private System.Action<object> write;
        private Game sourceGame;
        private SaveGame sourceSave;
        private WorldContainer world;
        private RemoteWorkAuthority authority;
        private WorkPlanet planet;
        private GameObject ordinary;
        private int ordinaryUnityId, ordinaryNativeId;
        private readonly List<GameObject> createdBlueprints = new List<GameObject>();
        private readonly List<RemoteWorkSavedOrder> knownOrders = new List<RemoteWorkSavedOrder>();
        private int[] initialPrefabIds;
        private RemoteWorkRequest lockerConfirm, outhouseConfirm, challengerConfirm;
        private RemoteWorkSavedOrder lockerOrder, outhouseOrder;
        private int thread, phase, nextFrame;
        private float deadline;
        private bool finished, cleanupStarted;
        private string baseline;

        internal static ScoutReceipt Scout(NativeBuildingBridgePermit value, System.Action exactMutableCopyGuard)
        {
            if (exactMutableCopyGuard == null) throw new ArgumentNullException(nameof(exactMutableCopyGuard));
            exactMutableCopyGuard();
            WorldContainer target = RequirePermit(value, true);
            var result = new ScoutReceipt { Frame = Time.frameCount, GameTime = GameClock.Instance.GetTime(), PermitSha256 = HashObject(value),
                GameAssemblySha256 = HashFile(typeof(Game).Assembly.Location), FrameworkAssemblySha256 = HashFile(typeof(KMonoBehaviour).Assembly.Location),
                LoadedProbeSha256 = HashFile(typeof(NativeBuildingBridgeProbe).Assembly.Location) };
            var materials = new Dictionary<string, float>(StringComparer.Ordinal);
            Site locker = ChooseMaterial(target, "StorageLocker", materials, result);
            if (locker == null) return BlockScout(result, "StorageLocker_material_stock_or_native_definition_unavailable");
            Site outhouse = ChooseMaterial(target, "Outhouse", materials, result);
            if (outhouse == null) return BlockScout(result, "Outhouse_material_stock_or_native_definition_unavailable");
            var pod = Components.Telepads.Items.FirstOrDefault(p => Grid.WorldIdx[Grid.PosToCell(p)] == target.id);
            if (pod == null) return BlockScout(result, "native_printing_pod_missing");
            int origin = Grid.PosToCell(pod);
            var center = new WorkCell(Grid.CellColumn(origin) - target.WorldOffset.x, Grid.CellRow(origin) - target.WorldOffset.y);
            var candidates = Candidates(center).ToArray();
            foreach (var site in new[] { locker, outhouse })
            {
                bool found = false;
                foreach (WorkCell point in candidates)
                {
                    result.CandidateChecks++;
                    var plan = Intent(point, site.Build, Builder, 5);
                    if (ReferenceEquals(site, outhouse) && WorkPlanGeometry.Overlaps(plan, Intent(locker.Anchor, locker.Build, Builder, 5))) continue;
                    if (!SafeBuildArea(target, plan)) continue;
                    WorkExecution check = NativeBuildingWork.Preflight(target, plan, true, null);
                    if (!check.Accepted) { CountReject(result, check.Reason); continue; }
                    site.Anchor = point; site.NativeCells = WorkPlanGeometry.Footprint(plan).Select(p => Cell(target, p)).ToArray(); found = true; break;
                }
                if (!found) return BlockScout(result, site.Build.PrefabId + "_safe_native_location_unavailable");
            }
            bool digFound = false;
            foreach (WorkCell point in candidates)
            {
                if (!SafeDigArea(target, point)) continue;
                if (new[] { locker, outhouse }.Any(s => WorkPlanGeometry.Footprint(Intent(s.Anchor, s.Build, Builder, 5)).Any(p => Math.Abs(p.X - point.X) + Math.Abs(p.Y - point.Y) < 5))) continue;
                result.OrdinaryDig = point; digFound = true; break;
            }
            if (!digFound) return BlockScout(result, "safe_ordinary_native_dig_control_unavailable");
            result.Locker = locker; result.Outhouse = outhouse; result.Ready = true;
            result.SelectionSha256 = SelectionHash(result);
            exactMutableCopyGuard(); RequirePermit(value, true);
            return result;
        }

        internal static void Begin(GameObject owner, NativeBuildingBridgePermit value, ScoutReceipt selection,
            Func<object> physicalFingerprint, System.Action exactMutableCopyGuard, System.Action<object> writeReceipt)
        {
            if (owner == null || value == null || selection == null || physicalFingerprint == null || exactMutableCopyGuard == null || writeReceipt == null)
                throw new InvalidOperationException("building_probe_exact_permit_callbacks_required");
            if (active != null || owner.GetComponent<NativeBuildingBridgeProbe>() != null) throw new InvalidOperationException("building_probe_already_used");
            exactMutableCopyGuard(); WorldContainer target = RequirePermit(value, true);
            if (!selection.Ready || selection.Blocked || selection.PermitSha256 != HashObject(value) || selection.SelectionSha256 != SelectionHash(selection) ||
                selection.GameTime != GameClock.Instance.GetTime()) throw new InvalidOperationException("building_probe_fresh_exact_scout_required");
            string scope = Path.GetFullPath(value.ActiveCopyPath) + ":" + value.ActiveCopySha256;
            if (!usedCopies.Add(scope)) throw new InvalidOperationException("building_probe_copy_already_used");
            var runner = owner.AddComponent<NativeBuildingBridgeProbe>(); active = runner;
            runner.permit = Clone(value);
            runner.permit.ExpectedGame = value.ExpectedGame; runner.permit.ExpectedSave = value.ExpectedSave;
            runner.permit.ExactOriginalStarters = (MinionIdentity[])value.ExactOriginalStarters.Clone();
            runner.scout = Clone(selection); runner.world = target;
            runner.fingerprint = physicalFingerprint; runner.guard = exactMutableCopyGuard; runner.write = writeReceipt;
            runner.sourceGame = Game.Instance; runner.sourceSave = SaveGame.Instance; runner.thread = Thread.CurrentThread.ManagedThreadId;
            runner.receipt = new Receipt { StartedFrame = Time.frameCount, InitialGameTime = GameClock.Instance.GetTime(),
                PermitSha256 = selection.PermitSha256, Scout = runner.scout };
            runner.deadline = Time.realtimeSinceStartup + 30f; runner.nextFrame = Time.frameCount + 1;
            try
            {
                runner.RequireScope(true);
                var saved = NativeWorkJournal.CaptureSavedState(OnlineColonyState.Current, value.PersistentOwnerId);
                if (RemoteWorkPersistence.Decode(saved.Payload).Planets.Any(p => p.PlanetId == value.PlanetId && (p.Plans.Length > 0 || p.Orders.Length > 0)) ||
                    NativeWorkJournal.Markers(value.PlanetId).Length != 0 || NativeWorkJournal.BuildMarkers(value.PlanetId).Length != 0)
                    throw new InvalidOperationException("building_probe_existing_remote_work_forbidden");
                runner.initialPrefabIds = ScenePrefabIds();
                runner.baseline = runner.PhysicalHash(); runner.receipt.BeforePhysicalSha256 = runner.baseline;
                runner.planet = new WorkPlanet { PlanetId = value.PlanetId, NativeWorldId = value.NativeWorldId, PersistentOwnerId = value.PersistentOwnerId,
                    AuthorityEpoch = Guid.NewGuid().ToString("N"), OwnerPeerId = Owner, AuthorityPeerId = Owner };
                runner.authority = new RemoteWorkAuthority(Owner, new Context { Value = runner.planet }, new NativeWorkExecutor(true, true));
                if (!runner.authority.Synchronize() || runner.authority.ExportConfirmedPlans().Length != 0) throw new InvalidOperationException("building_probe_authority_not_fresh");
                runner.Emit("inspected_before_orders"); // Exact coordinates/materials are durable before native writes.
            }
            catch (Exception error) { runner.Fail(error); }
        }

        private void Update()
        {
            if (finished || Time.frameCount < nextFrame) return;
            try
            {
                RequireScope(false);
                if (cleanupStarted) { Finish(false); return; }
                if (Time.realtimeSinceStartup > deadline || Time.frameCount - receipt.StartedFrame > 240) throw new InvalidOperationException("building_probe_bounded_frame_timeout");
                switch (phase++)
                {
                    case 0:
                        RevalidateSites();
                        ordinary = DigTool.PlaceDig(Cell(world, scout.OrdinaryDig), 0);
                        Need(ordinary != null && ordinary.GetComponent<RemoteWorkDigProvenance>() != null && !ordinary.GetComponent<RemoteWorkDigProvenance>().Managed,
                            "ordinary_native_dig_control_rejected_or_claimed");
                        ordinaryUnityId = ordinary.GetInstanceID(); ordinaryNativeId = NativeId(ordinary);
                        Need(ordinary.GetComponent<Prioritizable>() != null, "ordinary_native_priority_missing");
                        ordinary.GetComponent<Prioritizable>().SetMasterPriority(new PrioritySetting(PriorityScreen.PriorityClass.basic, 3));
                        Emit("ordinary_A_native_dig_created"); break;
                    case 1:
                        CheckOrdinary();
                        authority.SetPermissions(Builder, RemoteWorkPermission.IssueTasks);
                        CheckOrdinaryConflicts();
                        lockerConfirm = Submit(Builder, scout.Locker, 5);
                        outhouseConfirm = Submit(Builder, scout.Outhouse, 5);
                        Emit("B_both_native_blueprints_confirmed"); break;
                    case 2:
                        lockerOrder = CheckBlueprint(scout.Locker, Builder, lockerConfirm, 5, null);
                        outhouseOrder = CheckBlueprint(scout.Outhouse, Builder, outhouseConfirm, 5, null);
                        CheckReplay(Builder, lockerConfirm, lockerOrder); CheckReplay(Builder, outhouseConfirm, outhouseOrder);
                        CheckJournal(2); CheckPhysical(); Emit("B_exact_whole_footprints_materials_journal_and_replays_verified"); break;
                    case 3:
                        authority.SetPermissions(Challenger, RemoteWorkPermission.IssueTasks);
                        challengerConfirm = Submit(Challenger, scout.Outhouse, 8);
                        Emit("C_same_spec_higher_priority_confirmed"); break;
                    case 4:
                        var challenger = CheckBlueprint(scout.Outhouse, Challenger, challengerConfirm, 8, outhouseOrder);
                        CheckReplay(Challenger, challengerConfirm, challenger);
                        Need(authority.GetOwnPlans(Builder).Length == 2 && authority.GetOwnPlans(Challenger).Length == 1 && authority.GetMergedPlans().Length == 2,
                            "personal_candidates_or_whole_merge_changed");
                        CheckJournal(2); CheckPhysical(); Emit("C_attribution_priority_same_native_ID_token_verified");
                        Revoke(Challenger); break;
                    case 5:
                        CheckBlueprint(scout.Outhouse, Builder, outhouseConfirm, 5, outhouseOrder);
                        CheckBlueprint(scout.Locker, Builder, lockerConfirm, 5, lockerOrder);
                        Need(authority.GetOwnPlans(Challenger).Length == 0, "C_revoke_kept_candidate");
                        CheckJournal(2); CheckPhysical(); Emit("C_revoked_B_priority_and_identity_restored");
                        receipt.CleanupAttempted = true; Revoke(Builder); Emit("B_revoked_native_cleanup_requested"); break;
                    case 6:
                        CheckNoBlueprints(); CheckOrdinary(); CheckJournal(0); CheckPhysical();
                        receipt.CleanupComplete = true; Finish(true); return;
                }
                nextFrame = Time.frameCount + 2; // Native Start/cleanup gets real frames, never a sim step.
            }
            catch (Exception error) { Fail(error); }
        }

        private void RevalidateSites()
        {
            var reserved = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (Site site in new[] { scout.Locker, scout.Outhouse })
            {
                var plan = Intent(site.Anchor, site.Build, Builder, 5);
                Need(SafeBuildArea(world, plan), "scouted_build_safety_changed");
                WorkExecution check = NativeBuildingWork.Preflight(world, plan, true, null);
                Need(check.Accepted, "scouted_native_preflight_changed:" + check.Reason);
                Recipe.Ingredient[] ingredients = Assets.GetBuildingDef(site.Build.PrefabId).CraftRecipe.GetAllIngredients(site.Build.MaterialTags.Select(t => new Tag(t)).ToArray());
                Need(ingredients.Length == 1 && ingredients[0].tag.Name == site.Build.MaterialTags[0] && ingredients[0].amount == site.RequiredKg, "native_recipe_changed");
                float prior; reserved.TryGetValue(ingredients[0].tag.Name, out prior); reserved[ingredients[0].tag.Name] = prior + ingredients[0].amount;
                Need(world.worldInventory.GetAmount(ingredients[0].tag, false) >= reserved[ingredients[0].tag.Name], "native_combined_stock_insufficient");
                Need(site.NativeCells.SequenceEqual(WorkPlanGeometry.Footprint(plan).Select(p => Cell(world, p))), "native_scout_footprint_changed");
            }
            Need(SafeDigArea(world, scout.OrdinaryDig), "scouted_ordinary_dig_safety_changed");
        }
        private RemoteWorkRequest Submit(string actor, Site site, int priority)
        {
            RequireScope(false);
            var grant = authority.GetGrant(actor);
            Need(grant != null && grant.Permissions == RemoteWorkPermission.IssueTasks, "fixture_grant_scope_changed");
            var request = new RemoteWorkRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = planet.PlanetId, AuthorityEpoch = planet.AuthorityEpoch,
                GrantToken = grant.Token, Kind = RemoteWorkKind.Build, Layer = RemoteTaskLayer.Building, Build = site.Build.Copy(), Cells = new[] { site.Anchor }, Priority = priority, DryRun = true };
            RemoteWorkReceipt preview = authority.Handle(actor, request, Time.realtimeSinceStartupAsDouble);
            RecordRequest(actor, request, preview);
            Need(preview.Accepted && preview.DryRun, "native_build_preview_rejected:" + preview.Reason);
            request.RequestId = Guid.NewGuid().ToString("N"); request.DryRun = false; request.PreviewToken = preview.PreviewToken;
            receipt.Samples.Add(new { observed = "confirmation_before_native_write", actor, request.RequestId, request.Cells, request.Build, request.Priority,
                grantTokenSha256 = HashText(request.GrantToken), previewTokenSha256 = HashText(request.PreviewToken) });
            Emit(actor + "_confirmation_prepared_before_native_write");
            RemoteWorkReceipt confirm = authority.Handle(actor, request, Time.realtimeSinceStartupAsDouble);
            // Keep exact newly created objects even when a native error makes the outcome uncertain.
            TrackKnownBlueprints(); RecordRequest(actor, request, confirm);
            Need(confirm.Accepted && !authority.IsBlocked, "native_build_confirmation_rejected:" + confirm.Reason);
            return request;
        }
        private void CheckOrdinaryConflicts()
        {
            RemoteWorkGrant grant = authority.GetGrant(Builder);
            foreach (RemoteWorkKind kind in new[] { RemoteWorkKind.Dig, RemoteWorkKind.Build })
            {
                var request = new RemoteWorkRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = planet.PlanetId, AuthorityEpoch = planet.AuthorityEpoch,
                    GrantToken = grant.Token, Kind = kind, Layer = kind == RemoteWorkKind.Dig ? RemoteTaskLayer.Terrain : RemoteTaskLayer.Building,
                    Build = kind == RemoteWorkKind.Build ? scout.Locker.Build.Copy() : null, Cells = new[] { scout.OrdinaryDig }, DryRun = true };
                RemoteWorkReceipt rejected = authority.Handle(Builder, request, Time.realtimeSinceStartupAsDouble);
                RecordRequest(Builder, request, rejected);
                Need(!rejected.Accepted && !authority.IsBlocked && authority.ExportConfirmedPlans().Length == 0, "ordinary_native_task_conflict_was_accepted");
                if (kind == RemoteWorkKind.Dig) Need(rejected.Reason == "work_untracked_native_order", "ordinary_native_dig_rejection_not_exact");
                CheckOrdinary();
            }
        }
        private RemoteWorkSavedOrder CheckBlueprint(Site site, string actor, RemoteWorkRequest request, int priority, RemoteWorkSavedOrder same)
        {
            CheckOrdinary();
            WorkTaskIntent winner = authority.GetMergedPlans().Single(p => p.Cell.X == site.Anchor.X && p.Cell.Y == site.Anchor.Y && p.Kind == RemoteWorkKind.Build);
            Need(winner.ActorId == actor && winner.RequestId == request.RequestId && winner.Priority == priority && WorkPlanGeometry.SameBuild(winner.Build, site.Build), "canonical_native_winner_mismatch");
            GameObject order = Grid.Objects[Cell(world, site.Anchor), (int)ObjectLayer.Building];
            Need(order != null && NativeId(order) > 0, "native_pending_blueprint_missing");
            var marker = order.GetComponent<RemoteWorkBuildProvenance>();
            Need(marker != null && marker.Managed && marker.PlanetId == planet.PlanetId, "native_build_provenance_missing_or_quarantined");
            RemoteWorkSavedOrder saved = marker.CaptureOrder();
            Need(NativeBuildingWork.Matches(order, planet, winner, NativeId(order)) && RemoteWorkPersistence.SameIntent(saved.Winner, winner) &&
                saved.NativeObjectId == NativeId(order) && saved.OriginEpoch == planet.AuthorityEpoch && !string.IsNullOrEmpty(saved.Token), "native_exact_spec_footprint_or_provenance_mismatch");
            Need(order.GetComponent<Constructable>() != null && order.GetComponent<BuildingUnderConstruction>() != null &&
                order.GetComponent<Prioritizable>() != null && order.GetComponent<Prioritizable>().GetMasterPriority().priority_value == priority, "native_pending_priority_mismatch");
            var storage = order.GetComponent<Storage>();
            Need(storage != null && storage.items != null && storage.items.Count == 0, "paused_blueprint_storage_changed");
            var matches = UnityEngine.Object.FindObjectsByType<BuildingUnderConstruction>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(b => b != null && b.GetMyWorldId() == world.id && Grid.PosToCell(b) == Cell(world, site.Anchor)).ToArray();
            Need(matches.Length == 1 && matches[0].gameObject == order, "duplicate_native_blueprint_at_anchor");
            if (same != null) Need(saved.NativeObjectId == same.NativeObjectId && saved.Token == same.Token, "same_spec_priority_reallocated_native_blueprint");
            if (!createdBlueprints.Contains(order)) createdBlueprints.Add(order);
            if (!knownOrders.Any(o => o.Token == saved.Token)) knownOrders.Add(saved);
            receipt.Samples.Add(new { phase = receipt.Phase, observed = "exact_blueprint", frame = Time.frameCount, nativeId = saved.NativeObjectId,
                unityId = order.GetInstanceID(), tokenSha256 = HashText(saved.Token), epoch = saved.OriginEpoch, winner,
                footprint = site.NativeCells, priority, selectedMaterials = order.GetComponent<Constructable>().SelectedElementsTags.Select(t => t.Name).ToArray(), storageItems = storage.items.Count });
            return saved;
        }
        private void CheckReplay(string actor, RemoteWorkRequest request, RemoteWorkSavedOrder expected)
        {
            long revision = authority.ConfirmedRevision;
            int[] before = ScenePrefabIds();
            RemoteWorkReceipt replay = authority.Handle(actor, request, Time.realtimeSinceStartupAsDouble);
            RecordRequest(actor, request, replay);
            GameObject order = Grid.Objects[Cell(world, request.Cells[0]), (int)ObjectLayer.Building];
            Need(replay.Accepted && authority.ConfirmedRevision == revision && order != null && NativeId(order) == expected.NativeObjectId &&
                order.GetComponent<RemoteWorkBuildProvenance>().OrderToken == expected.Token && before.SequenceEqual(ScenePrefabIds()), "replayed_confirmation_created_or_changed_native_task");
        }
        private void CheckJournal(int orders)
        {
            NativeWorkSavedState encoded = NativeWorkJournal.CaptureSavedState(OnlineColonyState.Current, permit.PersistentOwnerId);
            var doc = RemoteWorkPersistence.Decode(encoded.Payload);
            var saved = doc.Planets.SingleOrDefault(p => p.PlanetId == permit.PlanetId);
            if (orders == 0) { Need(saved == null || saved.Orders.Length == 0 && saved.Plans.Length == 0, "journal_kept_cancelled_fixture_work"); return; }
            Need(saved != null && !saved.Blocked && saved.NativeWorldId == world.id && saved.OwnerPeerId == Owner && saved.Orders.Length == orders &&
                saved.Plans.Length == authority.ExportConfirmedPlans().Length, "journal_native_order_or_candidate_count_mismatch");
            foreach (RemoteWorkSavedOrder order in saved.Orders)
            {
                var marker = NativeWorkJournal.BuildMarkers(permit.PlanetId).Single(m => m.OrderToken == order.Token);
                RemoteWorkSavedOrder actual = marker.CaptureOrder();
                Need(actual.NativeObjectId == order.NativeObjectId && actual.OriginEpoch == order.OriginEpoch && RemoteWorkPersistence.SameIntent(actual.Winner, order.Winner), "journal_exact_marker_mismatch");
            }
            receipt.Samples.Add(new { phase = receipt.Phase, observed = "in_memory_native_save_journal", payloadSha256 = HashText(encoded.Payload),
                candidates = saved.Plans.Length, orders = saved.Orders.Select(o => new { tokenSha256 = HashText(o.Token), o.NativeObjectId, o.OriginEpoch, o.Winner }).ToArray() });
        }
        private void Revoke(string actor)
        {
            RequireScope(false);
            var result = authority.SetPermissions(actor, RemoteWorkPermission.None);
            receipt.Samples.Add(new { phase = receipt.Phase, observed = "production_revocation", actor, result.Revision, permissions = result.Permissions.ToString(),
                tokenSha256 = HashText(result.Token), blocked = authority.IsBlocked, error = authority.BlockReason });
            Need(result.Permissions == RemoteWorkPermission.None && !authority.IsBlocked, "native_revoke_execution_uncertain:" + authority.BlockReason);
        }
        private void CheckOrdinary()
        {
            Need(ordinary != null && ordinary.GetInstanceID() == ordinaryUnityId && NativeId(ordinary) == ordinaryNativeId &&
                Grid.Objects[Cell(world, scout.OrdinaryDig), (int)ObjectLayer.DigPlacer] == ordinary &&
                Grid.Solid[Cell(world, scout.OrdinaryDig)] && ordinary.GetComponent<RemoteWorkDigProvenance>() != null && !ordinary.GetComponent<RemoteWorkDigProvenance>().Managed &&
                ordinary.GetComponent<Prioritizable>().GetMasterPriority().priority_value == 3, "ordinary_A_native_dig_was_changed_or_deleted");
            receipt.OrdinaryDigPreserved = true;
        }
        private void CheckNoBlueprints()
        {
            Need(authority != null && !authority.IsBlocked && authority.GetMergedPlans().Length == 0 && authority.ExportConfirmedPlans().Length == 0,
                "fixture_authority_cleanup_not_confirmed");
            Need(NativeWorkJournal.BuildMarkers(permit.PlanetId).Length == 0 && createdBlueprints.All(go => go == null), "fixture_native_blueprint_cleanup_incomplete");
            foreach (Site site in new[] { scout.Locker, scout.Outhouse })
                foreach (int cell in site.NativeCells) Need(Grid.Objects[cell, (int)ObjectLayer.Building] == null, "fixture_cancelled_footprint_still_occupied");
        }
        private void TrackKnownBlueprints()
        {
            if (planet == null) return;
            foreach (var marker in NativeWorkJournal.BuildMarkers(permit.PlanetId))
            {
                var order = marker.CaptureOrder();
                if (order.OriginEpoch != planet.AuthorityEpoch || order.Winner.ActorId != Builder && order.Winner.ActorId != Challenger) continue;
                if (!createdBlueprints.Contains(marker.gameObject)) createdBlueprints.Add(marker.gameObject);
                if (!knownOrders.Any(o => o.Token == order.Token)) knownOrders.Add(order);
            }
        }
        private void Fail(Exception error)
        {
            receipt.Failures.Add(error.GetType().Name + ":" + error.Message);
            if (cleanupStarted) { Finish(false); return; }
            cleanupStarted = true; receipt.CleanupAttempted = authority != null;
            try { RequireScope(false); }
            catch (Exception guardError) { receipt.Failures.Add("cleanup_scope_refused:" + guardError.Message); Finish(false); return; }
            if (authority != null)
                foreach (string actor in new[] { Challenger, Builder })
                    try { Revoke(actor); }
                    catch (Exception cleanupError) { receipt.Failures.Add("cleanup_" + actor + ":" + cleanupError.Message); }
            try { TrackKnownBlueprints(); Emit("failed_production_cleanup_attempted"); }
            catch (Exception reportError) { receipt.Failures.Add("cleanup_receipt:" + reportError.Message); }
            nextFrame = Time.frameCount + 2;
        }
        private void Finish(bool success)
        {
            if (finished) return;
            try
            {
                RequireScope(true);
                receipt.CopyBytesUnchanged = HashFile(permit.ActiveCopyPath) == permit.ActiveCopySha256;
                receipt.SourceBytesUnchanged = HashFile(permit.OriginalSourcePath) == permit.OriginalSourceSha256;
                if (ordinary != null) CheckOrdinary();
                if (authority != null) { TrackKnownBlueprints(); CheckNoBlueprints(); CheckJournal(0); receipt.CleanupComplete = true; }
                if (baseline != null) CheckPhysical();
                Need(receipt.CopyBytesUnchanged && receipt.SourceBytesUnchanged, "native_checkpoint_bytes_changed");
            }
            catch (Exception error) { receipt.Failures.Add("final:" + error.Message); success = false; }
            receipt.AuthorityBlocked = authority != null && authority.IsBlocked;
            receipt.Passed = success && receipt.Failures.Count == 0 && receipt.CleanupComplete && receipt.OrdinaryDigPreserved && receipt.PhysicalFingerprintUnchanged;
            receipt.NativeBuildingExecutionValidated = receipt.Passed;
            receipt.Finished = true; receipt.FinishedFrame = Time.frameCount; finished = true;
            try { Emit(receipt.Passed ? "passed_paused_and_cleaned" : "failed_paused_exact_objects_preserved"); }
            catch (Exception error) { Debug.LogWarning("[OniOnline probe] Building receipt writer failed: " + error.Message); }
            active = null;
        }
        private void Emit(string phaseName)
        {
            receipt.Phase = phaseName;
            var remaining = NativeWorkJournal.BuildMarkers(permit.PlanetId).Select(m => m.CaptureOrder()).Select(o => new {
                o.NativeObjectId, tokenSha256 = HashText(o.Token), o.OriginEpoch, o.Winner }).ToArray();
            receipt.Samples.Add(new { phase = phaseName, frame = Time.frameCount, time = GameClock.Instance == null ? -1 : GameClock.Instance.GetTime(),
                ordinaryCell = scout.OrdinaryDig, ordinaryNativeId, ordinaryUnityId,
                canonicalRevision = authority == null ? 0 : authority.ConfirmedRevision, blocked = authority != null && authority.IsBlocked,
                remainingExactManagedOrders = remaining, knownNativeIds = knownOrders.Select(o => o.NativeObjectId).Distinct().ToArray(),
                plannedSites = new[] { scout.Locker, scout.Outhouse }.Where(s => s != null).Select(s => {
                    GameObject go = Grid.Objects[Cell(world, s.Anchor), (int)ObjectLayer.Building];
                    return new { s.Anchor, nativeId = NativeId(go), unityId = go == null ? 0 : go.GetInstanceID(),
                        hasMarker = go != null && go.GetComponent<RemoteWorkBuildProvenance>() != null,
                        managed = go != null && go.GetComponent<RemoteWorkBuildProvenance>() != null && go.GetComponent<RemoteWorkBuildProvenance>().Managed };
                }).ToArray() });
            write(JObject.FromObject(receipt));
        }
        private void RecordRequest(string actor, RemoteWorkRequest request, RemoteWorkReceipt result)
        {
            receipt.Samples.Add(new { observed = "production_authority_receipt", frame = Time.frameCount, actor, request.RequestId, request.DryRun,
                kind = request.Kind.ToString(), request.Cells, request.Build, request.Priority,
                grantTokenSha256 = HashText(request.GrantToken), previewTokenSha256 = HashText(result.PreviewToken),
                result.Accepted, result.Reason, result.Count, revision = authority.ConfirmedRevision });
        }
        private void RequireScope(bool hashFiles)
        {
            guard();
            Need(Thread.CurrentThread.ManagedThreadId == thread && ReferenceEquals(sourceGame, Game.Instance) && ReferenceEquals(sourceSave, SaveGame.Instance), "native_source_or_thread_changed");
            Need(ReferenceEquals(world, RequirePermit(permit, hashFiles)), "native_world_replaced");
            Need(receipt.InitialGameTime == GameClock.Instance.GetTime(), "native_time_advanced");
        }
        private void CheckPhysical()
        {
            receipt.AfterPhysicalSha256 = PhysicalHash(); receipt.PhysicalFingerprintUnchanged = receipt.AfterPhysicalSha256 == baseline;
            Need(receipt.PhysicalFingerprintUnchanged, "native_physical_fingerprint_changed");
            var allowed = new HashSet<int>(initialPrefabIds);
            foreach (GameObject go in createdBlueprints.Where(go => go != null).Concat(ordinary == null ? Array.Empty<GameObject>() : new[] { ordinary }))
                foreach (var id in go.GetComponentsInChildren<KPrefabID>(true)) allowed.Add(id.InstanceID);
            int[] current = ScenePrefabIds();
            Need(current.All(id => allowed.Contains(id)) && initialPrefabIds.All(id => current.Contains(id)), "unexpected_nonfixture_native_root_created_or_removed");
        }
        private string PhysicalHash()
        {
            // The caller fingerprint is the existing original-3 amounts/assignments/Grid
            // fingerprint. Native cancel may hide a UI panel; selection is not physical state.
            var supplied = JObject.FromObject(fingerprint()); supplied.Remove("selection");
            return HashObject(new { supplied, resources = CaptureResourceFingerprint() });
        }
        // Physical loose/stored resources, never the synthetic PrimaryElement on a
        // construction placeholder. Pickupable entries include items in construction storage;
        // the latter's owner metadata is excluded and our two blueprint stores must stay empty.
        internal static object CaptureResourceFingerprint()
        {
            var pickups = UnityEngine.Object.FindObjectsByType<Pickupable>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(p => p != null && p.gameObject.scene.IsValid() && p.GetComponent<PrimaryElement>() != null && p.GetComponent<BuildingUnderConstruction>() == null)
                .OrderBy(p => p.GetInstanceID()).Select(p => {
                    var element = p.GetComponent<PrimaryElement>();
                    return new { id = p.GetInstanceID(), nativeId = NativeId(p.gameObject), cell = Grid.PosToCell(p),
                        element = element.ElementID.ToString(), element.Mass, element.Temperature, element.DiseaseIdx, element.DiseaseCount };
                }).ToArray();
            var storage = UnityEngine.Object.FindObjectsByType<Storage>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(s => s != null && s.gameObject.scene.IsValid() && s.GetComponent<BuildingUnderConstruction>() == null)
                .OrderBy(s => s.GetInstanceID()).Select(s => new { owner = s.GetInstanceID(),
                    items = s.items == null ? null : s.items.Where(go => go != null).Select(go => go.GetInstanceID()).OrderBy(id => id).ToArray() }).ToArray();
            return new { pickups, storage };
        }
        private static WorldContainer RequirePermit(NativeBuildingBridgePermit p, bool hashFiles)
        {
            Need(p != null && p.MutableCopyAuthorized && p.GeneratedThisProcess && p.CreatedPid == Process.GetCurrentProcess().Id &&
                ReferenceEquals(p.ExpectedGame, Game.Instance) && ReferenceEquals(p.ExpectedSave, SaveGame.Instance) &&
                !string.IsNullOrEmpty(p.OriginalSourcePath) && !string.IsNullOrEmpty(p.ActiveCopyPath) &&
                !string.IsNullOrEmpty(p.OwnedSaveDirectory) && IsHash(p.ActiveCopySha256) && IsHash(p.OriginalSourceSha256) && IsHash(p.LoadedProbeSha256), "explicit_mutable_copy_permit_required");
            string root = Path.GetFullPath(p.OwnedSaveDirectory).TrimEnd(Path.DirectorySeparatorChar);
            string path = Path.GetFullPath(p.ActiveCopyPath), original = Path.GetFullPath(p.OriginalSourcePath);
            Need(Path.IsPathRooted(p.OwnedSaveDirectory) && Path.IsPathRooted(p.ActiveCopyPath) && Path.IsPathRooted(p.OriginalSourcePath) &&
                Path.GetFileName(root) == "OniOnlineHarness-" + p.CreatedPid && path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                original.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                path != original && File.Exists(path) && File.Exists(original) && !HasSymlink(root) && !HasSymlink(path) && !HasSymlink(original), "exact_distinct_owned_checkpoint_required");
            Need(Game.Instance != null && Game.Instance.baseAlreadyCreated && !Game.Instance.IsLoading() && SaveGame.Instance != null &&
                GameClock.Instance != null && SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused && ClusterManager.Instance != null &&
                DigTool.Instance != null && !DebugHandler.InstantBuildMode && !Game.Instance.SandboxModeActive, "paused_normal_native_colony_required");
            Need(Path.GetFullPath(SaveLoader.GetActiveSaveFilePath()) == path && (OnlineRuntime.Instance == null || OnlineRuntime.Instance.Session == null), "exact_active_copy_and_no_room_required");
            Need(p.OriginalStarterIds != null && p.OriginalStarterIds.Length == 3 && p.OriginalStarterIds.All(id => id > 0) && p.OriginalStarterIds.Distinct().Count() == 3 &&
                p.ExactOriginalStarters != null && p.ExactOriginalStarters.Length == 3 && p.ExactOriginalStarters.All(d => d != null && Components.LiveMinionIdentities.Items.Any(live => ReferenceEquals(live, d))) &&
                p.ExactOriginalStarters.Select(d => d.GetComponent<KPrefabID>().InstanceID).OrderBy(id => id).SequenceEqual(p.OriginalStarterIds.OrderBy(id => id)) &&
                Components.LiveMinionIdentities.Items.Where(d => d != null).Select(d => d.GetComponent<KPrefabID>().InstanceID).OrderBy(id => id).SequenceEqual(p.OriginalStarterIds.OrderBy(id => id)), "exact_original_three_required");
            var colony = OnlineColonyState.Current;
            var manifest = colony == null ? null : colony.CapturePlanet();
            Need(manifest != null && manifest.PlanetId == p.PlanetId && manifest.OwnerId == p.PersistentOwnerId && manifest.NativeWorldId == p.NativeWorldId &&
                Components.LiveMinionIdentities.Items.All(d => d.GetMyWorldId() == p.NativeWorldId), "native_owner_planet_world_mismatch");
            if (hashFiles) Need(HashFile(path) == p.ActiveCopySha256 && HashFile(original) == p.OriginalSourceSha256 &&
                HashFile(typeof(NativeBuildingBridgeProbe).Assembly.Location) == p.LoadedProbeSha256 && HashFile(typeof(Game).Assembly.Location) == GameHash &&
                HashFile(typeof(KMonoBehaviour).Assembly.Location) == FrameworkHash, "checkpoint_or_native_binary_hash_mismatch");
            var world = ClusterManager.Instance.GetWorld(p.NativeWorldId);
            Need(world != null && world.worldInventory != null, "native_world_inventory_missing"); return world;
        }
        private static Site ChooseMaterial(WorldContainer world, string prefab, Dictionary<string, float> reserved, ScoutReceipt receipt)
        {
            BuildingDef def = Assets.GetBuildingDef(prefab);
            if (def == null || def.MaterialCategory == null || def.MaterialCategory.Length != 1 || def.CraftRecipe == null || DiscoveredResources.Instance == null) return null;
            foreach (Tag material in MaterialSelector.GetValidMaterials(new Tag(def.MaterialCategory[0]), true).OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                bool discovered = DiscoveredResources.Instance.IsDiscovered(material);
                if (ElementLoader.GetElement(material) == null) continue;
                var ingredients = def.CraftRecipe.GetAllIngredients(new[] { material });
                if (ingredients.Length != 1 || ingredients[0].tag != material || !(ingredients[0].amount > 0) || float.IsInfinity(ingredients[0].amount)) continue;
                float prior; reserved.TryGetValue(material.Name, out prior);
                float available = world.worldInventory.GetAmount(material, false);
                receipt.MaterialChecks.Add(new { prefab, material = material.Name, discovered, requiredKg = ingredients[0].amount, previouslyReservedKg = prior, availableInThisWorldKg = available });
                if (!discovered || float.IsNaN(available) || float.IsInfinity(available) || available < prior + ingredients[0].amount) continue;
                reserved[material.Name] = prior + ingredients[0].amount;
                return new Site { Build = new WorkBuildSpec { PrefabId = prefab, MaterialTags = new[] { material.Name } }, RequiredKg = ingredients[0].amount, AvailableKg = available };
            }
            return null;
        }
        private static bool SafeBuildArea(WorldContainer world, WorkTaskIntent plan)
        {
            WorkCell[] footprint = WorkPlanGeometry.Footprint(plan);
            foreach (WorkCell p in footprint)
            {
                int cell = Cell(world, p);
                if (!SafeCell(world, p) || Grid.Solid[cell] || Grid.Foundation[cell]) return false;
                if (Components.LiveMinionIdentities.Items.Any(d => d != null && Math.Abs(Grid.CellColumn(Grid.PosToCell(d)) - Grid.CellColumn(cell)) <= 1 && Math.Abs(Grid.CellRow(Grid.PosToCell(d)) - Grid.CellRow(cell)) <= 1)) return false;
                for (int layer = 0; layer < (int)ObjectLayer.NumLayers; layer++) if (Grid.Objects[cell, layer] != null) return false;
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) if (!SafeCell(world, new WorkCell(p.X + dx, p.Y + dy))) return false;
            }
            return true;
        }
        private static bool SafeDigArea(WorldContainer world, WorkCell p)
        {
            if (!SafeCell(world, p)) return false;
            int cell = Cell(world, p);
            if (!Grid.Solid[cell] || Grid.Foundation[cell]) return false;
            string name = Grid.Element[cell].id.ToString();
            if (name != "Dirt" && name != "SandStone" && name != "Granite" && name != "IgneousRock" && name != "SedimentaryRock") return false;
            for (int dx = -2; dx <= 2; dx++) for (int dy = -2; dy <= 2; dy++)
            {
                var neighbor = new WorkCell(p.X + dx, p.Y + dy);
                if (!SafeCell(world, neighbor) || Grid.Foundation[Cell(world, neighbor)]) return false;
                for (int layer = 0; layer < (int)ObjectLayer.NumLayers; layer++) if (Grid.Objects[Cell(world, neighbor), layer] != null) return false;
            }
            return true;
        }
        private static bool SafeCell(WorldContainer world, WorkCell point)
        {
            if (point.X < 1 || point.Y < 1 || point.X >= world.WorldSize.x - 1 || point.Y >= world.WorldSize.y - 1) return false;
            int cell = Cell(world, point);
            if (!Grid.IsValidCell(cell) || Grid.WorldIdx[cell] != world.id || Grid.Visible[cell] == 0 || Grid.Element[cell] == null || Grid.Element[cell].IsLiquid ||
                Grid.Temperature[cell] < 270 || Grid.Temperature[cell] > 320 || Grid.DiseaseCount[cell] != 0) return false;
            string element = Grid.Element[cell].id.ToString();
            return !Grid.Element[cell].IsGas || Grid.Mass[cell] <= .05f || element == "Oxygen" || element == "CarbonDioxide";
        }
        private static IEnumerable<WorkCell> Candidates(WorkCell center)
        {
            int count = 0;
            for (int radius = 2; radius <= 48; radius++)
                for (int dx = -radius; dx <= radius; dx++) for (int dy = -radius; dy <= radius; dy++)
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == radius) { if (++count > 8192) yield break; yield return new WorkCell(center.X + dx, center.Y + dy); }
        }
        private static WorkTaskIntent Intent(WorkCell point, WorkBuildSpec build, string actor, int priority)
        { return new WorkTaskIntent { Cell = point, Build = build.Copy(), ActorId = actor, Kind = RemoteWorkKind.Build, Layer = RemoteTaskLayer.Building, Priority = priority }; }
        private static int Cell(WorldContainer world, WorkCell point) { return Grid.XYToCell(point.X + world.WorldOffset.x, point.Y + world.WorldOffset.y); }
        private static int NativeId(GameObject go) { var id = go == null ? null : go.GetComponent<KPrefabID>(); return id == null ? 0 : id.InstanceID; }
        private static int[] ScenePrefabIds()
        { return UnityEngine.Object.FindObjectsByType<KPrefabID>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(id => id != null && id.InstanceID > 0 && id.gameObject.scene.IsValid()).Select(id => id.InstanceID).OrderBy(id => id).ToArray(); }
        private static string SelectionHash(ScoutReceipt s) { return HashObject(new { s.PermitSha256, s.GameTime, s.Locker, s.Outhouse, s.OrdinaryDig }); }
        private static ScoutReceipt BlockScout(ScoutReceipt receipt, string reason) { receipt.Ready = false; receipt.Blocked = true; receipt.Reason = reason; return receipt; }
        private static void CountReject(ScoutReceipt receipt, string reason)
        { reason = reason ?? "unknown"; int count; receipt.Rejections.TryGetValue(reason, out count); receipt.Rejections[reason] = count + 1; }
        private static T Clone<T>(T value) { return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value)); }
        private static string HashObject(object value) { return HashText(JsonConvert.SerializeObject(value)); }
        private static string HashText(string value)
        { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""))); }
        private static string HashFile(string path)
        { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(stream)); }
        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        private static bool IsHash(string value) { return value != null && value.Length == 64 && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f'); }
        private static bool HasSymlink(string path)
        {
            for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true; if (current == Path.GetPathRoot(current)) break; }
            return false;
        }
        private static void Need(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        private void OnDestroy()
        {
            if (finished || receipt == null) return;
            receipt.Failures.Add("probe_owner_destroyed_before_confirmed_cleanup"); receipt.Finished = true; receipt.Passed = false;
            try { Emit("aborted_no_unknown_entity_cleanup"); } catch (Exception) { }
            finished = true; if (active == this) active = null;
        }
    }
}
