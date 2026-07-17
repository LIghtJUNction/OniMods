#!/usr/bin/env python3
"""Verify the live ONI APIs assumed by CycleTrim."""

from pathlib import Path
import re
import subprocess
import sys


DEFAULT_ASSEMBLY = (
    Path.home()
    / ".local/share/Steam/steamapps/common/OxygenNotIncluded/"
    "OxygenNotIncluded_Data/Managed/Assembly-CSharp.dll"
)


def decompile(assembly: Path, type_name: str) -> str:
    result = subprocess.run(
        ["ilspycmd", "-t", type_name, str(assembly)],
        check=True,
        capture_output=True,
        text=True,
    )
    return result.stdout


def method_body(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise ValueError(f"missing method: {signature}")
    opening = source.find("{", start)
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1 : index]
    raise ValueError(f"unterminated method: {signature}")


def main() -> int:
    assembly = Path(sys.argv[1]).expanduser() if len(sys.argv) > 1 else DEFAULT_ASSEMBLY
    if len(sys.argv) > 2:
        print("FAIL: usage: verify_cycletrim_target_contract.py [Assembly-CSharp.dll]", file=sys.stderr)
        return 2
    if not assembly.is_file():
        print(f"FAIL: assembly not found: {assembly}", file=sys.stderr)
        return 1

    try:
        # SmartReservoir: keep signal behavior stable when capacity changes.
        source = decompile(assembly, "SmartReservoir")
        sim_body = method_body(source, "public void Sim200ms(float dt)")
        update_body = method_body(source, "private void UpdateLogicCircuit(object data)")
        failures = []
        if not re.search(r"\bUpdateLogicCircuit\s*\(\s*null\s*\)\s*;", sim_body):
            failures.append("Sim200ms no longer calls UpdateLogicCircuit(null)")
        signal_calls = len(re.findall(r"\blogicPorts\.SendSignal\s*\(", update_body))
        if signal_calls != 1:
            failures.append(
                f"UpdateLogicCircuit has {signal_calls} LogicPorts.SendSignal calls; expected 1"
            )
        if not re.search(r"\bprivate\s+bool\s+activated\s*;", source):
            failures.append("SmartReservoir.activated bool field is missing")
        if not re.search(r"\bprivate\s+LogicPorts\s+logicPorts\s*;", source):
            failures.append("SmartReservoir.logicPorts LogicPorts field is missing")
        if not re.search(
            r"\bprotected\s+override\s+void\s+OnSpawn\s*\(\s*\)", source
        ):
            failures.append("SmartReservoir no longer declares protected override void OnSpawn()")
        for field in ("activateValue", "deactivateValue"):
            if not re.search(rf"\bprivate\s+int\s+{field}\b", source):
                failures.append(f"SmartReservoir.{field} int field is missing")
        if not re.search(
            r"if\s*\(\s*activated\s*\)\s*\{\s*if\s*\(\s*num\s*>=\s*"
            r"\(float\)deactivateValue\s*\)",
            update_body,
        ):
            failures.append("activated branch no longer deactivates at percent >= deactivateValue")
        if not re.search(
            r"else\s+if\s*\(\s*num\s*<=\s*\(float\)activateValue\s*\)",
            update_body,
        ):
            failures.append("inactive branch no longer activates at percent <= activateValue")
        if failures:
            for failure in failures:
                print(f"FAIL: {failure}", file=sys.stderr)
            return 1

        # FetchManager / sensor pipeline: candidate selection and sorting should remain compatible.
        fetch_source = decompile(assembly, "FetchManager")
        fetch_failures = []
        inner_signature = "public void UpdatePickups(Navigator worker_navigator, int worker)"
        if fetch_source.count(inner_signature) != 1:
            fetch_failures.append("exact FetchablesByPrefabId.UpdatePickups(Navigator, int) overload drifted")
        for pattern, message in (
            (r"public\s+KCompactedVector<Fetchable>\s+fetchables\s*;", "fetchables field drifted"),
            (r"public\s+List<Pickup>\s+finalPickups\b", "finalPickups field drifted"),
            (r"private\s+Dictionary<int,\s*int>\s+cellCosts\b", "cellCosts field drifted"),
            (r"pickupable\.CouldBePickedUpByMinion\s*\(\s*worker\s*\)", "worker eligibility check drifted"),
            (r"cellCosts\.TryGetValue\s*\(\s*pickupable\.cachedCell", "cached-cell cost lookup drifted"),
            (r"pickupable\.GetNavigationCost\s*\(\s*navigator\s*,\s*pickupable\.cachedCell\s*\)", "cached-cell navigation call drifted"),
        ):
            if not re.search(pattern, fetch_source):
                fetch_failures.append(message)
        outer_body = method_body(
            fetch_source,
            "public void UpdatePickups(Navigator navigator, WorkerBase worker)",
        )
        if not re.search(
            r"pickups\.Sort\s*\(\s*PickupComparerNoPriority\.CompareInst\s*\)",
            outer_body,
        ):
            fetch_failures.append("outer PickupComparerNoPriority final sort drifted")
        if fetch_failures:
            for failure in fetch_failures:
                print(f"FAIL: {failure}", file=sys.stderr)
            return 1

        # Chore scheduling path for pickup refresh + brain priority behavior.
        chore_failures = []
        sensor_source = decompile(assembly, "PickupableSensor")
        sensor_body = method_body(sensor_source, "public override void Update()")
        sensor_calls = (
            r"GlobalChoreProvider\.Instance\.UpdateFetches\s*\(\s*navigator\s*\)",
            r"Game\.Instance\.fetchManager\.UpdatePickups\s*\(\s*navigator\s*,\s*worker\s*\)",
        )
        if any(len(re.findall(call, sensor_body)) != 1 for call in sensor_calls):
            chore_failures.append("PickupableSensor.Update no longer has its two expected calls")

        consumer_source = decompile(assembly, "ChoreConsumer")
        consumer_signature = (
            "public bool FindNextChore(ref Chore.Precondition.Context out_context)"
        )
        if consumer_source.count(consumer_signature) != 1:
            chore_failures.append("ChoreConsumer.FindNextChore(ref Context) signature drifted")
        if not re.search(r"public\s+ChoreDriver\s+choreDriver\s*;", consumer_source):
            chore_failures.append("ChoreConsumer.choreDriver structure drifted")
        if not re.search(r"choreDriver\.GetCurrentChore\s*\(\s*\)", consumer_source):
            chore_failures.append("ChoreConsumer current chore access drifted")

        scheduler_source = decompile(assembly, "BrainScheduler")
        prioritize_matches = re.findall(
            r"public\s+void\s+PrioritizeBrain\s*\(\s*Brain\s+brain\s*\)",
            scheduler_source,
        )
        if len(prioritize_matches) < 1:
            chore_failures.append("BrainScheduler.PrioritizeBrain(Brain) is missing")

        navigator_source = decompile(assembly, "Navigator")
        async_source = decompile(assembly, "AsyncPathProber")
        abilities_source = decompile(assembly, "PathFinderAbilities")
        minion_abilities_source = decompile(assembly, "MinionPathFinderAbilities")
        robot_abilities_source = decompile(assembly, "RobotPathFinderAbilities")
        creature_path_abilities_source = decompile(assembly, "CreaturePathFinderAbilities")
        navigator_signature = "public void UpdateProbe(bool forceUpdate = false)"
        if navigator_source.count(navigator_signature) != 1:
            chore_failures.append("Navigator.UpdateProbe(bool) signature drifted")
        if navigator_source.count("public bool IsMoving()") != 1:
            chore_failures.append("Navigator.IsMoving() signature drifted")
        for field in ("reportOccupation", "executePathProbeTaskAsync"):
            if not re.search(rf"\bbool\s+{field}\s*;", navigator_source):
                chore_failures.append(f"Navigator.{field} field drifted")

        creature_source = decompile(assembly, "CreatureBrain")
        if not re.search(r"public\s+class\s+CreatureBrain\s*:\s*Brain", creature_source):
            chore_failures.append("CreatureBrain inheritance contract drifted")
        if not re.search(r"component\.UpdateProbe\s*\(\s*\)", creature_source):
            chore_failures.append("CreatureBrain UpdateProbe callback contract drifted")

        if chore_failures:
            for failure in chore_failures:
                print(f"FAIL: {failure}", file=sys.stderr)
            return 1

        # Invalidation generation targets: every Harmony target must retain the
        # exact API shape used by CycleTrim's version gates.
        generation_failures = []

        for signature in (
            "public HandleVector<int>.Handle Add(Pickupable pickupable)",
            "public void Remove(Tag prefab_tag, HandleVector<int>.Handle fetchable_handle)",
            "public void UpdateStorage(Tag prefab_tag, HandleVector<int>.Handle fetchable_handle, Storage storage)",
            "public void UpdateTags(Tag prefab_tag, HandleVector<int>.Handle fetchable_handle)",
        ):
            if fetch_source.count(signature) != 1:
                generation_failures.append(f"FetchManager target drifted: {signature}")
        # FetchablesByPrefabId has its own same-signature Sim1000ms; Harmony's
        # typeof(FetchManager) target disambiguates the outer method.
        fetch_sim_signature = "public void Sim1000ms(float dt)"
        if fetch_source.count(fetch_sim_signature) < 1:
            generation_failures.append(
                f"FetchManager target drifted: {fetch_sim_signature}"
            )

        pickupable_source = decompile(assembly, "Pickupable")
        for signature in (
            "public int Reserve(string context, int reserverID, float amount)",
            "public void Unreserve(string context, int ticket)",
            "public void ClearReservations()",
        ):
            if pickupable_source.count(signature) != 1:
                generation_failures.append(f"Pickupable target drifted: {signature}")

        automatable_source = decompile(assembly, "Automatable")
        for signature in (
            "public bool GetAutomationOnly()",
            "public void SetAutomationOnly(bool only)",
        ):
            if automatable_source.count(signature) != 1:
                generation_failures.append(f"Automatable target drifted: {signature}")

        chore_provider_source = decompile(assembly, "ChoreProvider")
        global_chore_provider_source = decompile(assembly, "GlobalChoreProvider")
        for owner, owner_source, modifier in (
            ("ChoreProvider", chore_provider_source, "virtual"),
            ("GlobalChoreProvider", global_chore_provider_source, "override"),
        ):
            for method in ("AddChore", "RemoveChore"):
                signature = f"public {modifier} void {method}(Chore chore)"
                if owner_source.count(signature) != 1:
                    generation_failures.append(f"{owner} target drifted: {signature}")

        prioritizable_source = decompile(assembly, "Prioritizable")
        for signature in (
            "public PrioritySetting GetMasterPriority()",
            "public void SetMasterPriority(PrioritySetting priority)",
        ):
            if prioritizable_source.count(signature) != 1:
                generation_failures.append(f"Prioritizable target drifted: {signature}")

        personal_priority_signature = (
            "public void SetPersonalPriority(ChoreGroup group, int value)"
        )
        if consumer_source.count(personal_priority_signature) != 1:
            generation_failures.append(
                f"ChoreConsumer target drifted: {personal_priority_signature}"
            )

        nav_grid_source = decompile(assembly, "NavGrid")
        for signature in (
            "public void AddDirtyCell(int cell)",
            "public void UpdateGraph()",
            "public void UpdateGraph(List<int> dirty_nav_cells)",
        ):
            if nav_grid_source.count(signature) != 1:
                generation_failures.append(f"NavGrid target drifted: {signature}")
        for pattern, message in (
            (r"public\s+NavTable\s+NavTable\s*\{\s*get;\s*private\s+set;\s*\}", "NavGrid.NavTable property drifted"),
            (r"public\s+Link\[\]\s+Links\s*;", "NavGrid.Links field drifted"),
            (r"public\s+NavType\[\]\s+ValidNavTypes\s*;", "NavGrid.ValidNavTypes field drifted"),
            (r"public\s+int\s+maxLinksPerCell\s*\{\s*get;\s*private\s+set;\s*\}", "NavGrid.maxLinksPerCell property drifted"),
            (r"private\s+byte\[\]\s+DirtyBitFlags\s*;", "NavGrid.DirtyBitFlags field drifted"),
            (r"private\s+List<int>\s+DirtyCells\s*;", "NavGrid.DirtyCells field drifted"),
        ):
            if not re.search(pattern, nav_grid_source):
                generation_failures.append(message)

        if not re.search(
            r"private\s+class\s+CreatureBrainGroup\s*:\s*BrainGroup",
            scheduler_source,
        ):
            generation_failures.append(
                "BrainScheduler.CreatureBrainGroup nested type drifted"
            )
        if scheduler_source.count(
            "public override void PostRenderEveryTick(float dt)"
        ) != 1:
            generation_failures.append(
                "CreatureBrainGroup.PostRenderEveryTick(float) target drifted"
            )
        if scheduler_source.count("public void RenderEveryTick(float dt)") < 2:
            generation_failures.append(
                "BrainGroup.RenderEveryTick(float) target drifted"
            )
        if not re.search(
            r"for\s*\(\s*int\s+i\s*=\s*0\s*;\s*i\s*!=\s*brains\.Count\s*;\s*i\+\+\s*\)",
            scheduler_source,
        ):
            generation_failures.append(
                "BrainGroup dynamic scan boundary drifted"
            )
        for pattern, message in (
            (r"protected\s+List<Brain>\s+brains\s*=", "BrainGroup.brains field drifted"),
            (r"protected\s+Queue<Brain>\s+priorityBrains\s*=", "BrainGroup.priorityBrains field drifted"),
            (r"protected\s+int\s+nextUpdateBrain\s*;", "BrainGroup.nextUpdateBrain field drifted"),
            (r"public\s+int\s+debugMaxPriorityBrainCountSeen\s*;", "BrainGroup debug priority field drifted"),
            (r"public\s+Tag\s+tag\s*\{\s*get;\s*private\s+set;\s*\}", "BrainGroup.tag property drifted"),
            (r"protected\s+abstract\s+int\s+InitialProbeCount\s*\(\s*\)", "BrainGroup.InitialProbeCount drifted"),
            (r"public\s+abstract\s+bool\s+AllowPriorityBrains\s*\(\s*\)", "BrainGroup.AllowPriorityBrains drifted"),
            (r"public\s+virtual\s+void\s+BeginBrainGroupUpdate\s*\(\s*\)", "BrainGroup.BeginBrainGroupUpdate drifted"),
            (r"public\s+virtual\s+void\s+EndBrainGroupUpdate\s*\(\s*\)", "BrainGroup.EndBrainGroupUpdate drifted"),
            (r"public\s+CreatureBrainGroup\s*\(\s*\)\s*\n\s*:\s*base\(GameTags\.CreatureBrain\)", "CreatureBrainGroup tag contract drifted"),
            (r"protected\s+override\s+void\s+OnPrefabInit\s*\(\s*\)", "BrainScheduler.OnPrefabInit lifecycle drifted"),
        ):
            if not re.search(pattern, scheduler_source):
                generation_failures.append(message)

        for pattern, message in (
            (r"public\s+NavGrid\s+NavGrid\s*\{\s*get;\s*private\s+set;\s*\}", "Navigator.NavGrid property drifted"),
            (r"public\s+NavType\s+CurrentNavType\s*;", "Navigator.CurrentNavType field drifted"),
            (r"public\s+PathFinder\.PotentialPath\.Flags\s+flags\s*;", "Navigator.flags field drifted"),
        ):
            if not re.search(pattern, navigator_source):
                generation_failures.append(message)

        creature_abilities_source = decompile(
            assembly, "CreaturePathFinderAbilities"
        )
        if not re.search(
            r"public\s+class\s+CreaturePathFinderAbilities\s*:\s*PathFinderAbilities",
            creature_abilities_source,
        ):
            generation_failures.append(
                "CreaturePathFinderAbilities inheritance target drifted"
            )

        patch_source = (
            Path(__file__).resolve().parents[1]
            / "mods/CycleTrim/Patches/InvalidationGenerationPatches.cs"
        ).read_text(encoding="utf-8")
        for needle, message in (
            ("byte[] ___DirtyBitFlags", "AddDirtyCell dirty-bit state injection missing"),
            ("out DirtyCellState __state", "AddDirtyCell prefix state injection missing"),
            ("new[] { typeof(List<int>) }", "UpdateGraph(List<int>) target injection missing"),
        ):
            if needle not in patch_source:
                generation_failures.append(message)
        if patch_source.count(
            "Finalizer(Exception __exception, bool __state)"
        ) != 2:
            generation_failures.append(
                "suppression finalizers no longer carry conditional bool state"
            )
        if patch_source.count("InvalidationSuppression.ExitIfEntered(__state)") != 2:
            generation_failures.append(
                "suppression finalizers no longer exit only their own scopes"
            )

        scheduler_patch_source = (
            Path(__file__).resolve().parents[1]
            / "mods/CycleTrim/Patches/CreatureBrainSchedulerRateCapPatch.cs"
        ).read_text(encoding="utf-8")
        scheduler_core_source = (
            Path(__file__).resolve().parents[1]
            / "mods/CycleTrim/Core/CreatureBrainSchedulePolicy.cs"
        ).read_text(encoding="utf-8")
        scheduler_simulator_source = (
            Path(__file__).resolve().parents[1]
            / "benchmarks/CycleTrim.BrainBenchmarks/CreatureBrainSchedulerSimulator.cs"
        ).read_text(encoding="utf-8")
        for needle, message in (
            ("typeof(BrainScheduler.BrainGroup)", "BrainGroup scheduler target missing"),
            ("List<Brain> ___brains", "BrainGroup brains field injection missing"),
            ("Queue<Brain> ___priorityBrains", "BrainGroup priority queue injection missing"),
            ("ref int ___nextUpdateBrain", "BrainGroup index injection missing"),
            ("__instance.GetType() != creatureBrainGroupType", "ordinary Creature exact-type guard missing"),
            ("__instance.tag != GameTags.CreatureBrain", "Creature tag guard missing"),
            ("new DynamicMethod(", "cached InitialProbeCount delegate missing"),
            ("ConditionalWeakTable<BrainScheduler.BrainGroup, RateState>", "per-group rate state missing"),
            ("finally", "BrainGroup End finally guard missing"),
            ("\"OnPrefabInit\"", "BrainScheduler lifecycle reset target missing"),
            ("ResetRateCaps();", "BrainScheduler lifecycle rate reset missing"),
            ("new CreatureBrainScheduleCursor(normalAllowance)", "shared scheduler cursor missing"),
            ("cursor.TrySelect(", "shared scheduler selection missing"),
            ("cursor.Complete(selection", "shared scheduler completion missing"),
            ("CreatureBrainSchedulePolicy.ObservePriorityMaximum(", "shared priority debug policy missing"),
        ):
            if needle not in scheduler_patch_source:
                generation_failures.append(message)
        for needle, message in (
            ("public struct CreatureBrainScheduleCursor", "allocation-free scheduler cursor missing"),
            ("scanned >= currentBrainCount", "bounded dynamic scheduler scan missing"),
            ("selection.Kind == CreatureBrainSelectionKind.Normal", "running normal allowance policy missing"),
        ):
            if needle not in scheduler_core_source:
                generation_failures.append(message)

        scheduler_prefix = method_body(
            scheduler_patch_source,
            "private static bool Prefix(",
        )
        ordered_lifecycle = (
            scheduler_prefix.find("try"),
            scheduler_prefix.find("__instance.BeginBrainGroupUpdate();"),
            scheduler_prefix.find("finally"),
            scheduler_prefix.find("__instance.EndBrainGroupUpdate();"),
        )
        if any(position < 0 for position in ordered_lifecycle) or tuple(
            sorted(ordered_lifecycle)
        ) != ordered_lifecycle:
            generation_failures.append(
                "BrainGroup Begin/End exception finalization order drifted"
            )
        if "AccessTools." in scheduler_prefix or "GetMethod(" in scheduler_prefix:
            generation_failures.append(
                "BrainGroup render prefix regained per-frame reflection"
            )
        if "for (var scanned" in scheduler_patch_source:
            generation_failures.append(
                "production scheduler duplicated shared scan algorithm"
            )
        if "new CreatureBrainScheduleCursor(normalAllowance)" not in scheduler_simulator_source:
            generation_failures.append(
                "scheduler simulator does not exercise shared cursor"
            )
        if "for (var scanned" in scheduler_simulator_source:
            generation_failures.append(
                "scheduler simulator duplicated shared scan algorithm"
            )

        async_patch_source = (
            Path(__file__).resolve().parents[1]
            / "mods/CycleTrim/Patches/AsyncPathProbeOptimizationPatch.cs"
        ).read_text(encoding="utf-8")
        for needle, message in (
            ("private WorkOrder makeWorkOrder(Navigator nav)", "Async makeWorkOrder target drifted"),
            ("public bool NextTask(out WorkOrder order, out WorkResult result)", "Async NextTask target drifted"),
            ("public void WorkCompleted(WorkResult result)", "Async WorkCompleted target drifted"),
            ("if (workQueue.Count >= 4)", "Async unique vanilla queue limit drifted"),
            ("navigators[order.navigator] = -1;", "Async in-flight marker drifted"),
        ):
            if needle not in async_source:
                generation_failures.append(message)
        if async_source.count("if (workQueue.Count >= 4)") != 1:
            generation_failures.append("Async queue limit is no longer unique")
        for needle, message in (
            ("public PathGrid TakeResult(ref AsyncPathProber.WorkResult result)", "Navigator.TakeResult target drifted"),
            ("pathGrid = result.navigator.TakeResult(ref result);", "TakeResult acceptance call drifted"),
        ):
            if needle not in (navigator_source + async_source):
                generation_failures.append(message)
        for needle, message in (
            ("protected int prefabInstanceID;", "abilities prefab fingerprint field drifted"),
            ("private bool idleNavMaskEnabled;", "Minion abilities dynamic field drifted"),
            ("private Tag prefabTag;", "Robot abilities permission field drifted"),
        ):
            if needle not in (abilities_source + minion_abilities_source + robot_abilities_source):
                generation_failures.append(message)
        for needle, message in (
            ("PathProbeAdmissionState", "shared PathProbe policy missing"),
            ("MaxConsecutiveSkips = 8", "bounded PathProbe fallback missing"),
            ("matches != 1", "TickFrame transpiler explicit failure missing"),
            ("PathProbeBackpressure.ComputeQueueQuota", "shared backpressure policy missing"),
            ("state.Admission.MarkApplied();", "TakeResult completion hook missing"),
            ("state.Navigators.Remove(nav);", "Navigator lifecycle cleanup missing"),
            ("FieldRefAccess<Navigator, PathFinderAbilities>(\"abilities\")", "zero-refresh abilities field access missing"),
            ("FieldRefAccess<AsyncPathProber.Manager, ushort>(\"activeSerialNo\")", "active serial field access missing"),
            ("BindingFlags.DeclaredOnly", "RecycleClone exact override guard missing"),
            ("il.Length == 1", "RecycleClone empty-body guard missing"),
            ("serialNo = ActiveSerialNo(__instance)", "WorkOrder active serial copy missing"),
        ):
            if needle not in async_patch_source:
                generation_failures.append(message)
        if not re.search(
            r"rawAbilities\s*=\s*Abilities\(nav\).*?rawAbilities\s*==\s*null"
            r".*?rawAbilities\.GetType\(\)\s*!=\s*typeof\(CreaturePathFinderAbilities\)"
            r".*?return\s+true",
            async_patch_source,
            re.DOTALL,
        ):
            generation_failures.append("zero-refresh exact supported ability guard missing")
        for source_text, pattern, message in (
            (navigator_source, r"private\s+PathFinderAbilities\s+abilities\s*;", "Navigator abilities field drifted"),
            (async_source, r"private\s+ushort\s+activeSerialNo\s*;", "Manager activeSerialNo field drifted"),
            (async_source, r"private\s+Thread\[\]\s+agents\s*;", "Manager agents field drifted"),
            (async_source, r"private\s+Dictionary<Navigator,\s*int>\s+navigators\s*=", "Manager navigators field drifted"),
        ):
            if not re.search(pattern, source_text):
                generation_failures.append(message)
        async_prefix = method_body(async_patch_source, "private static bool Prefix(")
        if async_prefix.count("nav.GetCurrentAbilities()") != 1:
            generation_failures.append("supported makeWorkOrder prefix must Refresh exactly once")
        for field in (
            "navigator = nav", "navGrid = nav.NavGrid",
            "gridClassification = nav.PathGrid.AllocatedClassification",
            "abilities = abilities.Clone()", "originCell = nav.cachedCell",
            "startingNavType = nav.CurrentNavType", "startingFlags = nav.flags",
            "computeReachables = nav.reportOccupation",
        ):
            if field not in async_prefix:
                generation_failures.append("complete WorkOrder construction missing: " + field)
        recycle_body = method_body(
            creature_path_abilities_source,
            "public override void RecycleClone()",
        )
        if recycle_body.strip():
            generation_failures.append("Creature RecycleClone is no longer empty")

        if generation_failures:
            for failure in generation_failures:
                print(f"FAIL: {failure}", file=sys.stderr)
            return 1
    except (OSError, subprocess.CalledProcessError, ValueError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1

    print(f"PASS: CycleTrim target contracts match ({assembly})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
