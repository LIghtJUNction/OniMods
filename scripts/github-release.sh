#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

mod_name="oni_mcp"
assembly_name="OniMcp"
tag=""
title=""
notes=""
draft=0
prerelease=0

usage() {
  cat <<'USAGE'
Usage:
  scripts/github-release.sh [--tag v0.1.8] [--title "OniMcp 0.1.8"] [--notes-file FILE] [--draft] [--prerelease]

Builds the OniMcp mod with onim, creates the git tag if needed, pushes it,
and creates a GitHub Release containing:
  dist/OniMcp.zip
  dist/OniMcp-src.tar.gz

If --tag is omitted, the script reads <ModVersion> from mods/oni_mcp/OniMcp.csproj
and uses v<ModVersion>.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tag)
      tag="${2:?missing value for --tag}"
      shift 2
      ;;
    --title)
      title="${2:?missing value for --title}"
      shift 2
      ;;
    --notes-file)
      notes="${2:?missing value for --notes-file}"
      shift 2
      ;;
    --draft)
      draft=1
      shift
      ;;
    --prerelease)
      prerelease=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

require_cmd() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

require_cmd cargo
require_cmd gh
require_cmd git

gh auth status >/dev/null

if [[ -z "${tag}" ]]; then
  version="$(
    sed -n 's:.*<ModVersion>\(.*\)</ModVersion>.*:\1:p' mods/oni_mcp/OniMcp.csproj | head -n 1
  )"
  if [[ -z "${version}" ]]; then
    echo "Could not read <ModVersion> from mods/oni_mcp/OniMcp.csproj" >&2
    exit 1
  fi
  tag="v${version}"
fi

if [[ -z "${title}" ]]; then
  title="OniMcp ${tag#v}"
fi

if git status --porcelain | grep -q .; then
  cat >&2 <<'MSG'
Working tree is not clean.
Commit or stash changes before creating a release so the tag matches the built source.
MSG
  exit 1
fi

echo "Building onim..."
cargo build --release --locked

echo "Building ${mod_name} release package..."
./target/release/onim -m "${mod_name}" build --release

zip_path="dist/${assembly_name}.zip"
src_path="dist/${assembly_name}-src.tar.gz"

if [[ ! -f "${zip_path}" ]]; then
  echo "Missing release package: ${zip_path}" >&2
  exit 1
fi

if [[ ! -f "${src_path}" ]]; then
  echo "Missing source package: ${src_path}" >&2
  exit 1
fi

if gh release view "${tag}" >/dev/null 2>&1; then
  echo "GitHub release already exists: ${tag}" >&2
  exit 1
fi

if git rev-parse -q --verify "refs/tags/${tag}" >/dev/null; then
  echo "Using existing local tag: ${tag}"
else
  echo "Creating local tag: ${tag}"
  git tag -a "${tag}" -m "${title}"
fi

echo "Pushing tag: ${tag}"
git push origin "${tag}"

release_args=(
  release create "${tag}"
  "${zip_path}"
  "${src_path}"
  --title "${title}"
)

if [[ -n "${notes}" ]]; then
  release_args+=(--notes-file "${notes}")
else
  release_args+=(--generate-notes)
fi

if [[ "${draft}" -eq 1 ]]; then
  release_args+=(--draft)
fi

if [[ "${prerelease}" -eq 1 ]]; then
  release_args+=(--prerelease)
fi

echo "Creating GitHub release: ${tag}"
gh "${release_args[@]}"

echo "Release created: ${tag}"
