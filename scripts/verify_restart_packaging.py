#!/usr/bin/env python3
"""Verify the relay asset at the exact runtime paths emitted by a build."""

from pathlib import Path
import sys
import zipfile


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "mods/oni_mcp/assets/restart_oni_steam_relay.sh"
BUILD = ROOT / "mods/oni_mcp/bin/Debug/assets/restart_oni_steam_relay.sh"
DIST = ROOT / "dist/OniMcp/assets/restart_oni_steam_relay.sh"
ARCHIVE = ROOT / "dist/OniMcp.zip"
MEMBER = "assets/restart_oni_steam_relay.sh"


def main() -> int:
    expected = SOURCE.read_bytes()
    for label, path in (("build", BUILD), ("dist", DIST)):
        if not path.is_file():
            raise AssertionError(f"missing {label} relay at runtime path: {path}")
        if path.read_bytes() != expected:
            raise AssertionError(f"{label} relay differs from source: {path}")

    if not ARCHIVE.is_file():
        raise AssertionError(f"missing package: {ARCHIVE}")
    with zipfile.ZipFile(ARCHIVE) as archive:
        names = archive.namelist()
        if MEMBER not in names:
            raise AssertionError(f"missing zip runtime member {MEMBER}; members={names}")
        if archive.read(MEMBER) != expected:
            raise AssertionError(f"zip runtime member differs from source: {MEMBER}")

    print("restart relay packaging verification passed")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except AssertionError as error:
        print(f"restart relay packaging verification FAILED: {error}")
        sys.exit(1)
