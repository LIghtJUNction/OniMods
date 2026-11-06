// Probe-only observation. This file has no native reservation, serialization, load or release path.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using OniOnline.Multiplayer;
using UnityEngine;

namespace OniOnline
{
    internal sealed class NativeRoundTripPreparationPermit
    {
        internal DuplicantTransferExpectation Source;
        internal int[] OriginalStarterIds;
        internal bool NormalCheckpointReloadProven, LifecycleProven;
        internal string ArtifactDirectory;
    }

    internal sealed class NativeRoundTripPreparationReport
    {
        public bool PreparationReady, ObservationCompleted, NativeFingerprintUnchanged;
        public bool CandidateIdsOnly = true, IdsReserved = false, NativeLoadAllowed = false;
        public bool CandidatePayloadPrepared;
        public int CandidateMinionId = -1, CandidateProxyId = -1, CounterObserved = -1;
        public DuplicantTransferPreflightResult SourcePreflight;
        public Dictionary<string, object> Observations = new Dictionary<string, object>();
        public List<string> BlockingReasons = new List<string>();
        public object PayloadPlan;
        public string[] RequiredExecutionContracts = { "reserve_native_ids_and_rebuild_payload", "atomic_async_drain_and_exact_source_auxiliary_release",
            "durable_source_three_to_two_checkpoint", "native_error_and_partial_load_rollback", "cross_frame_native_state_machine_and_same_person_validation",
            "new_three_person_checkpoint_reload", "complete_existing_save_history_and_stable_owner_migration" };
        public string Scope = "Read-only preparation for one existing starter in a disposable colony. Candidate payload is unreserved and cannot authorize native loading or source release. Not cross-world, two-game or existing-save migration acceptance.";
        [JsonIgnore] internal NativeDupeRemapPayload Payload;
        internal void Block(string reason) { if (!BlockingReasons.Contains(reason)) BlockingReasons.Add(reason); }
    }

    internal static class NativeDuplicantRoundTripPreparation
    {
        internal static object CaptureIdentityFingerprint()
        {
            var map = TrackerMap();
            return new { nextUniqueId = KPrefabID.NextUniqueID,
                tracker = map.OrderBy(pair => pair.Key).Select(pair => new { id = pair.Key,
                    unityId = pair.Value == null ? 0 : pair.Value.GetInstanceID() }).ToArray(),
                sceneIdentities = UnityEngine.Object.FindObjectsByType<KPrefabID>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(identity => identity != null).OrderBy(identity => identity.GetInstanceID())
                    .Select(identity => new { nativeId = identity.InstanceID, unityId = identity.GetInstanceID() }).ToArray(),
                serializationTemplates = Cache("serializationTemplatesByTypeName").Count,
                deserializationTemplates = Cache("deserializationTemplatesByTypeName").Count,
                deserializationMappings = Cache("deserializationMappings").Count };
        }

        internal static NativeRoundTripPreparationReport Inspect(NativeRoundTripPreparationPermit permit)
        {
            var report = new NativeRoundTripPreparationReport();
            if (permit == null || permit.Source == null)
            { report.Block("exact_source_preparation_permit_required"); return report; }
            if (!permit.NormalCheckpointReloadProven) report.Block("normal_exact_checkpoint_reload_not_proven");
            if (!permit.LifecycleProven) report.Block("lifecycle_success_not_proven_for_current_game_and_checkpoint");
            var selected = SelectTool.Instance == null ? null : SelectTool.Instance.selected;
            var minion = selected == null ? null : selected.GetComponent<MinionIdentity>();
            if (minion == null || permit.Source == null || permit.OriginalStarterIds == null)
            { report.Block("existing_original_starter_selection_required"); return report; }
            var identity = minion.GetComponent<KPrefabID>();
            if (identity == null || !permit.OriginalStarterIds.Contains(identity.InstanceID) || identity.InstanceID != permit.Source.MinionId)
            { report.Block("selected_identity_not_in_exact_original_starter_allowlist"); return report; }
            report.SourcePreflight = NativeDuplicantTransferPreflight.Inspect(permit.Source);
            if (report.SourcePreflight == null || report.SourcePreflight.Observation == null || report.SourcePreflight.Rejections == null)
            { report.Block("source_preflight_observation_incomplete"); return report; }
            if (!report.SourcePreflight.SourceEligible)
                foreach (string reason in report.SourcePreflight.Rejections) report.Block("source:" + reason);
            var map = TrackerMap();
            var nav = minion.GetComponent<Navigator>();
            Run(report, "auxiliary", () => InspectAuxiliary(nav, map, report));
            Run(report, "async", () => InspectAsync(nav, report));
            Run(report, "externalHistory", () => InspectHistory(minion, identity.InstanceID, report.SourcePreflight.Observation.ProxyId, report));
            WorkCell? destination = null;
            Run(report, "destination", () => InspectDestination(minion, nav, permit.Source.NativeWorldId, report, out destination));
            report.CounterObserved = KPrefabID.NextUniqueID;
            if (report.CounterObserved <= 0 || report.CounterObserved >= int.MaxValue - 1)
                report.Block("native_id_counter_not_in_reviewed_candidate_range");
            int candidate = Math.Max(1, report.CounterObserved);
            for (int attempts = 0; attempts < 1024 && candidate < int.MaxValue; attempts++, candidate++)
            {
                if (map.ContainsKey(candidate)) continue;
                if (report.CandidateMinionId < 0) report.CandidateMinionId = candidate;
                else { report.CandidateProxyId = candidate; break; }
            }
            if (report.CandidateProxyId < 0) report.Block("two_unoccupied_candidate_ids_not_found_without_reservation");
            Run(report, "artifactAndPayload", () => InspectArtifactAndPayload(permit, minion, destination, map.Keys.ToArray(), report));
            report.ObservationCompleted = true;
            report.PreparationReady = report.BlockingReasons.Count == 0 && report.CandidatePayloadPrepared;
            return report;
        }

        private static Dictionary<int, KPrefabID> TrackerMap()
        {
            var singleton = AccessTools.Field(typeof(KPrefabIDTracker), "Instance");
            var field = AccessTools.Field(typeof(KPrefabIDTracker), "prefabIdMap");
            object tracker = singleton == null ? null : singleton.GetValue(null);
            var map = tracker == null || field == null ? null : field.GetValue(tracker) as Dictionary<int, KPrefabID>;
            if (map == null || map.Count > 200000) throw new InvalidOperationException("existing_bounded_native_tracker_contract_missing");
            return map;
        }

        private static IDictionary Cache(string name)
        {
            var field = AccessTools.Field(typeof(KSerialization.Manager), name);
            var cache = field == null ? null : field.GetValue(null) as IDictionary;
            if (cache == null) throw new InvalidOperationException("existing_native_serializer_cache_contract_missing:" + name);
            return cache;
        }

        private static void Run(NativeRoundTripPreparationReport report, string name, Func<object> inspect)
        {
            try { report.Observations[name] = inspect(); }
            catch (Exception error) { report.Block(name + ":" + error.GetType().Name + ":" + error.Message); }
        }

        private static object InspectAuxiliary(Navigator nav, Dictionary<int, KPrefabID> map, NativeRoundTripPreparationReport report)
        {
            var field = AccessTools.Field(typeof(Navigator), "targetLocator");
            var locator = nav == null || field == null ? null : field.GetValue(nav) as KPrefabID;
            if (locator == null || locator.InstanceID <= 0 || locator.PrefabTag.Name != TargetLocator.ID ||
                !map.TryGetValue(locator.InstanceID, out var registered) || !ReferenceEquals(locator, registered))
                throw new InvalidOperationException("exact_registered_source_target_locator_missing");
            var navigators = UnityEngine.Object.FindObjectsByType<Navigator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int owners = navigators.Count(other => other != null && ReferenceEquals(field.GetValue(other), locator));
            int foreignTargets = navigators.Count(other => other != null && !ReferenceEquals(other, nav) && ReferenceEquals(other.target, locator));
            if (owners != 1 || foreignTargets != 0) report.Block("source_target_locator_not_exclusive_in_reviewed_navigator_fields");
            return new { id = locator.InstanceID, unityId = locator.GetInstanceID(), owners, foreignTargets,
                reviewedNavigatorCount = navigators.Length, nativeCleanupDeletesLocator = false,
                requiresExactAuxiliaryReleaseAndPostCleanupAudit = true };
        }

        private static object InspectAsync(Navigator nav, NativeRoundTripPreparationReport report)
        {
            if (nav == null || AsyncPathProber.Instance == null) throw new InvalidOperationException("existing_async_navigation_manager_missing");
            object manager = AsyncPathProber.Instance;
            lock (manager)
            {
                var navigators = Field(manager, "navigators") as IDictionary;
                var queue = Field(manager, "workQueue") as IEnumerable;
                var finished = Field(manager, "finishedWork") as IEnumerable;
                if (navigators == null || queue == null || finished == null || !navigators.Contains(nav))
                    throw new InvalidOperationException("registered_source_async_contract_missing");
                int status = (int)navigators[nav];
                int queued = queue.Cast<object>().Count(order => ReferenceEquals(Field(order, "navigator"), nav));
                int completed = finished.Cast<object>().Count(result => ReferenceEquals(Field(result, "navigator"), nav));
                if (status == -1 || completed != 0) report.Block("source_async_probe_in_flight_or_result_pending");
                return new { registered = true, status, queuedSourceOrders = queued, finishedSourceResults = completed,
                    capturedUnderNativeManagerLock = true, executionMustRecheckUnderSameLock = true, drainedOrUnregistered = false };
            }
        }

        private static object InspectHistory(MinionIdentity minion, int minionId, int proxyId, NativeRoundTripPreparationReport report)
        {
            if (ReportManager.Instance == null || ReportManager.Instance.reports == null || ReportManager.Instance.TodaysReport == null)
                throw new InvalidOperationException("native_daily_report_registry_missing");
            string name = minion.GetProperName();
            var seen = new HashSet<ReportManager.ReportEntry>();
            var matches = new List<object>();
            System.Action<ReportManager.ReportEntry> visit = null;
            visit = entry => {
                if (entry == null || !seen.Add(entry)) return;
                if (seen.Count > 100000) throw new InvalidOperationException("native_personal_history_node_bound");
                if (entry.contextId == minionId || entry.contextId == proxyId || (entry.contextId == 0 && entry.context == name))
                    matches.Add(new { entry.contextId, entry.context, reportType = entry.reportType.ToString(), entry.accumulate, entry.accPositive, entry.accNegative });
                if (entry.contextEntries.Count > 100000) throw new InvalidOperationException("native_personal_history_child_bound");
                for (int i = 0; i < entry.contextEntries.Count; i++) visit(entry.contextEntries[i]);
            };
            var days = ReportManager.Instance.reports.Concat(new[] { ReportManager.Instance.TodaysReport }).Distinct().ToArray();
            if (days.Length > 10000) throw new InvalidOperationException("native_daily_report_bound");
            foreach (var day in days)
            {
                if (day == null || day.reportEntries == null) throw new InvalidOperationException("native_daily_report_entry_registry_missing");
                foreach (var entry in day.reportEntries) visit(entry);
            }
            var trackers = UnityEngine.Object.FindObjectsByType<ColonyAchievementTracker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (trackers.Length != 1 || trackers[0] == null || trackers[0].dupesCompleteChoresInSuits == null)
                throw new InvalidOperationException("exact_native_achievement_history_registry_missing");
            if (trackers[0].dupesCompleteChoresInSuits.Count > 10000 || trackers[0].dupesCompleteChoresInSuits.Values.Any(ids => ids == null || ids.Count > 200000))
                throw new InvalidOperationException("native_achievement_id_history_bound_or_null_list");
            int achievementMatches = trackers[0].dupesCompleteChoresInSuits.Values.Sum(ids => ids.Count(id => id == minionId || id == proxyId));
            if (matches.Count > 0) report.Block("source_has_external_personal_report_entries");
            if (achievementMatches > 0) report.Block("source_has_external_personal_achievement_ids");
            if (GameClock.Instance.GetCycle() != 0) report.Block("nonzero_cycle_external_history_closure_not_reviewed_for_this_mechanism_experiment");
            return new { reportsInspected = days.Length, reportNodesInspected = seen.Count, reportMatches = matches,
                achievementMatches, generalExistingSaveHistoryMigrationProven = false };
        }

        private static object InspectDestination(MinionIdentity minion, Navigator nav, int worldId,
            NativeRoundTripPreparationReport report, out WorkCell? chosen)
        {
            chosen = null;
            int source = Grid.PosToCell(minion);
            var inspected = new List<object>();
            foreach (int dx in new[] { 1, -1 })
            {
                int x = Grid.CellColumn(source) + dx, y = Grid.CellRow(source);
                int foot = Grid.XYToCell(x, y), head = Grid.XYToCell(x, y + 1), floor = Grid.XYToCell(x, y - 1);
                bool inside = x >= 0 && x < Grid.WidthInCells && y > 0 && y + 1 < Grid.HeightInCells;
                bool safe = inside && new[] { foot, head, floor }.All(cell => Grid.IsValidCell(cell) && Grid.WorldIdx[cell] == worldId && Grid.Visible[cell] > 0);
                if (safe) safe = new[] { foot, head }.All(cell => !Grid.Solid[cell] && Grid.Element[cell] != null &&
                    Grid.Element[cell].id == SimHashes.Oxygen && Grid.Mass[cell] >= .1f && Grid.Temperature[cell] >= 270 && Grid.Temperature[cell] <= 320) &&
                    Grid.Solid[floor] && Grid.Temperature[floor] >= 270 && Grid.Temperature[floor] <= 320;
                bool occupied = !inside || Components.LiveMinionIdentities.Items.Any(other => other != null &&
                    new[] { Grid.PosToCell(other), Grid.OffsetCell(Grid.PosToCell(other), new CellOffset(0, 1)) }.Any(cell => cell == foot || cell == head));
                if (safe)
                {
                    for (int layer = 0; layer < Grid.ObjectLayers.Length; layer++)
                        if (Grid.Objects[foot, layer] != null || Grid.Objects[head, layer] != null) occupied = true;
                }
                // Pinned 744825 chain: CanReach -> GetNavigationCost -> PathGrid.GetCost ->
                // OffsetCell/IsValidSerialNo/ProberCells[].cost. No probe, SM getter or lazy state.
                bool reachable = safe && !occupied && nav != null && nav.PathGrid != null && nav.CanReach(foot);
                inspected.Add(new { x, y, foot, head, floor, safe, occupied, reachable });
                if (reachable && !chosen.HasValue) chosen = new WorkCell(x, y);
            }
            if (!chosen.HasValue) report.Block("no_safe_unoccupied_reachable_same_world_adjacent_two_cell_destination");
            return new { candidates = inspected, selected = chosen, sourceCell = source, worldId };
        }

        private static object InspectArtifactAndPayload(NativeRoundTripPreparationPermit permit, MinionIdentity minion,
            WorkCell? destination, int[] existingIds, NativeRoundTripPreparationReport report)
        {
            string directory = permit.ArtifactDirectory;
            if (string.IsNullOrEmpty(directory) || !Path.IsPathRooted(directory)) throw new InvalidOperationException("existing_absolute_archive_v2_directory_required");
            string manifestText = Encoding.UTF8.GetString(ReadBounded(Path.Combine(directory, "manifest.json"), 2 * 1024 * 1024));
            var document = Newtonsoft.Json.Linq.JObject.Parse(manifestText);
            if (document["Version"] == null || document["Version"].Type != Newtonsoft.Json.Linq.JTokenType.Integer || (int)document["Version"] != 2)
                throw new InvalidOperationException("explicit_archive_version_two_required_no_legacy_default");
            var manifest = JsonConvert.DeserializeObject<NativeDuplicantArchiveManifest>(manifestText);
            if (manifest == null || manifest.Version != 2 || manifest.Roots == null || manifest.Roots.Length != 2 || manifest.Assemblies == null)
                throw new InvalidOperationException("exact_two_root_archive_v2_manifest_required");
            int minionId = minion.GetComponent<KPrefabID>().InstanceID;
            int proxyId = report.SourcePreflight.Observation.ProxyId;
            if (manifest.SourceActiveSave != permit.Source.ActiveSavePath || manifest.SourceOwnerId != permit.Source.OwnerId ||
                manifest.SourcePlanetId != permit.Source.PlanetId || manifest.SourceWorldId != permit.Source.NativeWorldId ||
                manifest.Roots[0].InstanceId != minionId || manifest.Roots[1].InstanceId != proxyId ||
                manifest.SourceDupeOriginId != "oni-dupe-origin:" + permit.Source.OwnerId + ":" + permit.Source.PlanetId + ":" + minionId ||
                manifest.SourceTime != GameClock.Instance.GetTime() || manifest.SourceCycle != GameClock.Instance.GetCycle() ||
                manifest.SourceTimeInCycles != GameClock.Instance.GetTimeInCycles() || !manifest.PhysicalFingerprintUnchanged)
                throw new InvalidOperationException("archive_exact_source_checkpoint_object_or_clock_mismatch");
            string currentFingerprint = CurrentArchiveFingerprint(minion, proxyId);
            if (manifest.BeforeFingerprint != currentFingerprint || manifest.AfterFingerprint != currentFingerprint)
                throw new InvalidOperationException("archive_current_native_object_fingerprint_mismatch");
            Run(report, "immutableOrigin", () => InspectImmutableOrigin(manifest, permit.Source.NativeWorldId, minionId, report));
            string gameHash = HashFile(typeof(Game).Assembly.Location), frameworkHash = HashFile(typeof(KMonoBehaviour).Assembly.Location);
            if (gameHash != NativeDuplicantRemapPlanner.GameHash || frameworkHash != NativeDuplicantRemapPlanner.FrameworkHash || manifest.NativeBuild != "744825" ||
                !manifest.Assemblies.Any(a => a.Name == "Assembly-CSharp" && a.Sha256 == gameHash) ||
                !manifest.Assemblies.Any(a => a.Name == "Assembly-CSharp-firstpass" && a.Sha256 == frameworkHash))
                throw new InvalidOperationException("archive_and_current_native_assembly_fingerprints_mismatch");
            byte[] schema = ReadBounded(Path.Combine(directory, "schema.bin"), 4 * 1024 * 1024);
            if (schema.Length != manifest.SchemaBytes || Hash(schema) != manifest.SchemaSha256) throw new InvalidOperationException("archive_schema_hash_mismatch");
            var roots = new[] { ReadBounded(Path.Combine(directory, "root-0.bin"), 2 * 1024 * 1024), ReadBounded(Path.Combine(directory, "root-1.bin"), 2 * 1024 * 1024) };
            for (int i = 0; i < 2; i++)
                if (roots[i].Length != manifest.Roots[i].NativeBytes || Hash(roots[i]) != manifest.Roots[i].Sha256)
                    throw new InvalidOperationException("archive_root_hash_mismatch:" + i);
            if (!report.SourcePreflight.SourceEligible || !destination.HasValue || report.CandidateProxyId < 0)
                return new { archiveHashesVerified = true, payloadPrepared = false, reason = "source_eligibility_destination_or_candidate_id_blocked" };
            var world = ClusterManager.Instance.GetWorld(permit.Source.NativeWorldId);
            if (world == null) throw new InvalidOperationException("existing_source_world_missing");
            var context = new NativeDupeRemapContext { ArchiveVersion = 2, Build = 744825, GameAssemblyHash = gameHash, FrameworkAssemblyHash = frameworkHash,
                SourceMinionId = minionId, SourceProxyId = proxyId, DestinationMinionId = report.CandidateMinionId, DestinationProxyId = report.CandidateProxyId,
                ExistingDestinationIds = existingIds, SourceSeconds = (float)manifest.SourceTime, SourceWholeCycle = (int)manifest.SourceCycle,
                SourceFractionalCycles = (float)manifest.SourceTimeInCycles, DestinationSeconds = GameClock.Instance.GetTime(),
                DestinationWholeCycle = GameClock.Instance.GetCycle(), DestinationFractionalCycles = GameClock.Instance.GetTimeInCycles(),
                DestinationX = destination.Value.X + .5f, DestinationY = destination.Value.Y, DestinationWorldId = world.id,
                DestinationWorldX = world.WorldOffset.x, DestinationWorldY = world.WorldOffset.y,
                DestinationWorldWidth = world.WorldSize.x, DestinationWorldHeight = world.WorldSize.y };
            var plan = NativeDuplicantRemapPlanner.Plan(schema, roots, context);
            Run(report, "loadedNativeMappings", () => InspectLoadedMappings(schema, plan.UsedTemplates, report));
            // The artifact's used-template shapes are compared against already loaded native caches
            // above. This reuses those bytes as the candidate directory, without native serialization.
            report.Payload = NativeDuplicantRemapPlanner.PreparePayload(schema, roots, context, schema);
            report.CandidatePayloadPrepared = true;
            report.PayloadPlan = new { plan.BinaryLayoutComplete, plan.RootBytesFullyConsumed, plan.RootPrefabNames, plan.RootHashes,
                plan.SchemaHash, plan.Components, plan.ObjectIdSlots, plan.StateMachines, plan.UsedTemplates,
                edits = plan.Edits.Select(edit => new { edit.Root, edit.Offset, edit.Path, edit.Semantic, edit.Expected, edit.Replacement }).ToArray(),
                candidateHashes = report.Payload.RootHashes, context.DestinationMinionId, context.DestinationProxyId, context.DestinationX, context.DestinationY,
                destinationSchemaEvidence = "existing_native_cached_used_templates_compared_structurally_to_archive; no native directory serialization" };
            return new { archiveHashesVerified = true, payloadPrepared = true, manifest.SourceDupeOriginId, nativeSerializationInvoked = false };
        }

        private static string CurrentArchiveFingerprint(MinionIdentity minion, int proxyId)
        {
            if (!TrackerMap().TryGetValue(proxyId, out var proxyIdentity) || proxyIdentity == null)
                throw new InvalidOperationException("archive_current_proxy_identity_missing");
            var proxy = proxyIdentity.GetComponent<MinionAssignablesProxy>();
            var modifiers = minion.GetComponent<MinionModifiers>();
            if (proxy == null || modifiers == null || modifiers.amounts == null)
                throw new InvalidOperationException("archive_current_proxy_or_amounts_missing");
            var values = new List<string> { GameClock.Instance.GetTime().ToString("R", CultureInfo.InvariantCulture), minion.GetProperName(),
                minion.transform.position.ToString("R"), minion.GetMyWorldId().ToString(CultureInfo.InvariantCulture), proxy.TargetInstanceID.ToString(CultureInfo.InvariantCulture),
                minion.GetComponent<KPrefabID>().InstanceID + "," + proxyId,
                string.Join(",", Components.LiveMinionIdentities.Items.Select(other => other.GetComponent<KPrefabID>().InstanceID).OrderBy(id => id)) };
            values.AddRange(modifiers.amounts.ModifierList.Where(amount => amount != null && amount.amount != null)
                .Select(amount => amount.amount.Id + "=" + amount.value.ToString("R", CultureInfo.InvariantCulture)).OrderBy(value => value, StringComparer.Ordinal));
            return Hash(Encoding.UTF8.GetBytes(string.Join("\n", values)));
        }

        private static object InspectImmutableOrigin(NativeDuplicantArchiveManifest manifest, int worldId, int minionId,
            NativeRoundTripPreparationReport report)
        {
            var colony = OnlineColonyState.Current;
            if (colony == null || !colony.TryReadDuplicantOrigin(minionId, worldId, out var origin))
                throw new InvalidOperationException("stable_origin_exact_native_binding_missing_no_bootstrap_attempted");
            var saved = manifest.OriginalOrigin;
            bool matches = saved != null && saved.StableDupeId == origin.StableDupeId && saved.OriginalOwnerId == origin.OriginalOwnerId &&
                (saved.OriginPlanetId ?? "") == (origin.OriginPlanetId ?? "") && saved.OriginHomeSystemId == origin.OriginHomeSystemId && saved.Kind == origin.Kind.ToString();
            if (!matches) report.Block("archive_immutable_origin_does_not_match_current_read_only_ledger");
            return new { matches, origin.StableDupeId, origin.OriginalOwnerId, origin.OriginPlanetId, origin.OriginHomeSystemId,
                kind = origin.Kind.ToString(), legacyOriginPlanetExplicitlyUnknown = string.IsNullOrEmpty(origin.OriginPlanetId),
                ledgerInitializedOrRebound = false, roomPeerOwnerSubstituted = false };
        }

        private static object Field(object value, string name)
        {
            var field = value == null ? null : AccessTools.Field(value.GetType(), name);
            if (field == null) throw new InvalidOperationException("native_field_contract_missing:" + name);
            return field.GetValue(value);
        }

        private sealed class Shape { internal string Name; internal byte Code; internal Shape[] Args = Array.Empty<Shape>(); }
        private sealed class MemberShape { internal string Name; internal Shape Type; }
        private sealed class TemplateShape { internal int Fields, Properties; internal MemberShape[] Members; }

        private static object InspectLoadedMappings(byte[] schema, string[] used, NativeRoundTripPreparationReport report)
        {
            var shapes = ReadSchema(schema);
            var input = Cache("deserializationTemplatesByTypeName");
            var output = Cache("serializationTemplatesByTypeName");
            var mappings = Cache("deserializationMappings");
            var verified = new List<string>();
            foreach (string name in used)
            {
                var source = input[name] as KSerialization.DeserializationTemplate;
                var target = output[name] as KSerialization.SerializationTemplate;
                if (source == null || target == null || !mappings.Contains(source))
                { report.Block("already_loaded_native_deserialization_mapping_missing:" + name); continue; }
                var expected = shapes[name];
                var written = target.serializableFields.Select(member => new { Name = member.field.Name, Type = member.typeInfo })
                    .Concat(target.serializableProperties.Select(member => new { Name = member.property.Name, Type = member.typeInfo })).ToArray();
                bool equal = source.serializedMembers.Count == expected.Members.Length && written.Length == expected.Members.Length &&
                    target.serializableFields.Count == expected.Fields && target.serializableProperties.Count == expected.Properties;
                for (int i = 0; equal && i < written.Length; i++) equal = source.serializedMembers[i].name == expected.Members[i].Name &&
                    written[i].Name == expected.Members[i].Name && TypeMatches(source.serializedMembers[i].typeInfo, expected.Members[i].Type, 0) &&
                    TypeMatches(written[i].Type, expected.Members[i].Type, 0);
                object pair = mappings[source];
                if (pair == null || pair.GetType().GetProperty("Key") == null || pair.GetType().GetProperty("Value") == null)
                    throw new InvalidOperationException("existing_native_mapping_pair_contract_missing:" + name);
                object actualTarget = pair.GetType().GetProperty("Key").GetValue(pair, null);
                object mapping = pair.GetType().GetProperty("Value").GetValue(pair, null);
                var members = Field(mapping, "deserializationInfo") as IList;
                equal &= ReferenceEquals(actualTarget, target) && ReferenceEquals(Field(mapping, "template"), source) &&
                    members != null && members.Count == expected.Members.Length;
                if (members != null) foreach (object member in members) equal &= (bool)Field(member, "valid");
                if (!equal) report.Block("existing_native_mapping_schema_or_validity_mismatch:" + name);
                else verified.Add(name);
            }
            return new { templatesRequested = used.Length, templatesVerified = verified, nativeMappingsCreated = false, nativeDirectoryReplaced = false };
        }

        private static bool TypeMatches(KSerialization.TypeInfo current, Shape expected, int depth)
        {
            if (depth > 16 || current == null || current.type == null || (byte)current.info != expected.Code) return false;
            if (expected.Name != null && current.type.FullName != expected.Name) return false;
            var args = current.subTypes ?? Array.Empty<KSerialization.TypeInfo>();
            return args.Length == expected.Args.Length && args.Select((type, index) => TypeMatches(type, expected.Args[index], depth + 1)).All(match => match);
        }

        private static Dictionary<string, TemplateShape> ReadSchema(byte[] bytes)
        {
            var templates = new Dictionary<string, TemplateShape>(StringComparer.Ordinal);
            using (var stream = new MemoryStream(bytes, false)) using (var reader = new BinaryReader(stream, new UTF8Encoding(false, true)))
            {
                int count = Count(reader, 4096), total = 0;
                for (int i = 0; i < count; i++)
                {
                    string name = Text(reader); int fields = Count(reader, 512), properties = Count(reader, 512);
                    total += fields + properties; if (total > 50000) throw new InvalidDataException("schema_member_budget");
                    var members = new MemberShape[fields + properties];
                    for (int j = 0; j < members.Length; j++) members[j] = new MemberShape { Name = Text(reader), Type = ReadShape(reader, 0) };
                    templates.Add(name, new TemplateShape { Fields = fields, Properties = properties, Members = members });
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("schema_trailing_bytes");
            }
            return templates;
        }

        private static Shape ReadShape(BinaryReader reader, int depth)
        {
            if (depth > 16) throw new InvalidDataException("schema_type_depth");
            byte code = reader.ReadByte(); int kind = code & 63;
            if (kind > 23) throw new InvalidDataException("schema_type_kind");
            var shape = new Shape { Code = code };
            if ((code & 128) != 0)
            {
                if (kind == 0) shape.Name = Text(reader);
                int count = reader.ReadByte(); if (count < 1 || count > 16) throw new InvalidDataException("schema_generic_arity");
                shape.Args = Enumerable.Range(0, count).Select(_ => ReadShape(reader, depth + 1)).ToArray();
            }
            else if (kind == 17) shape.Args = new[] { ReadShape(reader, depth + 1) };
            else if (kind == 0 || kind == 13) shape.Name = Text(reader);
            return shape;
        }
        private static int Count(BinaryReader reader, int maximum) { int count = reader.ReadInt32(); if (count < 0 || count > maximum) throw new InvalidDataException("bounded_schema_count"); return count; }
        private static string Text(BinaryReader reader) { int length = Count(reader, 65536); byte[] bytes = reader.ReadBytes(length); if (bytes.Length != length || length == 0) throw new InvalidDataException("bounded_schema_string"); return new UTF8Encoding(false, true).GetString(bytes); }
        private static byte[] ReadBounded(string path, int maximum) { if (!File.Exists(path) || new FileInfo(path).Length > maximum) throw new InvalidDataException("bounded_archive_file_missing_or_oversized:" + Path.GetFileName(path)); return File.ReadAllBytes(path); }
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string HashFile(string path) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    }
}
