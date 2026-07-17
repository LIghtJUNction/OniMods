#!/usr/bin/env python3
"""Verify large integer PLib options use compact string fields, not sliders."""

import re
from pathlib import Path


def fail(message: str) -> None:
    raise SystemExit("ERROR: " + message)


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        fail(f"missing {label}: {needle}")


def property_prefix(source: str, property_name: str) -> str:
    marker = f"public int {property_name}"
    if marker not in source:
        fail(f"missing integer JSON property: {property_name}")
    return source[:source.index(marker)].rsplit("\n\n", 1)[-1]


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    path = root / "mods" / "oni_mcp" / "Config" / "OniMcpOptions.cs"
    source = path.read_text(encoding="utf-8")
    expected = {
        "Port": ("8788", "1024", "65535"),
        "ScreenshotRetentionMinutes": ("120", "1", "10080"),
        "ScreenshotMaxFiles": ("40", "1", "1000"),
    }
    for name, (default, minimum, maximum) in expected.items():
        require(source, f"public int {name} {{ get; set; }} = {default};", f"{name} JSON type/default")
        prefix = property_prefix(source, name)
        if "[Option(" in prefix or "[Limit(" in prefix:
            fail(f"{name} must not be rendered as an IntOptionsEntry slider")
        proxy = name + "Input"
        require(source, f"public string {proxy}", f"{name} string UI proxy")
        require(source, "[JsonIgnore]", "UI proxy JSON exclusion")
        require(source, f"ParseCompactInt(value, {name}, {minimum}, {maximum})", f"{name} validated proxy setter")
    require(source, "private static int ParseCompactInt", "shared compact integer parser")
    require(source, "return current;", "invalid text preserves current value")
    require(source, "NumberStyles.Integer", "strict integer parsing")
    require(source, "options.Port = 8788;", "port sanitize fallback")
    require(source, "options.ScreenshotRetentionMinutes = Clamp", "retention sanitize fallback")
    require(source, "options.ScreenshotMaxFiles = Clamp", "max-files sanitize fallback")
    require(source, "public string AuthToken", "token option remains unchanged")
    if re.search(r"\[Limit\([^\]]+\)\]\s*public int (Port|ScreenshotRetentionMinutes|ScreenshotMaxFiles)", source):
        fail("large integer options still have slider-producing Limit attributes")
    print("OK: compact validated text inputs for large integer PLib options")


if __name__ == "__main__":
    main()
