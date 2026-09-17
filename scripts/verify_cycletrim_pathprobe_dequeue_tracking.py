#!/usr/bin/env python3
"""Verify path-probe dequeues do not allocate tracking state for untracked navigators."""

from pathlib import Path
import re
import sys


ROOT = Path(__file__).resolve().parents[1]
PATCH = ROOT / "mods/CycleTrim/Patches/AsyncPathProbeOptimizationPatch.cs"


def body_between(source: str, start: str, end: str) -> str:
    begin = source.find(start)
    if begin < 0:
        raise AssertionError(f"missing section: {start}")
    finish = source.find(end, begin + len(start))
    if finish < 0:
        raise AssertionError(f"missing section terminator: {end}")
    return source[begin:finish]


def main() -> int:
    source = PATCH.read_text(encoding="utf-8")

    helper = body_between(
        source,
        "private static bool TryGetNavigatorState(",
        "private static void ResetNavigatorState(",
    )
    for needle, message in (
        ("States.TryGetValue(manager, out managerState)", "manager state lookup must be non-creating"),
        (
            "managerState.Navigators.TryGetValue(navigator, out state)",
            "navigator dequeue state lookup must be non-creating",
        ),
        (
            "SynchronizeNavigatorTick(managerState, state);",
            "tracked dequeue must preserve existing tick synchronization",
        ),
    ):
        if needle not in helper:
            raise AssertionError(message)
    if "GetOrCreateValue" in helper or "States.GetValue" in helper:
        raise AssertionError("non-creating dequeue lookup regressed to allocating state")

    next_task = body_between(
        source,
        "private static class NextTaskPatch",
        "[HarmonyPatch(typeof(Navigator), \"TakeResult\")]",
    )
    if "TryGetNavigatorState(__instance, order.navigator, out state)" not in next_task:
        raise AssertionError("NextTask dequeue path does not use non-creating state lookup")
    if re.search(r"\bGetNavigatorState\s*\(", next_task):
        raise AssertionError("NextTask dequeue path still uses creating state lookup")
    if "admission.MarkDequeued();" not in next_task:
        raise AssertionError("tracked dequeue transition is missing")

    make_work_order = body_between(
        source,
        "private static class MakeWorkOrderPatch",
        "[HarmonyPatch(typeof(AsyncPathProber.Manager), \"NextTask\")]",
    )
    if "var state = GetNavigatorState(__instance, nav);" not in make_work_order:
        raise AssertionError("supported makeWorkOrder path no longer creates tracking state")
    if "state.Admission.TryAdmit(stamp, supported: true)" not in make_work_order:
        raise AssertionError("supported makeWorkOrder admission tracking is missing")

    print("CycleTrim path-probe dequeue tracking contract passed")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except AssertionError as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
