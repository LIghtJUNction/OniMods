#!/usr/bin/env python3
"""Check the actual Git change range; never substitute a source-text scan."""

import json
import os
from pathlib import Path
import re
import subprocess
import sys


ZERO_SHA = "0" * 40


def git(repo: Path, *args: str, input_text: str | None = None) -> str:
    result = subprocess.run(
        ["git", *args], cwd=repo, input=input_text, text=True,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True, timeout=60,
    )
    return result.stdout.strip()


def commit(repo: Path, value: object) -> str:
    if not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{40}", value):
        raise ValueError("expected a full Git commit SHA")
    git(repo, "cat-file", "-e", value + "^{commit}")
    return value


def empty_tree(repo: Path) -> str:
    return git(repo, "hash-object", "-w", "-t", "tree", "--stdin", input_text="")


def change_range(repo: Path, event_name: str, event: dict) -> tuple[str, str]:
    if event_name == "pull_request":
        pr = event["pull_request"]
        head = commit(repo, pr["head"]["sha"])
        base = commit(repo, pr["base"]["sha"])
        # Inspect all PR commits, not just HEAD or unrelated changes on main.
        return git(repo, "merge-base", base, head), head
    if event_name == "push":
        head = commit(repo, event["after"])
        before = event["before"]
        base = empty_tree(repo) if before == ZERO_SHA else commit(repo, before)
        return base, head
    if event_name in ("workflow_dispatch", "schedule"):
        head = git(repo, "rev-parse", "HEAD")
        parents = git(repo, "rev-list", "--parents", "-n", "1", head).split()
        return (parents[1] if len(parents) > 1 else empty_tree(repo)), head
    raise ValueError("unsupported event: " + event_name)


def check(repo: Path, event_name: str, event: dict) -> int:
    base, head = change_range(repo, event_name, event)
    result = subprocess.run(
        ["git", "-c", "core.whitespace=blank-at-eol,blank-at-eof,space-before-tab",
         "diff", "--check", base, head, "--"],
        cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        check=False, timeout=60,
    )
    if result.stdout:
        print(result.stdout, end="")
    if result.stderr:
        print(result.stderr, end="", file=sys.stderr)
    if result.returncode:
        print("FAIL: git diff --check " + base + " " + head, file=sys.stderr)
    else:
        print("PASS: git diff --check " + base + " " + head)
    return result.returncode


def main() -> int:
    try:
        event_path = Path(os.environ["GITHUB_EVENT_PATH"])
        event = json.loads(event_path.read_text(encoding="utf-8"))
        if not isinstance(event, dict):
            raise ValueError("GitHub event must be an object")
        return check(Path.cwd(), os.environ["GITHUB_EVENT_NAME"], event)
    except (KeyError, ValueError, OSError, subprocess.SubprocessError) as error:
        detail = error.stderr if isinstance(error, subprocess.CalledProcessError) else str(error)
        print("FAIL: cannot verify Git diff; checkout full history (fetch-depth: 0). "
              + str(detail), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
