#!/usr/bin/env python3
"""Build the opt-in memory probe with a source-bound receipt."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent))
from probe_build import ProbeBuild

PROBE = ProbeBuild("Memory", ROOT, ('MemoryProbe.cs', 'MemoryRequest.cs', 'MemoryRequestTests.cs'))
verify_receipt = PROBE.verify_receipt


if __name__ == "__main__":
    PROBE.main()
