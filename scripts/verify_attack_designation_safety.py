#!/usr/bin/env python3
"""Verify OniMcp attack writes respect ONI targetability and resulting state."""

from pathlib import Path

from onimcp_verify_parsing import fail, matching_delimiter


def extract_block(text: str, marker: str) -> str:
    marker_index = text.find(marker)
    if marker_index < 0:
        fail(f"required marker not found: {marker}")
    start = text.find("{", marker_index)
    if start < 0:
        fail(f"opening brace not found after marker: {marker}")
    end = matching_delimiter(text, start, "{", "}")
    return text[start + 1 : end]


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    path = root / "mods/OniMcp/Tools/Impl/Orders/OrdersCombatTools.cs"
    source = path.read_text(encoding="utf-8")
    attack = extract_block(source, "public static McpTool Attack()")
    handler = extract_block(attack, "Handler = args =>")

    set_call = handler.find("target.SetPlayerTargeted(mark);")
    preflight = handler.find("AttackDesignationPolicy.CanAttemptMark")
    postcheck = handler.find("AttackDesignationPolicy.MarkAccepted")
    priority = handler.find("ApplyPriority(go, args);", set_call)
    changed = handler.find("changed++;", set_call)

    if set_call < 0:
        fail("attack handler no longer calls FactionAlignment.SetPlayerTargeted")
    if preflight < 0 or preflight > set_call:
        fail("attack mark must reject non-targetable/inactive targets before SetPlayerTargeted")
    if postcheck < set_call:
        fail("attack mark must verify IsPlayerTargeted after SetPlayerTargeted")
    if priority < 0 or postcheck > priority:
        fail("attack priority must not change before the game accepts the mark")
    if changed < 0 or postcheck > changed:
        fail("attack changed count must not advance before the game accepts the mark")

    preflight_block = extract_block(
        handler,
        "if (mark && !AttackDesignationPolicy.CanAttemptMark",
    )
    if "AttackDesignationPolicy.NotTargetableStatus" not in preflight_block:
        fail("preflight targetability rejection must return the stable skipped status")
    if "skipped++;" not in preflight_block or "continue;" not in preflight_block:
        fail("preflight targetability rejection must be counted and stop before mutation")

    postcheck_block = extract_block(
        handler,
        "if (mark && !AttackDesignationPolicy.MarkAccepted",
    )
    if "target.SetPlayerTargeted(false);" not in postcheck_block:
        fail("a rejected mark must clean up any player-targeted side effects")
    if "AttackDesignationPolicy.GameRejectedStatus" not in postcheck_block:
        fail("post-call rejection must return the stable game-rejected status")
    if "skipped++;" not in postcheck_block or "continue;" not in postcheck_block:
        fail("post-call rejection must be counted and stop before priority/success accounting")

    print("OniMcp attack designation safety contract verified")


if __name__ == "__main__":
    main()
