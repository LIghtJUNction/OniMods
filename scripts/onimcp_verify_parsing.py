"""Small parsing primitives shared by the ONI MCP static verifier."""

from __future__ import annotations

import sys


def fail(message: str) -> None:
    print(f"ERROR: {message}", file=sys.stderr)
    raise SystemExit(1)


def matching_delimiter(text: str, start: int, opening: str, closing: str) -> int:
    depth = 0
    index = start
    state = "normal"
    while index < len(text):
        char = text[index]
        next_char = text[index + 1] if index + 1 < len(text) else ""
        if state == "line_comment":
            if char == "\n":
                state = "normal"
        elif state == "block_comment":
            if char == "*" and next_char == "/":
                state = "normal"
                index += 1
        elif state == "string":
            if char == "\\":
                index += 1
            elif char == '"':
                state = "normal"
        elif state == "verbatim_string":
            if char == '"' and next_char == '"':
                index += 1
            elif char == '"':
                state = "normal"
        elif state == "character":
            if char == "\\":
                index += 1
            elif char == "'":
                state = "normal"
        else:
            if char == "/" and next_char == "/":
                state = "line_comment"
                index += 1
            elif char == "/" and next_char == "*":
                state = "block_comment"
                index += 1
            elif char == "@" and next_char == '"':
                state = "verbatim_string"
                index += 1
            elif char == '"':
                state = "string"
            elif char == "'":
                state = "character"
            elif char == opening:
                depth += 1
            elif char == closing:
                depth -= 1
                if depth == 0:
                    return index
        index += 1
    fail(f"unmatched delimiter {opening!r} at offset {start}")
    return -1
