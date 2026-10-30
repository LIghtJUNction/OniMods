#!/usr/bin/env python3
"""Keep both FetchPickupCandidatePatch layouts pooled and cleared on every exit."""

from pathlib import Path
import re
import sys


ROOT = Path(__file__).resolve().parents[1]
PATCH = ROOT / "mods/CycleTrim/Patches/FetchPickupCandidatePatch.cs"
POOL = ROOT / "mods/CycleTrim/Core/ThreadLocalObjectPool.cs"
BUFFER = ROOT / "mods/CycleTrim/Core/FetchCandidateBuffer.cs"


def main() -> int:
    patch = PATCH.read_text(encoding="utf-8")
    pool = POOL.read_text(encoding="utf-8")
    buffer = BUFFER.read_text(encoding="utf-8")
    failures = []

    if "ConcurrentStack" in patch:
        failures.append("FetchPickupCandidatePatch must not use ConcurrentStack for per-call reuse")
    if "ThreadLocalObjectPool<" not in patch:
        failures.append("FetchPickupCandidatePatch must use the thread-local reuse pool")
    if patch.count(".Rent();") != 2:
        failures.append("each mutually exclusive candidate layout must have one rental")
    if patch.count(".Return(candidates);") != 2:
        failures.append("each candidate layout must return its rental")
    if len(re.findall(
        r"finally\s*\{\s*candidates\.Clear\(\);\s*"
        r"ThreadLocalObjectPool<[^;]+>\.Return\(candidates\);\s*\}",
        patch,
        re.DOTALL,
    )) != 2:
        failures.append("both layouts must clear their buffer before returning it in finally")
    if patch.count("FetchLayoutExperiment.Compact") != 1:
        failures.append("layout selection must be captured once per invocation")
    if not re.search(r"if\s*\(compact\)\s*return\s+UpdateCompact\(", patch):
        failures.append("layout dispatch must occur before the established dictionary loop")
    if "Func<" in buffer or "Func<" in patch:
        failures.append("candidate comparison must not add a per-item delegate")
    if not re.search(r"private readonly List<TValue> values", buffer):
        failures.append("compact candidates must stay private until gathering finishes")
    if "indices.Clear();" not in buffer or "values.Clear();" not in buffer:
        failures.append("compact cleanup must release both keys and candidate references")

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
