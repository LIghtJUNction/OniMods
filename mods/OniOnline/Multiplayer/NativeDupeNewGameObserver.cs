using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniOnline.Multiplayer
{
    // Proof is the closed native Final -> SpawnMinions -> exact pad AddNewBaseMinion
    // chain, not arbitrary OnSpawn, first live access, or subsequent Printing Pod births.
    internal static class NativeDupeNewGameObserver
    {
        internal sealed class Scope
        {
            internal NewBaseScreen Screen;
            internal Game Game;
            internal SaveGame Save;
            internal OnlineColonyState Colony;
            internal Telepad Pad;
            internal int Expected = -1;
            internal bool Spawning, SpawnSucceeded;
            internal string Failure;
            internal readonly List<NativeDupeLoadEntry> Entries = new List<NativeDupeLoadEntry>();
        }
        private static Scope current, pending;
        internal static Scope Begin(NewBaseScreen screen)
        {
            if (current != null) { current.Failure = "nested_newgame"; return null; }
            pending = null;
            var save = SaveGame.Instance;
            var scope = new Scope { Screen = screen, Game = Game.Instance, Save = save,
                Colony = save == null ? null : save.GetComponent<OnlineColonyState>() };
            current = scope;
            if (scope.Game == null || scope.Save == null || scope.Colony == null || scope.Game.baseAlreadyCreated ||
                SaveLoader.Instance == null || SaveLoader.Instance.loadedFromSave || Components.LiveMinionIdentities.Items.Count != 0)
                scope.Failure = "not_pristine_standard_newgame";
            return scope;
        }
        internal static Scope BeginSpawn(NewBaseScreen screen, GameObject pad, ITelepadDeliverable[] stats)
        {
            var scope = current;
            if (scope == null) return null;
            if (!ReferenceEquals(scope.Screen, screen) || scope.Expected != -1 || pad == null || stats == null ||
                stats.Length == 0 || stats.Length > Core.DupeOriginCodec.MaxRecords || stats.Any(s => !(s is MinionStartingStats)))
                scope.Failure = "newgame_spawn_contract";
            scope.Expected = stats == null ? -1 : stats.Length;
            scope.Pad = pad == null ? null : pad.GetComponent<Telepad>(); scope.Spawning = true;
            if (scope.Pad == null) scope.Failure = "newgame_pad_missing";
            return scope;
        }
        internal static Scope Record(Telepad pad, GameObject dupeObject)
        {
            var scope = current;
            if (scope == null || !scope.Spawning) return null;
            if (!ReferenceEquals(scope.Pad, pad) || !ReferenceEquals(scope.Game, Game.Instance) || !ReferenceEquals(scope.Save, SaveGame.Instance))
            { scope.Failure = "newgame_pad_scope"; return scope; }
            var dupe = dupeObject == null ? null : dupeObject.GetComponent<MinionIdentity>();
            var id = dupe == null ? null : dupe.GetComponent<KPrefabID>();
            int cell = dupe == null ? -1 : Grid.PosToCell(dupe);
            if (dupe == null || id == null || !Grid.IsValidCell(cell) || Grid.WorldIdx == null || cell >= Grid.WorldIdx.Length)
            { scope.Failure = "newgame_native_identity"; return scope; }
            scope.Entries.Add(new NativeDupeLoadEntry { Dupe = dupe, NativeId = id.InstanceID, SavedWorldId = Grid.WorldIdx[cell] });
            return scope;
        }
        internal static void EndSpawn(Scope scope, Exception error)
        {
            if (scope == null) return;
            if (error != null || scope.Expected <= 0 || scope.Entries.Count != scope.Expected) scope.Failure = "newgame_incomplete_batch";
            scope.SpawnSucceeded = error == null; scope.Spawning = false;
        }
        internal static void End(Scope scope, Exception error)
        {
            if (scope == null) return;
            try
            {
                if (error != null || !scope.SpawnSucceeded || scope.Spawning || !ReferenceEquals(scope, current) ||
                    !ReferenceEquals(scope.Game, Game.Instance) || !ReferenceEquals(scope.Save, SaveGame.Instance) || !scope.Game.baseAlreadyCreated)
                    scope.Failure = "newgame_final_failed";
                if (scope.Failure == null) pending = scope;
                else Debug.LogWarning("[OniOnline] Standard starter ownership batch rejected: " + scope.Failure);
            }
            finally { if (ReferenceEquals(current, scope)) current = null; }
        }
        internal static void Reject(Exception error)
        {
            if (current != null) current.Failure = "newgame_observer_" + error.GetType().Name;
        }
        internal static void Cancel(OnlineColonyState colony)
        {
            // Compare the captured component reference, not the current singleton: an old
            // colony's delayed cleanup must not cancel another game's new starter batch.
            if (ReferenceEquals(colony, null)) return;
            if (current != null && ReferenceEquals(current.Colony, colony))
            { current.Failure = "newgame_colony_cleanup"; current = null; }
            if (pending != null && ReferenceEquals(pending.Colony, colony)) pending = null;
        }
        internal static void TryComplete(OnlineColonyState colony)
        {
            var scope = pending;
            if (scope == null) return;
            if (colony == null || scope.Save == null || scope.Game == null || !ReferenceEquals(scope.Save, SaveGame.Instance) ||
                !ReferenceEquals(scope.Game, Game.Instance) || !ReferenceEquals(scope.Colony, colony))
            { pending = null; return; }
            if (scope.Entries.Any(e => e.Dupe == null))
            { pending = null; return; }
            if (!scope.Game.baseAlreadyCreated || scope.Game.IsLoading() || scope.Entries.Any(e => !e.Dupe.isSpawned)) return;
            // Count first, then compare references. Partial live lists cannot pass, and no
            // extra spawned object (including an unrelated birth) joins this batch.
            var roster = Components.LiveMinionIdentities.Items;
            if (roster.Count < scope.Expected) return;
            if (roster.Count > scope.Expected)
            { pending = null; Debug.LogWarning("[OniOnline] Standard starter ownership roster contains an unobserved object."); return; }
            if (scope.Entries.Select(e => e.Dupe).Distinct().Count() != scope.Expected ||
                scope.Entries.Any(e => roster.Count(d => ReferenceEquals(d, e.Dupe)) != 1))
            { pending = null; Debug.LogWarning("[OniOnline] Standard starter ownership reference conflict."); return; }
            try
            {
                string error;
                if (!colony.AcceptNewGameOrigins(scope.Entries.ToArray(), out error))
                    Debug.LogWarning("[OniOnline] Standard starter ownership batch rejected: " + error);
            }
            catch (Exception error) { Debug.LogWarning("[OniOnline] Standard starter ownership observer failed: " + error.GetType().Name); }
            finally { pending = null; }
        }
    }
}
