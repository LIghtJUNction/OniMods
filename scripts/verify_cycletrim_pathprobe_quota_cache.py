#!/usr/bin/env python3
"""Keep the AsyncPathProber quota cache allocation-free and scoped to one TickFrame."""

from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[1]
PATCH = ROOT / "mods/CycleTrim/Patches/AsyncPathProbeOptimizationPatch.cs"


def method_body(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise ValueError(f"missing method: {signature}")
    opening = source.find("{", start)
    if opening < 0:
        raise ValueError(f"missing method body: {signature}")
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
    source = PATCH.read_text(encoding="utf-8")
    failures = []

    for needle, message in (
        ("internal int QueueQuotaTick = -1;", "per-manager quota tick cache missing"),
        ("internal int QueueQuota = 1;", "per-manager quota value cache missing"),
        ("States.GetValue(__instance, StateFactory).Tick++;", "TickFrame cache generation missing"),
    ):
        if needle not in source:
            failures.append(message)

    try:
        body = method_body(source, "private static int GetQueueQuota(")
    except ValueError as error:
        failures.append(str(error))
        body = ""

    if body:
        fast_path = body.find("managerState.QueueQuotaTick == tick")
        scan = body.find("foreach (var value in navigators.Values)")
        compute = body.find("PathProbeBackpressure.ComputeQueueQuota(")
        store_value = body.find("managerState.QueueQuota = quota;")
        store_tick = body.find("managerState.QueueQuotaTick = tick;")
        if min(fast_path, scan, compute, store_value, store_tick) < 0:
            failures.append("quota cache fast-path/scan/store sequence is incomplete")
        elif not fast_path < scan < compute < store_value < store_tick:
            failures.append("quota cache must check before scanning and store only after compute")
        if body.count("foreach (var value in navigators.Values)") != 1:
            failures.append("quota computation must contain exactly one navigator scan")
        if "return managerState.QueueQuota;" not in body:
            failures.append("same-tick quota fast path no longer returns cached value")
        if "new " in body:
            failures.append("queue quota hot helper must not allocate managed objects")

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}", file=sys.stderr)
        return 1

    print("PASS: CycleTrim path-probe queue quota is cached once per TickFrame")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
