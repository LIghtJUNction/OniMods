// Included only by OnlineRuntimeProbe. These empty objects have no KPrefabID or game entity data.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace OniOnline
{
    internal sealed class NativeLifecycleMarker : KMonoBehaviour
    {
        internal static System.Action<NativeLifecycleMarker, string> Observe;

        protected override void OnPrefabInit()
        {
            autoRegisterSimRender = false;
            Observe?.Invoke(this, "OnPrefabInit");
        }
        protected override void OnSpawn() { Observe?.Invoke(this, "OnSpawn"); }
        protected override void OnCmpEnable() { Observe?.Invoke(this, "OnCmpEnable"); }
        protected override void OnCleanUp() { Observe?.Invoke(this, "OnCleanUp"); }
    }

    internal sealed class NativeLifecycleProbe : MonoBehaviour
    {
        private sealed class EventRecord
        {
            public string callback, objectName;
            public int objectId, frame;
            public bool initialized, spawned, activeSelf, activeInHierarchy;
        }

        private sealed class Receipt
        {
            public string phase = "starting";
            public bool finished, passed, nativeFingerprintUnchanged, noRegisteredKObjects, noPendingDestroys,
                unityObjectsDestroyed, initializeOnce, spawnOnce, cleanUpOnce, noPrefabIdentity,
                lastGameObjectRetainsDestroyedWrapper, lastObjRetainsDestroyedId;
            public int templateId, cloneId, parentId, lastObjId, lastGameObjectRetainedObjectId, startedFrame, finishedFrame;
            public int[] remainingKObjectIds = Array.Empty<int>(), pendingDestroyIds = Array.Empty<int>();
            public object before, after;
            public List<object> stages = new List<object>();
            public List<EventRecord> callbacks = new List<EventRecord>();
            public List<string> failures = new List<string>();
            public string scope = "Empty test KMonoBehaviour lifecycle under inactive staging, using real Unity Awake/OnEnable/Start across frames. No duplicant, resource, native world, KPrefabID or importer barrier acceptance. Static KMB cache references are observations, not entity/resource leak claims.";
        }

        private Receipt receipt;
        private Func<object> fingerprint;
        private System.Action guard;
        private System.Action<object> write;
        private GameObject parent, template, clone;
        private NativeLifecycleMarker templateMarker, cloneMarker;
        private float deadline;
        private int nextFrame;
        private string prefix;
        private bool cleaning;
        private Game sourceGame;
        private string sourceCheckpoint, sourceCheckpointHash;
        private static Game successfulGame;
        private static string successfulCheckpoint, successfulCheckpointHash;

        internal static bool HasSuccessfulProof(Game game, string checkpoint, string checkpointHash)
        {
            return game != null && ReferenceEquals(game, successfulGame) && checkpoint == successfulCheckpoint &&
                !string.IsNullOrEmpty(checkpointHash) && checkpointHash == successfulCheckpointHash;
        }

        internal static void Begin(GameObject owner, Func<object> fingerprint, System.Action guard, System.Action<object> write,
            string sourceCheckpoint = null, string sourceCheckpointHash = null)
        {
            if (owner.GetComponent<NativeLifecycleProbe>() != null || NativeLifecycleMarker.Observe != null)
                throw new InvalidOperationException("An empty-object lifecycle experiment is already active.");
            if (KMonoBehaviour.isLoadingScene || KMonoBehaviour.isPoolPreInit || KObjectManager.Instance == null)
                throw new InvalidOperationException("Native lifecycle manager must be ready outside scene loading and pool pre-initialization.");
            guard();
            if (ReferenceEquals(successfulGame, Game.Instance))
            { successfulGame = null; successfulCheckpoint = null; successfulCheckpointHash = null; }
            object before = fingerprint();
            var runner = owner.AddComponent<NativeLifecycleProbe>();
            runner.fingerprint = fingerprint; runner.guard = guard; runner.write = write;
            runner.sourceGame = Game.Instance; runner.sourceCheckpoint = sourceCheckpoint; runner.sourceCheckpointHash = sourceCheckpointHash;
            runner.receipt = new Receipt { before = before, startedFrame = Time.frameCount };
            runner.prefix = "OniOnlineLifecycle-" + Guid.NewGuid().ToString("N");
            runner.deadline = Time.realtimeSinceStartup + 20;
            NativeLifecycleMarker.Observe = runner.RecordCallback;
            try
            {
                runner.parent = new GameObject(runner.prefix + "-parent"); runner.parent.SetActive(false);
                runner.receipt.parentId = runner.parent.GetInstanceID();
                runner.template = new GameObject(runner.prefix + "-template"); runner.template.SetActive(false);
                runner.template.transform.SetParent(runner.parent.transform, false);
                runner.receipt.templateId = runner.template.GetInstanceID();
                runner.templateMarker = runner.template.AddComponent<NativeLifecycleMarker>();
                runner.Stage("inactive_template_added");
                runner.clone = Util.KInstantiate(runner.template, Vector3.zero, Quaternion.identity, runner.parent,
                    runner.prefix + "-clone", initialize_id: false);
                if (runner.clone == null) throw new InvalidOperationException("Util.KInstantiate returned no empty clone.");
                runner.receipt.cloneId = runner.clone.GetInstanceID();
                runner.cloneMarker = runner.clone.GetComponent<NativeLifecycleMarker>();
                if (runner.cloneMarker == null) throw new InvalidOperationException("Empty clone lost its test marker.");
                runner.Stage("inactive_clone_instantiated");
                runner.cloneMarker.InitializeComponent(); runner.Stage("clone_initialize_first");
                runner.cloneMarker.InitializeComponent(); runner.Stage("clone_initialize_second");
                runner.clone.SetActive(true); runner.Stage("clone_active_self_parent_inactive");
                runner.Check(!runner.clone.activeInHierarchy && !runner.cloneMarker.isSpawned &&
                    runner.Count(runner.receipt.cloneId, "OnSpawn") == 0 && runner.Count(runner.receipt.cloneId, "OnCmpEnable") == 0,
                    "inactive_parent_did_not_hold_clone_before_activation");
                runner.parent.SetActive(true); runner.Stage("parent_activated_before_next_frame");
                runner.receipt.phase = "waiting_real_unity_start"; runner.nextFrame = Time.frameCount + 4;
                runner.Publish();
            }
            catch (Exception error) { runner.Fail(error); runner.BeginCleanup(); }
        }

        private void Update()
        {
            if (receipt == null || receipt.finished) return;
            try
            {
                if (!cleaning) guard();
                if (Time.realtimeSinceStartup > deadline && !cleaning)
                    throw new InvalidOperationException("Empty-object native lifecycle phase timed out.");
                if (Time.frameCount < nextFrame) return;
                if (!cleaning)
                {
                    Stage("after_real_unity_start_frames");
                    Check(cloneMarker != null && cloneMarker.IsInitialized() && cloneMarker.isSpawned,
                        "real_unity_start_did_not_spawn_clone");
                    if (cloneMarker != null) cloneMarker.InitializeComponent();
                    Stage("initialize_again_after_real_start");
                    receipt.initializeOnce = Count(receipt.cloneId, "OnPrefabInit") == 1;
                    receipt.spawnOnce = Count(receipt.cloneId, "OnSpawn") == 1;
                    receipt.noPrefabIdentity = parent.GetComponent<KPrefabID>() == null && template.GetComponent<KPrefabID>() == null && clone.GetComponent<KPrefabID>() == null;
                    Check(receipt.initializeOnce, "clone_initialize_callback_repeated_or_missing");
                    Check(receipt.spawnOnce, "real_unity_spawn_callback_repeated_or_missing");
                    Check(receipt.noPrefabIdentity, "empty_objects_unexpectedly_have_prefab_identity");
                    BeginCleanup();
                }
                else FinishCleanup();
            }
            catch (Exception error)
            {
                Fail(error);
                if (!cleaning) BeginCleanup();
                else if (Time.realtimeSinceStartup > deadline) Complete(false);
            }
        }

        private void BeginCleanup()
        {
            cleaning = true; receipt.phase = "waiting_native_destroy_and_manager_lateupdate";
            deadline = Time.realtimeSinceStartup + 15; nextFrame = Time.frameCount + 4;
            // Normal Unity destruction must run KMB's inactive-object QueueDestroy path.
            // No manager collection, scheduler, KMB cache or game identity is patched here.
            CleanOwnedObjects();
            Publish();
        }

        private void CleanOwnedObjects()
        {
            // Independent cleanup attempts keep one failing native callback from skipping
            // the other experiment objects. Acceptance still requires the later frame checks.
            try { DestroyOwned(clone, cloneMarker); } catch (Exception error) { Fail(error); }
            try { DestroyOwned(template, templateMarker); } catch (Exception error) { Fail(error); }
            try { if (parent != null) UnityEngine.Object.Destroy(parent); } catch (Exception error) { Fail(error); }
        }

        private static void DestroyOwned(GameObject item, NativeLifecycleMarker marker)
        {
            if (item == null) return;
            // An unexpectedly initialized but never activated template still needs normal
            // Unity lifetime cleanup. This is cleanup only; it cannot rescue acceptance.
            if (marker != null && marker.IsInitialized() && !item.activeInHierarchy)
            {
                item.transform.SetParent(null, false);
                item.SetActive(true);
            }
            item.SetActive(false);
            UnityEngine.Object.Destroy(item);
        }

        private void FinishCleanup()
        {
            var manager = KObjectManager.Instance;
            var objectsField = AccessTools.Field(typeof(KObjectManager), "objects");
            var pendingField = AccessTools.Field(typeof(KObjectManager), "pendingDestroys");
            var objects = manager == null || objectsField == null ? null : objectsField.GetValue(manager) as IDictionary;
            var pending = manager == null || pendingField == null ? null : pendingField.GetValue(manager) as IList;
            if (objects == null || pending == null) throw new InvalidOperationException("KObjectManager read-only residual field contract is unavailable.");
            int[] ids = new[] { receipt.templateId, receipt.cloneId, receipt.parentId }.Where(id => id != 0).ToArray();
            receipt.unityObjectsDestroyed = clone == null && template == null && parent == null;
            receipt.remainingKObjectIds = ids.Where(id => objects.Contains(id)).ToArray();
            receipt.pendingDestroyIds = ids.Where(id => pending.Contains(id)).ToArray();
            receipt.noRegisteredKObjects = receipt.remainingKObjectIds.Length == 0;
            receipt.noPendingDestroys = receipt.pendingDestroyIds.Length == 0;
            if ((!receipt.unityObjectsDestroyed || !receipt.noRegisteredKObjects || !receipt.noPendingDestroys) && Time.realtimeSinceStartup <= deadline)
                return;
            receipt.cleanUpOnce = Count(receipt.cloneId, "OnCleanUp") == 1;
            receipt.lastGameObjectRetainedObjectId = !ReferenceEquals(clone, null) && ReferenceEquals(KMonoBehaviour.lastGameObject, clone) ? receipt.cloneId :
                !ReferenceEquals(template, null) && ReferenceEquals(KMonoBehaviour.lastGameObject, template) ? receipt.templateId :
                !ReferenceEquals(parent, null) && ReferenceEquals(KMonoBehaviour.lastGameObject, parent) ? receipt.parentId : 0;
            receipt.lastGameObjectRetainsDestroyedWrapper = receipt.lastGameObjectRetainedObjectId != 0;
            receipt.lastObjId = KMonoBehaviour.lastObj == null ? 0 : KMonoBehaviour.lastObj.id;
            receipt.lastObjRetainsDestroyedId = ids.Contains(receipt.lastObjId);
            receipt.after = fingerprint();
            receipt.nativeFingerprintUnchanged = JsonConvert.SerializeObject(receipt.before) == JsonConvert.SerializeObject(receipt.after);
            Check(receipt.unityObjectsDestroyed, "empty_unity_objects_remain_after_destroy");
            Check(receipt.noRegisteredKObjects, "experiment_kobject_registration_remains_after_lateupdate");
            Check(receipt.noPendingDestroys, "experiment_pending_destroy_remains_after_lateupdate");
            Check(receipt.cleanUpOnce, "clone_cleanup_callback_repeated_or_missing");
            Check(receipt.nativeFingerprintUnchanged, "existing_native_source_fingerprint_changed");
            Complete(receipt.failures.Count == 0);
        }

        private void Complete(bool passed)
        {
            receipt.finished = true; receipt.passed = passed; receipt.finishedFrame = Time.frameCount;
            receipt.phase = passed ? "complete" : "failed_after_cleanup";
            NativeLifecycleMarker.Observe = null;
            try {
                Publish();
                if (passed && sourceGame != null && ReferenceEquals(sourceGame, Game.Instance) && !string.IsNullOrEmpty(sourceCheckpointHash))
                {
                    successfulGame = sourceGame; successfulCheckpoint = sourceCheckpoint; successfulCheckpointHash = sourceCheckpointHash;
                }
            }
            finally { UnityEngine.Object.Destroy(this); }
        }

        private void RecordCallback(NativeLifecycleMarker marker, string callback)
        {
            if (marker == null || !marker.name.StartsWith(prefix, StringComparison.Ordinal)) return;
            receipt.callbacks.Add(new EventRecord { callback = callback, objectName = marker.name, objectId = marker.gameObject.GetInstanceID(),
                frame = Time.frameCount, initialized = marker.IsInitialized(), spawned = marker.isSpawned,
                activeSelf = marker.gameObject.activeSelf, activeInHierarchy = marker.gameObject.activeInHierarchy });
        }

        private object State(string role, GameObject item, NativeLifecycleMarker marker)
        {
            return new { role, exists = item != null, initialized = marker != null && marker.IsInitialized(),
                spawned = marker != null && marker.isSpawned, activeSelf = item != null && item.activeSelf,
                activeInHierarchy = item != null && item.activeInHierarchy };
        }

        private void Stage(string phase)
        {
            receipt.stages.Add(new { phase, frame = Time.frameCount, template = State("template", template, templateMarker),
                clone = State("clone", clone, cloneMarker), parentActive = parent != null && parent.activeInHierarchy });
        }

        private int Count(int id, string callback) { return receipt.callbacks.Count(record => record.objectId == id && record.callback == callback); }
        private void Check(bool condition, string failure) { if (!condition && !receipt.failures.Contains(failure)) receipt.failures.Add(failure); }
        private void Fail(Exception error) { Check(false, error.GetType().Name + ":" + error.Message); }
        private void Publish() { write(receipt); }

        private void OnDestroy()
        {
            if (receipt == null || receipt.finished) return;
            Fail(new InvalidOperationException("Lifecycle runner destroyed before cross-frame cleanup acceptance."));
            NativeLifecycleMarker.Observe = null;
            CleanOwnedObjects();
            receipt.phase = "interrupted_cleanup_unverified";
            Publish();
        }
    }
}
