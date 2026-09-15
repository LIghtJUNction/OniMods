#!/usr/bin/env python3
"""Verify OniMcp's pinned, explicitly partial MCP 2026 conformance evidence map."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
from urllib.request import Request, urlopen


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_MANIFEST = ROOT / "tests/onimcp-conformance/2026-07-28.partial.json"
EXPECTED_REPOSITORY = "modelcontextprotocol/conformance"
EXPECTED_REVISION = "2026-07-28"
MAX_UPSTREAM_BYTES = 1024 * 1024
SHA1_RE = re.compile(r"^[0-9a-f]{40}$")


def fail(message: str) -> None:
    raise ValueError(message)


def load_manifest(path: Path) -> dict:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        fail(f"cannot read conformance map {path}: {error}")
    if not isinstance(data, dict):
        fail("conformance map root must be a JSON object")
    return data


def validate_repo_path(value: object, field: str) -> Path:
    if not isinstance(value, str) or not value or value.startswith("/"):
        fail(f"{field} must be a non-empty repository-relative path")
    path = Path(value)
    if ".." in path.parts:
        fail(f"{field} must not contain '..'")
    resolved = (ROOT / path).resolve()
    if ROOT != resolved and ROOT not in resolved.parents:
        fail(f"{field} escapes the repository root")
    return resolved


def validate_manifest(data: dict) -> tuple[dict, list[dict]]:
    if data.get("revision") != EXPECTED_REVISION:
        fail(f"revision must be {EXPECTED_REVISION}")
    if data.get("scope") != "partial-local-evidence":
        fail("scope must stay 'partial-local-evidence'; this map is not a full conformance claim")

    upstream = data.get("upstream")
    if not isinstance(upstream, dict):
        fail("upstream must be an object")
    if upstream.get("repository") != EXPECTED_REPOSITORY:
        fail(f"upstream repository must stay pinned to {EXPECTED_REPOSITORY}")
    if not SHA1_RE.fullmatch(str(upstream.get("commit", ""))):
        fail("upstream.commit must be a full 40-character lowercase SHA-1")
    if not SHA1_RE.fullmatch(str(upstream.get("gitBlobSha", ""))):
        fail("upstream.gitBlobSha must be a full 40-character lowercase Git blob SHA-1")
    requirements_path = upstream.get("requirementsPath")
    if requirements_path != f"requirements/{EXPECTED_REVISION}.yaml":
        fail(f"requirementsPath must be requirements/{EXPECTED_REVISION}.yaml")

    coverage = data.get("coverage")
    if not isinstance(coverage, list) or not coverage:
        fail("coverage must be a non-empty array")

    seen = set()
    for entry in coverage:
        if not isinstance(entry, dict):
            fail("each coverage entry must be an object")
        scenario = entry.get("scenario")
        if not isinstance(scenario, str) or not scenario:
            fail("coverage scenario must be a non-empty string")
        if scenario in seen:
            fail(f"duplicate coverage scenario: {scenario}")
        seen.add(scenario)
        if entry.get("claim") != "partial":
            fail(f"{scenario}: claim must stay 'partial'")

        test_path = validate_repo_path(entry.get("test"), f"{scenario}.test")
        if not test_path.is_file():
            fail(f"{scenario}: mapped test file does not exist: {test_path.relative_to(ROOT)}")
        source = test_path.read_text(encoding="utf-8")
        markers = entry.get("markers")
        if not isinstance(markers, list) or not markers:
            fail(f"{scenario}: markers must be a non-empty array")
        marker_seen = set()
        for marker in markers:
            if not isinstance(marker, str) or not marker:
                fail(f"{scenario}: every marker must be a non-empty string")
            if marker in marker_seen:
                fail(f"{scenario}: duplicate marker: {marker}")
            marker_seen.add(marker)
            if marker not in source:
                fail(f"{scenario}: mapped regression marker is missing: {marker!r}")

    not_claimed = data.get("notClaimed")
    if not isinstance(not_claimed, list) or not any(
        isinstance(item, str) and "does not claim full" in item for item in not_claimed
    ):
        fail("notClaimed must explicitly state that full conformance is not claimed")
    return upstream, coverage


def git_blob_sha(data: bytes) -> str:
    header = f"blob {len(data)}\0".encode("ascii")
    return hashlib.sha1(header + data).hexdigest()


def parse_required_server_scenarios(text: str) -> set[str]:
    scenarios: set[str] = set()
    in_server = False
    for raw_line in text.splitlines():
        line = raw_line.rstrip()
        if line == "server:":
            in_server = True
            continue
        if in_server and line == "client:":
            break
        if in_server and line.startswith("  - "):
            scenario = line[4:].strip()
            if scenario:
                scenarios.add(scenario)
    if not scenarios:
        fail("pinned requirements file contained no server scenarios")
    return scenarios


def fetch_pinned_requirements(upstream: dict) -> bytes:
    commit = upstream["commit"]
    path = upstream["requirementsPath"]
    url = f"https://raw.githubusercontent.com/{EXPECTED_REPOSITORY}/{commit}/{path}"
    request = Request(url, headers={"User-Agent": "OniMods-conformance-map-verifier/1"})
    with urlopen(request, timeout=20) as response:
        data = response.read(MAX_UPSTREAM_BYTES + 1)
    if len(data) > MAX_UPSTREAM_BYTES:
        fail(f"pinned requirements file exceeded {MAX_UPSTREAM_BYTES} bytes")
    actual_sha = git_blob_sha(data)
    if actual_sha != upstream["gitBlobSha"]:
        fail(f"pinned requirements Git blob SHA mismatch: expected {upstream['gitBlobSha']}, got {actual_sha}")
    return data


def verify_upstream(upstream: dict, coverage: list[dict]) -> None:
    try:
        data = fetch_pinned_requirements(upstream)
        text = data.decode("utf-8", errors="strict")
    except Exception as error:
        fail(f"could not verify pinned upstream requirements: {error}")
    required = parse_required_server_scenarios(text)
    mapped = {entry["scenario"] for entry in coverage}
    missing = sorted(mapped - required)
    if missing:
        fail("mapped scenarios are not in the frozen 2026 server requirement set: " + ", ".join(missing))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument(
        "--verify-upstream",
        action="store_true",
        help="download the immutable pinned requirements file, verify its Git blob SHA, and check scenario membership",
    )
    args = parser.parse_args()

    manifest_path = args.manifest if args.manifest.is_absolute() else ROOT / args.manifest
    try:
        data = load_manifest(manifest_path)
        upstream, coverage = validate_manifest(data)
        if args.verify_upstream:
            verify_upstream(upstream, coverage)
    except (OSError, UnicodeError, ValueError) as error:
        print(f"FAIL OniMcp conformance map: {error}", file=sys.stderr)
        return 1

    suffix = "; pinned upstream verified" if args.verify_upstream else ""
    print(
        f"PASS OniMcp {EXPECTED_REVISION} partial conformance map: "
        f"{len(coverage)} mapped scenarios at {upstream['commit'][:12]}{suffix}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
