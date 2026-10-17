#!/usr/bin/env python3
"""Verify research queue clear mutates game-owned state, not a copied queue."""

from pathlib import Path
import re
import sys


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "mods/OniMcp/Tools/Impl/Management/ResearchTools.cs"


def method_body(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise AssertionError(f"missing method: {signature}")
    brace = source.find("{", start)
    if brace < 0:
        raise AssertionError(f"missing method body: {signature}")
    depth = 0
    for index in range(brace, len(source)):
        char = source[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:index]
    raise AssertionError(f"unterminated method body: {signature}")


class FakeResearch:
    """Minimal model of the pinned Research queue ownership contract."""

    def __init__(self, queue):
        self._queue = list(queue)
        self.active = self._queue[0] if self._queue else None

    def get_research_queue(self):
        # ONI 744825 source contract returns a defensive copy.
        return list(self._queue)

    def get_target_research(self):
        return self._queue[-1] if self._queue else None

    def set_active_research(self, tech, clear_queue=False):
        if clear_queue:
            self._queue.clear()
        self.active = None
        if tech is not None:
            if not self._queue:
                self._queue.append(tech)
            self.active = self._queue[0]
        else:
            self._queue.clear()


def apply_detected_clear_strategy(body: str, research: FakeResearch) -> int:
    queue_before = research.get_research_queue()
    cleared = len(queue_before)

    if re.search(r"Research\.Instance\.SetActiveResearch\(\s*null\s*,\s*false\s*\)\s*;", body):
        research.set_active_research(None, False)
    elif re.search(r"\bqueue\.Clear\(\)\s*;", body):
        queue_before.clear()
    else:
        raise AssertionError("ClearResearch has no recognized game-queue mutation strategy")

    return cleared


def assert_clear_postconditions(body: str, initial_queue):
    research = FakeResearch(initial_queue)
    target_before = research.get_target_research()
    cleared = apply_detected_clear_strategy(body, research)
    queue_after = research.get_research_queue()

    assert cleared == len(initial_queue), (cleared, initial_queue)
    assert queue_after == [], (
        "research clear must empty the game-owned queue, not only a returned copy",
        target_before,
        queue_after,
    )
    assert research.active is None, (
        "research clear must reset active research through the game mutation API",
        research.active,
    )
    assert research.get_target_research() is None, (
        "research clear must reset target research",
        research.get_target_research(),
    )


def main() -> int:
    source = SOURCE.read_text(encoding="utf-8")
    clear_body = method_body(source, "public static McpTool ClearResearch()")
    control_body = method_body(source, "public static McpTool ControlResearch()")

    confirm_index = clear_body.find('ToolUtil.GetBool(args, "confirm", false)')
    mutation_candidates = [
        index
        for index in (
            clear_body.find("queue.Clear();"),
            clear_body.find("Research.Instance.SetActiveResearch(null, false);"),
        )
        if index >= 0
    ]
    assert confirm_index >= 0, "research clear lost its confirm=true gate"
    assert mutation_candidates and confirm_index < min(mutation_candidates), (
        "research mutation must remain after the confirm=true gate"
    )

    assert 'if (action == "clear")' in control_body, (
        "research_control no longer exposes the clear route"
    )
    assert "return ClearResearch().Handler(args);" in control_body, (
        "research_control clear route must use the shared handler"
    )

    assert_clear_postconditions(clear_body, ["BasicFarming", "AdvancedResearch"])
    assert_clear_postconditions(clear_body, [])

    print("OniMcp research clear contract: PASS")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except AssertionError as error:
        print(f"OniMcp research clear contract: FAIL: {error}", file=sys.stderr)
        sys.exit(1)
