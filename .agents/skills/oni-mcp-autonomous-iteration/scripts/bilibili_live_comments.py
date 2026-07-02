#!/usr/bin/env python3
import runpy
from pathlib import Path

script = (
    Path(__file__).resolve().parents[2]
    / "bilibili-live-comments"
    / "scripts"
    / "bilibili_live_comments.py"
)
runpy.run_path(str(script), run_name="__main__")
