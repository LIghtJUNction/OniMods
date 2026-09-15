#!/usr/bin/env python3
"""Verify the developer-only CycleTrim NavGrid workload probe stays observational."""

from pathlib import Path
import re
import sys


ROOT = Path(__file__).resolve().parents[1]
PATCH = ROOT / "mods/CycleTrim/Patches/NavGridWorkloadProbePatch.cs"
CORE = ROOT / "mods/CycleTrim/Core/NavGridWorkloadProbe.cs"


def main() -> int:
    patch = PATCH.read_text(encoding="utf-8")
    core = CORE.read_text(encoding="utf-8")
    failures = []

    required_patch_fragments = (
        '"CYCLETRIM_NAVGRID_PROBE"',
        '"PeterHan.FastTrack.PathPatches.NavGrid_UpdateGraph_Patch"',
        'AccessTools.Method(typeof(NavGrid), "UpdateGraph", Type.EmptyTypes)',
        "Harmony.GetPatchInfo(targetMethod)",
        "List<int> ___DirtyCells",
        "__instance.updateRangeX",
        "__instance.updateRangeY",
        "GameScheduler.Instance",
        "ScheduleNextFrame(ReportName, ReportCallback)",
        "Probe.FormatSummary(maxBuckets: 12)",
    )
    for fragment in required_patch_fragments:
        if fragment not in patch:
            failures.append(f"runtime probe contract missing: {fragment}")

    if not re.search(
        r"private\s+static\s+void\s+Prefix\s*\(",
        patch,
    ):
        failures.append("NavGrid workload probe Prefix must remain observational void")
    if "ref List<int> ___DirtyCells" in patch or "out List<int> ___DirtyCells" in patch:
        failures.append("NavGrid workload probe must not replace the DirtyCells list")
    if "return false;" not in patch:
        failures.append("probe opt-in Prepare guard is missing")
    if "GetEnvironmentVariable(EnvironmentVariable)" not in patch:
        failures.append("probe no longer has an explicit opt-in environment guard")
    if "UnityEngine" in core or "Harmony" in core or "NavGrid" not in core:
        failures.append("aggregate collector must remain independent of Unity/Harmony runtime APIs")
    record_body = core.split("internal void Record", 1)[1].split(
        "internal long GetBucketCount", 1
    )[0]
    if "new " in record_body:
        failures.append("Record hot path must not allocate objects")

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}", file=sys.stderr)
        return 1

    print("PASS CycleTrim NavGrid workload probe remains opt-in and observational")
    return 0


if __name__ == "__main__":
    sys.exit(main())
