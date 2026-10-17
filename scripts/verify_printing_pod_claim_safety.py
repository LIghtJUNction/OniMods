#!/usr/bin/env python3
"""Lock the printing-pod care-package claim to the native one-shot lifecycle."""

from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "mods/OniMcp/Tools/Impl/Facility/FacilityPrintingPodRewardTools.cs"


def fail(message: str) -> None:
    raise AssertionError(message)


def method_body(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        fail(f"missing method signature: {signature}")
    brace = source.find("{", start)
    if brace < 0:
        fail(f"missing method body: {signature}")

    depth = 0
    for index in range(brace, len(source)):
        char = source[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:index]
    fail(f"unterminated method body: {signature}")
    return ""


def require_in_order(body: str, markers: list[str], label: str) -> None:
    cursor = -1
    for marker in markers:
        position = body.find(marker, cursor + 1)
        if position < 0:
            fail(f"{label}: missing ordered step {marker!r}")
        cursor = position


def main() -> int:
    source = SOURCE.read_text(encoding="utf-8")
    claim = method_body(source, "private static CallToolResult ClaimPrintingReward(")

    if "Immigration.Instance.EndImmigration()" in claim:
        fail("ClaimPrintingReward must not call EndImmigration after Telepad.OnAcceptDelivery; the native delivery already ends immigration")

    require_in_order(
        claim,
        [
            "!Immigration.Instance.ImmigrantsAvailable",
            "CurrentCarePackages().ToList()",
            "TryGetImmigrantScreenContainers(out screenContainers)",
            "telepad.OnAcceptDelivery(selected)",
            "CloseImmigrantScreen()",
            "PrintingPodClaimCleanup.Clear(screenContainers",
        ],
        "ClaimPrintingReward",
    )

    if claim.count("telepad.OnAcceptDelivery(selected)") != 1:
        fail("ClaimPrintingReward must invoke the native delivery exactly once")

    current = method_body(source, "private static IEnumerable<CarePackageInfo> CurrentCarePackages()")
    require_in_order(
        current,
        [
            "Immigration.Instance",
            "!immigration.ImmigrantsAvailable",
            "typeof(CarePackageContainer).GetField",
        ],
        "CurrentCarePackages",
    )

    preflight = method_body(source, "private static bool TryGetImmigrantScreenContainers(")
    for marker in (
        "ImmigrantScreen.instance",
        "typeof(CharacterSelectionController).GetField",
        "BindingFlags.Instance | BindingFlags.NonPublic",
        "List<ITelepadDeliverableContainer>",
    ):
        if marker not in preflight:
            fail(f"TryGetImmigrantScreenContainers: missing native-screen preflight {marker!r}")

    print("Printing-pod claim safety source contract passed")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except AssertionError as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
