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
STEAM_NEWS_ENDPOINT = "https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/"
ONI_STEAM_APP_ID = 457140
COMMIT_RE = re.compile(r"^[0-9a-f]{40}$")
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
STEAM_NEWS_GID_RE = re.compile(r"^[0-9]+$")
STEAM_NEWS_URL_RE = re.compile(
    r"^https://store\.steampowered\.com/news/app/(?P<app_id>[0-9]+)/view/(?P<view_id>[0-9]+)$"
)
PROPERTY_RE = re.compile(
    r"<(?P<name>[A-Za-z_][A-Za-z0-9_.-]*)>(?P<value>[^<]*)</(?P=name)>"
)
PROPERTY_REF_RE = re.compile(r"^\$\((?P<name>[A-Za-z_][A-Za-z0-9_.-]*)\)$")
KLEI_CHANGE_LIST_RE = re.compile(
    r"\bpublic\s+const\s+uint\s+ChangeList\s*=\s*(?P<build>[0-9]+)U?\s*;"
)
KLEI_BUILD_BRANCH_RE = re.compile(
    r'\bpublic\s+const\s+string\s+BuildBranch\s*=\s*"(?P<branch>[^"]+)"\s*;'
)
OFFICIAL_BUILD_TITLE_PATTERNS = (
    re.compile(r"^\[Game (?:Update|Hotfix)\]\s*-\s*(?P<build>[0-9]+)\s*$", re.I),
    re.compile(r"^HOTFIX\s*-\s*(?P<build>[0-9]+)\s*$", re.I),
    re.compile(r"^Game Update\s+(?P<build>[0-9]+)(?:\s*-\s*.*)?$", re.I),
)


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


def download_steam_news(app_id: int, count: int = 100) -> list[dict]:
    if app_id != ONI_STEAM_APP_ID:
        raise ValueError(f"refusing unexpected Steam app id: {app_id}")
    query = urllib.parse.urlencode(
        {
            "appid": app_id,
            "count": count,
            "maxlength": 1,
            "format": "json",
        }
    )
    request = urllib.request.Request(
        f"{STEAM_NEWS_ENDPOINT}?{query}",
        headers={"User-Agent": "OniMods-reference-ci/1"},
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        payload = json.loads(response.read().decode("utf-8"))

    appnews = payload.get("appnews")
    if not isinstance(appnews, dict):
        raise ValueError("Steam news response is missing appnews")
    if int(appnews.get("appid", -1)) != app_id:
        raise ValueError("Steam news response app id does not match ONI")
    items = appnews.get("newsitems")
    if not isinstance(items, list):
        raise ValueError("Steam news response is missing newsitems")
    return items


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


def parse_klei_version_source(text: str) -> tuple[int, str]:
    change_list = KLEI_CHANGE_LIST_RE.search(text)
    if change_list is None:
        raise ValueError("KleiVersion source is missing numeric ChangeList")
    build_branch = KLEI_BUILD_BRANCH_RE.search(text)
    if build_branch is None:
        raise ValueError("KleiVersion source is missing BuildBranch")
    return int(change_list.group("build")), build_branch.group("branch")


def validate_repo_path(path_value: str, label: str) -> None:
    path = Path(path_value)
    if not path_value or path.is_absolute() or ".." in path.parts:
        raise ValueError(f"{label} must be repository-relative")


def validate_branch(branch: str, label: str) -> None:
    if not branch or branch.startswith("/") or ".." in branch.split("/"):
        raise ValueError(f"{label} is invalid")


def validate_manifest(manifest: dict) -> tuple[int, int, dict, dict, dict | None]:
    if manifest.get("schema") != 1:
        raise ValueError(f"unsupported manifest schema: {manifest.get('schema')!r}")

    official_build = int(manifest["official_oni_build"])
    official_tracking = manifest["official_tracking"]
    app_id = int(official_tracking["steam_app_id"])
    if app_id != ONI_STEAM_APP_ID:
        raise ValueError(
            f"official Steam app id must be ONI {ONI_STEAM_APP_ID}, got {app_id}"
        )
    release_gid = str(official_tracking["release_news_gid"])
    if not STEAM_NEWS_GID_RE.fullmatch(release_gid):
        raise ValueError("official Steam release news gid must be numeric")
    release_url = official_tracking.get("release_url")
    if not isinstance(release_url, str):
        raise ValueError("official Steam release URL must be a string")
    release_url_match = STEAM_NEWS_URL_RE.fullmatch(release_url)
    if release_url_match is None:
        raise ValueError("official Steam release URL must be an HTTPS Steam News URL")
    release_url_app_id = int(release_url_match.group("app_id"))
    if release_url_app_id != app_id:
        raise ValueError(
            "official Steam release URL app id does not match tracked app id: "
            f"{release_url_app_id} != {app_id}"
        )

    reference = manifest["reference_source"]
    method_body = manifest["method_body_source"]

    repository = reference["repository"]
    commit = reference["commit"]
    if "/" not in repository:
        raise ValueError(f"invalid reference repository: {repository!r}")
    if not COMMIT_RE.fullmatch(commit):
        raise ValueError("reference source must be pinned to a full lowercase commit SHA")

    license_path = reference.get("license_path")
    if not isinstance(license_path, str):
        raise ValueError("reference license path must be a string")
    validate_repo_path(license_path, "reference license path")
    if not COMMIT_RE.fullmatch(reference.get("license_blob_sha", "")):
        raise ValueError("reference license must include a full Git blob SHA")

    declared_build = int(reference["declared_oni_build"])
    marker = reference["version_marker"]
    validate_repo_path(marker.get("path", ""), "reference version marker path")
    if not COMMIT_RE.fullmatch(marker.get("blob_sha", "")):
        raise ValueError("reference version marker must include a full Git blob SHA")

    method_repository = method_body.get("repository", "")
    method_commit = method_body.get("commit", "")
    if "/" not in method_repository:
        raise ValueError(f"invalid method-body repository: {method_repository!r}")
    if not COMMIT_RE.fullmatch(method_commit):
        raise ValueError("method-body source must be pinned to a full lowercase commit SHA")
    method_build = int(method_body["official_oni_build"])
    if method_build != official_build:
        raise ValueError(
            "method-body source build does not match manifest official_oni_build: "
            f"{method_build} != {official_build}"
        )

    method_files = method_body.get("files")
    if not isinstance(method_files, list) or not method_files:
        raise ValueError("method-body source must include pinned source files")
    method_files_by_name = {}
    seen_method_paths = set()
    for item in method_files:
        name = item.get("name", "")
        path_value = item.get("path", "")
        if not name or name in method_files_by_name:
            raise ValueError(f"duplicate/invalid method-body source name: {name!r}")
        validate_repo_path(path_value, f"method-body source {name} path")
        if path_value in seen_method_paths:
            raise ValueError(f"duplicate method-body source path: {path_value}")
        if not COMMIT_RE.fullmatch(item.get("blob_sha", "")):
            raise ValueError(f"method-body source lacks Git blob identity: {path_value}")
        method_files_by_name[name] = item
        seen_method_paths.add(path_value)

    method_tracking = method_body.get("upstream_tracking")
    if not isinstance(method_tracking, dict):
        raise ValueError("method-body source must declare upstream tracking")
    validate_branch(method_tracking.get("branch", ""), "method-body upstream tracking branch")
    version_marker_name = method_tracking.get("version_marker", "")
    if version_marker_name not in method_files_by_name:
        raise ValueError("method-body upstream version marker must name a pinned source file")
    contract_names = method_tracking.get("contract_files")
    if not isinstance(contract_names, list) or not contract_names:
        raise ValueError("method-body upstream tracking must include contract files")
    if len(set(contract_names)) != len(contract_names):
        raise ValueError("method-body upstream contract files must be unique")
    if version_marker_name in contract_names:
        raise ValueError("method-body version marker must be separate from contract files")
    missing_contract_names = sorted(set(contract_names) - set(method_files_by_name))
    if missing_contract_names:
        raise ValueError(
            "method-body upstream contract files are not pinned source files: "
            + ", ".join(missing_contract_names)
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
        validate_branch(branch, "reference upstream tracking branch")
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
        missing_paths = sorted(set(manifest_files) - seen_paths)
        if missing_paths:
            raise ValueError(
                "reference upstream tracking does not cover pinned reference assemblies: "
                + ", ".join(missing_paths)
            )

    return official_build, declared_build, reference, marker, tracking


def parse_official_build_title(title: str) -> int | None:
    for pattern in OFFICIAL_BUILD_TITLE_PATTERNS:
        match = pattern.fullmatch(title.strip())
        if match:
            return int(match.group("build"))
    return None


def compare_official_steam_state(
    tracked_build: int,
    tracked_release_gid: str,
    news_items: list[dict],
) -> dict:
    matches = []
    for item in news_items:
        if not isinstance(item, dict):
            raise ValueError("Steam news item must be an object")
        title = item.get("title")
        if not isinstance(title, str):
            continue
        build = parse_official_build_title(title)
        if build is None:
            continue
        gid = str(item.get("gid", ""))
        if not STEAM_NEWS_GID_RE.fullmatch(gid):
            raise ValueError(f"official Steam build item has invalid gid: {gid!r}")
        published = item.get("date")
        if isinstance(published, bool) or not isinstance(published, int) or published <= 0:
            raise ValueError(f"official Steam build item has invalid date: {published!r}")
        matches.append(
            {
                "observed_build": build,
                "observed_gid": gid,
                "published_unix": published,
                "title": title,
            }
        )

    if not matches:
        raise ValueError("Steam news window contains no recognized ONI build announcement")

    latest = max(matches, key=lambda item: (item["published_unix"], item["observed_gid"]))
    latest["has_official_drift"] = latest["observed_build"] != tracked_build
    latest["release_identity_matches"] = (
        latest["observed_build"] == tracked_build
        and latest["observed_gid"] == tracked_release_gid
    )
    return latest


def fetch_official_steam_state(official_tracking: dict, tracked_build: int) -> dict:
    app_id = int(official_tracking["steam_app_id"])
    news_items = download_steam_news(app_id)
    return compare_official_steam_state(
        tracked_build,
        str(official_tracking["release_news_gid"]),
        news_items,
    )


def report_official_steam_state(
    official_tracking: dict,
    tracked_build: int,
    state: dict,
) -> None:
    observed_build = state["observed_build"]
    if observed_build < tracked_build:
        print(
            "OFFICIAL_ONI_STATUS status=unknown source=steam "
            f"appid={official_tracking['steam_app_id']} tracked_build={tracked_build} "
            f"observed_build={observed_build} gid={state['observed_gid']}"
        )
        print(
            "::warning title=Official ONI drift status inconclusive::"
            f"Steam's newest recognized build in the fetched news window is {observed_build}, "
            f"older than manifest build {tracked_build}; do not infer a rollback or freshness."
        )
        return

    status = "drift" if state["has_official_drift"] else "ok"
    if not state["has_official_drift"] and not state["release_identity_matches"]:
        status = "identity-mismatch"
    print(
        f"OFFICIAL_ONI_STATUS status={status} source=steam "
        f"appid={official_tracking['steam_app_id']} tracked_build={tracked_build} "
        f"observed_build={observed_build} gid={state['observed_gid']} "
        f"published_unix={state['published_unix']}"
    )

    if observed_build > tracked_build:
        print(
            "::warning title=Official ONI build advanced::"
            f"Steam reports ONI build {observed_build} after manifest build {tracked_build}. "
            "Keep compile-reference provenance separate and review reference/API lag before "
            "updating any immutable pin."
        )
    elif not state["release_identity_matches"]:
        print(
            "::warning title=Official ONI release identity changed::"
            f"Steam still reports build {tracked_build}, but its newest matching release gid "
            f"is {state['observed_gid']} instead of manifest gid "
            f"{official_tracking['release_news_gid']}. Review the official-release evidence."
        )


def verify_official_steam(official_tracking: dict, tracked_build: int) -> None:
    try:
        state = fetch_official_steam_state(official_tracking, tracked_build)
    except (OSError, ValueError, KeyError, json.JSONDecodeError, UnicodeDecodeError) as error:
        print(
            "OFFICIAL_ONI_STATUS status=unknown source=steam "
            f"appid={official_tracking.get('steam_app_id')} reason={type(error).__name__}"
        )
        print(
            "::warning title=Official ONI drift status unknown::"
            f"Could not inspect current Steam ONI news: {error}"
        )
        return
    report_official_steam_state(official_tracking, tracked_build, state)


def verify_reference_license(reference: dict) -> None:
    path_value = reference["license_path"]
    data = download_text(
        f"{RAW_PREFIX}{reference['repository']}/{reference['commit']}/{path_value}"
    )
    actual_blob = git_blob_sha(data)
    expected_blob = reference["license_blob_sha"]
    if actual_blob != expected_blob:
        raise ValueError(
            f"reference license blob changed: {actual_blob} != {expected_blob}"
        )
    print(
        "PROVENANCE reference-license "
        f"repo={reference['repository']} commit={reference['commit']} "
        f"path={path_value} blob={actual_blob}"
    )


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
    status = "drift" if state["has_reference_drift"] else "ok"
    print(
        f"UPSTREAM_REFERENCE_STATUS status={status} "
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
            f"{state['head_sha']}, but the tracked version marker and all pinned "
            "source-repo reference assembly Git blobs still match the pinned baseline."
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


def compare_method_body_upstream_state(
    source: dict,
    head_sha: str,
    head_version_text: str,
    head_files: dict[str, dict],
) -> dict:
    if not COMMIT_RE.fullmatch(head_sha):
        raise ValueError(f"invalid method-body upstream HEAD commit: {head_sha!r}")

    tracking = source["upstream_tracking"]
    files_by_name = {item["name"]: item for item in source["files"]}
    marker = files_by_name[tracking["version_marker"]]
    current_marker = head_files.get(marker["path"])
    if current_marker is None:
        raise ValueError(f"method-body upstream result missing {marker['path']}")
    marker_blob = current_marker.get("sha", "")
    if not COMMIT_RE.fullmatch(marker_blob):
        raise ValueError(f"invalid method-body marker blob identity for {marker['path']}")

    head_build, head_branch = parse_klei_version_source(head_version_text)
    changed_files = []
    for name in tracking["contract_files"]:
        item = files_by_name[name]
        current = head_files.get(item["path"])
        if current is None:
            raise ValueError(f"method-body upstream result missing {item['path']}")
        current_blob = current.get("sha", "")
        if not COMMIT_RE.fullmatch(current_blob):
            raise ValueError(f"invalid method-body upstream blob identity for {item['path']}")
        if current_blob != item["blob_sha"]:
            changed_files.append(item["path"])

    marker_changed = marker_blob != marker["blob_sha"]
    return {
        "head_sha": head_sha,
        "head_advanced": head_sha != source["commit"],
        "head_oni_build": head_build,
        "head_build_branch": head_branch,
        "marker_changed": marker_changed,
        "changed_files": changed_files,
        "has_method_body_drift": (
            head_build != int(source["official_oni_build"])
            or head_branch != "release"
            or marker_changed
            or bool(changed_files)
        ),
    }


def fetch_method_body_upstream_state(source: dict) -> dict:
    tracking = source["upstream_tracking"]
    repository = source["repository"]
    branch = tracking["branch"]
    branch_ref = urllib.parse.quote(branch, safe="")
    commit_data = download_json(f"{API_PREFIX}{repository}/commits/{branch_ref}")
    head_sha = commit_data.get("sha", "")
    if not COMMIT_RE.fullmatch(head_sha):
        raise ValueError("GitHub API did not return a valid method-body upstream HEAD commit")

    files_by_name = {item["name"]: item for item in source["files"]}
    marker = files_by_name[tracking["version_marker"]]
    current_files = {}
    for item in source["files"]:
        path_value = item["path"]
        encoded_path = urllib.parse.quote(path_value, safe="/")
        file_meta = download_json(
            f"{API_PREFIX}{repository}/contents/{encoded_path}?ref={head_sha}"
        )
        current_files[path_value] = {
            "sha": file_meta.get("sha", ""),
            "size": file_meta.get("size"),
        }

    marker_text = download_text(
        f"{RAW_PREFIX}{repository}/{head_sha}/{marker['path']}"
    ).decode("utf-8-sig")
    return compare_method_body_upstream_state(
        source,
        head_sha,
        marker_text,
        current_files,
    )


def report_method_body_upstream_state(source: dict, state: dict) -> None:
    tracking = source["upstream_tracking"]
    changed_files = ",".join(state["changed_files"]) or "none"
    status = "drift" if state["has_method_body_drift"] else "ok"
    print(
        f"UPSTREAM_METHOD_BODY_STATUS status={status} "
        f"branch={tracking['branch']} head={state['head_sha']} "
        f"head_oni_build={state['head_oni_build']} "
        f"head_build_branch={state['head_build_branch']} "
        f"marker_changed={str(state['marker_changed']).lower()} "
        f"changed_contract_files={changed_files}"
    )

    if state["has_method_body_drift"]:
        print(
            "::warning title=ONI method-body source drift detected::"
            f"{source['repository']} {tracking['branch']} is {state['head_sha']}; "
            f"build={state['head_oni_build']} branch={state['head_build_branch']}, "
            f"version-marker changed={state['marker_changed']}, "
            f"tracked contract files changed={changed_files}. Keep this source-only "
            "evidence separate from compile references and review the affected contracts "
            "before changing the immutable pin."
        )
    elif state["head_advanced"]:
        marker_note = "changed" if state["marker_changed"] else "unchanged"
        print(
            "::notice title=ONI method-body upstream advanced without contract drift::"
            f"{source['repository']} {tracking['branch']} advanced to {state['head_sha']}; "
            f"the version marker is {marker_note}, but it still declares release build "
            f"{state['head_oni_build']} and tracked method-body contract blobs are unchanged."
        )


def verify_method_body_upstream_head(source: dict) -> None:
    try:
        state = fetch_method_body_upstream_state(source)
    except (OSError, ValueError, KeyError, json.JSONDecodeError, UnicodeDecodeError) as error:
        print(
            "UPSTREAM_METHOD_BODY_STATUS status=unknown "
            f"source={source.get('repository')} reason={type(error).__name__}"
        )
        print(
            "::warning title=ONI method-body upstream drift status unknown::"
            f"Could not inspect current {source.get('repository')} source state: {error}"
        )
        return
    report_method_body_upstream_state(source, state)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", default="ci/oni-reference-assemblies.json")
    parser.add_argument("--verify-upstream", action="store_true")
    parser.add_argument("--verify-official", action="store_true")
    args = parser.parse_args()

    manifest = json.loads(Path(args.manifest).read_text(encoding="utf-8"))
    official_build, declared_build, reference, marker, tracking = validate_manifest(manifest)

    if args.verify_official:
        verify_official_steam(manifest["official_tracking"], official_build)
    if args.verify_upstream:
        verify_reference_license(reference)
        verify_upstream_marker(reference, marker, declared_build)
        if tracking is not None:
            verify_upstream_head(reference, marker, declared_build)
        verify_method_body_upstream_head(manifest["method_body_source"])

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
