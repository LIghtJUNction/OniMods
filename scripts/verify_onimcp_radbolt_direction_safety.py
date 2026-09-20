#!/usr/bin/env python3
"""Lock radbolt direction discovery/preview/write to one eligibility path."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
HANDLERS = ROOT / "mods/OniMcp/Tools/Impl/Facility/OptionControlHandlers.cs"
ROUTER = ROOT / "mods/OniMcp/Tools/Impl/Facility/GenericSideSurfaceTools.cs"


def section(text: str, start: str, end: str) -> str:
    start_at = text.index(start)
    end_at = text.index(end, start_at)
    return text[start_at:end_at]


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise AssertionError(label)


def require_before(text: str, first: str, second: str, label: str) -> None:
    if text.index(first) >= text.index(second):
        raise AssertionError(label)


def main() -> None:
    handlers = HANDLERS.read_text(encoding="utf-8")
    router = ROUTER.read_text(encoding="utf-8")

    setter = section(
        handlers,
        "private static CallToolResult SetRadboltDirectionControl",
        "private static Dictionary<string, object> ControlInfo",
    )
    listing = section(
        handlers,
        "private static Dictionary<string, object> ControlInfo",
        "private static Dictionary<string, object> OptionInfo",
    )

    shared_gate = "TryGetRadboltDirectionControl"
    require(setter, shared_gate, "radbolt setter must use the shared native-eligibility gate")
    require(listing, shared_gate, "radbolt discovery must use the same native-eligibility gate")
    require(setter, 'ToolUtil.GetBool(args, "dryRun", false)',
            "radbolt setter must provide mutation-free dry-run")
    require_before(setter, shared_gate, "control.Direction = direction;",
                   "radbolt eligibility must be checked before Direction mutation")
    require_before(setter, 'ToolUtil.GetBool(args, "dryRun", false)', "control.Direction = direction;",
                   "radbolt dry-run must return before Direction mutation")
    require(router, 'return OptionControlTools.ControlSideOption().Handler(args);',
            "public side-surface option route must stay on the corrected shared handler")

    print("OniMcp radbolt direction safety contract passed")


if __name__ == "__main__":
    main()
