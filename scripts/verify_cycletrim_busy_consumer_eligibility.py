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
    identity_check = pickup.find("consumer.GetComponent<MinionIdentity>()")
    state_creation = pickup.find("States.GetValue(consumer, StateFactory)")
    require(identity_check >= 0, "pickup eligibility does not check MinionIdentity")
    require(state_creation >= 0, "pickup eligibility no longer uses the shared state table")
    require(
        identity_check < state_creation,
        "non-duplicant consumers can be inserted into the throttle state table",
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

    print("PASS busy chore throttle eligibility is restricted to duplicants")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
