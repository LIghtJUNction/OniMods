#!/usr/bin/env python3
"""Static contract for duplicant-anchored UI speech bubbles."""

from pathlib import Path
import sys
import traceback


ROOT = Path(__file__).resolve().parents[1]


def main() -> int:
    overlay = (ROOT / "mods/oni_mcp/ToolCallSpeechOverlay.cs").read_text(encoding="utf-8")
    game = (ROOT / "mods/oni_mcp/Tools/Impl/Core/GameControlTools.cs").read_text(encoding="utf-8")
    ui = (ROOT / "mods/oni_mcp/Tools/Impl/Ui/UiControlTools.cs").read_text(encoding="utf-8")
    hint = (ROOT / "mods/oni_mcp/Tools/Impl/Ui/UiHintTools.cs").read_text(encoding="utf-8")
    english = (ROOT / "mods/oni_mcp/Tools/CoreToolEnglishDescriptions.cs").read_text(encoding="utf-8")

    assert "ShowNearPlayerMouse" in overlay
    assert "ShowBubble(message.Trim(), seconds, null)" in overlay
    assert "ShowNearDuplicant" in overlay
    assert "Camera.main.WorldToScreenPoint(head)" in overlay
    assert "followTarget = target != null" in overlay
    assert "Mathf.Max(0.5f, seconds)" in overlay
    assert "PositionBubbleNearTarget();" in overlay
    assert "if (bubbleTarget == null || Camera.main == null)" in overlay
    assert "bool visible = screen.z > 0f" in overlay
    assert "bubbleRoot.SetActive(visible);" in overlay

    for source in (game, ui, hint):
        assert "speech_bubble" in source
        for parameter in ('["name"]', '["id"]', '["text"]', '["duration"]'):
            assert parameter in source, f"missing {parameter} schema in aggregate layer"

    assert "ToolUtil.FindDupe(args)" in hint
    assert "ToolCallSpeechOverlay.ShowNearDuplicant(text, dupe, duration)" in hint
    assert "Mathf.Clamp(ToolUtil.GetFloat(args, \"duration\") ?? 5f, 0.5f, 30f)" in hint
    assert 'case "speech_bubble":\n                            return CreateDuplicantSpeechBubble().Handler(args);' in hint
    speech_tool = hint[hint.index("private static McpTool CreateDuplicantSpeechBubble()") :]
    assert "Hidden = true" in speech_tool
    for field in ('["created"]', '["text"]', '["duration"]', '["dupe"]'):
        assert field in hint, f"missing result field {field}"
    assert "Duplicant not found for the supplied name or id" in hint
    assert "speech_bubble" in english and 'd["duration"]' in english

    print("duplicant speech-bubble contract passed")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except AssertionError as error:
        print(f"duplicant speech-bubble contract FAILED: {error}")
        traceback.print_exc()
        sys.exit(1)
