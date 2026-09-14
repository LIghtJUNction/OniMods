#!/usr/bin/env python3
"""Verify the pinned ONI NavGrid.UpdateGraph method-body contract used by CycleTrim.

This check downloads source text only from a commit-pinned public decompilation mirror.
It never executes or republishes game binaries. The mirror currently has no declared
license, so the source is used transiently as compatibility evidence only.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import urllib.request
from pathlib import Path


RAW_PREFIX = "https://raw.githubusercontent.com/"


def git_blob_sha(data: bytes) -> str:
    header = f"blob {len(data)}\0".encode("ascii")
    return hashlib.sha1(header + data).hexdigest()


def download_source(repository: str, commit: str, item: dict[str, object]) -> str:
    path = str(item["path"])
    url = f"{RAW_PREFIX}{repository}/{commit}/{path}"
    request = urllib.request.Request(
        url,
        headers={"User-Agent": "OniMods-cycletrim-contract/1"},
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        data = response.read()

    expected_blob = str(item["blob_sha"]).lower()
    actual_blob = git_blob_sha(data)
    if actual_blob != expected_blob:
        raise ValueError(
            f"{item['name']}: git blob {actual_blob} != expected {expected_blob}"
        )

    print(
        f"SOURCE_PROVENANCE {item['name']} commit={commit} "
        f"blob_sha={actual_blob} bytes={len(data)}"
    )
    return data.decode("utf-8-sig")


def method_body(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise ValueError(f"missing method: {signature}")
    opening = source.find("{", start)
    if opening < 0:
        raise ValueError(f"missing method body: {signature}")
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1 : index]
    raise ValueError(f"unterminated method: {signature}")


def require(pattern: str, text: str, message: str) -> None:
    # pi-lens-ignore: python-unsafe-regex
    if not re.search(pattern, text, re.MULTILINE | re.DOTALL):
        raise ValueError(message)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", default="ci/oni-reference-assemblies.json")
    args = parser.parse_args()

    manifest = json.loads(Path(args.manifest).read_text(encoding="utf-8"))
    source = manifest["method_body_source"]
    expected_build = int(manifest["official_oni_build"])
    if int(source["official_oni_build"]) != expected_build:
        raise ValueError(
            "method-body source build does not match manifest official_oni_build"
        )

    files = {str(item["name"]): item for item in source["files"]}
    repository = str(source["repository"])
    commit = str(source["commit"])
    nav_grid = download_source(repository, commit, files["NavGrid.cs"])
    klei_version = download_source(repository, commit, files["KleiVersion.cs"])

    require(
        rf"public\s+const\s+uint\s+ChangeList\s*=\s*{expected_build}U\s*;",
        klei_version,
        f"KleiVersion.ChangeList is not {expected_build}",
    )
    require(
        r'public\s+const\s+string\s+BuildBranch\s*=\s*"release"\s*;',
        klei_version,
        "pinned source is not a release build",
    )

    for pattern, message in (
        (r"private\s+byte\[\]\s+DirtyBitFlags\s*;", "DirtyBitFlags field drifted"),
        (r"private\s+List<int>\s+DirtyCells\s*;", "DirtyCells field drifted"),
        (
            r"private\s+static\s+List<int>\s+dirtyCellsSwapBuffer\s*=\s*new\s+List<int>\s*\(\s*\)\s*;",
            "dirtyCellsSwapBuffer field drifted",
        ),
    ):
        require(pattern, nav_grid, message)

    body = method_body(nav_grid, "public void UpdateGraph()")
    for pattern, message in (
        (
            r"int\s+count\s*=\s*this\.DirtyCells\.Count\s*;",
            "UpdateGraph no longer snapshots the original DirtyCells count",
        ),
        (
            r"for\s*\(\s*int\s+i\s*=\s*0\s*;\s*i\s*<\s*count\s*;\s*i\+\+\s*\)",
            "UpdateGraph dirty-source loop shape drifted",
        ),
        (
            r"Grid\.ClampX\s*\(\s*num\s*-\s*this\.updateRangeX\s*\)",
            "UpdateGraph minimum-X expansion drifted",
        ),
        (
            r"Grid\.ClampY\s*\(\s*num2\s*-\s*this\.updateRangeY\s*\)",
            "UpdateGraph minimum-Y expansion drifted",
        ),
        (
            r"Grid\.ClampX\s*\(\s*num\s*\+\s*this\.updateRangeX\s*\)",
            "UpdateGraph maximum-X expansion drifted",
        ),
        (
            r"Grid\.ClampY\s*\(\s*num2\s*\+\s*this\.updateRangeY\s*\)",
            "UpdateGraph maximum-Y expansion drifted",
        ),
        (
            r"this\.AddDirtyCell\s*\(\s*Grid\.XYToCell\s*\(\s*k\s*,\s*j\s*\)\s*\)\s*;",
            "UpdateGraph no longer expands through AddDirtyCell",
        ),
        (
            r"List<int>\s+list\s*=\s*NavGrid\.dirtyCellsSwapBuffer\s*;\s*"
            r"NavGrid\.dirtyCellsSwapBuffer\s*=\s*this\.DirtyCells\s*;\s*"
            r"this\.DirtyCells\s*=\s*list\s*;",
            "UpdateGraph dirty-list swap semantics drifted",
        ),
        (
            r"this\.UpdateGraph\s*\(\s*NavGrid\.dirtyCellsSwapBuffer\s*\)\s*;",
            "UpdateGraph no longer forwards the expanded list to UpdateGraph(List<int>)",
        ),
        (
            r"this\.DirtyBitFlags\s*\[\s*num7\s*/\s*8\s*\]\s*=\s*0\s*;",
            "UpdateGraph dirty-bit cleanup drifted",
        ),
        (
            r"NavGrid\.dirtyCellsSwapBuffer\.Clear\s*\(\s*\)\s*;",
            "UpdateGraph swap-buffer cleanup drifted",
        ),
    ):
        require(pattern, body, message)

    print(
        f"PASS NavGrid.UpdateGraph source contract matches ONI {expected_build} "
        f"at {repository}@{commit}"
    )
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
