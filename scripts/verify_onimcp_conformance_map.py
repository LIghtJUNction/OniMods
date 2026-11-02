#!/usr/bin/env python3
"""Validate archived 2026 evidence and current compiled legacy transport regressions."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET
from urllib.request import Request, urlopen


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_MANIFEST = ROOT / "tests/onimcp-conformance/2026-07-28.partial.json"
EXPECTED_REPOSITORY = "modelcontextprotocol/conformance"
EXPECTED_REVISION = "2026-07-28"
MAX_UPSTREAM_BYTES = 1024 * 1024
SHA1_RE = re.compile(r"^[0-9a-f]{40}$")
RETIRED_REGRESSION = "tests/OniMcp.Server.Tests/RetiredProtocolRegression.cs"
COMPILED_REGRESSION = "../OniMcp.Server.Tests/RetiredProtocolRegression.cs"
EXPECTED_ENTRY_POINTS = {
    "tests/OniMcp.ModernDiscoveryFinalWire.Tests/OniMcp.ModernDiscoveryFinalWire.Tests.csproj": "RetiredDiscoveryRegressionEntry",
    "tests/OniMcp.ModernHeaders.Tests/OniMcp.ModernHeaders.Tests.csproj": "RetiredHeadersRegressionEntry",
    "tests/OniMcp.ModernProtocol.Tests/OniMcp.ModernProtocol.Tests.csproj": "RetiredProtocolRegressionEntry",
    "tests/OniMcp.ResourceReadConformance.Tests/OniMcp.ResourceReadConformance.Tests.csproj": "RetiredResourcesRegressionEntry",
}
RETIRED_REGRESSION_GUARDS = (
    'private const string Current = "2025-11-25";',
    'private const string Retired = "2026-07-28";',
    'case "discovery": VerifyDiscovery(client',
    'case "headers": VerifyHeaders(client',
    'case "protocol": VerifyProtocol(client',
    'case "resources": VerifyResources(client',
    "OniToolRegistry.Calls == toolCalls",
    "&& OniResourceRegistry.ResourceReads == resourceReads",
    "&& OniResourceRegistry.CatalogReads == catalogReads",
    "&& server.GetSessionSummaries().Count == sessions",
    "Require(response.StatusCode == HttpStatusCode.BadRequest,",
    'Require((int)json["error"]["code"] == -32022,',
    '"Current tools/list HTTP status changed"',
    '"Supported protocol could not dispatch a tool call"',
    '"Current read-only resource failed"',
)


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


def validate_retirement(data: dict) -> None:
    retirement = data.get("retirement")
    if not isinstance(retirement, dict):
        fail("retired 2026 manifest requires explicit supported/retired protocol evidence")
    if retirement.get("supportedProtocol") != "2025-11-25":
        fail("supported transport baseline must stay 2025-11-25")
    if retirement.get("retiredProtocol") != EXPECTED_REVISION:
        fail("retired protocol evidence is not bound to the archived revision")
    if retirement.get("regressionTest") != RETIRED_REGRESSION:
        fail("retirement requires the shared executable production HTTP regression")
    if retirement.get("projectEntryPoints") != EXPECTED_ENTRY_POINTS:
        fail("all four original test project entry points must still compile retirement regressions")

    regression = validate_repo_path(RETIRED_REGRESSION, "retirement.regressionTest")
    if not regression.is_file():
        fail("compiled protocol retirement regression is missing")
    source = regression.read_text(encoding="utf-8")
    for marker in RETIRED_REGRESSION_GUARDS:
        if marker not in source:
            fail(f"compiled retirement regression guard is missing: {marker!r}")

    for project_name, entry_point in EXPECTED_ENTRY_POINTS.items():
        project_path = validate_repo_path(project_name, "retirement.project")
        if not project_path.is_file():
            fail(f"required protocol regression project is missing: {project_name}")
        try:
            project = ET.parse(project_path).getroot()
        except ET.ParseError as error:
            fail(f"invalid protocol regression project XML: {project_name}: {error}")
        startup = [node.text for node in project.iter("StartupObject")]
        compiled = [node.attrib.get("Include", "") for node in project.iter("Compile")]
        if startup != [entry_point]:
            fail(f"{project_name}: expected executable entry point {entry_point}")
        if compiled.count(COMPILED_REGRESSION) != 1:
            fail(f"{project_name}: must compile the shared retirement regression exactly once")
        if any(path == "Program.cs" or path.startswith("Modern") for path in compiled):
            fail(f"{project_name}: removed 2026 success-path regression is still compiled")


def validate_manifest(data: dict) -> tuple[dict, list[dict]]:
    if data.get("revision") != EXPECTED_REVISION:
        fail(f"revision must be {EXPECTED_REVISION}")
    if data.get("scope") != "retired-historical-evidence":
        fail("2026 coverage must remain explicitly retired, not claim active conformance")
    validate_retirement(data)

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
        if entry.get("claim") != "historical-source-only":
            fail(f"{scenario}: retired source evidence must not claim executable coverage")

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
                fail(f"{scenario}: archived source evidence marker is missing: {marker!r}")

    not_claimed = data.get("notClaimed")
    if not isinstance(not_claimed, list) or not any(
        isinstance(item, str) and "does not claim full" in item for item in not_claimed
    ):
        fail("notClaimed must explicitly state that full conformance is not claimed")
    if not any(
        isinstance(item, str) and "not claim full or partial active" in item
        for item in not_claimed
    ):
        fail("retired 2026 evidence must not be advertised as active conformance")
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
        help="verify frozen historical requirements provenance and scenario membership",
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
        f"PASS OniMcp {EXPECTED_REVISION} retired historical evidence and compiled transport regressions: "
        f"{len(coverage)} mapped scenarios at {upstream['commit'][:12]}{suffix}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
