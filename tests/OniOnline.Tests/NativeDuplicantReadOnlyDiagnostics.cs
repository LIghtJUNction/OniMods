// Probe-only bounded observations. No lazy state access, native repairs or admission decisions.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniOnline.Core;
using OniOnline.Multiplayer;
using UnityEngine;

namespace OniOnline
{
    internal static class NativeDuplicantReadOnlyDiagnostics
    {
        private const int MaxRoster = 200000, MaxController = 256, MaxPath = 256, MaxOffsets = 64;
        private const int MaxManifestBytes = 1024 * 1024;
        private const string GameHash = "6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1";
        private const string FrameworkHash = "117fcc6962eeed82b4929347b9a466fb41e807a01b5a9a7acc8bdc446da276c7";
        internal sealed class ReadError { public string Stage, Type; }
        internal sealed class OriginFields
        {
            public string StableDupeId, OriginalOwnerId, OriginPlanetId, OriginHomeSystemId, Kind;
            internal static OriginFields From(DupeOriginRecord origin)
            { return new OriginFields { StableDupeId = origin.StableDupeId, OriginalOwnerId = origin.OriginalOwnerId,
                OriginPlanetId = origin.OriginPlanetId, OriginHomeSystemId = origin.OriginHomeSystemId, Kind = origin.Kind.ToString() }; }
        }
        internal sealed class OriginProof
        {
            public int RequestedId, NativeId = -1, WorldId = -1, Cell = -1, UnityId, GlobalLiveIdOccurrences;
            public bool NativeExists, TrackerExactReference, PrefabConflicted, PendingDestruction, ReadSucceeded;
            public OriginFields Origin;
        }
        internal sealed class Frame
        {
            public int FrameNumber, GameId, SaveId, NextUniqueId, SelectedNativeId = -1;
            public double Time;
            public bool Loaded, Paused, OriginalRosterMatches;
            public string ActiveSave;
            public int[] LiveIds;
            public OriginProof[] Origins;
            public object Navigation, Async;
            public List<ReadError> Errors = new List<ReadError>();
            [JsonIgnore] internal Game GameRef;
            [JsonIgnore] internal SaveGame SaveRef;
            [JsonIgnore] internal MinionIdentity[] NativeRefs;
            [JsonIgnore] internal Navigator NavigatorRef;
            [JsonIgnore] internal StateMachineController ControllerRef;
            [JsonIgnore] internal StateMachine.Instance CachedSmiRef;
            [JsonIgnore] internal object TargetRef, TargetLocatorRef, PathGridRef, TransitionDriverRef, TransitionRef, ControllerMachinesRef, SerializerRef, SerializerEntriesRef, TrackerRef;
        }
        internal sealed class ManifestComparison
        {
            public bool Requested, PathVerified, Parsed, ComparisonPerformed, ExactMatch;
            public bool SelectedIdMatches, WorldMatches, ActiveSaveMatches;
            public string Sha256;
            public OriginFields ArchivedOrigin;
            public Dictionary<string, bool> FieldMatches = new Dictionary<string, bool>(StringComparer.Ordinal);
            public List<ReadError> Errors = new List<ReadError>();
        }
        internal sealed class Result
        {
            public bool NativeLoadAllowed = false, NativeAssembliesMatched, ObservationCompleted,
                ReferencesUnchanged, ObservedSnapshotUnchanged, AllThreeExistingBindingsVerified;
            public string GameAssemblySha256, FrameworkAssemblySha256;
            public Frame Before, After;
            public ManifestComparison SelectedArchive;
            public List<ReadError> Errors = new List<ReadError>();
            public string Scope = "Bounded read of existing native navigation and three immutable bindings. ObservedSnapshotUnchanged compares only these bounded observations, not the caller full colony fingerprint. Async worker snapshots may naturally differ. No SourceEligible change, state creation, rebind, native loading or release permission. Without a manifest no archive comparison is performed.";
        }
        internal static object Capture(int[] originalStarterIds, int selectedId, string selectedArchiveManifestPath = null)
        {
            var result = new Result();
            if (originalStarterIds == null || originalStarterIds.Length != 3 || originalStarterIds.Any(id => id <= 0) ||
                originalStarterIds.Distinct().Count() != 3 || !originalStarterIds.Contains(selectedId))
            { Error(result.Errors, "arguments.original_starters", "InvalidAllowlist"); return result; }
            Run(result.Errors, "assemblies", () => {
                result.GameAssemblySha256 = HashFile(typeof(Game).Assembly.Location);
                result.FrameworkAssemblySha256 = HashFile(typeof(KMonoBehaviour).Assembly.Location);
                result.NativeAssembliesMatched = result.GameAssemblySha256 == GameHash && result.FrameworkAssemblySha256 == FrameworkHash;
                if (!result.NativeAssembliesMatched) Error(result.Errors, "assemblies", "UnreviewedNativeAssembly");
            });
            if (!result.NativeAssembliesMatched) return result;
            int[] ids = originalStarterIds.OrderBy(id => id).ToArray();
            result.Before = ReadFrame(ids, selectedId);
            var selected = result.Before.Origins == null ? null : result.Before.Origins.FirstOrDefault(o => o.RequestedId == selectedId);
            result.SelectedArchive = CompareSelectedManifest(selectedArchiveManifestPath, selectedId, selected,
                result.Before.ActiveSave);
            result.After = ReadFrame(ids, selectedId);
            result.ReferencesUnchanged = ReferenceEquals(result.Before.GameRef, result.After.GameRef) &&
                ReferenceEquals(result.Before.SaveRef, result.After.SaveRef) &&
                ReferenceEquals(result.Before.NavigatorRef, result.After.NavigatorRef) &&
                ReferenceEquals(result.Before.ControllerRef, result.After.ControllerRef) &&
                ReferenceEquals(result.Before.CachedSmiRef, result.After.CachedSmiRef) &&
                ReferenceEquals(result.Before.TargetRef, result.After.TargetRef) && ReferenceEquals(result.Before.TargetLocatorRef, result.After.TargetLocatorRef) &&
                ReferenceEquals(result.Before.PathGridRef, result.After.PathGridRef) && ReferenceEquals(result.Before.TransitionDriverRef, result.After.TransitionDriverRef) &&
                ReferenceEquals(result.Before.TransitionRef, result.After.TransitionRef) && ReferenceEquals(result.Before.ControllerMachinesRef, result.After.ControllerMachinesRef) &&
                ReferenceEquals(result.Before.SerializerRef, result.After.SerializerRef) && ReferenceEquals(result.Before.SerializerEntriesRef, result.After.SerializerEntriesRef) &&
                ReferenceEquals(result.Before.TrackerRef, result.After.TrackerRef) &&
                result.Before.NativeRefs != null && result.After.NativeRefs != null &&
                result.Before.NativeRefs.Select((dupe, i) => ReferenceEquals(dupe, result.After.NativeRefs[i])).All(same => same);
            // Async is deliberately separate: workers can complete while the main-thread
            // diagnostic runs. That does not establish probe mutation or async quiescence.
            Run(result.Errors, "snapshot.compare", () => result.ObservedSnapshotUnchanged = JsonConvert.SerializeObject(Physical(result.Before)) == JsonConvert.SerializeObject(Physical(result.After)));
            result.AllThreeExistingBindingsVerified = result.ReferencesUnchanged && result.Before.OriginalRosterMatches && result.After.OriginalRosterMatches &&
                result.Before.Origins != null && result.After.Origins != null &&
                result.Before.Origins.All(o => o.ReadSucceeded && o.Origin != null && !o.PrefabConflicted && !o.PendingDestruction && o.TrackerExactReference && o.GlobalLiveIdOccurrences == 1) &&
                result.After.Origins.All(o => o.ReadSucceeded && o.Origin != null && !o.PrefabConflicted && !o.PendingDestruction && o.TrackerExactReference && o.GlobalLiveIdOccurrences == 1);
            result.ObservationCompleted = result.Before.Errors.Count == 0 && result.After.Errors.Count == 0 && result.Errors.Count == 0;
            return result;
        }
        private static object Physical(Frame frame)
        { return new { frame.GameId, frame.SaveId, frame.NextUniqueId, frame.SelectedNativeId, frame.Time, frame.Loaded, frame.Paused,
            frame.ActiveSave, frame.LiveIds, frame.OriginalRosterMatches, frame.Origins, frame.Navigation }; }
        private static Frame ReadFrame(int[] ids, int selectedId)
        {
            var frame = new Frame { NativeRefs = new MinionIdentity[3], Origins = ids.Select(id => new OriginProof { RequestedId = id }).ToArray() };
            Run(frame.Errors, "context", () => {
                frame.FrameNumber = UnityEngine.Time.frameCount; frame.GameRef = Game.Instance; frame.SaveRef = SaveGame.Instance;
                frame.GameId = frame.GameRef == null ? 0 : frame.GameRef.GetInstanceID(); frame.SaveId = frame.SaveRef == null ? 0 : frame.SaveRef.GetInstanceID();
                frame.Loaded = frame.GameRef != null && frame.SaveRef != null && frame.GameRef.baseAlreadyCreated && !frame.GameRef.IsLoading();
                frame.Paused = SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused;
                frame.Time = GameClock.Instance == null ? double.NaN : GameClock.Instance.GetTime();
                frame.NextUniqueId = KPrefabID.NextUniqueID; frame.ActiveSave = SaveLoader.GetActiveSaveFilePath();
                if (!frame.Loaded || !frame.Paused) throw new InvalidOperationException();
            });
            if (!frame.Loaded || !frame.Paused) return frame;
            MinionIdentity[] roster = null;
            Dictionary<int, KPrefabID> map = null;
            Run(frame.Errors, "roster", () => {
                if (Components.LiveMinionIdentities.Items.Count > MaxRoster) throw new InvalidDataException();
                roster = Components.LiveMinionIdentities.Items.Where(d => d != null).ToArray();
                frame.LiveIds = roster.Select(d => d.GetComponent<KPrefabID>()).Select(id => id == null ? -1 : id.InstanceID).OrderBy(id => id).ToArray();
                frame.OriginalRosterMatches = frame.LiveIds.SequenceEqual(ids);
            });
            Run(frame.Errors, "tracker.existing", () => {
                object tracker = StaticField(typeof(KPrefabIDTracker), "Instance").GetValue(null); frame.TrackerRef = tracker;
                map = tracker == null ? null : Read(tracker, "prefabIdMap") as Dictionary<int, KPrefabID>;
                if (map == null || map.Count > MaxRoster) throw new InvalidDataException();
            });
            OnlineColonyState colony = null;
            Run(frame.Errors, "origin.existing_colony", () => colony = OnlineColonyState.Current);
            if (roster != null)
                for (int i = 0; i < ids.Length; i++)
                {
                    int index = i;
                    Run(frame.Errors, "origin." + ids[i], () => {
                        var proof = frame.Origins[index];
                        var matches = roster.Where(d => { var id = d.GetComponent<KPrefabID>(); return id != null && id.InstanceID == proof.RequestedId; }).ToArray();
                        proof.GlobalLiveIdOccurrences = matches.Length;
                        if (matches.Length != 1) throw new InvalidDataException();
                        var dupe = matches[0]; frame.NativeRefs[index] = dupe; proof.NativeExists = true;
                        var native = dupe.GetComponent<KPrefabID>(); proof.NativeId = native.InstanceID; proof.UnityId = dupe.GetInstanceID();
                        proof.PrefabConflicted = native.conflicted; proof.PendingDestruction = native.pendingDestruction;
                        proof.Cell = Grid.PosToCell(dupe);
                        if (!Grid.IsValidCell(proof.Cell) || Grid.WorldIdx == null || proof.Cell >= Grid.WorldIdx.Length) throw new InvalidDataException();
                        proof.WorldId = Grid.WorldIdx[proof.Cell];
                        KPrefabID tracked; proof.TrackerExactReference = map != null && map.TryGetValue(proof.NativeId, out tracked) && ReferenceEquals(tracked, native);
                        DupeOriginRecord origin = null;
                        proof.ReadSucceeded = colony != null && colony.TryReadDuplicantOrigin(proof.NativeId, proof.WorldId, out origin);
                        if (proof.ReadSucceeded && origin != null) proof.Origin = OriginFields.From(origin);
                        else Error(frame.Errors, "origin." + proof.RequestedId, "ExistingBindingUnavailable");
                    });
                }
            MinionIdentity selectedDupe = null;
            var requestedDupe = frame.NativeRefs[Array.IndexOf(ids, selectedId)];
            Run(frame.Errors, "selection", () => {
                var selected = SelectTool.Instance == null ? null : SelectTool.Instance.selected;
                selectedDupe = selected == null ? null : selected.GetComponent<MinionIdentity>();
                var selectedIdentity = selectedDupe == null ? null : selectedDupe.GetComponent<KPrefabID>();
                frame.SelectedNativeId = selectedIdentity == null ? -1 : selectedIdentity.InstanceID;
                if (frame.SelectedNativeId != selectedId || !ReferenceEquals(selectedDupe, requestedDupe))
                    Error(frame.Errors, "selection", "ExactSelectedReferenceMismatch");
            });
            if (requestedDupe != null)
            {
                Run(frame.Errors, "navigation.components", () => {
                    frame.NavigatorRef = requestedDupe.GetComponent<Navigator>();
                    frame.ControllerRef = requestedDupe.GetComponent<StateMachineController>();
                });
                Run(frame.Errors, "navigation.existing", () => frame.Navigation = ReadNavigation(frame));
                Run(frame.Errors, "navigation.async", () => frame.Async = ReadAsync(frame.NavigatorRef));
            }
            return frame;
        }
        private static object ReadNavigation(Frame frame)
        {
            var nav = frame.NavigatorRef; var controller = frame.ControllerRef;
            if (nav == null || nav.GetType() != typeof(Navigator) || controller == null || controller.GetType() != typeof(StateMachineController))
                throw new InvalidDataException();
            // GetSMI is the cached-field override. Never use the lazy .smi property or IsMoving().
            var cached = nav.GetSMI(); frame.CachedSmiRef = cached;
            object cacheObservation = DescribeSmi(cached, nav);
            var machines = Read(controller, "stateMachines") as IList; frame.ControllerMachinesRef = machines;
            if (machines == null || machines.Count > MaxController) throw new InvalidDataException();
            var machineRows = machines.Cast<object>().Select(item => new { type = TypeName(item), reference = Token(item),
                exactNavigatorType = item != null && item.GetType() == typeof(Navigator.StatesInstance),
                sameAsCached = ReferenceEquals(item, cached), navigator = item != null && item.GetType() == typeof(Navigator.StatesInstance) ? DescribeSmi((StateMachine.Instance)item, nav) : null }).ToArray();
            object pendingObservation = null, raw = null;
            Run(frame.Errors, "navigation.pending_serializer", () => {
                object serializer = Read(controller, "serializer"); frame.SerializerRef = serializer;
                var entries = serializer == null ? null : Read(serializer, "entries") as IList; frame.SerializerEntriesRef = entries;
                if (entries == null || entries.Count > MaxController) throw new InvalidDataException();
                pendingObservation = entries.Cast<object>().Select(entry => {
                    if (entry == null || entry.GetType().DeclaringType != typeof(StateMachineSerializer) || entry.GetType().Name != "Entry") throw new InvalidDataException();
                    var type = Read(entry, "type") as Type;
                    return new { type = type == null ? null : type.FullName, suffix = Bounded(Read(entry, "typeSuffix") as string, 512),
                        currentState = Bounded(Read(entry, "currentState") as string, 512), isNavigator = type == typeof(Navigator.StatesInstance), reference = Token(entry) };
                }).ToArray();
            });
            Run(frame.Errors, "navigation.raw", () => {
                if (nav.targetOffsets != null && nav.targetOffsets.Length > MaxOffsets) throw new InvalidDataException();
                if (nav.path.nodes != null && nav.path.nodes.Count > MaxPath) throw new InvalidDataException();
                var driver = nav.transitionDriver;
                if (driver == null || driver.GetType() != typeof(TransitionDriver)) throw new InvalidDataException();
                var interrupts = Read(driver, "interruptOverrideStack") as ICollection;
                if (interrupts == null || interrupts.Count > 64 || driver.overrideLayers == null || driver.overrideLayers.Count > 64) throw new InvalidDataException();
                var target = nav.target; var targetIdentity = target == null ? null : target.GetComponent<KPrefabID>();
                var targetLocator = Read(nav, "targetLocator") as KPrefabID;
                var transition = driver.GetTransition;
                frame.TargetRef = target; frame.TargetLocatorRef = targetLocator; frame.PathGridRef = nav.PathGrid;
                frame.TransitionDriverRef = driver; frame.TransitionRef = transition;
                raw = new { nav.isSpawned, initialized = nav.IsInitialized(), nav.enabled, activeInHierarchy = nav.gameObject.activeInHierarchy,
                    componentType = nav.GetType().FullName, navUnityId = nav.GetInstanceID(),
                    nav.CurrentNavType, nav.cachedCell, actualCell = Grid.PosToCell(nav), reservedCell = nav.GetReservedCell(),
                    targetNull = target == null, targetUnityId = target == null ? 0 : target.GetInstanceID(), targetNativeId = targetIdentity == null ? -1 : targetIdentity.InstanceID,
                    offsetsNull = nav.targetOffsets == null, offsets = nav.targetOffsets == null ? null : nav.targetOffsets.Select(offset => new { offset.x, offset.y }).ToArray(),
                    nodesNull = nav.path.nodes == null, pathCost = nav.path.cost, nodes = nav.path.nodes == null ? null : nav.path.nodes.Select(node => new { node.cell, node.navType, node.transitionId }).ToArray(),
                    pathGridPresent = nav.PathGrid != null, pathGridReference = Token(nav.PathGrid),
                    driverReference = Token(driver), transitionNull = transition == null, transitionReference = Token(transition),
                    transitionSnapshot = transition == null ? null : new { transition.x, transition.y, transition.start, transition.end, transition.isLooping, transition.speed, transition.animSpeed },
                    interruptCount = interrupts.Count, interruptReferences = interrupts.Cast<object>().Select(Token).ToArray(),
                    overrideLayers = driver.overrideLayers.Select(layer => new { type = TypeName(layer), reference = Token(layer) }).ToArray(),
                    isComplete = (bool)Read(driver, "isComplete"), driverNavigatorNull = Read(driver, "navigator") == null,
                    driverNavigatorExact = ReferenceEquals(Read(driver, "navigator"), nav),
                    targetLocatorId = targetLocator == null ? -1 : targetLocator.InstanceID, targetLocatorUnityId = targetLocator == null ? 0 : targetLocator.GetInstanceID(),
                    nav.executePathProbeTaskAsync, nav.reportOccupation };
            });
            return new { cache = cacheObservation, controllerReference = Token(controller), controllerUnityId = controller.GetInstanceID(),
                controllerCount = machines.Count, stateMachines = machineRows,
                navigatorExactTypeCount = machines.Cast<object>().Count(item => item != null && item.GetType() == typeof(Navigator.StatesInstance)),
                cachedControllerOccurrences = cached == null ? 0 : machines.Cast<object>().Count(item => ReferenceEquals(item, cached)),
                pendingSerializer = pendingObservation, raw };
        }
        private static object DescribeSmi(StateMachine.Instance value, Navigator nav)
        {
            if (value == null) return new { exists = false };
            if (value.GetType() != typeof(Navigator.StatesInstance)) return new { exists = true, exactType = false, type = TypeName(value), reference = Token(value) };
            var state = (Navigator.StatesInstance)value; var current = state.GetCurrentState();
            return new { exists = true, exactType = true, type = TypeName(state), reference = Token(state),
                masterExact = ReferenceEquals(state.master, nav), running = state.IsRunning(), status = state.GetStatus().ToString(),
                currentState = current == null ? null : Bounded(current.name, 512), state.isCrashed };
        }
        private static object ReadAsync(Navigator nav)
        {
            var manager = AsyncPathProber.Instance;
            if (nav == null || manager == null) throw new InvalidDataException();
            lock (manager)
            {
                var map = Read(manager, "navigators") as IDictionary; var queue = Read(manager, "workQueue") as IList; var results = Read(manager, "finishedWork") as IList;
                if (map == null || queue == null || results == null || map.Count > MaxRoster || queue.Count > 10000 || results.Count > 10000) throw new InvalidDataException();
                bool registered = map.Contains(nav);
                return new { managerReference = Token(manager), registered, status = registered ? (int)map[nav] : int.MinValue,
                    navigatorCount = map.Count, queued = queue.Cast<object>().Count(item => ReferenceEquals(Read(item, "navigator"), nav)),
                    finished = results.Cast<object>().Count(item => ReferenceEquals(Read(item, "navigator"), nav)),
                    observedUnderNativeManagerLock = true, admissionFenced = false, quiescenceProven = false };
            }
        }
        internal static ManifestComparison CompareSelectedManifest(string path, int selectedId, OriginProof selected, string activeSave)
        {
            var result = new ManifestComparison { Requested = !string.IsNullOrEmpty(path) };
            if (!result.Requested) return result;
            Run(result.Errors, "manifest.path", () => CheckManifestPath(path));
            if (result.Errors.Count != 0) return result;
            result.PathVerified = true;
            Run(result.Errors, "manifest.read_parse", () => {
                byte[] bytes;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length < 1 || stream.Length > MaxManifestBytes) throw new InvalidDataException();
                    bytes = new byte[(int)stream.Length]; int done = 0;
                    while (done < bytes.Length) { int count = stream.Read(bytes, done, bytes.Length - done); if (count < 1) throw new EndOfStreamException(); done += count; }
                    if (stream.ReadByte() != -1) throw new InvalidDataException();
                }
                CheckManifestPath(path); result.Sha256 = Hash(bytes);
                string json = new UTF8Encoding(false, true).GetString(bytes);
                RejectDuplicateProperties(json);
                using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 16, DateParseHandling = DateParseHandling.None })
                {
                    var doc = JObject.Load(reader);
                    if (reader.Read()) throw new InvalidDataException();
                    if (RequiredInt(doc, "Version") != 2) throw new InvalidDataException();
                    var roots = doc["Roots"] as JArray;
                    if (roots == null || roots.Count != 2 || !(roots[0] is JObject) || !(roots[1] is JObject)) throw new InvalidDataException();
                    var root = (JObject)roots[0];
                    if (RequiredString(root, "Role") != "living-minion" || RequiredString(root, "Prefab") != "Minion") throw new InvalidDataException();
                    int archivedId = RequiredInt(root, "InstanceId");
                    var origin = doc["OriginalOrigin"] as JObject;
                    string[] fields = { "StableDupeId", "OriginalOwnerId", "OriginPlanetId", "OriginHomeSystemId", "Kind" };
                    if (origin == null || origin.Properties().Count() != 5 || !fields.All(name => ExactProperty(origin, name) != null)) throw new InvalidDataException();
                    result.ArchivedOrigin = new OriginFields { StableDupeId = RequiredString(origin, "StableDupeId"), OriginalOwnerId = RequiredString(origin, "OriginalOwnerId"),
                        OriginPlanetId = RequiredString(origin, "OriginPlanetId", true), OriginHomeSystemId = RequiredString(origin, "OriginHomeSystemId"), Kind = RequiredString(origin, "Kind") };
                    if (result.ArchivedOrigin.Kind != "NativeLocal" && result.ArchivedOrigin.Kind != "LegacyLocal") throw new InvalidDataException();
                    result.Parsed = true;
                    if (selected == null || !selected.ReadSucceeded || selected.Origin == null || !selected.NativeExists ||
                        !selected.TrackerExactReference || selected.PrefabConflicted || selected.PendingDestruction || selected.GlobalLiveIdOccurrences != 1) { Error(result.Errors, "manifest.current_origin", "ExistingBindingUnavailable"); return; }
                    result.SelectedIdMatches = archivedId == selectedId && selected.NativeId == selectedId && selected.RequestedId == selectedId;
                    result.WorldMatches = RequiredInt(doc, "SourceWorldId") == selected.WorldId;
                    result.ActiveSaveMatches = string.Equals(RequiredString(doc, "SourceActiveSave", false, 4096), activeSave, StringComparison.Ordinal);
                    var current = selected.Origin; var saved = result.ArchivedOrigin;
                    result.FieldMatches["StableDupeId"] = string.Equals(current.StableDupeId, saved.StableDupeId, StringComparison.Ordinal);
                    result.FieldMatches["OriginalOwnerId"] = string.Equals(current.OriginalOwnerId, saved.OriginalOwnerId, StringComparison.Ordinal);
                    result.FieldMatches["OriginPlanetId"] = string.Equals(current.OriginPlanetId, saved.OriginPlanetId, StringComparison.Ordinal);
                    result.FieldMatches["OriginHomeSystemId"] = string.Equals(current.OriginHomeSystemId, saved.OriginHomeSystemId, StringComparison.Ordinal);
                    result.FieldMatches["Kind"] = string.Equals(current.Kind, saved.Kind, StringComparison.Ordinal);
                    result.ComparisonPerformed = true;
                    result.ExactMatch = result.SelectedIdMatches && result.WorldMatches && result.ActiveSaveMatches && result.FieldMatches.Values.All(equal => equal);
                }
            });
            return result;
        }
        // The shipped Json.NET predates JsonLoadSettings. Validate the original
        // token stream before JObject.Load could silently replace a duplicate key.
        private static void RejectDuplicateProperties(string json)
        {
            var objects = new Stack<HashSet<string>>(); int tokens = 0;
            using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 16, DateParseHandling = DateParseHandling.None })
                while (reader.Read())
                {
                    if (++tokens > 32768 || reader.TokenType == JsonToken.Comment || reader.TokenType == JsonToken.Undefined ||
                        reader.TokenType == JsonToken.StartConstructor || reader.TokenType == JsonToken.EndConstructor) throw new InvalidDataException();
                    if (reader.TokenType == JsonToken.StartObject) objects.Push(new HashSet<string>(StringComparer.Ordinal));
                    else if (reader.TokenType == JsonToken.StartArray) objects.Push(null);
                    else if (reader.TokenType == JsonToken.EndObject || reader.TokenType == JsonToken.EndArray)
                    { if (objects.Count == 0) throw new InvalidDataException(); objects.Pop(); }
                    else if (reader.TokenType == JsonToken.PropertyName)
                    {
                        if (objects.Count == 0 || objects.Peek() == null || !objects.Peek().Add((string)reader.Value))
                            throw new InvalidDataException();
                    }
                }
            if (objects.Count != 0) throw new InvalidDataException();
        }
        private static JProperty ExactProperty(JObject obj, string name)
        { return obj.Properties().SingleOrDefault(property => string.Equals(property.Name, name, StringComparison.Ordinal)); }
        private static void CheckManifestPath(string path)
        {
            if (!Path.IsPathRooted(path) || Path.GetFileName(path) != "manifest.json") throw new InvalidDataException();
            string full = Path.GetFullPath(path); if (!string.Equals(full, path, StringComparison.Ordinal)) throw new InvalidDataException();
            var info = new FileInfo(full);
            if (!info.Exists || info.Length < 1 || info.Length > MaxManifestBytes || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
            for (var directory = info.Directory; directory != null; directory = directory.Parent)
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
        }
        private static string RequiredString(JObject obj, string name, bool nullable = false, int maximum = 512)
        {
            var property = ExactProperty(obj, name);
            if (property == null || (property.Value.Type != JTokenType.String && !(nullable && property.Value.Type == JTokenType.Null))) throw new InvalidDataException();
            string value = property.Value.Type == JTokenType.Null ? null : (string)property.Value;
            return Bounded(value, maximum);
        }
        private static int RequiredInt(JObject obj, string name)
        {
            var property = ExactProperty(obj, name);
            if (property == null || property.Value.Type != JTokenType.Integer) throw new InvalidDataException();
            long value = (long)property.Value; if (value < int.MinValue || value > int.MaxValue) throw new InvalidDataException(); return (int)value;
        }
        private static string Bounded(string value, int maximum) { if (value != null && value.Length > maximum) throw new InvalidDataException(); return value; }
        private static FieldInfo StaticField(Type type, string name)
        { return type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new MissingFieldException(); }
        private static object Read(object value, string name)
        {
            if (value == null) throw new InvalidDataException();
            return (value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new MissingFieldException()).GetValue(value);
        }
        private static int Token(object value) { return ReferenceEquals(value, null) ? 0 : RuntimeHelpers.GetHashCode(value); }
        private static string TypeName(object value) { return ReferenceEquals(value, null) ? null : Bounded(value.GetType().FullName, 512); }
        private static void Run(List<ReadError> errors, string stage, System.Action read)
        { try { read(); } catch (Exception error) { Error(errors, stage, error.GetType().Name); } }
        private static void Error(List<ReadError> errors, string stage, string type) { if (errors.Count < 64) errors.Add(new ReadError { Stage = stage, Type = type }); }
        private static string HashFile(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(stream)); }
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    }
}
