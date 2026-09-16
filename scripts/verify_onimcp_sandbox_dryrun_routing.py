#!/usr/bin/env python3
"""Verify the public game_control sandbox dry-run guard is wired before mutation routing."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
GAME_CONTROL = ROOT / "mods/OniMcp/Tools/Impl/Core/GameControlTools.cs"
POLICY = ROOT / "mods/OniMcp/Tools/Impl/Core/SandboxDryRunRoutingPolicy.cs"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(message)


def main() -> int:
    game_control = GAME_CONTROL.read_text(encoding="utf-8")
    policy = POLICY.read_text(encoding="utf-8")

    marker = "private static CallToolResult ForwardSandbox(JObject args)"
    require(marker in game_control, "ForwardSandbox routing method not found")
    forward = game_control.split(marker, 1)[1]
    require(
        "SandboxDryRunRoutingPolicy.TryRejectUnsupported" in forward,
        "game_control sandbox routing must consult the dry-run fail-closed policy",
    )
    require(
        forward.index("SandboxDryRunRoutingPolicy.TryRejectUnsupported")
        < forward.index("return SandboxTools.ControlSandbox().Handler(forwarded);"),
        "dry-run guard must run before sandbox handler dispatch",
    )
    require(
        "仅 kind=map_designate 与 kind=area action=flood_fill 支持只预览" in game_control,
        "public dryRun schema must name the sandbox actions that really support previews",
    )

    require("kind == \"area\" && action == \"flood_fill\"" in policy,
            "flood_fill preview must remain allowed")
    require("kind == \"map_designate\"" in policy,
            "map_designate preview must remain allowed")
    require("kind == \"area\" || kind == \"entity\" || kind == \"entities\"" in policy,
            "unsupported area/entity dry-runs must fail closed")
    require("IsSandboxModeAction(action)" in policy,
            "sandbox mode writes must not silently ignore dryRun")

    print("OniMcp sandbox dry-run routing contract verified")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
