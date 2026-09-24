#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MOD_KEY="${ONIM_PUBLISH_MOD:-CycleTrim}"
CREATOR_APP_ID="636750"
CONSUMER_APP_ID="457140"
EXPECTED_OWNER="76561199137573787"
PUBLISHER_PROJECT="$ROOT/tools/OniMods.SteamPublisher/OniMods.SteamPublisher.csproj"
PUBLISHER_DLL="$ROOT/tools/OniMods.SteamPublisher/bin/Release/net10.0/OniMods.SteamPublisher.dll"
case "$MOD_KEY" in
CycleTrim)
  EXPECTED_ID="3766318556"
  TITLE_CONTAINS="CycleTrim"
  ;;
OniMcp)
  EXPECTED_ID="3731864673"
  TITLE_CONTAINS="ONI MCP Server"
  ;;
*)
  echo "Unsupported publish target: $MOD_KEY" >&2
  exit 2
  ;;
esac
DIST_DIR="$ROOT/dist/$MOD_KEY"
VDF_PATH="$ROOT/dist/$MOD_KEY.workshop.vdf"
DRY_RUN=false
PREFLIGHT_ONLY=false
SKIP_TESTS=false
ALLOW_DIRTY=false

usage() {
  cat <<'EOF'
Usage: scripts/publish_cycletrim_steam.sh [options]

  --dry-run      Build, test, and validate metadata without uploading
  --preflight-only  Query both Steam App contexts without uploading
  --skip-tests   Skip contract, binary, benchmark, and Rust tests
  --allow-dirty  Permit publishing an uncommitted worktree
  -h, --help     Show this help

The publisher uses the logged-in Steam client and uploads a single legacy ZIP.
SteamCMD's directory upload is incompatible with ONI's Workshop loader.
It verifies app, item, owner, and installed ZIP before reporting success.
EOF
}

while (($#)); do
  case "$1" in
  --dry-run) DRY_RUN=true ;;
  --preflight-only) PREFLIGHT_ONLY=true ;;
  --steamcmd | --login)
    echo "SteamCMD directory upload cannot produce an ONI-compatible legacy item" >&2
    exit 2
    ;;
  --skip-tests) SKIP_TESTS=true ;;
  --allow-dirty) ALLOW_DIRTY=true ;;
  -h | --help)
    usage
    exit 0
    ;;
  *)
    echo "Unknown option: $1" >&2
    usage >&2
    exit 2
    ;;
  esac
  shift
done

if [[ "$DRY_RUN" == true && "$PREFLIGHT_ONLY" == true ]]; then
  echo "--dry-run and --preflight-only cannot be combined" >&2
  exit 2
fi

lock_dir="${XDG_RUNTIME_DIR:-/tmp}/onim-${MOD_KEY,,}-publish.lock.d"
mkdir "$lock_dir" 2>/dev/null || {
  echo "Another $MOD_KEY publish is running" >&2
  exit 1
}
trap 'rmdir "$lock_dir" 2>/dev/null || true' EXIT

validate_target() {
  local configured_id
  configured_id="$(
    python - "$ROOT/onim.toml" "$MOD_KEY" <<'PY'
import sys
import tomllib

with open(sys.argv[1], "rb") as handle:
    config = tomllib.load(handle)
print(config.get("mods", {}).get(sys.argv[2], {}).get("publishedfileid", ""))
PY
  )"
  if [[ "$configured_id" != "$EXPECTED_ID" ]]; then
    echo "Refusing upload: $MOD_KEY publishedfileid is '$configured_id', expected '$EXPECTED_ID'" >&2
    exit 1
  fi

  if [[ "$DRY_RUN" != true ]]; then
    local page
    page="$(curl --proto '=https' --tlsv1.2 -fsSL --max-time 20 \
      "https://steamcommunity.com/sharedfiles/filedetails/?id=$EXPECTED_ID")"
    grep -Fq "steamcommunity.com/app/$CONSUMER_APP_ID" <<<"$page" || {
      echo "Refusing upload: Workshop item does not belong to ONI app $CONSUMER_APP_ID" >&2
      exit 1
    }
    grep -Fqi "$TITLE_CONTAINS" <<<"$page" || {
      echo "Refusing upload: Workshop item title does not contain $TITLE_CONTAINS" >&2
      exit 1
    }
  fi
}

require_clean_worktree() {
  [[ "$ALLOW_DIRTY" == true ]] && return
  if [[ -n "$(git -C "$ROOT" status --porcelain --untracked-files=normal)" ]]; then
    echo "Refusing upload from a dirty worktree. Commit the release or pass --allow-dirty." >&2
    exit 1
  fi
}

run_tests() {
  [[ "$SKIP_TESTS" == true ]] && return
  if [[ "$MOD_KEY" == "CycleTrim" ]]; then
    (cd "$ROOT" && python scripts/verify_cycletrim_target_contract.py)
    (cd "$ROOT" && cargo run --quiet --release -- \
      --config "$ROOT/onim.toml" --mod CycleTrim build --release)
    (cd "$ROOT" && python scripts/verify_cycletrim_release_binary.py)
    (cd "$ROOT" && dotnet run --project benchmarks/CycleTrim.BrainBenchmarks/CycleTrim.BrainBenchmarks.csproj)
  else
    (cd "$ROOT" && dotnet build mods/OniMcp/OniMcp.csproj -c Debug -warnaserror)
    (cd "$ROOT" && dotnet build mods/OniMcp/OniMcp.csproj -c Release -warnaserror)
    (cd "$ROOT" && dotnet build mods/CycleTrim/CycleTrim.csproj -c Release -warnaserror)
    (cd "$ROOT" && dotnet format mods/OniMcp/OniMcp.csproj style \
      --diagnostics IDE0005 --verify-no-changes --no-restore)
    (cd "$ROOT" && for verifier in scripts/verify_*.py; do python "$verifier"; done)
    (cd "$ROOT" && python scripts/onimcp_verify_parsing.py)
  fi
  (cd "$ROOT" && cargo test)
  (cd "$ROOT" && dotnet build "$PUBLISHER_PROJECT" -c Release)
  (cd "$ROOT" && python scripts/test_legacy_workshop_package.py)
  (cd "$ROOT" && dotnet run --project tests/OniMods.SteamPublisher.Tests/OniMods.SteamPublisher.Tests.csproj -c Release)
}

run_publisher() {
  local context_app_id="$CREATOR_APP_ID"
  local argument
  for argument in "$@"; do
    if [[ "$argument" == "--consumer-context" || "$argument" == "--verify-installed" ]]; then
      context_app_id="$CONSUMER_APP_ID"
    fi
  done
  env SteamAppId="$context_app_id" SteamGameId="$context_app_id" \
    ONIM_PUBLISH_CREATOR_APP_ID="$CREATOR_APP_ID" \
    ONIM_PUBLISH_CONSUMER_APP_ID="$CONSUMER_APP_ID" \
    ONIM_PUBLISH_WORKSHOP_ID="$EXPECTED_ID" \
    ONIM_PUBLISH_EXPECTED_OWNER="$EXPECTED_OWNER" \
    ONIM_PUBLISH_TITLE_CONTAINS="$TITLE_CONTAINS" \
    ONIM_PUBLISH_NAME="$MOD_KEY" \
    dotnet "$PUBLISHER_DLL" "$@"
}

build_metadata() {
  (cd "$ROOT" && cargo run --quiet --release -- \
    --config "$ROOT/onim.toml" --mod "$MOD_KEY" publish \
    --non-interactive --auto-note --dry-run)
  test -f "$VDF_PATH"
  test ! -e "$DIST_DIR/workshop.vdf"
  grep -Eq "\"publishedfileid\"[[:space:]]+\"$EXPECTED_ID\"" "$VDF_PATH"
  dotnet build "$PUBLISHER_PROJECT" -c Release >/dev/null
  run_publisher --validate-vdf --vdf "$VDF_PATH"
}

start_steam_client() {
  pgrep -x steam >/dev/null 2>&1 && return

  local runtime_dir wayland_display display
  runtime_dir="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
  wayland_display="${WAYLAND_DISPLAY:-}"
  if [[ -z "$wayland_display" ]]; then
    local socket
    for socket in "$runtime_dir"/wayland-*; do
      if [[ -S "$socket" ]]; then
        wayland_display="$(basename "$socket")"
        break
      fi
    done
  fi
  display="${DISPLAY:-}"
  if [[ -z "$display" && -S /tmp/.X11-unix/X0 ]]; then
    display=":0"
  fi

  env DISPLAY="$display" WAYLAND_DISPLAY="$wayland_display" \
    XDG_RUNTIME_DIR="$runtime_dir" \
    DBUS_SESSION_BUS_ADDRESS="${DBUS_SESSION_BUS_ADDRESS:-unix:path=$runtime_dir/bus}" \
    nohup steam -silent </dev/null >/tmp/onim-steam-client.log 2>&1 &

  for _ in {1..60}; do
    pgrep -x steam >/dev/null 2>&1 && return
    sleep 2
  done
  echo "Steam client did not start" >&2
  exit 1
}

client_preflight() {
  dotnet build "$PUBLISHER_PROJECT" -c Release >/dev/null
  start_steam_client

  local creator_log="$ROOT/dist/$MOD_KEY.creator-preflight.log"
  local consumer_log="$ROOT/dist/$MOD_KEY.consumer-preflight.log"
  mkdir -p "$ROOT/dist"
  : >"$creator_log"
  : >"$consumer_log"
  for _ in {1..90}; do
    if run_publisher --query-only >"$creator_log" 2>&1 \
      && grep -Fq "steamContext=Creator appId=$CREATOR_APP_ID" "$creator_log" \
      && run_publisher --query-only --consumer-context >"$consumer_log" 2>&1 \
      && grep -Fq "steamContext=Consumer appId=$CONSUMER_APP_ID" "$consumer_log"; then
      cat "$creator_log" "$consumer_log"
      return
    fi
    sleep 2
  done
  tail -20 "$creator_log" "$consumer_log" >&2
  echo "Steam creator/consumer context preflight failed" >&2
  exit 1
}

validate_target

if [[ "$PREFLIGHT_ONLY" == true ]]; then
  pgrep -x steam >/dev/null 2>&1 || {
    echo "Steam client must already be running for read-only preflight" >&2
    exit 1
  }
  client_preflight
  echo "$MOD_KEY Steam creator/consumer preflight passed without upload"
  exit 0
fi

require_clean_worktree

if [[ "$DRY_RUN" != true ]]; then
  client_preflight
fi

run_tests
build_metadata
if [[ "$DRY_RUN" == true ]]; then
  echo "$MOD_KEY Steam dry-run passed"
  exit 0
fi

# Builds and metadata generation can touch tracked files. Re-check immediately
# before uploading so an unexpected mutation cannot be published silently.
require_clean_worktree

run_publisher --vdf "$VDF_PATH"
