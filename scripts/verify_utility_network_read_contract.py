#!/usr/bin/env python3
"""Guard native utility observation; this is not runtime acceptance."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / "mods/OniMcp/Tools/Impl/Build"


def main() -> None:
    text = (BUILD / "BuildPlanningUtilityPathReadiness.cs").read_text(encoding="utf-8")
    for forbidden in ("ForceRebuildNetworks", "AddToNetworks", "UpdateConnections", "ClearCell", "disconnectable.Connect", "GetMethod("):
        assert forbidden not in text, forbidden
    for required in ("IsCompletedUtilityPath", "BuildingComplete", "ReferenceEquals", "TryExpectedConnectionBits", "is_physical_building: true"):
        assert required in text, required
    for name in ("BuildPlanningNativeUtilityPath.cs", "BuildPlanningActionPlacement.cs"):
        caller = (BUILD / name).read_text(encoding="utf-8")
        assert "ValidateCompletedUtilityPathNetwork" in caller
        assert '"networkConnected"' in caller and '"requiresVerification"' in caller
    print("PASS: physical utility read-only validation; pending plans do not claim a connected network")


if __name__ == "__main__":
    main()
