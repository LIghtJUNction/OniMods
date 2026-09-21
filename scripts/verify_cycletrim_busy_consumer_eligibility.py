#!/usr/bin/env python3
"""Verify the busy-duplicant throttle fails open for non-duplicant consumers."""

from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[1]
PATCH = ROOT / "mods/CycleTrim/Patches/BusyDuplicantChoreThrottlePatch.cs"


def method_body(source: str, marker: str) -> str:
    start = source.find(marker)
    if start < 0:
        raise ValueError(f"missing method marker: {marker}")
    opening = source.find("{", start)
    if opening < 0:
        raise ValueError(f"missing method body: {marker}")
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1 : index]
    raise ValueError(f"unterminated method body: {marker}")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def main() -> int:
    source = PATCH.read_text(encoding="utf-8")

    pickup = method_body(source, "private static bool TryGetDuplicantConsumer(")
    positive_cache = pickup.find("States.TryGetValue(consumer, out state)")
    negative_cache = pickup.find("NonDuplicants.TryGetValue(consumer, out _)")
    identity_check = pickup.find("consumer.GetComponent<MinionIdentity>()")
    negative_creation = pickup.find(
        "NonDuplicants.GetValue(consumer, NonDuplicantFactory)"
    )
    state_creation = pickup.find("States.GetValue(consumer, StateFactory)")
    require(positive_cache >= 0, "pickup eligibility does not reuse duplicant state")
    require(negative_cache >= 0, "pickup eligibility does not cache non-duplicants")
    require(identity_check >= 0, "pickup eligibility does not check MinionIdentity")
    require(negative_creation >= 0, "non-duplicant classification is not retained")
    require(state_creation >= 0, "pickup eligibility no longer uses the throttle state table")
    require(
        positive_cache < identity_check and negative_cache < identity_check,
        "pickup hot path repeats MinionIdentity classification after a cached result",
    )
    require(
        identity_check < negative_creation and identity_check < state_creation,
        "throttle state can be created before duplicant eligibility is known",
    )

    chore_patch_start = source.find("private static class FindNextChorePatch")
    require(chore_patch_start >= 0, "FindNextChore patch is missing")
    chore_prefix = method_body(
        source[chore_patch_start:],
        "private static bool Prefix(",
    )
    require(
        "!state.IsDuplicant" in chore_prefix,
        "FindNextChore does not fail open for a non-duplicant state",
    )

    print(
        "PASS busy chore throttle caches identity once and remains restricted to duplicants"
    )
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
