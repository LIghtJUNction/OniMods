#!/usr/bin/env python3
"""Fetch pinned ONI reference assemblies and verify their provenance.

The downloaded files are build inputs only. This script never executes them.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys
import tempfile
import urllib.request
import zipfile


RAW_PREFIX = "https://raw.githubusercontent.com/"
NUGET_PREFIX = "https://api.nuget.org/"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def download(url: str, destination: Path) -> None:
    if not (url.startswith(RAW_PREFIX) or url.startswith(NUGET_PREFIX)):
        raise ValueError(f"refusing unexpected download host: {url}")
    request = urllib.request.Request(url, headers={"User-Agent": "OniMods-reference-ci/1"})
    destination.parent.mkdir(parents=True, exist_ok=True)
    with urllib.request.urlopen(request, timeout=90) as response, destination.open("wb") as out:
        shutil.copyfileobj(response, out)


def verify(path: Path, *, expected_size: int | None, expected_sha256: str,
           allow_missing_sha256: bool, label: str) -> str:
    actual_size = path.stat().st_size
    if expected_size is not None and actual_size != expected_size:
        raise ValueError(f"{label}: size {actual_size} != expected {expected_size}")
    actual_sha256 = sha256(path)
    if expected_sha256:
        if actual_sha256.lower() != expected_sha256.lower():
            raise ValueError(
                f"{label}: sha256 {actual_sha256} != expected {expected_sha256}"
            )
    elif not allow_missing_sha256:
        raise ValueError(f"{label}: manifest is missing sha256")
    print(f"PROVENANCE {label} size={actual_size} sha256={actual_sha256}")
    return actual_sha256


def extract_named_dll(package_path: Path, output_dir: Path, name: str) -> Path:
    with zipfile.ZipFile(package_path) as archive:
        candidates = [entry for entry in archive.namelist() if entry.endswith("/" + name)]
        if not candidates:
            raise ValueError(f"{name}: not found in {package_path.name}")
        candidates.sort(key=lambda entry: ("netstandard2.0" not in entry.lower(), len(entry), entry))
        selected = candidates[0]
        destination = output_dir / name
        with archive.open(selected) as source, destination.open("wb") as out:
            shutil.copyfileobj(source, out)
        print(f"AUXILIARY {name} extracted_from={selected}")
        return destination


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", default="ci/oni-reference-assemblies.json")
    parser.add_argument("--output", default=".oni-managed")
    parser.add_argument("--allow-missing-sha256", action="store_true")
    args = parser.parse_args()

    manifest_path = Path(args.manifest)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    output_dir = Path(args.output)
    output_dir.mkdir(parents=True, exist_ok=True)

    source = manifest["reference_source"]
    repository = source["repository"]
    commit = source["commit"]
    for item in manifest["files"]:
        url = f"{RAW_PREFIX}{repository}/{commit}/{item['path']}"
        destination = output_dir / item["name"]
        with tempfile.NamedTemporaryFile(dir=output_dir, delete=False) as temporary:
            temporary_path = Path(temporary.name)
        try:
            download(url, temporary_path)
            verify(
                temporary_path,
                expected_size=item.get("size"),
                expected_sha256=item.get("sha256", ""),
                allow_missing_sha256=args.allow_missing_sha256,
                label=item["name"],
            )
            temporary_path.replace(destination)
        finally:
            temporary_path.unlink(missing_ok=True)

    auxiliary = manifest.get("auxiliary_unity_modules")
    if auxiliary:
        package_path = output_dir / (auxiliary["package"] + ".nupkg")
        download(auxiliary["url"], package_path)
        try:
            verify(
                package_path,
                expected_size=auxiliary.get("size"),
                expected_sha256=auxiliary.get("sha256", ""),
                allow_missing_sha256=args.allow_missing_sha256,
                label=auxiliary["package"] + "@" + auxiliary["version"],
            )
            for item in auxiliary["extract"]:
                destination = extract_named_dll(package_path, output_dir, item["name"])
                verify(
                    destination,
                    expected_size=item.get("size"),
                    expected_sha256=item.get("sha256", ""),
                    allow_missing_sha256=args.allow_missing_sha256,
                    label=item["name"] + " (auxiliary)",
                )
        finally:
            package_path.unlink(missing_ok=True)

    print(
        "Reference set ready. The auxiliary Unity modules are compile-only and must not "
        "be used as evidence of ONI runtime compatibility."
    )
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:  # concise CI failure with provenance context
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
