#!/usr/bin/env python3
"""Compare extracted Harmony target API signatures with the checked-in baseline."""

from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path
import tempfile

from extract_harmony_api_surface import compare_baseline, digest_text


def load_baseline(directory: Path) -> dict:
    index_path = directory / "index.json"
    baseline = json.loads(index_path.read_text(encoding="utf-8"))
    hydrated = copy.deepcopy(baseline)
    for target in hydrated.get("targets", []):
        files = target.pop("signature_files", None)
        single = target.pop("signature_file", None)
        if files is None:
            files = [single] if single else []
        if not files:
            raise ValueError(
                f"baseline target {target.get('requested_type')!r} has no signature file"
            )
        chunks = []
        for file_name in files:
            path = directory / file_name
            chunks.append(path.read_text(encoding="utf-8"))
        signature_text = "".join(chunks)
        actual_hash = digest_text(signature_text)
        expected_hash = target.get("signature_sha256")
        if actual_hash != expected_hash:
            raise ValueError(
                f"baseline signature hash mismatch for {target.get('requested_type')}: "
                f"expected {expected_hash}, got {actual_hash}"
            )
        target["signatures"] = signature_text.splitlines()
    return hydrated


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--current", required=True, help="Extracted API surface index.json")
    parser.add_argument("--baseline", required=True, help="Checked-in baseline directory")
    args = parser.parse_args()

    current = json.loads(Path(args.current).read_text(encoding="utf-8"))
    try:
        baseline = load_baseline(Path(args.baseline))
    except (OSError, ValueError, json.JSONDecodeError) as error:
        parser.error(str(error))

    with tempfile.TemporaryDirectory() as temp_dir:
        hydrated = Path(temp_dir) / "baseline.json"
        hydrated.write_text(
            json.dumps(baseline, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        return 0 if compare_baseline(current, hydrated) else 1


if __name__ == "__main__":
    raise SystemExit(main())
