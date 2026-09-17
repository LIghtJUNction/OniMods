#!/usr/bin/env python3
"""Verify CycleTrim safety guards in the compiled Release assembly."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASSEMBLY = ROOT / "mods/CycleTrim/bin/Release/CycleTrim.dll"


def decompile(type_name: str) -> str:
    ilspycmd = shutil.which("ilspycmd")
    if not ilspycmd:
        raise RuntimeError("ilspycmd is required")
    completed = subprocess.run(
        [ilspycmd, "-t", type_name, str(ASSEMBLY)],
        check=True,
        capture_output=True,
        text=True,
    )
    return completed.stdout


def main() -> int:
    if not ASSEMBLY.is_file():
        print(f"Release assembly not found: {ASSEMBLY}", file=sys.stderr)
        return 1

    async_code = decompile("CycleTrim.Patches.AsyncPathProbeOptimizationPatch")
    busy_code = decompile("CycleTrim.Patches.BusyDuplicantChoreThrottlePatch")
    stationary_code = decompile(
        "CycleTrim.Patches.StationaryCritterNavigationThrottlePatch"
    )
    checks = {
        "async mismatch logs a skip": (
            "Skipping AsyncPathProber.Manager.TickFrame optimization" in async_code
        ),
        "async mismatch returns incoming IL": (
            "if (num != 1)" in async_code and "return list;" in async_code
        ),
        "async mismatch does not throw": (
            "expected exactly one TickFrame queue limit constant" not in async_code
        ),
        "IdleChore is not busy work": (
            "return !(currentChore is IdleChore);" in busy_code
        ),
        "pickup and chore paths use the idle guard": (
            busy_code.count("!IsBusyChore(currentChore)") == 2
        ),
        "busy chore paths avoid navigator component lookups": (
            "Navigator ___navigator" in busy_code
            and "GetComponent<Navigator>()" not in busy_code
        ),
        "busy chore stamps reuse navigator cached root cell": (
            "navigator.cachedCell" in busy_code
            and "Grid.PosToCell(navigator)" not in busy_code
        ),
        "stationary probes reuse vanilla cached root cell": (
            "cachedCell" in stationary_code
            and "Grid.PosToCell" not in stationary_code
        ),
    }

    for name, passed in checks.items():
        print(f"{'PASS' if passed else 'FAIL'} {name}")
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
