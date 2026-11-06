using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace OniOnline.Multiplayer
{
    // Observes the synchronous native whole-save call chain, never arbitrary root loads.
    // No Update/OnSpawn classifier and no reader access/stream repositioning.
    internal static class NativeDupeLoadObserver
    {
        internal sealed class Scope
        {
            internal SaveLoader Loader;
            internal Game Game;
            internal SaveGame Save;
            internal bool ReaderActive, ManagerActive, ReaderSucceeded, ManagerSucceeded, PublicSucceeded;
            internal int Readers, Managers;
            internal string Failure;
            internal readonly List<Root> Roots = new List<Root>();
            internal Application.LogCallback LogHandler;
            internal void Fail(string reason) { Interlocked.CompareExchange(ref Failure, reason ?? "load_observer_rejected", null); }
        }
        internal sealed class Root
        {
            internal Scope Scope;
            internal Vector3 Position;
            internal bool ExpectedMinion;
            internal MinionIdentity Dupe;
            internal int NativeId;
        }
        [ThreadStatic] private static Scope current;
        internal static Scope Fault(Exception error)
        {
            var scope = current;
            if (scope != null) scope.Fail("load_observer_" + error.GetType().Name);
            return scope;
        }
        internal static Scope Begin(SaveLoader loader)
        {
            NativeDupeBirthObserver.SuspendForLoad();
            if (current != null) { current.Fail("nested_public_load"); return null; }
            var scope = new Scope { Loader = loader };
            current = scope;
            scope.LogHandler = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    scope.Fail("native_load_error");
                // SaveManager skips missing prefab groups but still returns true.
                else if (type == LogType.Warning && message != null && message.IndexOf("Could not find prefab '", StringComparison.Ordinal) >= 0)
                    scope.Fail("native_load_missing_prefab");
            };
            Application.logMessageReceivedThreaded += scope.LogHandler;
            if (loader == null || SaveManager.DEBUG_OnlyLoadThisCellsObjects != -1) scope.Fail("nonstandard_load_scope");
            return scope;
        }
        internal static Scope BeginReader(SaveLoader loader)
        {
            NativeDupeBirthObserver.SuspendForLoad();
            var scope = current;
            if (scope == null) return null;
            if (!ReferenceEquals(scope.Loader, loader) || scope.ReaderActive || ++scope.Readers != 1)
                scope.Fail("reader_load_scope");
            scope.ReaderActive = true; return scope;
        }
        internal static Scope BeginManager(SaveManager manager)
        {
            NativeDupeBirthObserver.SuspendForLoad();
            var scope = current;
            if (scope == null) return null;
            if (!scope.ReaderActive || scope.ManagerActive || !ReferenceEquals(scope.Loader.saveManager, manager) || ++scope.Managers != 1)
                scope.Fail("manager_load_scope");
            scope.ManagerActive = true;
            scope.Game = Game.Instance;
            if (scope.Game == null) scope.Fail("load_game_missing");
            return scope;
        }
        internal static Root BeginRoot(GameObject prefab, Vector3 position)
        {
            NativeDupeBirthObserver.Invalidate(); // Includes standalone/imported root loads.
            var scope = current;
            if (scope == null) return null;
            if (!scope.ReaderActive || !scope.ManagerActive)
            { scope.Fail("root_outside_save_manager"); return null; }
            var root = new Root { Scope = scope, Position = position,
                ExpectedMinion = prefab != null && prefab.GetComponent<MinionIdentity>() != null };
            if (root.ExpectedMinion)
            {
                if (scope.Roots.Count >= Core.DupeOriginCodec.MaxRecords)
                { scope.Fail("native_root_budget"); return null; }
                scope.Roots.Add(root);
            }
            return root;
        }
        internal static void EndRoot(Root root, SaveLoadRoot result, Exception error)
        {
            if (root == null) return;
            var scope = root.Scope;
            if (error != null || result == null) { scope.Fail("native_root_failed"); return; }
            if (!ReferenceEquals(scope, current) || !scope.ManagerActive || !ReferenceEquals(scope.Game, Game.Instance))
            { scope.Fail("native_root_scope"); return; }
            var save = result.GetComponent<SaveGame>();
            if (save != null)
            {
                if (scope.Save != null || !ReferenceEquals(save, SaveGame.Instance)) scope.Fail("load_save_game_conflict");
                scope.Save = save;
            }
            var dupe = result.GetComponent<MinionIdentity>();
            if ((dupe != null) != root.ExpectedMinion) { scope.Fail("native_root_identity_changed"); return; }
            if (dupe == null) return;
            var id = dupe.GetComponent<KPrefabID>();
            if (id == null) { scope.Fail("native_root_id_missing"); return; }
            root.Dupe = dupe; root.NativeId = id.InstanceID;
        }
        internal static void EndManager(Scope scope, bool result, Exception error)
        {
            if (scope == null) return;
            if (error != null || !result || !scope.ManagerActive) scope.Fail("native_manager_failed");
            scope.ManagerSucceeded = error == null && result; scope.ManagerActive = false;
        }
        internal static void EndReader(Scope scope, bool result, Exception error)
        {
            if (scope == null) return;
            if (error != null || !result || !scope.ReaderActive || !scope.ManagerSucceeded || scope.ManagerActive ||
                !ReferenceEquals(scope.Game, Game.Instance) || scope.Save == null || !ReferenceEquals(scope.Save, SaveGame.Instance))
                scope.Fail("native_reader_failed");
            scope.ReaderSucceeded = error == null && result; scope.ReaderActive = false;
        }
        internal static void End(Scope scope, bool result, Exception error)
        {
            if (scope == null) return;
            try
            {
                // Native call has fully returned. Seal the scoped diagnostic collection
                // before our validation/commit so our own warnings cannot change its result.
                Application.logMessageReceivedThreaded -= scope.LogHandler;
                scope.PublicSucceeded = result && error == null;
                if (!ReferenceEquals(current, scope) || !scope.PublicSucceeded || !scope.ReaderSucceeded || !scope.ManagerSucceeded ||
                    scope.Readers != 1 || scope.Managers != 1 || scope.ReaderActive || scope.ManagerActive ||
                    !ReferenceEquals(scope.Loader, SaveLoader.Instance) || !ReferenceEquals(scope.Game, Game.Instance) ||
                    scope.Save == null || !ReferenceEquals(scope.Save, SaveGame.Instance)) scope.Fail("native_load_incomplete");
                if (scope.Failure == null)
                {
                    var entries = new List<NativeDupeLoadEntry>();
                    foreach (var root in scope.Roots)
                    {
                        if (!root.ExpectedMinion) continue;
                        int cell = Grid.PosToCell(root.Position);
                        if (!Grid.IsValidCell(cell) || Grid.WorldIdx == null || cell >= Grid.WorldIdx.Length)
                        { scope.Fail("native_saved_position_invalid"); break; }
                        entries.Add(new NativeDupeLoadEntry { Dupe = root.Dupe, NativeId = root.NativeId, SavedWorldId = Grid.WorldIdx[cell] });
                    }
                    if (scope.Failure == null)
                    {
                        var colony = scope.Save.GetComponent<OnlineColonyState>(); string reason;
                        if (colony == null) scope.Fail("native_colony_state_missing");
                        else if (!colony.AcceptNormalLoadOrigins(entries.ToArray(), out reason)) scope.Fail(reason);
                    }
                }
            }
            catch (Exception observerError) { scope.Fail("load_observer_" + observerError.GetType().Name); }
            finally
            {
                Application.logMessageReceivedThreaded -= scope.LogHandler;
                if (ReferenceEquals(current, scope)) current = null;
            }
            if (scope.Failure != null) Debug.LogWarning("[OniOnline] Native ownership load batch rejected: " + scope.Failure);
        }
    }
}
