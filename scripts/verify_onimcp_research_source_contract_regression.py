#!/usr/bin/env python3
"""Regression coverage for the Research source-contract checker."""

from verify_onimcp_research_source_contract import verify_set_active_contract


GOOD_BODY = """
if (clearQueue)
{
    this.queuedTech.Clear();
}
this.activeResearch = null;
if (tech != null)
{
    this.activeResearch = tech;
}
else
{
    this.queuedTech.Clear();
}
this.NotifyResearchCenters(GameHashes.ActiveResearchChanged, this.queuedTech);
"""

BAD_NON_NULL_BODY = """
if (clearQueue)
{
    this.queuedTech.Clear();
}
if (tech != null)
{
    this.activeResearch = null;
    this.activeResearch = tech;
}
else
{
    this.queuedTech.Clear();
}
this.NotifyResearchCenters(GameHashes.ActiveResearchChanged, this.queuedTech);
"""

BAD_CLEAR_QUEUE_BODY = """
if (clearQueue)
{
    this.queuedTech.Clear();
    this.activeResearch = null;
}
if (tech != null)
{
    this.activeResearch = tech;
}
else
{
    this.queuedTech.Clear();
}
this.NotifyResearchCenters(GameHashes.ActiveResearchChanged, this.queuedTech);
"""


def require_rejected(body: str, scenario: str) -> None:
    try:
        verify_set_active_contract(body)
    except ValueError:
        print(f"PASS rejects {scenario}")
        return
    raise AssertionError(f"checker accepted {scenario}")


def main() -> int:
    verify_set_active_contract(GOOD_BODY)
    require_rejected(
        BAD_NON_NULL_BODY,
        "activeResearch reset that exists only on the non-null path",
    )
    require_rejected(
        BAD_CLEAR_QUEUE_BODY,
        "activeResearch reset that exists only when clearQueue=true",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
