#!/usr/bin/env python3
"""Verify pinned ONI reference provenance and report reference/upstream drift."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import urllib.parse
import urllib.request


RAW_PREFIX = "https://raw.githubusercontent.com/"
API_PREFIX = "https://api.github.com/repos/"
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


def download_json(url: str) -> dict:
    if not url.startswith(API_PREFIX):
        raise ValueError(f"refusing unexpected GitHub API host: {url}")
    request = urllib.request.Request(
        url,
        headers={
            "Accept": "application/vnd.github+json",
            "User-Agent": "OniMods-reference-ci/1",
        },
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.loads(response.read().decode("utf-8"))


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


def validate_repo_path(path_value: str, label: str) -> None:
    path = Path(path_value)
    if not path_value or path.is_absolute() or ".." in path.parts:
        raise ValueError(f"{label} must be repository-relative")


def validate_manifest(manifest: dict) -> tuple[int, int, dict, dict, dict | None]:
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
    validate_repo_path(marker.get("path", ""), "reference version marker path")
    if not re.fullmatch(r"[0-9a-f]{40}", marker.get("blob_sha", "")):
        raise ValueError("reference version marker must include a full Git blob SHA")

    method_build = int(method_body["official_oni_build"])
    if method_build != official_build:
        raise ValueError(
            "method-body source build does not match manifest official_oni_build: "
            f"{method_build} != {official_build}"
        )

    manifest_files = {}
    for item in manifest.get("files", []):
        validate_repo_path(item.get("path", ""), f"{item.get('name', '<unnamed>')} path")
        if not SHA256_RE.fullmatch(item.get("sha256", "")):
            raise ValueError(f"{item.get('name', '<unnamed>')}: missing/invalid SHA256")
        if int(item.get("size", 0)) <= 0:
            raise ValueError(f"{item.get('name', '<unnamed>')}: missing/invalid size")
        manifest_files[item["path"]] = item

    tracking = reference.get("upstream_tracking")
    if tracking is not None:
        branch = tracking.get("branch", "")
        if not branch or branch.startswith("/") or ".." in branch.split("/"):
            raise ValueError("reference upstream tracking branch is invalid")
        tracked_files = tracking.get("files")
        if not isinstance(tracked_files, list) or not tracked_files:
            raise ValueError("reference upstream tracking must include at least one file")
        seen_paths = set()
        for item in tracked_files:
            path_value = item.get("path", "")
            validate_repo_path(path_value, "reference upstream tracked file path")
            if path_value in seen_paths:
                raise ValueError(f"duplicate upstream tracked file: {path_value}")
            seen_paths.add(path_value)
            if path_value not in manifest_files:
                raise ValueError(
                    f"upstream tracked file is not a pinned reference assembly: {path_value}"
                )
            if not COMMIT_RE.fullmatch(item.get("pinned_blob_sha", "")):
                raise ValueError(f"upstream tracked file lacks Git blob identity: {path_value}")

    return official_build, declared_build, reference, marker, tracking


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


def compare_upstream_state(
    reference: dict,
    marker: dict,
    declared_build: int,
    head_sha: str,
    head_marker_blob: str,
    head_marker_text: str,
    head_files: dict[str, dict],
) -> dict:
    if not COMMIT_RE.fullmatch(head_sha):
        raise ValueError(f"invalid upstream HEAD commit: {head_sha!r}")
    if not COMMIT_RE.fullmatch(head_marker_blob):
        raise ValueError(f"invalid upstream version-marker blob: {head_marker_blob!r}")

    properties = parse_properties(head_marker_text)
    head_declared_build = resolve_numeric_property(properties, marker["property"])
    changed_files = []
    tracking = reference["upstream_tracking"]
    for item in tracking["files"]:
        current = head_files.get(item["path"])
        if current is None:
            raise ValueError(f"upstream tracking result missing {item['path']}")
        current_blob = current.get("sha", "")
        if not COMMIT_RE.fullmatch(current_blob):
            raise ValueError(f"invalid upstream blob identity for {item['path']}")
        if current_blob != item["pinned_blob_sha"]:
            changed_files.append(item["path"])

    marker_changed = head_marker_blob != marker["blob_sha"]
    return {
        "head_sha": head_sha,
        "head_advanced": head_sha != reference["commit"],
        "head_declared_build": head_declared_build,
        "marker_changed": marker_changed,
        "changed_files": changed_files,
        "has_reference_drift": (
            head_declared_build != declared_build or marker_changed or bool(changed_files)
        ),
    }


def fetch_upstream_state(reference: dict, marker: dict, declared_build: int) -> dict:
    tracking = reference["upstream_tracking"]
    repository = reference["repository"]
    branch = tracking["branch"]
    branch_ref = urllib.parse.quote(branch, safe="")
    commit_data = download_json(f"{API_PREFIX}{repository}/commits/{branch_ref}")
    head_sha = commit_data.get("sha", "")
    if not COMMIT_RE.fullmatch(head_sha):
        raise ValueError("GitHub API did not return a valid upstream HEAD commit")

    marker_path = urllib.parse.quote(marker["path"], safe="/")
    marker_meta = download_json(
        f"{API_PREFIX}{repository}/contents/{marker_path}?ref={head_sha}"
    )
    marker_blob = marker_meta.get("sha", "")
    marker_data = download_text(
        f"{RAW_PREFIX}{repository}/{head_sha}/{marker['path']}"
    ).decode("utf-8-sig")

    current_files = {}
    for item in tracking["files"]:
        path_value = item["path"]
        encoded_path = urllib.parse.quote(path_value, safe="/")
        file_meta = download_json(
            f"{API_PREFIX}{repository}/contents/{encoded_path}?ref={head_sha}"
        )
        current_files[path_value] = {
            "sha": file_meta.get("sha", ""),
            "size": file_meta.get("size"),
        }

    return compare_upstream_state(
        reference,
        marker,
        declared_build,
        head_sha,
        marker_blob,
        marker_data,
        current_files,
    )


def report_upstream_state(reference: dict, state: dict) -> None:
    tracking = reference["upstream_tracking"]
    changed_files = ",".join(state["changed_files"]) or "none"
    print(
        "UPSTREAM_REFERENCE_STATUS status=ok "
        f"branch={tracking['branch']} head={state['head_sha']} "
        f"head_declared_oni_build={state['head_declared_build']} "
        f"marker_changed={str(state['marker_changed']).lower()} "
        f"changed_reference_files={changed_files}"
    )

    if state["has_reference_drift"]:
        print(
            "::warning title=ONI upstream reference drift detected::"
            f"{reference['repository']} {tracking['branch']} is {state['head_sha']}; "
            f"declared build={state['head_declared_build']}, "
            f"version-marker changed={state['marker_changed']}, "
            f"tracked reference files changed={changed_files}. Review provenance/API "
            "differences before changing the immutable pin."
        )
    elif state["head_advanced"]:
        print(
            "::notice title=ONI upstream advanced without tracked reference drift::"
            f"{reference['repository']} {tracking['branch']} advanced to "
            f"{state['head_sha']}, but the tracked version marker and key reference "
            "assembly Git blobs still match the pinned baseline."
        )


def verify_upstream_head(reference: dict, marker: dict, declared_build: int) -> None:
    try:
        state = fetch_upstream_state(reference, marker, declared_build)
    except (OSError, ValueError, KeyError, json.JSONDecodeError, UnicodeDecodeError) as error:
        print(
            "UPSTREAM_REFERENCE_STATUS status=unknown "
            f"source={reference['repository']} reason={type(error).__name__}"
        )
        print(
            "::warning title=ONI upstream drift status unknown::"
            f"Could not inspect current {reference['repository']} reference state: {error}"
        )
        return
    report_upstream_state(reference, state)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", default="ci/oni-reference-assemblies.json")
    parser.add_argument("--verify-upstream", action="store_true")
    args = parser.parse_args()

    manifest = json.loads(Path(args.manifest).read_text(encoding="utf-8"))
    official_build, declared_build, reference, marker, tracking = validate_manifest(manifest)

    if args.verify_upstream:
        verify_upstream_marker(reference, marker, declared_build)
        if tracking is not None:
            verify_upstream_head(reference, marker, declared_build)

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
