#!/usr/bin/env python3
"""Check CycleTrim's reflected Harmony API dependencies in a pinned ONI reference DLL.

The public reference assemblies used by CI have method bodies stripped (ILSpy renders
methods as ``extern``), so the live-game semantic verifier cannot run against them.
This check intentionally validates metadata shape only: target methods, injected
fields/properties, nested target types, parameter types, return types, and visibility.
"""

from __future__ import annotations

import argparse
from pathlib import Path
import re
import subprocess
import sys


def decompile(assembly: Path, type_name: str) -> str:
    result = subprocess.run(
        ["ilspycmd", "-t", type_name, str(assembly)],
        check=False,
        capture_output=True,
        text=True,
        timeout=60,
    )
    if result.returncode != 0 or not result.stdout.strip():
        detail = " ".join(result.stderr.split())
        raise RuntimeError(f"could not decompile {type_name}: {detail}")
    source = result.stdout.replace("\r\n", "\n")
    # Reference assemblies expose stripped method bodies as `extern`; remove the
    # marker so one metadata pattern works for both full game DLLs and refasmer DLLs.
    return re.sub(r"\bextern\s+", "", source)


def method_pattern(prefix: str, name: str, parameters: str) -> str:
    return (
        rf"\b{prefix}\s+{re.escape(name)}\s*\(\s*"
        + parameters
        + r"\s*\)\s*;"
    )


def check(
    source: str,
    pattern: str,
    label: str,
    failures: list[str],
    *,
    minimum: int = 1,
) -> None:
    matches = len(re.findall(pattern, source, flags=re.M | re.S))
    if matches < minimum:
        failures.append(f"{label}: expected at least {minimum}, found {matches}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("assembly", help="Pinned Assembly-CSharp.dll reference assembly")
    args = parser.parse_args()
    assembly = Path(args.assembly).expanduser()
    if not assembly.is_file():
        parser.error(f"assembly not found: {assembly}")

    try:
        sources = {
            name: decompile(assembly, name)
            for name in (
                "SmartReservoir",
                "FetchManager",
                "PickupableSensor",
                "ChoreConsumer",
                "BrainScheduler",
                "Navigator",
                "AsyncPathProber",
                "Pickupable",
                "Automatable",
                "ChoreProvider",
                "GlobalChoreProvider",
                "Prioritizable",
                "NavGrid",
            )
        }
    except (OSError, subprocess.TimeoutExpired, RuntimeError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1

    failures: list[str] = []

    smart = sources["SmartReservoir"]
    check(
        smart,
        method_pattern(r"protected\s+override\s+void", "OnSpawn", ""),
        "SmartReservoir.OnSpawn()",
        failures,
    )
    check(
        smart,
        method_pattern(r"private\s+void", "UpdateLogicCircuit", r"object\s+\w+"),
        "SmartReservoir.UpdateLogicCircuit(object)",
        failures,
    )
    for field_type, field_name in (
        ("bool", "activated"),
        ("LogicPorts", "logicPorts"),
        ("int", "activateValue"),
        ("int", "deactivateValue"),
    ):
        check(
            smart,
            rf"\bprivate\s+{field_type}\s+{field_name}\s*;",
            f"SmartReservoir.{field_name}",
            failures,
        )

    fetch = sources["FetchManager"]
    check(
        fetch,
        method_pattern(
            r"public\s+void",
            "UpdatePickups",
            r"Navigator\s+\w+\s*,\s*int\s+\w+",
        ),
        "FetchablesByPrefabId.UpdatePickups(Navigator,int)",
        failures,
    )
    for pattern, label in (
        (
            r"\bpublic\s+KCompactedVector<Fetchable>\s+fetchables\s*;",
            "FetchablesByPrefabId.fetchables",
        ),
        (
            r"\bpublic\s+List<Pickup>\s+finalPickups\s*;",
            "FetchablesByPrefabId.finalPickups",
        ),
        (
            r"\bprivate\s+Dictionary<int,\s*int>\s+cellCosts\s*;",
            "FetchablesByPrefabId.cellCosts",
        ),
        (
            method_pattern(
                r"public\s+HandleVector<int>\.Handle",
                "Add",
                r"Pickupable\s+\w+",
            ),
            "FetchManager.Add(Pickupable)",
        ),
        (
            method_pattern(
                r"public\s+void",
                "Remove",
                r"Tag\s+\w+\s*,\s*HandleVector<int>\.Handle\s+\w+",
            ),
            "FetchManager.Remove(Tag,Handle)",
        ),
        (
            method_pattern(
                r"public\s+void",
                "UpdateStorage",
                r"Tag\s+\w+\s*,\s*HandleVector<int>\.Handle\s+\w+\s*,\s*Storage\s+\w+",
            ),
            "FetchManager.UpdateStorage",
        ),
        (
            method_pattern(
                r"public\s+void",
                "UpdateTags",
                r"Tag\s+\w+\s*,\s*HandleVector<int>\.Handle\s+\w+",
            ),
            "FetchManager.UpdateTags",
        ),
        (
            method_pattern(r"public\s+void", "Sim1000ms", r"float\s+\w+"),
            "FetchManager.Sim1000ms(float)",
        ),
    ):
        check(fetch, pattern, label, failures)

    sensor = sources["PickupableSensor"]
    check(
        sensor,
        method_pattern(r"public\s+override\s+void", "Update", ""),
        "PickupableSensor.Update()",
        failures,
    )

    consumer = sources["ChoreConsumer"]
    check(
        consumer,
        method_pattern(
            r"public\s+bool",
            "FindNextChore",
            r"ref\s+Chore\.Precondition\.Context\s+\w+",
        ),
        "ChoreConsumer.FindNextChore(ref Context)",
        failures,
    )
    check(
        consumer,
        r"\bpublic\s+ChoreDriver\s+choreDriver\s*;",
        "ChoreConsumer.choreDriver",
        failures,
    )
    check(
        consumer,
        method_pattern(
            r"public\s+void",
            "SetPersonalPriority",
            r"ChoreGroup\s+\w+\s*,\s*int\s+\w+",
        ),
        "ChoreConsumer.SetPersonalPriority",
        failures,
    )

    scheduler = sources["BrainScheduler"]
    check(
        scheduler,
        method_pattern(r"public\s+void", "PrioritizeBrain", r"Brain\s+\w+"),
        "BrainScheduler.PrioritizeBrain(Brain)",
        failures,
    )
    check(
        scheduler,
        method_pattern(r"protected\s+override\s+void", "OnPrefabInit", ""),
        "BrainScheduler.OnPrefabInit()",
        failures,
    )
    check(
        scheduler,
        r"\bprivate\s+class\s+CreatureBrainGroup\s*:\s*BrainGroup\b",
        "BrainScheduler.CreatureBrainGroup",
        failures,
    )
    check(
        scheduler,
        method_pattern(r"protected\s+abstract\s+int", "InitialProbeCount", ""),
        "BrainGroup.InitialProbeCount()",
        failures,
    )
    check(
        scheduler,
        method_pattern(r"public\s+void", "RenderEveryTick", r"float\s+\w+"),
        "BrainGroup.RenderEveryTick(float)",
        failures,
        minimum=2,
    )
    check(
        scheduler,
        method_pattern(
            r"public\s+override\s+void",
            "PostRenderEveryTick",
            r"float\s+\w+",
        ),
        "CreatureBrainGroup.PostRenderEveryTick(float)",
        failures,
    )
    for pattern, label in (
        (r"\bprotected\s+List<Brain>\s+brains\b", "BrainGroup.brains"),
        (
            r"\bprotected\s+Queue<Brain>\s+priorityBrains\b",
            "BrainGroup.priorityBrains",
        ),
        (
            r"\bprotected\s+int\s+nextUpdateBrain\s*;",
            "BrainGroup.nextUpdateBrain",
        ),
        (
            r"\bpublic\s+int\s+debugMaxPriorityBrainCountSeen\s*;",
            "BrainGroup.debugMaxPriorityBrainCountSeen",
        ),
    ):
        check(scheduler, pattern, label, failures)

    navigator = sources["Navigator"]
    check(
        navigator,
        method_pattern(
            r"public\s+void",
            "UpdateProbe",
            r"bool\s+\w+\s*=\s*false",
        ),
        "Navigator.UpdateProbe(bool)",
        failures,
    )
    for pattern, label in (
        (
            r"\bpublic\s+NavGrid\s+NavGrid\s*\{\s*get;\s*private\s+set;\s*\}",
            "Navigator.NavGrid",
        ),
        (
            r"\bpublic\s+NavType\s+CurrentNavType\s*;",
            "Navigator.CurrentNavType",
        ),
        (
            r"\bpublic\s+PathFinder\.PotentialPath\.Flags\s+flags\s*;",
            "Navigator.flags",
        ),
        (r"\bbool\s+reportOccupation\s*;", "Navigator.reportOccupation"),
        (
            r"\bbool\s+executePathProbeTaskAsync\s*;",
            "Navigator.executePathProbeTaskAsync",
        ),
    ):
        check(navigator, pattern, label, failures)

    async_path = sources["AsyncPathProber"]
    for pattern, label in (
        (
            method_pattern(
                r"private\s+WorkOrder",
                "makeWorkOrder",
                r"Navigator\s+\w+",
            ),
            "AsyncPathProber.Manager.makeWorkOrder(Navigator)",
        ),
        (
            method_pattern(r"public\s+void", "TickFrame", ""),
            "AsyncPathProber.Manager.TickFrame()",
        ),
        (
            method_pattern(
                r"private\s+bool",
                "NextTask",
                r"out\s+WorkOrder\s+\w+",
            ),
            "AsyncPathProber.Manager.NextTask(out WorkOrder)",
        ),
        (
            method_pattern(r"public\s+void", "Unregister", r"Navigator\s+\w+"),
            "AsyncPathProber.Manager.Unregister(Navigator)",
        ),
        (
            method_pattern(r"public\s+void", "Shutdown", ""),
            "AsyncPathProber.Manager.Shutdown()",
        ),
        (r"\bprivate\s+Thread\[\]\s+agents\s*;", "AsyncPathProber.Manager.agents"),
        (
            r"\bprivate\s+Dictionary<Navigator,\s*int>\s+navigators\b",
            "AsyncPathProber.Manager.navigators",
        ),
        (
            r"\bprivate\s+ushort\s+activeSerialNo\s*;",
            "AsyncPathProber.Manager.activeSerialNo",
        ),
    ):
        check(async_path, pattern, label, failures)

    pickupable = sources["Pickupable"]
    for pattern, label in (
        (
            method_pattern(
                r"public\s+int",
                "Reserve",
                r"string\s+\w+\s*,\s*int\s+\w+\s*,\s*float\s+\w+",
            ),
            "Pickupable.Reserve",
        ),
        (
            method_pattern(
                r"public\s+void",
                "Unreserve",
                r"string\s+\w+\s*,\s*int\s+\w+",
            ),
            "Pickupable.Unreserve",
        ),
        (
            method_pattern(r"public\s+void", "ClearReservations", ""),
            "Pickupable.ClearReservations",
        ),
    ):
        check(pickupable, pattern, label, failures)

    automatable = sources["Automatable"]
    check(
        automatable,
        method_pattern(
            r"public\s+void",
            "SetAutomationOnly",
            r"bool\s+\w+",
        ),
        "Automatable.SetAutomationOnly(bool)",
        failures,
    )

    for owner, prefix in (
        ("ChoreProvider", r"public\s+virtual\s+void"),
        ("GlobalChoreProvider", r"public\s+override\s+void"),
    ):
        source = sources[owner]
        for name in ("AddChore", "RemoveChore"):
            check(
                source,
                method_pattern(prefix, name, r"Chore\s+\w+"),
                f"{owner}.{name}(Chore)",
                failures,
            )

    prioritizable = sources["Prioritizable"]
    check(
        prioritizable,
        method_pattern(
            r"public\s+void",
            "SetMasterPriority",
            r"PrioritySetting\s+\w+",
        ),
        "Prioritizable.SetMasterPriority(PrioritySetting)",
        failures,
    )

    nav_grid = sources["NavGrid"]
    for pattern, label in (
        (
            method_pattern(r"public\s+void", "AddDirtyCell", r"int\s+\w+"),
            "NavGrid.AddDirtyCell(int)",
        ),
        (
            method_pattern(r"public\s+void", "UpdateGraph", ""),
            "NavGrid.UpdateGraph()",
        ),
        (
            method_pattern(r"public\s+void", "UpdateGraph", r"List<int>\s+\w+"),
            "NavGrid.UpdateGraph(List<int>)",
        ),
        (r"\bprivate\s+byte\[\]\s+DirtyBitFlags\s*;", "NavGrid.DirtyBitFlags"),
        (r"\bprivate\s+List<int>\s+DirtyCells\s*;", "NavGrid.DirtyCells"),
    ):
        check(nav_grid, pattern, label, failures)

    if failures:
        print("FAIL: CycleTrim pinned reference API contract drifted", file=sys.stderr)
        for failure in failures:
            print(f"- {failure}", file=sys.stderr)
        return 1

    print(
        "PASS CycleTrim pinned reference API contract "
        "(dynamic/reflected Harmony dependencies)"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
