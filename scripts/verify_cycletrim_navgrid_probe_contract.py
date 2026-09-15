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
        "Probe = new NavGridWorkloadProbe();",
        "ReportCallback = ReportDeferred;",
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

    eager_probe = re.search(
        r"private\s+static\s+(?:readonly\s+)?NavGridWorkloadProbe\s+\w+\s*=\s*new\s+NavGridWorkloadProbe",
        patch,
    )
    eager_callback = re.search(
        r"private\s+static\s+(?:readonly\s+)?Action<object>\s+\w+\s*=\s*ReportDeferred",
        patch,
    )
    if eager_probe or eager_callback:
        failures.append("default-off probe must not allocate collector/report state in static field initializers")
    try:
        opt_in_guard = patch.index("if (!IsRequested())")
        target_guard = patch.index("if (targetMethod == null)", opt_in_guard)
        probe_allocation = patch.index("Probe = new NavGridWorkloadProbe();", target_guard)
        callback_allocation = patch.index("ReportCallback = ReportDeferred;", probe_allocation)
        if not (opt_in_guard < target_guard < probe_allocation < callback_allocation):
            failures.append("probe state must be allocated only after opt-in and target resolution")
    except ValueError:
        failures.append("probe lazy-allocation ordering could not be verified")

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
