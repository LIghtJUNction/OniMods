#!/usr/bin/env python3
"""Build the opt-in texture probe with a source-bound receipt."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent))
from probe_build import ProbeBuild

PROBE = ProbeBuild("Texture", ROOT, ('TextureProbe.cs',))
verify_receipt = PROBE.verify_receipt


if __name__ == "__main__":
    PROBE.main()
