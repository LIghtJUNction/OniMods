#!/usr/bin/env python3
"""Check that the removed direct-world mutation implementation cannot return silently.

This source check supplements the executable public-router regression. It does
not establish native gameplay or Unity runtime correctness.
"""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MOD = ROOT / "mods/OniMcp"


def main() -> int:
    failures: list[str] = []
    forbidden = (
        "SandboxTools.",
        "class SandboxTools",
        "SandboxDryRunRoutingPolicy",
        "game_sandbox_mode_set",
        "SandboxModeActive =",
        "TryBuildVirtualFileInstantBuild",
        "TryCompleteExistingVirtualFileBlueprint",
        "CanBypassUtilityResearch",
        "IsFreeBuildContext",
        "def.Build(",
    )
    for path in MOD.rglob("*.cs"):
        text = path.read_text(encoding="utf-8")
        for marker in forbidden:
            if marker in text:
                failures.append(f"{path.relative_to(ROOT)}: removed implementation {marker!r}")
    sandbox = MOD / "Tools/Impl/Sandbox"
    if sandbox.exists() and any(sandbox.glob("*.cs")):
        failures.append("The direct-world mutation implementation directory must be removed")
    project = (ROOT / "tests/OniMcp.Tools.Tests/OniMcp.Tools.Tests.csproj").read_text(encoding="utf-8")
    if "Sandbox" in project:
        failures.append("Host tests still compile a removed sandbox implementation")
    if failures:
        raise SystemExit("\n".join(failures))
    print("OniMcp survival-only implementation contract verified")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
