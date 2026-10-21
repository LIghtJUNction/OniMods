// Probe-only eligibility for reversible async admission observation, never source release.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using OniOnline.Core;
using OniOnline.Multiplayer;

namespace OniOnline
{
    internal static class NativeNavigationExperimentEligibility
    {
        private const string GameHash = "6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1";
        private const string FrameworkHash = "117fcc6962eeed82b4929347b9a466fb41e807a01b5a9a7acc8bdc446da276c7";
        private static readonly string[] DormantRejections = {
            "transfer_stopped_floor_navigation_required", "transfer_navigation_or_rocket_transition_present" };

        // Real object identity stays process-local and is cleared before publishing.
        internal sealed class ExactReferences
        {
            internal bool Complete;
            internal object Game, Save, Source, Navigator, CachedSmi, Controller, StateMachines,
                TransitionDriver, PathGrid, TargetLocator, Serializer, SerializerEntries,
                FallSmi, AsyncManager, Tracker, TrackerMap, DriverOverrideLayers, PathNodes, DriverInterruptStack,
                AsyncNavigators, AsyncWorkQueue, AsyncFinishedWork,
                Starter0, Starter1, Starter2, StarterIdentity0, StarterIdentity1, StarterIdentity2;
            internal object[] ControllerInstances, OverrideLayerInstances;
        }
        internal sealed class ReferenceStability
        {
            public bool ComparisonPerformed, SameExactRefs;
            public int CheckedObjects;
            public string[] Replacements = Array.Empty<string>();
        }
        internal sealed class Facts
        {
            [JsonIgnore] internal ExactReferences References;
            public bool NativeAssembliesMatched, ExactContext, LifecycleProven, ExactStarterBindings,
                ExactNativeComponents, UniqueCachedControllerInstance, PendingSerializerClear,
                FloorStanding, CachedCellMatches, TargetClear, PathClear, TransitionClear, DriverOwnerClear,
                AsyncRegistered, InitializedDormant, ReviewedRunningStopped, DormantDriverIncomplete;
            public int CachedControllerOccurrences, NavigatorInstanceCount, PendingSerializerCount,
                CurrentNavType = -1, ActualCell = -1, CachedCell = -1, ReservedCell = -1, AsyncStatus = int.MinValue;
            public string CachedStatus, CachedCurrentState;
            public int[] StarterIds, StarterUnityIds;
            public List<string> ReadFailures = new List<string>();
        }
        internal sealed class Result
        {
            public bool ExperimentEligible, NativeLoadAllowed = false, SourceEligible;
            public string ExperimentEligibilityKind = "Rejected";
            public string[] SourceRejections = Array.Empty<string>(), Rejections = Array.Empty<string>();
            public Facts Observation;
            public string Scope = "Qualification only for a reversible single-navigator async admission experiment. Preserves actual production SourceEligible and its rejections. Not async quiescence, source release, movement or native load permission.";
        }

        // Pure reference comparison; Unity overloaded equality and integer hashes
        // cannot establish identity of replaced-but-similar native state objects.
        internal static ReferenceStability CompareReferences(ExactReferences baseline, ExactReferences current)
        {
            var result = new ReferenceStability();
            if (baseline == null || current == null || !baseline.Complete || !current.Complete) return result;
            result.ComparisonPerformed = true;
            var replaced = new List<string>();
            foreach (var field in typeof(ExactReferences).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (field.FieldType == typeof(object))
                {
                    result.CheckedObjects++;
                    if (!ReferenceEquals(field.GetValue(baseline), field.GetValue(current))) replaced.Add(field.Name);
                }
                else if (field.FieldType == typeof(object[]))
                {
                    var before = (object[])field.GetValue(baseline); var after = (object[])field.GetValue(current);
                    if (before == null || after == null || before.Length != after.Length)
                    { replaced.Add(field.Name); continue; }
                    for (int i = 0; i < before.Length; i++)
                    { result.CheckedObjects++; if (!ReferenceEquals(before[i], after[i])) replaced.Add(field.Name + "." + i); }
                }
            }
            result.Replacements = replaced.ToArray(); result.SameExactRefs = replaced.Count == 0;
            return result;
        }
        // Pure policy: never patches or normalizes the production observation.
        internal static Result Evaluate(DuplicantTransferExpectation expected, DuplicantTransferPreflightResult source, Facts facts)
        {
            var result = new Result { SourceEligible = source != null && source.SourceEligible, Observation = facts,
                SourceRejections = source == null || source.Rejections == null ? Array.Empty<string>() : (string[])source.Rejections.Clone() };
            var failures = new List<string>();
            if (source == null || source.Observation == null || source.Rejections == null)
                failures.Add("experiment_source_observation_missing");
            else
            {
                var recomputed = DuplicantTransferPreflightPolicy.Evaluate(expected, source.Observation);
                if (source.SourceEligible != recomputed.SourceEligible || !SameReasons(source.Rejections, recomputed.Rejections))
                    failures.Add("experiment_source_result_inconsistent");
                if (!source.SourceEligible && !SameReasons(source.Rejections, DormantRejections))
                    failures.Add("experiment_non_navigation_source_rejection");
                // These are bundled with nav fields in the existing policy. They must
                // remain independently true even in the two-rejection dormant branch.
                if (!source.Observation.Standing || !source.Observation.RocketMoveClear)
                    failures.Add("experiment_standing_and_rocket_clear_required");
            }
            if (facts == null) failures.Add("experiment_native_facts_missing");
            else
            {
                Require(facts.NativeAssembliesMatched, "pinned_native_assemblies", failures);
                Require(facts.ExactContext, "exact_private_checkpoint_context", failures);
                Require(facts.LifecycleProven, "same_game_checkpoint_lifecycle_proof", failures);
                Require(facts.ExactStarterBindings, "original_three_exact_existing_bindings", failures);
                Require(facts.ExactNativeComponents, "exact_native_component_types", failures);
                Require(facts.UniqueCachedControllerInstance, "unique_existing_cache_controller_instance", failures);
                Require(facts.PendingSerializerClear, "pending_serializer_clear", failures);
                Require(facts.FloorStanding, "native_floor_standing", failures);
                Require(facts.CachedCellMatches, "native_cached_cell_matches", failures);
                Require(facts.TargetClear, "native_target_clear", failures);
                Require(facts.PathClear, "native_path_clear", failures);
                Require(facts.TransitionClear, "native_transition_clear", failures);
                Require(facts.DriverOwnerClear, "native_driver_owner_clear", failures);
                Require(facts.AsyncRegistered, "existing_async_registration", failures);
                if (facts.ReadFailures == null || facts.ReadFailures.Count != 0) failures.Add("experiment_native_read_incomplete");
                if (source != null && source.SourceEligible)
                {
                    if (!facts.ReviewedRunningStopped || facts.InitializedDormant)
                        failures.Add("experiment_running_stopped_state_required");
                    else result.ExperimentEligibilityKind = "ReviewedRunningStoppedObservation";
                }
                else
                {
                    if (!facts.InitializedDormant || facts.ReviewedRunningStopped || !facts.DormantDriverIncomplete ||
                        source == null || source.Observation == null || source.Observation.NavigationInitialized)
                        failures.Add("experiment_initialized_dormant_state_required");
                    else result.ExperimentEligibilityKind = "InitializedDormantSingleNavObservation";
                }
            }
            result.Rejections = failures.Distinct(StringComparer.Ordinal).ToArray();
            result.ExperimentEligible = result.Rejections.Length == 0;
            if (!result.ExperimentEligible) result.ExperimentEligibilityKind = "Rejected";
            return result;
        }
        internal static Result Inspect(NativeNavigationQuiescencePermit permit)
        {
            var facts = new Facts();
            DuplicantTransferPreflightResult preflight = null;
            Read(facts, "assembly", () => facts.NativeAssembliesMatched = HashFile(typeof(Game).Assembly.Location) == GameHash &&
                HashFile(typeof(KMonoBehaviour).Assembly.Location) == FrameworkHash);
            if (facts.NativeAssembliesMatched)
            {
                Read(facts, "preflight", () => preflight = NativeDuplicantTransferPreflight.Inspect(permit == null ? null : permit.Source));
                Read(facts, "context", () => Capture(permit, facts));
            }
            return Evaluate(permit == null ? null : permit.Source, preflight, facts);
        }
        private static void Capture(NativeNavigationQuiescencePermit permit, Facts facts)
        {
            if (permit == null || permit.Source == null || permit.OriginalStarterIds == null || permit.OriginalStarterIds.Length != 3 ||
                permit.OriginalStarterIds.Any(id => id <= 0) || permit.OriginalStarterIds.Distinct().Count() != 3 ||
                !permit.OriginalStarterIds.Contains(permit.Source.MinionId)) throw new InvalidOperationException();
            var game = Game.Instance; var save = SaveGame.Instance;
            var references = new ExactReferences { Game = game, Save = save }; facts.References = references;
            string path = permit.Source.ActiveSavePath;
            facts.ExactContext = game != null && save != null && game.baseAlreadyCreated && !game.IsLoading() &&
                SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused && !KMonoBehaviour.isLoadingScene &&
                !KMonoBehaviour.isPoolPreInit && !string.IsNullOrEmpty(path) && Path.IsPathRooted(path) &&
                SaveLoader.GetActiveSaveFilePath() == path && !string.IsNullOrEmpty(permit.CheckpointSha256) &&
                Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(path))).StartsWith("OniOnlineHarness-", StringComparison.Ordinal) &&
                HashFile(path) == permit.CheckpointSha256;
            facts.LifecycleProven = NativeLifecycleProbe.HasSuccessfulProof(game, path, permit.CheckpointSha256);
            if (!facts.ExactContext) return;
            var roster = Components.LiveMinionIdentities.Items;
            if (roster.Count != 3 || roster.Any(dupe => dupe == null)) return;
            var ordered = roster.OrderBy(dupe => dupe.GetComponent<KPrefabID>() == null ? -1 : dupe.GetComponent<KPrefabID>().InstanceID).ToArray();
            facts.StarterIds = ordered.Select(dupe => dupe.GetComponent<KPrefabID>() == null ? -1 : dupe.GetComponent<KPrefabID>().InstanceID).ToArray();
            facts.StarterUnityIds = ordered.Select(dupe => dupe.GetInstanceID()).ToArray();
            var tracker = Field(typeof(KPrefabIDTracker), "Instance", true).GetValue(null);
            var map = tracker == null ? null : Value(tracker, "prefabIdMap") as Dictionary<int, KPrefabID>;
            var colony = OnlineColonyState.Current;
            references.Tracker = tracker; references.TrackerMap = map;
            references.Starter0 = ordered[0]; references.Starter1 = ordered[1]; references.Starter2 = ordered[2];
            references.StarterIdentity0 = ordered[0].GetComponent<KPrefabID>(); references.StarterIdentity1 = ordered[1].GetComponent<KPrefabID>();
            references.StarterIdentity2 = ordered[2].GetComponent<KPrefabID>();
            bool bindings = facts.StarterIds.SequenceEqual(permit.OriginalStarterIds.OrderBy(id => id)) &&
                map != null && map.Count <= 200000 && colony != null;
            foreach (var dupe in ordered)
            {
                var id = dupe.GetComponent<KPrefabID>(); int cell = Grid.PosToCell(dupe); KPrefabID tracked;
                DupeOriginRecord origin;
                bindings &= id != null && !id.conflicted && !id.pendingDestruction && dupe.isSpawned && dupe.gameObject.activeInHierarchy &&
                    Grid.IsValidCell(cell) && map != null && map.TryGetValue(id.InstanceID, out tracked) && ReferenceEquals(tracked, id) &&
                    colony != null && colony.TryReadDuplicantOrigin(id.InstanceID, Grid.WorldIdx[cell], out origin) && origin != null;
            }
            facts.ExactStarterBindings = bindings;
            var source = ordered.SingleOrDefault(dupe => dupe.GetComponent<KPrefabID>().InstanceID == permit.Source.MinionId);
            var selected = SelectTool.Instance == null ? null : SelectTool.Instance.selected;
            if (source == null || selected == null || !ReferenceEquals(selected.GetComponent<MinionIdentity>(), source))
            { facts.ExactContext = false; return; }
            int actual = Grid.PosToCell(source);
            if (!Grid.IsValidCell(actual) || Grid.WorldIdx[actual] != permit.Source.NativeWorldId) { facts.ExactContext = false; return; }
            var nav = source.GetComponent<Navigator>(); var controller = source.GetComponent<StateMachineController>();
            var locator = nav == null ? null : Value(nav, "targetLocator") as KPrefabID;
            references.Source = source; references.Navigator = nav; references.Controller = controller; references.TargetLocator = locator;
            facts.ExactNativeComponents = nav != null && nav.GetType() == typeof(Navigator) && nav.isSpawned && nav.IsInitialized() &&
                nav.enabled && nav.gameObject.activeInHierarchy && controller != null && controller.GetType() == typeof(StateMachineController) &&
                nav.transitionDriver != null && nav.transitionDriver.GetType() == typeof(TransitionDriver) && nav.executePathProbeTaskAsync && nav.PathGrid != null && locator != null;
            if (!facts.ExactNativeComponents) return;
            var cached = nav.GetSMI(); var machines = Value(controller, "stateMachines") as IList;
            references.CachedSmi = cached; references.StateMachines = machines; references.PathGrid = nav.PathGrid;
            if (machines == null || machines.Count > 256 || machines.Cast<object>().Any(item => item == null)) throw new InvalidOperationException();
            references.ControllerInstances = machines.Cast<object>().ToArray();
            facts.NavigatorInstanceCount = machines.Cast<object>().Count(item => item is Navigator.StatesInstance);
            facts.CachedControllerOccurrences = cached == null ? 0 : machines.Cast<object>().Count(item => ReferenceEquals(item, cached));
            var state = cached as Navigator.StatesInstance;
            facts.UniqueCachedControllerInstance = state != null && state.GetType() == typeof(Navigator.StatesInstance) &&
                ReferenceEquals(state.master, nav) && facts.NavigatorInstanceCount == 1 && facts.CachedControllerOccurrences == 1 && !state.isCrashed;
            if (facts.UniqueCachedControllerInstance)
            {
                facts.CachedStatus = state.GetStatus().ToString(); var current = state.GetCurrentState();
                facts.CachedCurrentState = current == null ? null : current.name;
                if (facts.CachedCurrentState != null && facts.CachedCurrentState.Length > 512) throw new InvalidOperationException();
                facts.InitializedDormant = state.GetStatus() == StateMachine.Status.Initialized && !state.IsRunning() && current == null;
                facts.ReviewedRunningStopped = state.GetStatus() == StateMachine.Status.Running && state.IsRunning() && current != null &&
                    state.IsInsideState(state.sm.normal.stopped);
            }
            var serializer = Value(controller, "serializer"); var entries = serializer == null ? null : Value(serializer, "entries") as IList;
            if (entries == null || entries.Count > 256) throw new InvalidOperationException();
            references.Serializer = serializer; references.SerializerEntries = entries;
            facts.PendingSerializerCount = entries.Count;
            // This first experiment has only proven the empty restored serializer; any
            // pending SMI entry is conservatively unknown, even for another component.
            facts.PendingSerializerClear = entries.Count == 0;
            var fall = controller.GetSMI<FallMonitor.Instance>(); references.FallSmi = fall;
            facts.CurrentNavType = (int)nav.CurrentNavType;
            facts.FloorStanding = nav.CurrentNavType == NavType.Floor && fall != null && fall.GetType() == typeof(FallMonitor.Instance) &&
                fall.IsRunning() && fall.IsInsideState(fall.sm.standing);
            facts.ActualCell = actual; facts.CachedCell = nav.cachedCell; facts.ReservedCell = nav.GetReservedCell();
            facts.CachedCellMatches = actual == nav.cachedCell;
            facts.TargetClear = nav.target == null && nav.targetOffsets == null &&
                (facts.ReservedCell == NavigationReservations.InvalidReservation || facts.ReservedCell == actual);
            var nodes = nav.path.nodes; references.PathNodes = nodes;
            facts.PathClear = nodes == null || nodes.Count == 0;
            var driver = nav.transitionDriver; var interrupts = Value(driver, "interruptOverrideStack") as ICollection;
            if (interrupts == null || interrupts.Count > 64 || driver.overrideLayers == null || driver.overrideLayers.Count > 64 ||
                driver.overrideLayers.Any(layer => layer == null || layer.GetType().Assembly != typeof(Navigator).Assembly)) throw new InvalidOperationException();
            references.TransitionDriver = driver; references.DriverOverrideLayers = driver.overrideLayers;
            references.DriverInterruptStack = interrupts;
            references.OverrideLayerInstances = driver.overrideLayers.Cast<object>().ToArray();
            facts.TransitionClear = driver.GetTransition == null && interrupts.Count == 0;
            var owner = Value(driver, "navigator"); facts.DriverOwnerClear = owner == null || ReferenceEquals(owner, nav);
            facts.DormantDriverIncomplete = !(bool)Value(driver, "isComplete");
            var manager = AsyncPathProber.Instance; references.AsyncManager = manager;
            if (manager == null) return;
            lock (manager)
            {
                var registered = Value(manager, "navigators") as IDictionary;
                var queue = Value(manager, "workQueue") as IList; var results = Value(manager, "finishedWork") as IList;
                if (registered == null || registered.Count > 200000 || queue == null || results == null || queue.Count > 10000 || results.Count > 10000)
                    throw new InvalidOperationException();
                references.AsyncNavigators = registered; references.AsyncWorkQueue = queue; references.AsyncFinishedWork = results;
                facts.AsyncRegistered = registered.Contains(nav);
                if (facts.AsyncRegistered) { facts.AsyncStatus = (int)registered[nav]; facts.AsyncRegistered &= facts.AsyncStatus >= -1; }
            }
            facts.ExactContext &= ReferenceEquals(game, Game.Instance) && ReferenceEquals(save, SaveGame.Instance) &&
                ReferenceEquals(selected, SelectTool.Instance.selected) && Grid.PosToCell(source) == actual;
            references.Complete = true;
        }
        private static bool SameReasons(string[] actual, string[] expected)
        { return actual != null && expected != null && actual.Length == actual.Distinct(StringComparer.Ordinal).Count() &&
            actual.OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(expected.OrderBy(s => s, StringComparer.Ordinal)); }
        private static void Require(bool value, string reason, List<string> failures) { if (!value) failures.Add("experiment_" + reason + "_required"); }
        private static void Read(Facts facts, string stage, System.Action read)
        { try { read(); } catch (Exception error) { if (facts.ReadFailures.Count < 16) facts.ReadFailures.Add(stage + ":" + error.GetType().Name); } }
        private static FieldInfo Field(Type type, string name, bool isStatic = false)
        { return type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance)) ?? throw new MissingFieldException(); }
        private static object Value(object value, string name) { return Field(value.GetType(), name).GetValue(value); }
        private static string HashFile(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    }
}
