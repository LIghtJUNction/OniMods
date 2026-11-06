// Probe-only: reversible admission observation, never a release/import authorization.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using HarmonyLib;
using Newtonsoft.Json;
using OniOnline.Multiplayer;
using UnityEngine;

namespace OniOnline
{
    internal sealed class NativeNavigationQuiescencePermit
    {
        internal DuplicantTransferExpectation Source;
        internal int[] OriginalStarterIds;
        internal string CheckpointSha256;
    }

    internal sealed class NativeNavigationQuiescenceProbe : MonoBehaviour
    {
        private const string GameHash = "6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1";
        private const string FrameworkHash = "117fcc6962eeed82b4929347b9a466fb41e807a01b5a9a7acc8bdc446da276c7";
        private const int EntryOffset = 0x20b, ContinueOffset = 0x249;
        private static readonly MethodInfo Tick = AccessTools.Method(typeof(AsyncPathProber.Manager), "TickFrame");
        private static readonly MethodInfo MakeOrder = AccessTools.Method(typeof(AsyncPathProber.Manager), "makeWorkOrder");
        private static readonly FieldInfo Ordering = AccessTools.Field(typeof(AsyncPathProber.Manager), "navigatorOrdering");
        private static readonly MethodInfo ListItem = AccessTools.Method(typeof(List<Navigator>), "get_Item");
        private static readonly MethodInfo Admission = AccessTools.Method(typeof(NativeNavigationQuiescenceProbe), nameof(ShouldAdmit));
        private static NativeNavigationQuiescenceProbe active;

        internal sealed class Snapshot
        {
            public int Frame, Status, SourceQueued, SourceFinished, RegisteredNavigators;
            public bool Registered;
        }
        internal sealed class Receipt
        {
            public string Phase = "starting";
            public bool Finished, Passed, NativeLoadAllowed = false, SourceEligible;
            public string ExperimentEligibilityKind;
            public NativeNavigationExperimentEligibility.Result InitialExperimentEligibility, QuiescentExperimentEligibility, FinalExperimentEligibility;
            public bool SameExactRefs;
            public NativeNavigationExperimentEligibility.ReferenceStability InitialReferenceStability, QuiescentReferenceStability, FinalReferenceStability;
            public bool PatchInstalled, PatchRemoved, QuiescenceObserved, NativeReadmissionObserved, NativeFingerprintUnchanged;
            public int SourceId, SourceUnityId, StartedFrame, FinishedFrame, SourceSkips, SourceAdmissions,
                NativeTicks, FinishedResultsObservedBeforeTick, QuiescentSamples;
            public int[] OtherStarterIds, OtherStarterAdmissions, OtherStarterAdmissionsWhileFenced;
            public string BeforeInvariantSha256, QuiescentInvariantSha256, AfterInvariantSha256;
            public string GameAssemblySha256, FrameworkAssemblySha256;
            public Snapshot Initial, Quiescent, Resumed;
            public List<Snapshot> Samples = new List<Snapshot>();
            public List<string> Failures = new List<string>();
            public string Scope = "Reversible single-nav admission fence with native cross-frame quiescence and readmission. No queue edits, unregister/register, identity reservation, source/locator release, resource/dupe creation, or native loading. Not an importer barrier or release permission.";
        }
        private Receipt receipt;
        private NativeNavigationExperimentEligibility.ExactReferences referenceBaseline;
        private NativeNavigationQuiescencePermit permit;
        private Func<object> fingerprint;
        private System.Action guard;
        private System.Action<object> write;
        private Harmony harmony;
        private Game sourceGame;
        private SaveGame sourceSave;
        private MinionIdentity source;
        private Navigator navigator;
        private Navigator[] otherStarters;
        private MinionIdentity[] originalStarters;
        private object manager;
        private volatile bool fencing;
        private string hookFailure;
        private bool finished;
        private int skipped, admitted, ticks, priorFinished, resumeAdmissionBaseline, quiescentSamples;
        private int[] otherAdmissions, otherFencedAdmissions;
        private float deadline;

        // Caller supplies the existing exact private-checkpoint guard and the native
        // three-starter amounts/assignments/Grid fingerprint used by other probes.
        internal static void Begin(GameObject owner, NativeNavigationQuiescencePermit permit,
            Func<object> fingerprint, System.Action guard, System.Action<object> write)
        {
            if (owner == null || permit == null || permit.Source == null || fingerprint == null || guard == null || write == null)
                throw new InvalidOperationException("navigation_probe_exact_permit_and_callbacks_required");
            if (active != null || owner.GetComponent<NativeNavigationQuiescenceProbe>() != null)
                throw new InvalidOperationException("navigation_probe_already_active");
            guard();
            var selected = SelectTool.Instance == null ? null : SelectTool.Instance.selected;
            var minion = selected == null ? null : selected.GetComponent<MinionIdentity>();
            if (minion == null || permit.OriginalStarterIds == null || permit.OriginalStarterIds.Length != 3 ||
                permit.OriginalStarterIds.Distinct().Count() != 3 || !permit.OriginalStarterIds.Contains(permit.Source.MinionId))
                throw new InvalidOperationException("navigation_probe_original_starter_selection_required");
            var eligibility = NativeNavigationExperimentEligibility.Inspect(permit);
            if (!eligibility.ExperimentEligible) throw new InvalidOperationException("navigation_probe_experiment_ineligible:" + string.Join(",", eligibility.Rejections));
            var runner = owner.AddComponent<NativeNavigationQuiescenceProbe>();
            runner.permit = permit; runner.fingerprint = fingerprint; runner.guard = guard; runner.write = write;
            runner.sourceGame = Game.Instance; runner.sourceSave = SaveGame.Instance; runner.source = minion;
            runner.navigator = minion.GetComponent<Navigator>(); runner.manager = AsyncPathProber.Instance;
            runner.receipt = new Receipt { StartedFrame = Time.frameCount, SourceId = permit.Source.MinionId, SourceUnityId = minion.GetInstanceID(),
                SourceEligible = eligibility.SourceEligible, ExperimentEligibilityKind = eligibility.ExperimentEligibilityKind, InitialExperimentEligibility = eligibility };
            runner.referenceBaseline = eligibility.Observation.References;
            active = runner;
            try
            {
                runner.RequireSource();
                var others = Components.LiveMinionIdentities.Items.Where(d => !ReferenceEquals(d, minion)).OrderBy(d => d.GetComponent<KPrefabID>().InstanceID).ToArray();
                runner.originalStarters = others.Concat(new[] { minion }).OrderBy(d => d.GetComponent<KPrefabID>().InstanceID).ToArray();
                runner.otherStarters = others.Select(d => d.GetComponent<Navigator>()).ToArray();
                runner.otherAdmissions = new int[others.Length]; runner.otherFencedAdmissions = new int[others.Length];
                runner.receipt.OtherStarterIds = others.Select(d => d.GetComponent<KPrefabID>().InstanceID).ToArray();
                if (others.Length != 2 || runner.otherStarters.Any(n => n == null)) throw new InvalidOperationException("navigation_probe_exact_other_starters_missing");
                runner.receipt.BeforeInvariantSha256 = runner.InvariantHash();
                runner.receipt.Initial = runner.ReadSnapshot();
                runner.RequireExperiment("begin");
                runner.Install();
                runner.fencing = true; runner.deadline = Time.realtimeSinceStartup + 20f;
                runner.receipt.Phase = "waiting_for_native_quiescence";
            }
            catch (Exception error) { runner.Complete(false, "begin:" + error.GetType().Name + ":" + error.Message); }
        }
        private void RequireSource()
        {
            guard();
            if (!ReferenceEquals(sourceGame, Game.Instance) || !ReferenceEquals(sourceSave, SaveGame.Instance) ||
                sourceGame == null || sourceSave == null || source == null || navigator == null ||
                !ReferenceEquals(manager, AsyncPathProber.Instance) || navigator.GetType() != typeof(Navigator) ||
                !navigator.executePathProbeTaskAsync || Game.Instance.IsLoading() || !Game.Instance.baseAlreadyCreated ||
                SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused ||
                source.GetComponent<KPrefabID>().InstanceID != permit.Source.MinionId ||
                !ReferenceEquals(source.GetComponent<Navigator>(), navigator) ||
                SelectTool.Instance == null || SelectTool.Instance.selected == null ||
                !ReferenceEquals(SelectTool.Instance.selected.GetComponent<MinionIdentity>(), source))
                throw new InvalidOperationException("navigation_probe_exact_native_context_changed");
            var ids = Components.LiveMinionIdentities.Items.Where(d => d != null).Select(d => d.GetComponent<KPrefabID>().InstanceID).OrderBy(i => i).ToArray();
            if (!ids.SequenceEqual(permit.OriginalStarterIds.OrderBy(i => i))) throw new InvalidOperationException("navigation_probe_original_roster_changed");
            if (originalStarters != null)
            {
                var actual = Components.LiveMinionIdentities.Items.Where(d => d != null).OrderBy(d => d.GetComponent<KPrefabID>().InstanceID).ToArray();
                if (actual.Length != originalStarters.Length || actual.Select((dupe, index) => ReferenceEquals(dupe, originalStarters[index])).Any(same => !same))
                    throw new InvalidOperationException("navigation_probe_original_native_references_changed");
            }
            string path = permit.Source.ActiveSavePath;
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path) || SaveLoader.GetActiveSaveFilePath() != path ||
                string.IsNullOrEmpty(permit.CheckpointSha256) || HashFile(path) != permit.CheckpointSha256 ||
                !Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(path))).StartsWith("OniOnlineHarness-", StringComparison.Ordinal))
                throw new InvalidOperationException("navigation_probe_exact_private_checkpoint_changed");
            if (!NativeLifecycleProbe.HasSuccessfulProof(sourceGame, path, permit.CheckpointSha256))
                throw new InvalidOperationException("navigation_probe_exact_lifecycle_proof_missing");
        }
        private NativeNavigationExperimentEligibility.Result RequireExperiment(string phase)
        {
            var observed = NativeNavigationExperimentEligibility.Inspect(permit);
            var refs = NativeNavigationExperimentEligibility.CompareReferences(referenceBaseline, observed.Observation == null ? null : observed.Observation.References);
            if (phase == "final") { receipt.FinalExperimentEligibility = observed; receipt.FinalReferenceStability = refs; }
            else if (phase == "quiescent") { receipt.QuiescentExperimentEligibility = observed; receipt.QuiescentReferenceStability = refs; }
            else { receipt.InitialReferenceStability = refs; }
            // Only the private baseline needs native references beyond this read.
            if (observed.Observation != null) observed.Observation.References = null;
            if (!refs.ComparisonPerformed || !refs.SameExactRefs)
                throw new InvalidOperationException("navigation_probe_exact_state_references_changed:" + string.Join(",", refs.Replacements));
            if (!observed.ExperimentEligible || observed.SourceEligible != receipt.SourceEligible || observed.ExperimentEligibilityKind != receipt.ExperimentEligibilityKind)
                throw new InvalidOperationException("navigation_probe_experiment_eligibility_changed:" + string.Join(",", observed.Rejections));
            return observed;
        }
        private string InvariantHash()
        {
            RequireSource();
            var colony = OnlineColonyState.Current;
            if (colony == null) throw new InvalidOperationException("navigation_probe_existing_colony_missing");
            var dupes = Components.LiveMinionIdentities.Items.OrderBy(d => d.GetComponent<KPrefabID>().InstanceID).Select(d => {
                int id = d.GetComponent<KPrefabID>().InstanceID, cell = Grid.PosToCell(d);
                if (!Grid.IsValidCell(cell) || !colony.TryReadDuplicantOrigin(id, Grid.WorldIdx[cell], out var origin))
                    throw new InvalidOperationException("navigation_probe_existing_immutable_origin_missing");
                return new { id, unityId = d.GetInstanceID(), cell, position = d.transform.position.ToString("R"),
                    origin.StableDupeId, origin.OriginalOwnerId, origin.OriginPlanetId, origin.OriginHomeSystemId, origin.Kind };
            }).ToArray();
            return Hash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new {
                suppliedNativeState = fingerprint(), dupes, identityState = NativeDuplicantRoundTripPreparation.CaptureIdentityFingerprint(),
                game = sourceGame.GetInstanceID(), save = sourceSave.GetInstanceID(), clock = GameClock.Instance.GetTime(),
                paused = SpeedControlScreen.Instance.IsPaused, activeSave = SaveLoader.GetActiveSaveFilePath(), checkpoint = HashFile(permit.Source.ActiveSavePath) })));
        }
        private static FieldInfo Field(string name)
        {
            var field = AccessTools.Field(typeof(AsyncPathProber.Manager), name);
            if (field == null) throw new InvalidOperationException("navigation_probe_native_field_missing:" + name);
            return field;
        }
        private Snapshot ReadSnapshot()
        {
            lock (manager)
            {
                var registered = Field("navigators").GetValue(manager) as IDictionary;
                var queue = Field("workQueue").GetValue(manager) as IList;
                var results = Field("finishedWork").GetValue(manager) as IList;
                if (registered == null || queue == null || results == null || registered.Count > 200000 || queue.Count > 10000 || results.Count > 10000)
                    throw new InvalidOperationException("navigation_probe_bounded_native_collections_missing");
                bool present = registered.Contains(navigator);
                return new Snapshot { Frame = Time.frameCount, Registered = present, Status = present ? (int)registered[navigator] : int.MinValue,
                    RegisteredNavigators = registered.Count,
                    SourceQueued = queue.Cast<object>().Count(o => ReferenceEquals(AccessTools.Field(o.GetType(), "navigator").GetValue(o), navigator)),
                    SourceFinished = results.Cast<object>().Count(o => ReferenceEquals(AccessTools.Field(o.GetType(), "navigator").GetValue(o), navigator)) };
            }
        }
        private void Install()
        {
            receipt.GameAssemblySha256 = HashFile(typeof(Game).Assembly.Location);
            receipt.FrameworkAssemblySha256 = HashFile(typeof(KMonoBehaviour).Assembly.Location);
            if (receipt.GameAssemblySha256 != GameHash || receipt.FrameworkAssemblySha256 != FrameworkHash)
                throw new InvalidOperationException("navigation_probe_unreviewed_native_assemblies");
            foreach (string name in new[] { "TickFrame", "NextTask", "WorkCompleted", "Register", "Unregister" })
            {
                var method = AccessTools.Method(typeof(AsyncPathProber.Manager), name);
                var info = method == null ? null : Harmony.GetPatchInfo(method);
                if (method == null || (info != null && info.Owners.Any())) throw new InvalidOperationException("navigation_probe_existing_native_patch:" + name);
            }
            harmony = new Harmony("OniOnline.Probe.NavQuiescence." + Guid.NewGuid().ToString("N"));
            harmony.Patch(Tick,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeNavigationQuiescenceProbe), nameof(TickPrefix))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(NativeNavigationQuiescenceProbe), nameof(TickPostfix))),
                transpiler: new HarmonyMethod(AccessTools.Method(typeof(NativeNavigationQuiescenceProbe), nameof(Transpile))),
                finalizer: new HarmonyMethod(AccessTools.Method(typeof(NativeNavigationQuiescenceProbe), nameof(TickFinalizer))));
            receipt.PatchInstalled = true;
        }
        private static void TickPrefix(AsyncPathProber.Manager __instance)
        {
            var runner = active;
            if (runner == null || !ReferenceEquals(runner.manager, __instance)) return;
            try { runner.priorFinished += runner.ReadSnapshot().SourceFinished; }
            catch (Exception error) { runner.HookFailed(error); }
        }
        private static void TickPostfix(AsyncPathProber.Manager __instance)
        {
            var runner = active;
            if (runner == null || !ReferenceEquals(runner.manager, __instance)) return;
            try
            {
                runner.ticks++;
                var state = runner.ReadSnapshot();
                if (!runner.fencing && runner.admitted > runner.resumeAdmissionBaseline &&
                    (state.SourceQueued > 0 || state.SourceFinished > 0 || state.Status == -1)) runner.receipt.NativeReadmissionObserved = true;
            }
            catch (Exception error) { runner.HookFailed(error); }
        }
        private static Exception TickFinalizer(Exception __exception)
        {
            var runner = active;
            if (__exception != null && runner != null) runner.HookFailed(new InvalidOperationException("native_tick_failed"));
            return __exception;
        }
        private void HookFailed(Exception error)
        { Interlocked.CompareExchange(ref hookFailure, error.GetType().Name + ":" + error.Message, null); fencing = false; }
        private static bool ShouldAdmit(AsyncPathProber.Manager candidateManager, Navigator candidate)
        {
            var runner = active;
            if (runner == null || !ReferenceEquals(runner.manager, candidateManager)) return true;
            if (ReferenceEquals(candidate, runner.navigator))
            {
                if (runner.fencing) { runner.skipped++; return false; }
                runner.admitted++; return true;
            }
            if (runner.otherStarters != null)
                for (int i = 0; i < runner.otherStarters.Length; i++)
                    if (ReferenceEquals(candidate, runner.otherStarters[i]))
                    { runner.otherAdmissions[i]++; if (runner.fencing) runner.otherFencedAdmissions[i]++; }
            return true;
        }
        private void Update()
        {
            if (finished) return;
            try
            {
                RequireSource();
                if (hookFailure != null) throw new InvalidOperationException("navigation_probe_hook_failed:" + hookFailure);
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(receipt.Phase);
                var state = ReadSnapshot();
                if (!state.Registered) throw new InvalidOperationException("navigation_probe_source_registration_changed");
                if (receipt.Samples.Count < 64) receipt.Samples.Add(state);
                if (fencing)
                {
                    bool quiet = skipped >= 2 && ticks >= 2 && state.Status >= 0 && state.SourceQueued == 0 && state.SourceFinished == 0 &&
                        otherFencedAdmissions != null && otherFencedAdmissions.All(count => count > 0);
                    quiescentSamples = quiet ? quiescentSamples + 1 : 0;
                    if (quiescentSamples < 2) return;
                    receipt.QuiescentExperimentEligibility = RequireExperiment("quiescent");
                    receipt.QuiescentInvariantSha256 = InvariantHash();
                    if (receipt.QuiescentInvariantSha256 != receipt.BeforeInvariantSha256) throw new InvalidOperationException("navigation_probe_source_changed_while_fenced");
                    receipt.QuiescenceObserved = true; receipt.Quiescent = state;
                    resumeAdmissionBaseline = admitted; fencing = false;
                    receipt.Phase = "waiting_for_native_readmission"; deadline = Time.realtimeSinceStartup + 20f;
                }
                else if (receipt.QuiescenceObserved && receipt.NativeReadmissionObserved && admitted > resumeAdmissionBaseline)
                { receipt.Resumed = state; Complete(true, null); }
            }
            catch (Exception error) { Complete(false, error.GetType().Name + ":" + error.Message); }
        }
        private void Complete(bool success, string error)
        {
            if (finished) return;
            finished = true;
            if (error != null) receipt.Failures.Add(error);
            try
            {
                receipt.FinalExperimentEligibility = RequireExperiment("final");
                receipt.AfterInvariantSha256 = InvariantHash();
                receipt.NativeFingerprintUnchanged = receipt.BeforeInvariantSha256 == receipt.AfterInvariantSha256;
                if (!receipt.NativeFingerprintUnchanged) receipt.Failures.Add("navigation_probe_native_invariant_changed");
            }
            catch (Exception failure) { receipt.Failures.Add("after_invariant:" + failure.GetType().Name + ":" + failure.Message); }
            finally
            {
                fencing = false;
                if (ReferenceEquals(active, this)) active = null;
                try
                {
                    if (harmony != null) harmony.Unpatch(Tick, HarmonyPatchType.All, harmony.Id);
                    var info = Harmony.GetPatchInfo(Tick);
                    receipt.PatchRemoved = harmony == null || info == null || !info.Owners.Contains(harmony.Id);
                    if (!receipt.PatchRemoved) receipt.Failures.Add("navigation_probe_own_patch_still_registered");
                }
                catch (Exception failure) { receipt.Failures.Add("restore:" + failure.GetType().Name + ":" + failure.Message); }
            }
            receipt.SourceSkips = skipped; receipt.SourceAdmissions = admitted; receipt.NativeTicks = ticks;
            receipt.FinishedResultsObservedBeforeTick = priorFinished; receipt.QuiescentSamples = quiescentSamples;
            receipt.OtherStarterAdmissions = otherAdmissions == null ? Array.Empty<int>() : (int[])otherAdmissions.Clone();
            receipt.OtherStarterAdmissionsWhileFenced = otherFencedAdmissions == null ? Array.Empty<int>() : (int[])otherFencedAdmissions.Clone();
            if (success && (otherFencedAdmissions == null || otherFencedAdmissions.Any(count => count == 0))) receipt.Failures.Add("navigation_probe_other_starter_admission_while_fenced_unproven");
            receipt.Finished = true; receipt.FinishedFrame = Time.frameCount;
            receipt.SameExactRefs = receipt.InitialReferenceStability != null && receipt.InitialReferenceStability.SameExactRefs &&
                receipt.QuiescentReferenceStability != null && receipt.QuiescentReferenceStability.SameExactRefs &&
                receipt.FinalReferenceStability != null && receipt.FinalReferenceStability.SameExactRefs;
            receipt.Passed = success && receipt.SameExactRefs && receipt.QuiescenceObserved && receipt.NativeReadmissionObserved && receipt.NativeFingerprintUnchanged && receipt.PatchRemoved && receipt.Failures.Count == 0;
            receipt.Phase = receipt.Passed ? "restored_single_nav_quiescence_observed" :
                !receipt.PatchRemoved ? "failed_fence_disabled_patch_removal_unproven" :
                receipt.PatchInstalled ? "failed_native_admission_restored" : "failed_without_native_patch";
            var writer = write;
            // Drop every native/static reference even if evidence writing itself fails.
            write = null; guard = null; fingerprint = null; permit = null; harmony = null; referenceBaseline = null;
            if (receipt.InitialExperimentEligibility != null && receipt.InitialExperimentEligibility.Observation != null)
                receipt.InitialExperimentEligibility.Observation.References = null;
            if (receipt.QuiescentExperimentEligibility != null && receipt.QuiescentExperimentEligibility.Observation != null)
                receipt.QuiescentExperimentEligibility.Observation.References = null;
            if (receipt.FinalExperimentEligibility != null && receipt.FinalExperimentEligibility.Observation != null)
                receipt.FinalExperimentEligibility.Observation.References = null;
            sourceGame = null; sourceSave = null; source = null; navigator = null; manager = null; otherStarters = null; originalStarters = null; otherAdmissions = null; otherFencedAdmissions = null;
            try { if (writer != null) writer(receipt); }
            finally { UnityEngine.Object.Destroy(this); }
        }
        private void OnDestroy() { if (!finished && receipt != null) Complete(false, "navigation_probe_owner_destroyed_before_restore"); }
        private void OnApplicationQuit() { if (!finished && receipt != null) Complete(false, "navigation_probe_application_quit"); }

        // Also callable by an ignored metadata-only fixture; it never invokes native TickFrame.
        internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod)
        {
            var code = instructions.Select(i => new CodeInstruction(i.opcode, i.operand) {
                labels = new List<Label>(i.labels), blocks = new List<ExceptionBlock>(i.blocks) }).ToList();
            if (__originalMethod != Tick || generator == null) throw new InvalidOperationException("navigation_fence_exact_method_required");
            var body = Tick.GetMethodBody();
            byte[] raw = body == null ? null : body.GetILAsByteArray();
            if (raw == null || raw.Length != 644 || body.LocalVariables.Count != 13 ||
                body.LocalVariables[11].LocalType != typeof(int) || body.LocalVariables[12].LocalType != typeof(AsyncPathProber.WorkOrder))
                throw new InvalidOperationException("navigation_fence_native_body_shape_mismatch");
            var offsets = Offsets(raw);
            if (offsets.Count != code.Count) throw new InvalidOperationException("navigation_fence_input_instruction_count_changed");
            for (int i = 0; i < code.Count; i++) if (Opcode(raw, offsets[i]) != code[i].opcode) throw new InvalidOperationException("navigation_fence_input_opcode_changed");
            int entry = offsets.IndexOf(EntryOffset), next = offsets.IndexOf(ContinueOffset);
            var calls = code.Select((c, index) => new { c, index }).Where(x => x.c.Calls(MakeOrder)).ToArray();
            if (entry < 0 || next < 0 || calls.Length != 1 || calls[0].index != entry + 5 ||
                code[entry].opcode != OpCodes.Ldarg_0 || code[entry + 1].opcode != OpCodes.Ldarg_0 ||
                !code[entry + 2].LoadsField(Ordering) || Local(code[entry + 3]) != 11 || !code[entry + 4].Calls(ListItem) ||
                !IsStore(code[entry + 6], 12) || Local(code[next]) != 11 || code[next + 1].opcode != OpCodes.Ldc_I4_1 ||
                code[next + 2].opcode != OpCodes.Add || !IsStore(code[next + 3], 11) ||
                code[next - 1].opcode != OpCodes.Callvirt || ((MethodInfo)code[next - 1].operand).Name != "RecycleClone" ||
                code[entry].labels.Count != 1 || code[entry].blocks.Count != 0 || code[next].labels.Count != 1 || code[next].blocks.Count != 0)
                throw new InvalidOperationException("navigation_fence_unique_admission_pattern_mismatch");
            // Native add-path branches to this exact increment with an empty stack;
            // skipped path also has an empty stack because ShouldAdmit consumes both args.
            var add = AccessTools.Method(typeof(List<AsyncPathProber.WorkOrder>), "Add");
            var addSites = code.Select((c, index) => new { c, index }).Where(x => x.c.Calls(add)).ToArray();
            if (addSites.Length != 1 || addSites[0].index < entry || addSites[0].index >= next ||
                code[addSites[0].index + 1].opcode != OpCodes.Br_S ||
                !(code[addSites[0].index + 1].operand is Label) ||
                !code[next].labels.Contains((Label)code[addSites[0].index + 1].operand))
                throw new InvalidOperationException("navigation_fence_continue_stack_contract_mismatch");
            if (!SameProtectedRegion(body, EntryOffset, ContinueOffset) || code.Skip(entry).Take(next - entry + 1).Any(c => c.blocks.Count != 0))
                throw new InvalidOperationException("navigation_fence_exception_region_mismatch");
            var injected = new List<CodeInstruction> {
                new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, Ordering), new CodeInstruction(code[entry + 3].opcode, code[entry + 3].operand),
                new CodeInstruction(OpCodes.Callvirt, ListItem), new CodeInstruction(OpCodes.Call, Admission),
                new CodeInstruction(OpCodes.Brfalse, code[next].labels[0]) };
            injected[0].labels.AddRange(code[entry].labels); code[entry].labels.Clear();
            code.InsertRange(entry, injected); return code;
        }
        private static bool SameProtectedRegion(MethodBody body, int a, int b)
        {
            bool enclosed = false;
            foreach (var clause in body.ExceptionHandlingClauses)
            {
                bool at = a >= clause.TryOffset && a < clause.TryOffset + clause.TryLength;
                bool bt = b >= clause.TryOffset && b < clause.TryOffset + clause.TryLength;
                bool ah = a >= clause.HandlerOffset && a < clause.HandlerOffset + clause.HandlerLength;
                bool bh = b >= clause.HandlerOffset && b < clause.HandlerOffset + clause.HandlerLength;
                if (at != bt || ah || bh || clause.Flags == ExceptionHandlingClauseOptions.Filter) return false;
                enclosed |= at && clause.Flags == ExceptionHandlingClauseOptions.Finally;
            }
            return enclosed;
        }
        private static bool IsStore(CodeInstruction code, int index)
        { return (code.opcode == OpCodes.Stloc || code.opcode == OpCodes.Stloc_S) && Local(code) == index; }
        private static int Local(CodeInstruction code)
        {
            if (code.opcode == OpCodes.Ldloc_0) return 0; if (code.opcode == OpCodes.Ldloc_1) return 1;
            if (code.opcode == OpCodes.Ldloc_2) return 2; if (code.opcode == OpCodes.Ldloc_3) return 3;
            if (code.operand is LocalBuilder) return ((LocalBuilder)code.operand).LocalIndex;
            if (code.operand is LocalVariableInfo) return ((LocalVariableInfo)code.operand).LocalIndex;
            if (code.operand is byte) return (byte)code.operand; if (code.operand is int) return (int)code.operand;
            return -1;
        }
        private static readonly Dictionary<short, OpCode> opcodes = typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => o.Value);
        private static OpCode Opcode(byte[] bytes, int offset)
        { return opcodes[bytes[offset] == 0xfe ? (short)(0xfe00 | bytes[offset + 1]) : (short)bytes[offset]]; }
        private static List<int> Offsets(byte[] bytes)
        {
            var result = new List<int>();
            for (int i = 0; i < bytes.Length;)
            {
                result.Add(i); var op = Opcode(bytes, i); i += op.Size;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: i++; break;
                    case OperandType.InlineVar: i += 2; break;
                    case OperandType.InlineI8: case OperandType.InlineR: i += 8; break;
                    case OperandType.InlineSwitch: int count = BitConverter.ToInt32(bytes, i); if (count < 0 || count > 10000) throw new InvalidOperationException("navigation_fence_switch_budget"); i += 4 + count * 4; break;
                    default: i += 4; break;
                }
                if (i > bytes.Length) throw new InvalidOperationException("navigation_fence_truncated_il");
            }
            return result;
        }
        private static string HashFile(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(stream)); }
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    }
}
