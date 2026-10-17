#!/usr/bin/env python3
"""Run the executable onim setup regression for platform-specific ONI Managed paths."""

from __future__ import annotations

from pathlib import Path
import shutil
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]


def main() -> int:
    cargo = shutil.which("cargo")
    if cargo is None:
        print("FAIL: cargo was not found; cannot execute the onim setup regression", file=sys.stderr)
        return 1

    result = subprocess.run(
        [cargo, "test", "--locked", "setup::tests::", "--", "--nocapture"],
        cwd=ROOT,
        check=False,
    )
    if result.returncode != 0:
        return result.returncode

    print("PASS: onim setup uses the expected platform-specific ONI Managed paths")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
