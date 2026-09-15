#!/usr/bin/env python3
"""Keep public OniMcp protocol docs aligned with the implemented dual-era transport."""

from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
API_GUIDE = ROOT / "docs/api-developer-guide.md"
TOOLS_REFERENCE = ROOT / "docs/mcp-tools-reference.md"
README = ROOT / "mods/OniMcp/README.md"


def fail(message: str) -> None:
    print(f"FAIL: {message}", file=sys.stderr)
    raise SystemExit(1)


def require(text: str, token: str, label: str) -> None:
    if token not in text:
        fail(f"{label} is missing {token!r}")


def main() -> int:
    api = API_GUIDE.read_text(encoding="utf-8")
    tools = TOOLS_REFERENCE.read_text(encoding="utf-8")
    readme = README.read_text(encoding="utf-8")

    for label, text in (
        ("api developer guide", api),
        ("MCP tools reference", tools),
        ("OniMcp README", readme),
    ):
        require(text, "2026-07-28", label)
        require(text, "server/discover", label)
        require(text, "2025-11-25", label)
        require(text, "initialize", label)
        require(text, "Mcp-Session-Id", label)

    for label, text in (("api developer guide", api), ("MCP tools reference", tools)):
        require(text, "Mcp-Method", label)
        require(text, "Mcp-Name", label)
        require(text, "_meta", label)
        require(text, "benchmark", label)

    if "第一个请求必须是 `initialize`" in api:
        fail("api developer guide still claims every client must initialize")
    if "非 `initialize` 请求必须携带会话协商后的 `Mcp-Session-Id`" in tools:
        fail("MCP tools reference still applies legacy session requirements to modern requests")

    modern_no_session_phrases = (
        "不创建、要求或返回 `Mcp-Session-Id`",
        "不要求或返回 `Mcp-Session-Id`",
        "不发送 `Mcp-Session-Id`",
    )
    if not any(phrase in api for phrase in modern_no_session_phrases):
        fail("api developer guide does not state that modern requests are sessionless")
    if not any(phrase in tools for phrase in modern_no_session_phrases):
        fail("MCP tools reference does not state that modern requests are sessionless")

    require(api, "只读 `benchmark`", "api developer guide")
    require(tools, "Modern `2026-07-28` `tools/list`: 当前只广告只读 `benchmark`", "MCP tools reference")

    print("OK: public OniMcp docs describe the 2026 stateless path and 2025 legacy fallback")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
