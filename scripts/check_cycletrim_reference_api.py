#!/usr/bin/env python3
"""Validate CycleTrim's reflected Harmony dependencies against pinned ONI refs.

The public CI reference DLL has stripped method bodies, so the live semantic verifier
cannot run on it. This checker intentionally validates metadata/API shape only.
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
    # refasmer-style references expose stripped methods as `extern`.
    return re.sub(r"\bextern\s+", "", source)


def method(prefix: str, name: str, parameters: str = "") -> str:
    return rf"\b{prefix}\s+{re.escape(name)}\s*\(\s*{parameters}\s*\)\s*;"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("assembly", help="Pinned Assembly-CSharp.dll reference assembly")
    args = parser.parse_args()
    assembly = Path(args.assembly).expanduser()
    if not assembly.is_file():
        parser.error(f"assembly not found: {assembly}")

    cache: dict[str, str] = {}
    failures: list[str] = []

    def source(type_name: str) -> str:
        if type_name not in cache:
            cache[type_name] = decompile(assembly, type_name)
        return cache[type_name]

    def expect(type_name: str, pattern: str, label: str, minimum: int = 1) -> None:
        count = len(re.findall(pattern, source(type_name), flags=re.M | re.S))
        if count < minimum:
            failures.append(f"{label}: expected at least {minimum}, found {count}")

    try:
        # SmartReservoirSignalPatch: reflected methods plus injected private fields.
        expect("SmartReservoir", method(r"protected\s+override\s+void", "OnSpawn"),
               "SmartReservoir.OnSpawn()")
        expect("SmartReservoir", method(r"private\s+void", "UpdateLogicCircuit", r"object\s+\w+"),
               "SmartReservoir.UpdateLogicCircuit(object)")
        for field_type, field_name in (
            ("bool", "activated"), ("LogicPorts", "logicPorts"),
            ("int", "activateValue"), ("int", "deactivateValue"),
        ):
            expect("SmartReservoir", rf"\bprivate\s+{field_type}\s+{field_name}\s*;",
                   f"SmartReservoir.{field_name}")

        # FetchPickupCandidatePatch and fetch invalidation targets.
        expect("FetchManager", method(r"public\s+void", "UpdatePickups",
                                      r"Navigator\s+\w+\s*,\s*int\s+\w+"),
               "FetchablesByPrefabId.UpdatePickups(Navigator,int)")
        for pattern, label in (
            (r"\bpublic\s+KCompactedVector<Fetchable>\s+fetchables\s*;", "FetchablesByPrefabId.fetchables"),
            (r"\bpublic\s+List<Pickup>\s+finalPickups\s*;", "FetchablesByPrefabId.finalPickups"),
            (r"\bprivate\s+Dictionary<int,\s*int>\s+cellCosts\s*;", "FetchablesByPrefabId.cellCosts"),
            (method(r"public\s+HandleVector<int>\.Handle", "Add", r"Pickupable\s+\w+"), "FetchManager.Add(Pickupable)"),
            (method(r"public\s+void", "Remove", r"Tag\s+\w+\s*,\s*HandleVector<int>\.Handle\s+\w+"), "FetchManager.Remove(Tag,Handle)"),
            (method(r"public\s+void", "UpdateStorage", r"Tag\s+\w+\s*,\s*HandleVector<int>\.Handle\s+\w+\s*,\s*Storage\s+\w+"), "FetchManager.UpdateStorage"),
            (method(r"public\s+void", "UpdateTags", r"Tag\s+\w+\s*,\s*HandleVector<int>\.Handle\s+\w+"), "FetchManager.UpdateTags"),
            (method(r"public\s+void", "Sim1000ms", r"float\s+\w+"), "FetchManager.Sim1000ms(float)"),
        ):
            expect("FetchManager", pattern, label)

        # Busy duplicant/chore scheduler targets.
        expect("PickupableSensor", method(r"public\s+override\s+void", "Update"),
               "PickupableSensor.Update()")
        expect("ChoreConsumer", method(r"public\s+bool", "FindNextChore",
                                       r"ref\s+Chore\.Precondition\.Context\s+\w+"),
               "ChoreConsumer.FindNextChore(ref Context)")
        expect("ChoreConsumer", r"\bpublic\s+ChoreDriver\s+choreDriver\s*;",
               "ChoreConsumer.choreDriver")
        expect("ChoreConsumer", method(r"public\s+void", "SetPersonalPriority",
                                       r"ChoreGroup\s+\w+\s*,\s*int\s+\w+"),
               "ChoreConsumer.SetPersonalPriority")

        # Creature brain scheduler reflected/nested targets and injected fields.
        expect("BrainScheduler", method(r"public\s+void", "PrioritizeBrain", r"Brain\s+\w+"),
               "BrainScheduler.PrioritizeBrain(Brain)")
        expect("BrainScheduler", method(r"protected\s+override\s+void", "OnPrefabInit"),
               "BrainScheduler.OnPrefabInit()")
        expect("BrainScheduler", r"\bprivate\s+class\s+CreatureBrainGroup\s*:\s*BrainGroup\b",
               "BrainScheduler.CreatureBrainGroup")
        expect("BrainScheduler", method(r"protected\s+abstract\s+int", "InitialProbeCount"),
               "BrainGroup.InitialProbeCount()")
        expect("BrainScheduler", method(r"public\s+void", "RenderEveryTick", r"float\s+\w+"),
               "BrainGroup.RenderEveryTick(float)", minimum=2)
        expect("BrainScheduler", method(r"public\s+override\s+void", "PostRenderEveryTick", r"float\s+\w+"),
               "CreatureBrainGroup.PostRenderEveryTick(float)")
        for pattern, label in (
            (r"\bprotected\s+List<Brain>\s+brains\b", "BrainGroup.brains"),
            (r"\bprotected\s+Queue<Brain>\s+priorityBrains\b", "BrainGroup.priorityBrains"),
            (r"\bprotected\s+int\s+nextUpdateBrain\s*;", "BrainGroup.nextUpdateBrain"),
            (r"\bpublic\s+int\s+debugMaxPriorityBrainCountSeen\s*;", "BrainGroup.debugMaxPriorityBrainCountSeen"),
        ):
            expect("BrainScheduler", pattern, label)

        # Navigator and async path targets. The existing canonical surface baseline
        # separately locks full property accessor shape; this check only needs the
        # readable NavGrid member because CycleTrim consumes it as a value.
        expect("Navigator", method(r"public\s+void", "UpdateProbe", r"bool\s+\w+\s*=\s*false"),
               "Navigator.UpdateProbe(bool)")
        for pattern, label in (
            (r"\bpublic\s+NavGrid\s+NavGrid\b", "Navigator.NavGrid"),
            (r"\bpublic\s+NavType\s+CurrentNavType\s*;", "Navigator.CurrentNavType"),
            (r"\bpublic\s+PathFinder\.PotentialPath\.Flags\s+flags\s*;", "Navigator.flags"),
            (r"\bpublic\s+bool\s+reportOccupation\s*;", "Navigator.reportOccupation"),
            (r"\bpublic\s+bool\s+executePathProbeTaskAsync\s*;", "Navigator.executePathProbeTaskAsync"),
        ):
            expect("Navigator", pattern, label)

        for pattern, label in (
            (method(r"private\s+WorkOrder", "makeWorkOrder", r"Navigator\s+\w+"), "AsyncPathProber.Manager.makeWorkOrder(Navigator)"),
            (method(r"public\s+void", "TickFrame"), "AsyncPathProber.Manager.TickFrame()"),
            (method(r"public\s+bool", "NextTask", r"out\s+WorkOrder\s+\w+\s*,\s*out\s+WorkResult\s+\w+"), "AsyncPathProber.Manager.NextTask(out WorkOrder,out WorkResult)"),
            (method(r"public\s+void", "Unregister", r"Navigator\s+\w+"), "AsyncPathProber.Manager.Unregister(Navigator)"),
            (method(r"public\s+void", "Shutdown"), "AsyncPathProber.Manager.Shutdown()"),
            (r"\bprivate\s+Thread\[\]\s+agents\s*;", "AsyncPathProber.Manager.agents"),
            (r"\bprivate\s+Dictionary<Navigator,\s*int>\s+navigators\b", "AsyncPathProber.Manager.navigators"),
            (r"\bprivate\s+ushort\s+activeSerialNo\s*;", "AsyncPathProber.Manager.activeSerialNo"),
        ):
            expect("AsyncPathProber", pattern, label)

        # Invalidation-generation reflected targets.
        for pattern, label in (
            (method(r"public\s+int", "Reserve", r"string\s+\w+\s*,\s*int\s+\w+\s*,\s*float\s+\w+"), "Pickupable.Reserve"),
            (method(r"public\s+void", "Unreserve", r"string\s+\w+\s*,\s*int\s+\w+"), "Pickupable.Unreserve"),
            (method(r"public\s+void", "ClearReservations"), "Pickupable.ClearReservations"),
        ):
            expect("Pickupable", pattern, label)
        expect("Automatable", method(r"public\s+void", "SetAutomationOnly", r"bool\s+\w+"),
               "Automatable.SetAutomationOnly(bool)")
        for owner, prefix in (("ChoreProvider", r"public\s+virtual\s+void"),
                              ("GlobalChoreProvider", r"public\s+override\s+void")):
            for name in ("AddChore", "RemoveChore"):
                expect(owner, method(prefix, name, r"Chore\s+\w+"), f"{owner}.{name}(Chore)")
        expect("Prioritizable", method(r"public\s+void", "SetMasterPriority", r"PrioritySetting\s+\w+"),
               "Prioritizable.SetMasterPriority(PrioritySetting)")
        for pattern, label in (
            (method(r"public\s+void", "AddDirtyCell", r"int\s+\w+"), "NavGrid.AddDirtyCell(int)"),
            (method(r"public\s+void", "UpdateGraph"), "NavGrid.UpdateGraph()"),
            (method(r"public\s+void", "UpdateGraph", r"List<int>\s+\w+"), "NavGrid.UpdateGraph(List<int>)"),
            (r"\bprivate\s+byte\[\]\s+DirtyBitFlags\s*;", "NavGrid.DirtyBitFlags"),
            (r"\bprivate\s+List<int>\s+DirtyCells\s*;", "NavGrid.DirtyCells"),
        ):
            expect("NavGrid", pattern, label)
    except (OSError, subprocess.TimeoutExpired, RuntimeError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1

    if failures:
        print("FAIL: CycleTrim pinned reference API contract drifted", file=sys.stderr)
        for failure in failures:
            print(f"- {failure}", file=sys.stderr)
        return 1

    print("PASS CycleTrim pinned reference API contract (dynamic/reflected Harmony dependencies)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
