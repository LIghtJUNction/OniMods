#!/usr/bin/env python3
"""Check public protocol examples against the supported session transport."""

from pathlib import Path
import re
import sys


ROOT = Path(__file__).resolve().parents[1]
PROTOCOL_SOURCE = ROOT / "mods/OniMcp/Server/McpHttpServerProtocolTasks.cs"
DOCUMENTS = (
    ROOT / "docs/api-developer-guide.md",
    ROOT / "docs/mcp-tools-reference.md",
    ROOT / "mods/OniMcp/README.md",
    ROOT / "mods/OniMcp/README_EN.md",
)


def validate_document(text: str, version: str) -> None:
    for required in (version, "initialize", "Mcp-Session-Id"):
        if required not in text:
            raise ValueError(f"missing supported session guidance: {required}")

    for example in re.findall(r"```[^\n]*\n(.*?)```", text, re.DOTALL):
        versions = re.findall(
            r"\bMcp-Protocol-Version\s*:\s*(\d{4}-\d{2}-\d{2})", example, re.IGNORECASE
        )
        versions += re.findall(
            r'"(?:io\.modelcontextprotocol/)?protocolVersion"\s*:\s*"([^"]+)"', example
        )
        if any(value != version for value in versions):
            raise ValueError(f"example uses an unsupported protocol: {versions}")
        if re.search(r'"method"\s*:\s*"server/discover"', example):
            raise ValueError("example invokes retired server/discover")


def main() -> int:
    try:
        source = PROTOCOL_SOURCE.read_text(encoding="utf-8")
        version = re.search(r'CurrentProtocolVersion\s*=\s*"([^"]+)"', source)
        if version is None:
            raise ValueError("supported protocol version is missing from the server")
        for path in DOCUMENTS:
            try:
                validate_document(path.read_text(encoding="utf-8"), version.group(1))
            except ValueError as error:
                raise ValueError(f"{path.relative_to(ROOT)}: {error}") from error
    except (OSError, ValueError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    print(f"OK: public examples use the supported {version.group(1)} session protocol")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
