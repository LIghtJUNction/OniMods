#!/usr/bin/env python3
"""Guard CycleTrim perf captures against unreported third-party Harmony ownership."""

import copy
import importlib.util
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ANALYZER = ROOT / "scripts/analyze_cycletrim_perf_probe.py"
PATCH = ROOT / "mods/CycleTrim/Patches/PerformanceProbePatch.cs"


def load_analyzer():
    spec = importlib.util.spec_from_file_location("cycletrim_perf_probe_analyzer", ANALYZER)
    if spec is None or spec.loader is None:
        raise RuntimeError("unable to load CycleTrim performance probe analyzer")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def make_target(name: str) -> dict:
    return {
        "name": name,
        "thread": "worker" if "WorkOrder" in name else "main",
        "resolved": True,
        "fastTrackPatched": False,
        "externalPatched": False,
        "patchOwners": ["CycleTrim.Tests"],
        "calls": 3,
        "totalTicks": 45,
        "meanTicks": 15.0,
        "maxTicks": 20,
        "intervalCalls": 3,
        "intervalTotalTicks": 45,
        "intervalMeanTicks": 15.0,
    }


def make_report(analyzer, sequence: int = 1) -> dict:
    return {
        "stopwatchFrequency": 10_000_000,
        "captureGeneration": 1,
        "reportSequence": sequence,
        "intervalDurationTicks": 2_000_000,
        "gc": {
            "gen0Delta": 0,
            "gen1Delta": 0,
            "gen2Delta": 0,
            "heapBytes": 123456,
            "heapDeltaBytes": 0,
        },
        "targets": [make_target(name) for name in analyzer.DEFAULT_REQUIRED],
    }


def main() -> int:
    analyzer = load_analyzer()
    patch = PATCH.read_text(encoding="utf-8")
    failures: list[str] = []

    for needle, message in (
        ("externalPatched", "probe report does not expose generic external Harmony ownership"),
        ("patchOwners", "probe report does not expose Harmony owner IDs"),
        ("patch.owner", "probe does not inspect Harmony owner IDs"),
        ("CycleTrimNamespacePrefix", "probe cannot distinguish its own patches from external patches"),
    ):
        if needle not in patch:
            failures.append(message)

    if '"--reject-external-patches"' not in ANALYZER.read_text(encoding="utf-8"):
        failures.append("analyzer has no fail-closed generic Harmony ownership mode")

    clean = make_report(analyzer)
    try:
        strict_failures = analyzer.validate(clean, analyzer.DEFAULT_REQUIRED, reject_external=True)
    except TypeError:
        failures.append("analyzer validate() cannot reject generic external Harmony ownership")
    else:
        if strict_failures:
            failures.append("strict ownership validation rejected a clean CycleTrim-only report")

    external = copy.deepcopy(clean)
    external["targets"][0]["externalPatched"] = True
    external["targets"][0]["patchOwners"] = ["CycleTrim.Tests", "Example.ThirdParty"]
    default_failures = analyzer.validate(external, analyzer.DEFAULT_REQUIRED)
    if any("external Harmony" in failure for failure in default_failures):
        failures.append("default analyzer mode must keep external ownership informational")
    try:
        strict_failures = analyzer.validate(
            external,
            analyzer.DEFAULT_REQUIRED,
            reject_external=True,
        )
    except TypeError:
        pass
    else:
        if not any("external Harmony" in failure for failure in strict_failures):
            failures.append("strict analyzer mode accepted a third-party-patched target")

    malformed = copy.deepcopy(clean)
    malformed["targets"][0]["patchOwners"] = ["CycleTrim.Tests", 42]
    malformed_failures = analyzer.validate(malformed, analyzer.DEFAULT_REQUIRED)
    if not any("patchOwners" in failure for failure in malformed_failures):
        failures.append("analyzer accepted malformed Harmony owner metadata")

    drifted = copy.deepcopy(clean)
    drifted["reportSequence"] = 2
    drifted["targets"][0]["externalPatched"] = True
    drifted["targets"][0]["patchOwners"] = ["CycleTrim.Tests", "Example.ThirdParty"]
    series_failures = analyzer.validate_series([clean, drifted], analyzer.DEFAULT_REQUIRED)
    if not any("Harmony ownership changed within one capture" in failure for failure in series_failures):
        failures.append("series analyzer accepted mid-capture generic Harmony ownership drift")

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}", file=sys.stderr)
        return 1

    print("PASS CycleTrim perf probe reports generic Harmony ownership")
    return 0


if __name__ == "__main__":
    sys.exit(main())
