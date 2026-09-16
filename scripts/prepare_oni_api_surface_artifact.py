#!/usr/bin/env python3
"""Reduce extracted ONI API diagnostics to publishable signatures/JSON only."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import sys


FORBIDDEN_SUFFIXES = {".cs", ".dll", ".exe", ".nupkg", ".pdb"}


def digest_text(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def child_path(root: Path, name: str) -> Path:
    relative = Path(name)
    if relative.is_absolute() or ".." in relative.parts:
        raise ValueError(f"unsafe artifact path: {name!r}")
    path = (root / relative).resolve()
    try:
        path.relative_to(root.resolve())
    except ValueError as error:
        raise ValueError(f"artifact path escapes output directory: {name!r}") from error
    return path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", nargs="?", default="artifacts/oni-api-surface")
    args = parser.parse_args()

    root = Path(args.directory)
    index_path = root / "index.json"
    index = json.loads(index_path.read_text(encoding="utf-8"))

    allowed = {index_path.resolve()}
    removed_sources = 0
    targets = index.get("targets", [])
    if not isinstance(targets, list):
        raise ValueError("index targets must be a list")

    for target in targets:
        if not isinstance(target, dict):
            raise ValueError("index target entries must be objects")

        # ILSpy output is needed transiently to derive the canonical signatures, but
        # full decompiled game source must not be persisted in the uploaded artifact.
        source_name = target.pop("file", None)
        target.pop("sha256", None)
        if source_name:
            source_path = child_path(root, source_name)
            if source_path.suffix.lower() != ".cs":
                raise ValueError(f"unexpected decompiled source path: {source_name!r}")
            if source_path.exists():
                source_path.unlink()
                removed_sources += 1

        signature_name = target.get("signature_file")
        expected_hash = target.get("signature_sha256")
        if not signature_name or not expected_hash:
            raise ValueError(
                f"target {target.get('requested_type')!r} is missing signature metadata"
            )
        signature_path = child_path(root, signature_name)
        if not signature_path.is_file():
            raise ValueError(f"missing signature artifact: {signature_name}")
        actual_hash = digest_text(signature_path.read_text(encoding="utf-8"))
        if actual_hash != expected_hash:
            raise ValueError(
                f"signature hash mismatch for {target.get('requested_type')}: "
                f"expected {expected_hash}, got {actual_hash}"
            )
        allowed.add(signature_path.resolve())

    index_path.write_text(
        json.dumps(index, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )

    unexpected = []
    forbidden = []
    for path in sorted(root.rglob("*")):
        if not path.is_file():
            continue
        resolved = path.resolve()
        if path.suffix.lower() in FORBIDDEN_SUFFIXES:
            forbidden.append(str(path.relative_to(root)))
        if resolved not in allowed:
            unexpected.append(str(path.relative_to(root)))

    if forbidden:
        raise ValueError("forbidden files remain in API artifact: " + ", ".join(forbidden))
    if unexpected:
        raise ValueError("unexpected files in API artifact: " + ", ".join(unexpected))

    print(
        f"PASS API artifact sanitized: targets={len(targets)} "
        f"removed_decompiled_sources={removed_sources} files={len(allowed)}"
    )
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
