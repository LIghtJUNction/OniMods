using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace OniOnline.Multiplayer
{
    // Call only on the game's main thread. This reads existing objects; it never repairs missing
    // identities, resolves Ref.Get, creates a state machine or changes assignments/navigation.
    internal static class NativeDuplicantTransferPreflight
    {
        private const int MaxAssignables = 20000;
        private const int MaxOwnerEdges = 256;
        private const int MaxGroupNodes = 2048;
        private const int MaxIdentityVisits = 100000;
        private static readonly BindingFlags PrivateFields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo ColonyInitialized = RequiredField(typeof(OnlineColonyState), "initialized");
        private static readonly FieldInfo ColonyOwner = RequiredField(typeof(OnlineColonyState), "ownerId");
        private static readonly FieldInfo ColonySelectedWorld = RequiredField(typeof(OnlineColonyState), "selectedNativeWorldId");
        private static readonly FieldInfo ColonyPlanets = RequiredField(typeof(OnlineColonyState), "planets");
        private static readonly FieldInfo IdentityProxyRefId = RequiredField(typeof(Ref<MinionAssignablesProxy>), "id");
        private static readonly FieldInfo IdentityProxyRefObject = RequiredField(typeof(Ref<MinionAssignablesProxy>), "obj");
        private static readonly FieldInfo AssigneeRef = RequiredField(typeof(Assignable), "assignee_identityRef");
        private static readonly FieldInfo AssigneeRefId = RequiredField(typeof(Ref<KMonoBehaviour>), "id");
        private static readonly FieldInfo AssigneeRefObject = RequiredField(typeof(Ref<KMonoBehaviour>), "obj");
        private static readonly FieldInfo AssigneeGroup = RequiredField(typeof(Assignable), "assignee_groupID");
        private static readonly FieldInfo TrackerSingleton = typeof(KPrefabIDTracker).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo TrackerMap = RequiredField(typeof(KPrefabIDTracker), "prefabIdMap");
        private static readonly FieldInfo Interrupts = RequiredField(typeof(TransitionDriver), "interruptOverrideStack");

        internal static DuplicantTransferPreflightResult Inspect(DuplicantTransferExpectation expected)
        {
            var observed = new DuplicantTransferObservation();
            var failures = new List<string>();
            try { Capture(expected, observed, failures); }
            catch (Exception error)
            {
                // No retry or state repair after incomplete native reads. Keep the exception type,
                // not arbitrary messages which could include private paths or mod data.
                failures.Add("transfer_native_read_failed:" + error.GetType().Name);
            }
            observed.ReadFailures = failures.Distinct(StringComparer.Ordinal).ToArray();
            return DuplicantTransferPreflightPolicy.Evaluate(expected, observed);
        }

        private static void Capture(DuplicantTransferExpectation expected, DuplicantTransferObservation state, List<string> failures)
        {
            if (new[] { ColonyInitialized, ColonyOwner, ColonySelectedWorld, ColonyPlanets, IdentityProxyRefId, IdentityProxyRefObject, AssigneeRef, AssigneeRefId, AssigneeRefObject, AssigneeGroup, TrackerSingleton, TrackerMap, Interrupts }.Any(f => f == null))
            { failures.Add("transfer_native_field_contract_missing"); return; }
            state.Build = Convert.ToInt32(typeof(KleiVersion).GetField("ChangeList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue());
            state.Loaded = Game.Instance != null && Game.Instance.baseAlreadyCreated && !Game.Instance.IsLoading();
            state.Paused = SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused;
            if (!state.Loaded || !state.Paused || expected == null || state.Build != 744825) return;
            if (GameClock.Instance == null || SelectTool.Instance == null || SelectTool.Instance.selected == null)
                throw new InvalidOperationException("missing_native_clock_or_selection");
            state.SourceTime = GameClock.Instance.GetTime();
            var selection = SelectTool.Instance.selected;
            var minion = selection.GetComponent<MinionIdentity>();
            if (minion == null) return;
            var identity = minion.GetComponent<KPrefabID>();
            state.MinionId = identity == null ? -1 : identity.InstanceID;
            state.ExistingSelectedMinion = minion.isSpawned && minion.gameObject.activeInHierarchy && Components.LiveMinionIdentities.Items.Contains(minion) &&
                expected.MinionId == state.MinionId;
            if (!state.ExistingSelectedMinion) return;
            int cell = Grid.PosToCell(minion);
            if (!Grid.IsValidCell(cell)) return;
            state.NativeWorldId = Grid.WorldIdx[cell];
            ReadColonyIdentity(state, failures);
            state.ActiveSavePath = SaveLoader.GetActiveSaveFilePath() ?? "";
            state.SavedSource = !string.IsNullOrEmpty(state.ActiveSavePath) && Path.IsPathRooted(state.ActiveSavePath) && File.Exists(state.ActiveSavePath);
            if (identity == null || minion.assignableProxy == null || TrackerSingleton == null)
                throw new InvalidOperationException("missing_existing_identity_fields");
            state.ProxyId = (int)IdentityProxyRefId.GetValue(minion.assignableProxy);
            var tracker = TrackerSingleton.GetValue(null);
            var map = tracker == null ? null : TrackerMap.GetValue(tracker) as Dictionary<int, KPrefabID>;
            if (map == null || !map.TryGetValue(state.ProxyId, out var proxyIdentity)) return;
            var proxy = proxyIdentity.GetComponent<MinionAssignablesProxy>();
            object cachedProxy = IdentityProxyRefObject.GetValue(minion.assignableProxy);
            state.ExactTwoRegisteredRoots = state.MinionId > 0 && state.ProxyId > 0 && state.MinionId != state.ProxyId &&
                map.TryGetValue(state.MinionId, out var registeredMinion) && ReferenceEquals(registeredMinion, identity) &&
                proxy != null && proxy.isSpawned && (cachedProxy == null || ReferenceEquals(cachedProxy, proxy)) &&
                proxy.TargetInstanceID == state.MinionId && ReferenceEquals(proxy.target, minion) &&
                minion.GetComponent<SaveLoadRoot>() != null && proxy.GetComponent<SaveLoadRoot>() != null;
            if (!state.ExactTwoRegisteredRoots) return;
            state.BiologicalModel = minion.model == MinionConfig.MODEL;
            state.Live = !identity.HasTag(GameTags.Dead) && !identity.HasTag(GameTags.Dying) && !identity.HasTag(GameTags.Incapacitated);
            var health = minion.GetComponent<Health>();
            var modifiers = minion.GetComponent<MinionModifiers>();
            if (health != null && health.GetAmountInstance != null)
            {
                state.HitPoints = health.hitPoints;
                state.MaxHitPoints = health.maxHitPoints;
                state.HealthyState = health.State == Health.HealthState.Perfect;
            }
            if (modifiers != null && modifiers.sicknesses != null) state.Sicknesses = modifiers.sicknesses.Count;
            var storage = minion.GetComponent<Storage>();
            if (storage != null && storage.items != null) state.StorageItems = storage.items.Count;
            ReadSlots(minion, proxy, state, failures);
            ReadRuntime(minion, state, failures);
            ReadAssignments(minion, proxy, state, failures);
            state.Stable = Game.Instance != null && !Game.Instance.IsLoading() && SpeedControlScreen.Instance != null &&
                SpeedControlScreen.Instance.IsPaused && GameClock.Instance.GetTime() == state.SourceTime &&
                SelectTool.Instance != null && ReferenceEquals(SelectTool.Instance.selected, selection) &&
                identity.InstanceID == state.MinionId && proxyIdentity.InstanceID == state.ProxyId &&
                proxy.TargetInstanceID == state.MinionId && ReferenceEquals(proxy.target, minion) &&
                (int)IdentityProxyRefId.GetValue(minion.assignableProxy) == state.ProxyId &&
                ReferenceEquals(cachedProxy, IdentityProxyRefObject.GetValue(minion.assignableProxy)) && Grid.PosToCell(minion) == cell &&
                state.ActiveSavePath == (SaveLoader.GetActiveSaveFilePath() ?? "");
        }

        private static void ReadColonyIdentity(DuplicantTransferObservation state, List<string> failures)
        {
            var colony = OnlineColonyState.Current;
            // CapturePlanet(), PersistentOwnerId and SelectedNativeWorldId call Ensure(), so read
            // already initialized backing state instead of generating an identity during preflight.
            if (colony == null || !(bool)ColonyInitialized.GetValue(colony))
            { failures.Add("transfer_persistent_identity_not_initialized"); return; }
            state.OwnerId = ColonyOwner.GetValue(colony) as string ?? "";
            int selected = (int)ColonySelectedWorld.GetValue(colony);
            var planets = ColonyPlanets.GetValue(colony) as List<PlanetIdentity>;
            if (planets == null || planets.Count > 256 || selected != state.NativeWorldId)
            { failures.Add("transfer_selected_persistent_world_mismatch"); return; }
            var matches = planets.Where(p => p != null && p.NativeWorldId == selected).ToArray();
            if (matches.Length != 1) { failures.Add("transfer_persistent_planet_not_exact"); return; }
            state.PlanetId = matches[0].PlanetId;
        }

        private static void ReadSlots(MinionIdentity minion, MinionAssignablesProxy proxy, DuplicantTransferObservation state, List<string> failures)
        {
            state.EquipmentAssignments = 0; state.OwnableAssignments = 0;
            if (!proxy.IsConfigured || proxy.ownables == null || proxy.ownables.Count != 1 ||
                !ReferenceEquals(proxy.ownables[0], proxy.GetComponent<Ownables>()))
                failures.Add("transfer_proxy_owner_list_not_exact");
            var owners = minion.GetComponents<Assignables>().Concat(proxy.GetComponents<Assignables>()).ToArray();
            if (!owners.Any(o => o is Equipment) || !owners.Any(o => o is Ownables)) failures.Add("transfer_assignment_slots_missing");
            foreach (var owner in owners)
            {
                if ((owner.GetType() != typeof(Equipment) && owner.GetType() != typeof(Ownables)) || owner.Slots == null || owner.Slots.Count > 256)
                { failures.Add("transfer_assignment_slots_not_reviewed"); continue; }
                foreach (var slot in owner.Slots)
                {
                    if (slot == null) { failures.Add("transfer_null_assignment_slot"); continue; }
                    if (slot.IsUnassigning()) failures.Add("transfer_assignment_slot_changing");
                    if (slot.assignable == null) continue;
                    if (owner is Equipment) state.EquipmentAssignments++; else state.OwnableAssignments++;
                }
            }
        }

        private static void ReadRuntime(MinionIdentity minion, DuplicantTransferObservation state, List<string> failures)
        {
            var worker = minion.GetComponent<StandardWorker>();
            state.WorkerIdle = worker != null && worker.GetType() == typeof(StandardWorker) && worker.GetState() == WorkerBase.State.Idle && worker.GetWorkable() == null;
            var driver = minion.GetComponent<ChoreDriver>();
            if (driver != null && driver.GetType() != typeof(ChoreDriver)) failures.Add("transfer_native_chore_driver_not_reviewed");
            var chore = driver == null || driver.GetType() != typeof(ChoreDriver) ? null : driver.GetSMI() as ChoreDriver.StatesInstance;
            state.ChoreStateInitialized = chore != null && ReferenceEquals(chore.master, driver) && chore.IsRunning();
            Chore current = null;
            if (chore != null)
            {
                current = chore.GetCurrentChore(); var next = chore.GetNextChore();
                state.CurrentChore = current == null ? "" : current.GetType().FullName;
                state.NextChore = next == null ? "" : next.GetType().FullName;
            }
            var nav = minion.GetComponent<Navigator>();
            if (nav != null && nav.GetType() != typeof(Navigator)) failures.Add("transfer_native_navigator_not_reviewed");
            var navState = nav == null || nav.GetType() != typeof(Navigator) ? null : nav.GetSMI() as Navigator.StatesInstance;
            state.NavigationInitialized = navState != null && navState.IsRunning() && nav.transitionDriver != null;
            if (state.NavigationInitialized)
            {
                state.NavigationStopped = navState.IsInsideState(navState.sm.normal.stopped);
                state.FloorNavigation = nav.CurrentNavType == NavType.Floor;
                var interrupts = Interrupts.GetValue(nav.transitionDriver) as ICollection;
                state.TransitionClear = nav.transitionDriver.GetTransition == null && interrupts != null && interrupts.Count == 0;
                state.NavigationTargetClear = nav.target == null && nav.targetOffsets == null && (nav.GetReservedCell() == NavigationReservations.InvalidReservation || nav.GetReservedCell() == Grid.PosToCell(minion));
                state.NavigationPathClear = nav.path.nodes == null || nav.path.nodes.Count == 0;
            }
            // Standard save/load reconstructs this exact transient idle, never its source chore,
            // scheduled callbacks or SMI. Read the existing auto-property; do not instantiate one.
            state.ExactNativeIdleChore = current != null && current.GetType() == typeof(IdleChore);
            if (state.ExactNativeIdleChore)
            {
                var idle = (IdleChore)current;
                var idleState = idle.smi;
                state.IdleChoreStateInitialized = idleState != null && idleState.GetType() == typeof(IdleChore.StatesInstance) && ReferenceEquals(idleState.master, idle) && idleState.IsRunning();
                if (state.IdleChoreStateInitialized)
                {
                    state.IdleChoreOnFloor = idleState.IsInsideState(idleState.sm.idle.onfloor);
                    var idleTarget = idle.target as KMonoBehaviour;
                    state.IdleChoreBoundToSelected = ReferenceEquals(idle.driver, driver) && ReferenceEquals(driver.gameObject, minion.gameObject) &&
                        idleTarget != null && ReferenceEquals(idleTarget.gameObject, minion.gameObject) &&
                        ReferenceEquals(idleState.navigator, nav) && ReferenceEquals(idleState.sm.idler.Get(idleState), minion.gameObject);
                }
            }
            var controller = minion.GetComponent<StateMachineController>();
            var fall = controller == null ? null : controller.GetSMI<FallMonitor.Instance>();
            var death = controller == null ? null : controller.GetSMI<DeathMonitor.Instance>();
            var incapacitation = controller == null ? null : controller.GetSMI<IncapacitationMonitor.Instance>();
            var passenger = controller == null ? null : controller.GetSMI<RocketPassengerMonitor.Instance>();
            state.Standing = fall != null && fall.IsRunning() && fall.IsInsideState(fall.sm.standing);
            state.Live &= death != null && death.IsRunning() && death.IsInsideState(death.sm.alive);
            state.HealthyState &= incapacitation != null && incapacitation.IsRunning() && incapacitation.IsInsideState(incapacitation.sm.healthy);
            state.RocketMoveClear = passenger != null && passenger.IsRunning() && passenger.IsInsideState(passenger.sm.satisfied) &&
                passenger.sm.targetCell.Get(passenger) == Grid.InvalidCell;
            if (controller == null) failures.Add("transfer_native_state_machine_controller_missing");
        }

        private static void ReadAssignments(MinionIdentity minion, MinionAssignablesProxy proxy, DuplicantTransferObservation state, List<string> failures)
        {
            if (Game.Instance.assignmentManager == null || Game.Instance.assignmentManager.assignment_groups == null)
            { failures.Add("transfer_assignment_registry_missing"); return; }
            var groups = Game.Instance.assignmentManager.assignment_groups;
            if (groups.Count > 512) { failures.Add("transfer_assignment_group_bound"); return; }
            state.AssignmentGroups = groups.Count;
            var edges = new List<DuplicantTransferOwnerEdge>();
            var groupMatches = new Dictionary<string, bool>(StringComparer.Ordinal);
            AssignmentGroup ambientPublic = null;
            var ambientMembership = new List<DuplicantTransferAmbientMembership>();
            int identityBudget = MaxIdentityVisits;
            foreach (var pair in groups)
            {
                bool matches = GroupContains(pair.Value, minion, proxy, new HashSet<AssignmentGroup>(), failures, ref identityBudget);
                groupMatches.Add(pair.Key, matches);
                if (pair.Key == AssignmentManager.PUBLIC_GROUP_ID)
                {
                    var membership = ReadPublicMembership(pair.Key, pair.Value, groups.Values, minion, proxy);
                    if (DuplicantTransferPreflightPolicy.IsStandardPublicMembership(membership))
                    { ambientPublic = pair.Value; ambientMembership.Add(membership); }
                }
                if (matches && !ReferenceEquals(pair.Value, ambientPublic))
                    Edge(edges, new DuplicantTransferOwnerEdge { Kind = "group-member", Group = pair.Key, Type = typeof(AssignmentGroup).FullName }, failures);
            }
            state.AmbientMembership = ambientMembership.ToArray();
            // Include inactive scene objects: registry-only enumeration misses an unspawned or
            // temporarily disabled building holding a serialized incoming assignee reference.
            var scene = UnityEngine.Object.FindObjectsByType<Assignable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (scene.Length > MaxAssignables) { failures.Add("transfer_scene_assignable_bound"); return; }
            state.SceneAssignables = scene.Length;
            var manager = new HashSet<Assignable>();
            foreach (var item in Game.Instance.assignmentManager)
            {
                if (item == null || !manager.Add(item)) failures.Add("transfer_assignment_registry_inconsistent");
                if (manager.Count > MaxAssignables) { failures.Add("transfer_assignment_registry_bound"); return; }
            }
            var registered = new HashSet<Assignable>(Components.AssignableItems.Items);
            var sceneSet = new HashSet<Assignable>(scene);
            if (!manager.SetEquals(registered) || manager.Any(a => !sceneSet.Contains(a)) || scene.Any(a => a.isSpawned && !manager.Contains(a)))
                failures.Add("transfer_assignment_registry_incomplete");
            foreach (var item in scene)
            {
                if (item == null) { failures.Add("transfer_scene_assignment_changed"); continue; }
                if (item.GetType().Assembly != typeof(Assignable).Assembly) failures.Add("transfer_mod_assignable_contract_not_reviewed");
                object savedRef = AssigneeRef.GetValue(item);
                int savedId = savedRef == null ? -1 : (int)AssigneeRefId.GetValue(savedRef);
                string savedGroup = AssigneeGroup.GetValue(item) as string ?? "";
                var cachedOwner = savedRef == null ? null : AssigneeRefObject.GetValue(savedRef) as KMonoBehaviour;
                bool cached = false;
                if (cachedOwner != null)
                {
                    var cachedId = cachedOwner.GetComponent<KPrefabID>();
                    cached = cachedId != null && (cachedId.InstanceID == state.MinionId || cachedId.InstanceID == state.ProxyId);
                    if (cachedOwner.GetType().Assembly != typeof(MinionIdentity).Assembly ||
                        (!(cachedOwner is MinionIdentity) && !(cachedOwner is StoredMinionIdentity) && !(cachedOwner is MinionAssignablesProxy)))
                        failures.Add("transfer_assignment_cached_identity_not_reviewed");
                }
                bool direct = savedId == state.MinionId || savedId == state.ProxyId;
                bool ambientLive = ambientPublic != null && ReferenceEquals(item.assignee, ambientPublic);
                bool live = !ambientLive && IdentityContains(item.assignee, minion, proxy, new HashSet<AssignmentGroup>(), failures, ref identityBudget);
                bool grouped = false;
                if (savedGroup.Length != 0 && !groupMatches.TryGetValue(savedGroup, out grouped)) failures.Add("transfer_saved_assignment_group_missing");
                bool ambientSaved = ambientPublic != null && savedGroup == AssignmentManager.PUBLIC_GROUP_ID;
                if (ambientSaved) grouped = false;
                if (!direct && !cached && !live && !grouped)
                {
                    if (ambientLive || ambientSaved) state.AmbientPublicFacilities++;
                    continue;
                }
                var id = item.GetComponent<KPrefabID>();
                Edge(edges, new DuplicantTransferOwnerEdge { Kind = direct ? "saved-ref" : cached ? "cached-ref" : live ? "live-owner" : "saved-group",
                    ObjectId = id == null ? -1 : id.InstanceID, SavedOwnerId = savedId, Type = item.GetType().FullName,
                    Slot = item.slotID ?? "", Group = savedGroup }, failures);
            }
            state.IncomingOwners = edges.ToArray();
            state.AssignmentAuditComplete = !failures.Any(f => f.StartsWith("transfer_assignment_", StringComparison.Ordinal) ||
                f.StartsWith("transfer_scene_", StringComparison.Ordinal) || f.StartsWith("transfer_saved_assignment_", StringComparison.Ordinal) ||
                f == "transfer_mod_assignable_contract_not_reviewed");
        }

        private static DuplicantTransferAmbientMembership ReadPublicMembership(string key, AssignmentGroup group,
            IEnumerable<AssignmentGroup> registry, MinionIdentity minion, MinionAssignablesProxy proxy)
        {
            var fact = new DuplicantTransferAmbientMembership { RegistryKey = key };
            if (group == null) return fact;
            fact.GroupId = group.id; fact.ExactNativeType = group.GetType() == typeof(AssignmentGroup);
            fact.RegistryOccurrences = registry.Count(value => ReferenceEquals(value, group));
            var members = group.GetMembers();
            if (members == null || members.Count > MaxGroupNodes) return fact;
            fact.ReviewedProxyMembers = true;
            var references = new HashSet<IAssignableIdentity>();
            var ids = new HashSet<int>();
            int selectedId = minion.GetComponent<KPrefabID>().InstanceID;
            foreach (var member in members)
            {
                if (ReferenceEquals(member, proxy)) fact.DirectSelectedProxyCount++;
                if (ReferenceEquals(member, minion)) fact.DirectSelectedMinionCount++;
                fact.HasNestedMembers |= member is AssignmentGroup;
                if (!references.Add(member)) fact.HasDuplicateMembers = true;
                var entry = member as MinionAssignablesProxy;
                if (entry == null || entry.GetType() != typeof(MinionAssignablesProxy) || !entry.isSpawned)
                { fact.ReviewedProxyMembers = false; continue; }
                var identity = entry.GetComponent<KPrefabID>();
                if (identity == null || identity.InstanceID <= 0 || !ids.Add(identity.InstanceID)) fact.HasDuplicateMembers = true;
                // An alias proxy targeting this minion is not the standard one-to-one registration.
                if (!ReferenceEquals(entry, proxy) && (entry.TargetInstanceID == selectedId || ReferenceEquals(entry.target, minion)))
                    fact.ReviewedProxyMembers = false;
                if (entry.TargetInstanceID <= 0 || !(entry.target is KMonoBehaviour target) || target == null ||
                    (target.GetType() != typeof(MinionIdentity) && target.GetType() != typeof(StoredMinionIdentity)))
                    fact.ReviewedProxyMembers = false;
            }
            return fact;
        }

        private static bool IdentityContains(IAssignableIdentity owner, MinionIdentity minion, MinionAssignablesProxy proxy,
            HashSet<AssignmentGroup> visited, List<string> failures, ref int identityBudget)
        {
            if (--identityBudget < 0) { failures.Add("transfer_assignment_identity_visit_bound"); return false; }
            if (owner == null) return false;
            if (ReferenceEquals(owner, minion) || ReferenceEquals(owner, proxy)) return true;
            if (owner is AssignmentGroup group) return GroupContains(group, minion, proxy, visited, failures, ref identityBudget);
            if (owner is KMonoBehaviour component)
            {
                if (component.GetType().Assembly != typeof(MinionIdentity).Assembly ||
                    (!(component is MinionIdentity) && !(component is StoredMinionIdentity) && !(component is MinionAssignablesProxy)))
                    failures.Add("transfer_assignment_identity_not_reviewed");
                var identity = component.GetComponent<KPrefabID>();
                return identity != null && (identity.InstanceID == minion.GetComponent<KPrefabID>().InstanceID || identity.InstanceID == proxy.GetComponent<KPrefabID>().InstanceID);
            }
            failures.Add("transfer_assignment_identity_not_reviewed"); return false;
        }

        private static bool GroupContains(AssignmentGroup group, MinionIdentity minion, MinionAssignablesProxy proxy,
            HashSet<AssignmentGroup> visited, List<string> failures, ref int identityBudget)
        {
            if (group == null || group.GetType() != typeof(AssignmentGroup)) { failures.Add("transfer_assignment_group_not_reviewed"); return false; }
            if (!visited.Add(group)) { failures.Add("transfer_assignment_group_cycle"); return false; }
            var members = group.GetMembers();
            if (visited.Count > 64 || members == null || members.Count > MaxGroupNodes)
            { failures.Add("transfer_assignment_group_member_bound"); return false; }
            bool result = false;
            foreach (var member in members) result |= IdentityContains(member, minion, proxy, visited, failures, ref identityBudget);
            visited.Remove(group);
            return result;
        }

        private static void Edge(List<DuplicantTransferOwnerEdge> edges, DuplicantTransferOwnerEdge edge, List<string> failures)
        {
            if (edges.Count < MaxOwnerEdges) edges.Add(edge); else failures.Add("transfer_assignment_edge_bound");
        }

        private static FieldInfo RequiredField(Type type, string name)
        {
            return type.GetField(name, PrivateFields);
        }
    }
}
