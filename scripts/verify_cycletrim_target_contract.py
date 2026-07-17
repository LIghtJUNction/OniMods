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
    except (OSError, subprocess.CalledProcessError, ValueError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1

    print(f"PASS: CycleTrim target contracts match ({assembly})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
