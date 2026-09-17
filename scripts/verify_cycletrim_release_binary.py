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


def method_slice(code: str, start_marker: str, end_marker: str) -> str:
    start = code.find(start_marker)
    end = code.find(end_marker, start + len(start_marker))
    if start < 0 or end < 0:
        return ""
    return code[start:end]


def method_body(code: str, signature: str) -> str:
    start = code.find(signature)
    if start < 0:
        return ""
    opening = code.find("{", start + len(signature))
    if opening < 0:
        return ""
    depth = 0
    for index in range(opening, len(code)):
        if code[index] == "{":
            depth += 1
        elif code[index] == "}":
            depth -= 1
            if depth == 0:
                return code[opening + 1 : index]
    return ""


def main() -> int:
    if not ASSEMBLY.is_file():
        print(f"Release assembly not found: {ASSEMBLY}", file=sys.stderr)
        return 1

    async_code = decompile("CycleTrim.Patches.AsyncPathProbeOptimizationPatch")
    busy_code = decompile("CycleTrim.Patches.BusyDuplicantChoreThrottlePatch")
    stationary_code = decompile(
        "CycleTrim.Patches.StationaryCritterNavigationThrottlePatch"
    )
    create_state_code = method_slice(
        busy_code,
        "private static State CreateState",
        "private static bool IsBusyChore",
    )
    duplicant_lookup_code = method_slice(
        busy_code,
        "private static bool TryGetDuplicantConsumer",
        "private static RefreshStamp CaptureStamp",
    )
    prioritize_prefix_code = method_body(
        busy_code,
        "private static void Prefix(Brain brain)",
    )
    stationary_prefix_code = method_body(
        stationary_code,
        "private static bool Prefix(",
    )
    stationary_last_try_get = stationary_prefix_code.rfind("States.TryGetValue")
    stationary_identity_lookup = stationary_prefix_code.find(
        "GetComponent<CreatureBrain>()"
    )
    stationary_state_create = stationary_prefix_code.find(
        "States.GetValue(__instance, StateFactory)"
    )
    priority_cached_consumer = prioritize_prefix_code.find("BrainChoreConsumer")
    priority_fallback_lookup = prioritize_prefix_code.find(
        "GetComponent<ChoreConsumer>()"
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
        "release decompile exposes identity-cache methods": (
            bool(create_state_code) and bool(duplicant_lookup_code)
        ),
        "pickup hot path caches duplicant identity classification": (
            "GetComponent<MinionIdentity>()" in create_state_code
            and "GetComponent<MinionIdentity>()" not in duplicant_lookup_code
            and "IsDuplicant" in duplicant_lookup_code
        ),
        "priority invalidation does not materialize untracked throttle state": (
            bool(prioritize_prefix_code)
            and "States.TryGetValue" in prioritize_prefix_code
            and "States.GetValue" not in prioritize_prefix_code
            and ".Invalidate();" in prioritize_prefix_code
        ),
        "priority invalidation reuses Brain cached chore consumer": (
            bool(prioritize_prefix_code)
            and priority_cached_consumer >= 0
            and priority_fallback_lookup > priority_cached_consumer
            and prioritize_prefix_code.count("GetComponent<ChoreConsumer>()") == 1
            and "GetComponent<MinionIdentity>()" not in prioritize_prefix_code
            and "IsDuplicant" in prioritize_prefix_code
            and ".Invalidate();" in prioritize_prefix_code
        ),
        "stationary probes reuse vanilla cached root cell": (
            "cachedCell" in stationary_code
            and "Grid.PosToCell" not in stationary_code
        ),
        "stationary critter identity lookup is first-hit only": (
            bool(stationary_prefix_code)
            and stationary_prefix_code.count("States.TryGetValue") >= 3
            and stationary_prefix_code.count("GetComponent<CreatureBrain>()") == 1
            and stationary_last_try_get >= 0
            and stationary_identity_lookup > stationary_last_try_get
            and stationary_state_create > stationary_identity_lookup
        ),
    }

    for name, passed in checks.items():
        print(f"{'PASS' if passed else 'FAIL'} {name}")
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
