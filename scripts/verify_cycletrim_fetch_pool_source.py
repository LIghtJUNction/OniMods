#!/usr/bin/env python3
"""Keep FetchPickupCandidatePatch's temporary dictionary reuse allocation-free and bounded."""

from pathlib import Path
import re
import sys


ROOT = Path(__file__).resolve().parents[1]
PATCH = ROOT / "mods/CycleTrim/Patches/FetchPickupCandidatePatch.cs"
POOL = ROOT / "mods/CycleTrim/Core/ThreadLocalObjectPool.cs"


def main() -> int:
    patch = PATCH.read_text(encoding="utf-8")
    pool = POOL.read_text(encoding="utf-8")
    failures = []

    if "ConcurrentStack" in patch:
        failures.append("FetchPickupCandidatePatch must not use ConcurrentStack for per-call reuse")
    if "ThreadLocalObjectPool<" not in patch:
        failures.append("FetchPickupCandidatePatch must use the thread-local reuse pool")
    if patch.count(".Rent();") != 1:
        failures.append("FetchPickupCandidatePatch must rent exactly one candidate dictionary")
    if patch.count(".Return(candidates);") != 1:
        failures.append("FetchPickupCandidatePatch must return exactly one candidate dictionary")
    if not re.search(
        r"finally\s*\{\s*candidates\.Clear\(\);\s*"
        r"ThreadLocalObjectPool<[^;]+>\.Return\(candidates\);\s*\}",
        patch,
        re.DOTALL,
    ):
        failures.append("candidate dictionary must be cleared before it is returned in finally")

    if "[ThreadStatic]" not in pool:
        failures.append("ThreadLocalObjectPool storage must remain thread-local")
    if "private static T item;" not in pool:
        failures.append("ThreadLocalObjectPool must retain a single object per thread")
    if "Stack<T>" in pool or "ConcurrentStack" in pool:
        failures.append("ThreadLocalObjectPool must not retain an unbounded collection per thread")
    if "item = null;" not in pool:
        failures.append("Rent must clear the retained slot before returning an object")
    if not re.search(r"if\s*\(item\s*==\s*null\)\s*\{\s*item\s*=\s*value;\s*\}", pool, re.DOTALL):
        failures.append("Return must keep at most one object in the thread-local slot")

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}", file=sys.stderr)
        return 1

    print("PASS CycleTrim fetch candidate pool source contract")
    return 0


if __name__ == "__main__":
    sys.exit(main())
