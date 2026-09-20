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

BAD_BODY = """
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


def main() -> int:
    verify_set_active_contract(GOOD_BODY)

    try:
        verify_set_active_contract(BAD_BODY)
    except ValueError:
        print("PASS rejects activeResearch reset that exists only on the non-null path")
        return 0

    raise AssertionError(
        "checker accepted a SetActiveResearch body whose null path leaves activeResearch stale"
    )


if __name__ == "__main__":
    raise SystemExit(main())
