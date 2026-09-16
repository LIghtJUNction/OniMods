#!/usr/bin/env python3
"""Verify pinned ONI reference provenance and report reference lag without loading DLLs."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import urllib.request


RAW_PREFIX = "https://raw.githubusercontent.com/"
COMMIT_RE = re.compile(r"^[0-9a-f]{40}$")
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
PROPERTY_RE = re.compile(
    r"<(?P<name>[A-Za-z_][A-Za-z0-9_.-]*)>(?P<value>[^<]*)</(?P=name)>"
)
PROPERTY_REF_RE = re.compile(r"^\$\((?P<name>[A-Za-z_][A-Za-z0-9_.-]*)\)$")


def download_text(url: str) -> bytes:
    if not url.startswith(RAW_PREFIX):
        raise ValueError(f"refusing unexpected provenance host: {url}")
    request = urllib.request.Request(url, headers={"User-Agent": "OniMods-reference-ci/1"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


def git_blob_sha(data: bytes) -> str:
    prefix = f"blob {len(data)}\0".encode("ascii")
    return hashlib.sha1(prefix + data).hexdigest()


def parse_properties(text: str) -> dict[str, str]:
    properties: dict[str, str] = {}
    for match in PROPERTY_RE.finditer(text):
        properties[match.group("name")] = match.group("value").strip()
    return properties


def resolve_numeric_property(
    properties: dict[str, str], name: str, seen: set[str] | None = None
) -> int:
    if seen is None:
        seen = set()
    if name in seen:
        raise ValueError(f"cyclic MSBuild property reference while resolving {name}")
    if name not in properties:
        raise ValueError(f"MSBuild property {name!r} not found in version marker")

    seen = set(seen)
    seen.add(name)
    value = properties[name]
    if value.isdigit():
        return int(value)
    reference = PROPERTY_REF_RE.fullmatch(value)
    if reference:
        return resolve_numeric_property(properties, reference.group("name"), seen)
    raise ValueError(f"MSBuild property {name!r} is not a numeric/property value: {value!r}")


def validate_manifest(manifest: dict) -> tuple[int, int, dict, dict]:
    if manifest.get("schema") != 1:
        raise ValueError(f"unsupported manifest schema: {manifest.get('schema')!r}")

    official_build = int(manifest["official_oni_build"])
    reference = manifest["reference_source"]
    method_body = manifest["method_body_source"]

    repository = reference["repository"]
    commit = reference["commit"]
    if "/" not in repository:
        raise ValueError(f"invalid reference repository: {repository!r}")
    if not COMMIT_RE.fullmatch(commit):
        raise ValueError("reference source must be pinned to a full lowercase commit SHA")

    declared_build = int(reference["declared_oni_build"])
    marker = reference["version_marker"]
    if not marker.get("path") or Path(marker["path"]).is_absolute() or ".." in Path(marker["path"]).parts:
        raise ValueError("reference version marker path must be repository-relative")
    if not re.fullmatch(r"[0-9a-f]{40}", marker.get("blob_sha", "")):
        raise ValueError("reference version marker must include a full Git blob SHA")

    method_build = int(method_body["official_oni_build"])
    if method_build != official_build:
        raise ValueError(
            "method-body source build does not match manifest official_oni_build: "
            f"{method_build} != {official_build}"
        )

    for item in manifest.get("files", []):
        if not SHA256_RE.fullmatch(item.get("sha256", "")):
            raise ValueError(f"{item.get('name', '<unnamed>')}: missing/invalid SHA256")
        if int(item.get("size", 0)) <= 0:
            raise ValueError(f"{item.get('name', '<unnamed>')}: missing/invalid size")

    return official_build, declared_build, reference, marker


def verify_upstream_marker(reference: dict, marker: dict, declared_build: int) -> None:
    url = (
        f"{RAW_PREFIX}{reference['repository']}/{reference['commit']}/{marker['path']}"
    )
    data = download_text(url)
    actual_blob = git_blob_sha(data)
    if actual_blob != marker["blob_sha"]:
        raise ValueError(
            "reference version marker blob changed: "
            f"{actual_blob} != {marker['blob_sha']}"
        )

    text = data.decode("utf-8-sig")
    properties = parse_properties(text)
    property_name = marker["property"]
    resolved_build = resolve_numeric_property(properties, property_name)
    if resolved_build != declared_build:
        raise ValueError(
            f"reference source declares ONI {resolved_build}, manifest records {declared_build}"
        )

    expected_leaf = marker.get("resolved_property")
    if expected_leaf:
        leaf_build = resolve_numeric_property(properties, expected_leaf)
        if leaf_build != declared_build:
            raise ValueError(
                f"reference leaf property {expected_leaf} resolves to {leaf_build}, "
                f"expected {declared_build}"
            )
        if properties[property_name] != f"$({expected_leaf})":
            raise ValueError(
                f"reference {property_name} no longer points to {expected_leaf}: "
                f"{properties[property_name]!r}"
            )

    print(
        "PROVENANCE reference-version-marker "
        f"repo={reference['repository']} commit={reference['commit']} "
        f"blob={actual_blob} declared_oni_build={resolved_build}"
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", default="ci/oni-reference-assemblies.json")
    parser.add_argument("--verify-upstream", action="store_true")
    args = parser.parse_args()

    manifest = json.loads(Path(args.manifest).read_text(encoding="utf-8"))
    official_build, declared_build, reference, marker = validate_manifest(manifest)

    if args.verify_upstream:
        verify_upstream_marker(reference, marker, declared_build)

    print(
        "REFERENCE_STATUS "
        f"official_oni_build={official_build} "
        f"compile_reference_declared_build={declared_build} "
        f"source={reference['repository']}@{reference['commit']}"
    )

    if declared_build < official_build:
        print(
            "::warning title=ONI reference lag::"
            f"Pinned compile/API reference source declares ONI {declared_build}, while "
            f"the manifest tracks official ONI {official_build}. Compile and API-surface "
            f"success must not be reported as {official_build} binary compatibility."
        )
    elif declared_build > official_build:
        print(
            "::warning title=ONI reference source ahead of official manifest::"
            f"Pinned compile/API reference source declares ONI {declared_build}, while "
            f"the manifest tracks official ONI {official_build}. Refresh official release "
            "metadata before making compatibility claims."
        )

    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, KeyError, json.JSONDecodeError, UnicodeDecodeError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
