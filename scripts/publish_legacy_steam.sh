#!/usr/bin/env bash
set -Eeuo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  cat <<'USAGE'
Usage: scripts/publish_legacy_steam.sh <ModKey> [stage]

Stages for a new Workshop item (publishedfileid absent):
  --prepare-first-item --plan <path>
  --create-first-item --plan <path> --expected-plan-sha256 <sha256>
  --verify-first-item --plan <path> --expected-plan-sha256 <sha256>
  --publish-first-item --plan <path> --expected-plan-sha256 <sha256>

For an existing publishedfileid, use --update-existing.
The target name/ID/staticID come from onim.toml and the selected mod package.
A new item also requires ONIM_PUBLISH_EXPECTED_OWNER (SteamID64) and
ONIM_PUBLISH_TAGS (comma-separated). Updates use the logged-in Steam account
unless ONIM_PUBLISH_EXPECTED_OWNER is set.
All uploads use the single-file Legacy ZIP API. SteamCMD and directory uploads
are rejected by repository policy.
USAGE
}

[[ $# -ge 1 ]] || { usage >&2; exit 2; }
MOD_KEY="$1"; shift
CONFIG="$ROOT/onim.toml"
PUBLISHER="$ROOT/tools/OniMods.SteamPublisher/bin/Release/net10.0/OniMods.SteamPublisher.dll"
read_config() {
  python - "$CONFIG" "$MOD_KEY" <<'PY'
import sys, tomllib
with open(sys.argv[1], "rb") as f:
    cfg = tomllib.load(f)
mod = cfg.get("mods", {}).get(sys.argv[2])
if mod is None:
    raise SystemExit(f"Unknown mod key in onim.toml: {sys.argv[2]}")
print(mod.get("publishedfileid", ""))
print(mod.get("workshop_title", sys.argv[2]))
print(mod["path"])
PY
}
mapfile -t MOD_CONFIG < <(read_config)
[[ ${#MOD_CONFIG[@]} -eq 3 ]] || { echo "Could not read selected mod configuration" >&2; exit 2; }
WORKSHOP_ID="${MOD_CONFIG[0]}"
WORKSHOP_TITLE="${MOD_CONFIG[1]}"
MOD_PATH="${MOD_CONFIG[2]}"
[[ "$WORKSHOP_ID" != "0" ]] || WORKSHOP_ID=""
DIST="$ROOT/dist/$MOD_KEY"
VDF="$ROOT/dist/$MOD_KEY.workshop.vdf"
STATIC_ID=""
if [[ -f "$DIST/mod.yaml" ]]; then
  STATIC_ID="$(sed -nE 's/^[[:space:]]*staticID:[[:space:]]*["'"'"']?([^"'"'"'#[:space:]]+).*/\1/p' "$DIST/mod.yaml" | head -n1)"
fi

MODE=""
PLAN=""
PLAN_HASH=""
while (($#)); do
  case "$1" in
    --prepare-first-item|--create-first-item|--verify-first-item|--publish-first-item|--update-existing)
      [[ -z "$MODE" ]] || { echo "Choose one publish stage" >&2; exit 2; }
      MODE="$1" ;;
    --plan|--expected-plan-sha256)
      [[ $# -ge 2 && -n "$2" && "$2" != --* ]] || { echo "Missing value for $1" >&2; exit 2; }
      if [[ "$1" == --plan ]]; then PLAN="$2"; else PLAN_HASH="$2"; fi
      shift ;;
    --steamcmd|--login|--contentfolder)
      echo "SteamCMD and Workshop directory uploads are forbidden for ONI" >&2; exit 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
  shift
done
[[ -n "$MODE" ]] || { usage >&2; exit 2; }

(cd "$ROOT" && cargo run --quiet --release -- --config "$CONFIG" --mod "$MOD_KEY" publish --non-interactive --auto-note --dry-run)
test -f "$VDF"
dotnet build "$ROOT/tools/OniMods.SteamPublisher/OniMods.SteamPublisher.csproj" -c Release >/dev/null
test -f "$PUBLISHER"
STATIC_ID="$(sed -nE 's/^[[:space:]]*staticID:[[:space:]]*["'"'"']?([^"'"'"'#[:space:]]+).*/\1/p' "$DIST/mod.yaml" | head -n1)"
[[ -n "$STATIC_ID" ]] || { echo "Selected package has no staticID" >&2; exit 1; }
run_publisher() {
  local app_id=636750 arg
  for arg in "$@"; do
    [[ "$arg" == --verify-first-item || "$arg" == --publish-first-item ]] && app_id=457140
  done
  env SteamAppId="$app_id" SteamGameId="$app_id" \
    ONIM_PUBLISH_NAME="$MOD_KEY" ONIM_PUBLISH_TITLE_CONTAINS="$WORKSHOP_TITLE" \
    ONIM_PUBLISH_STATIC_ID="$STATIC_ID" \
    ONIM_PUBLISH_WORKSHOP_ID="${WORKSHOP_ID:-0}" \
    ONIM_PUBLISH_EXPECTED_OWNER="${ONIM_PUBLISH_EXPECTED_OWNER:-}" \
    ONIM_PUBLISH_TAGS="${ONIM_PUBLISH_TAGS:-}" \
    dotnet "$PUBLISHER" "$@"
}
require_first_publication_identity() {
  [[ -n "${ONIM_PUBLISH_EXPECTED_OWNER:-}" ]] || {
    echo "Set ONIM_PUBLISH_EXPECTED_OWNER to the SteamID64 that will own the new item" >&2
    exit 2
  }
  [[ -n "${ONIM_PUBLISH_TAGS:-}" ]] || {
    echo "Set ONIM_PUBLISH_TAGS to a comma-separated Workshop tag list" >&2
    exit 2
  }
}
if [[ -z "$WORKSHOP_ID" ]]; then
  require_first_publication_identity
  case "$MODE" in
    --prepare-first-item)
      [[ -n "$PLAN" ]] || { echo "--plan is required" >&2; exit 2; }
      run_publisher --prepare-first-item --vdf "$VDF" --output "$PLAN" ;;
    --create-first-item|--verify-first-item|--publish-first-item)
      [[ -n "$PLAN" && -n "$PLAN_HASH" ]] || { echo "This stage requires --plan and --expected-plan-sha256" >&2; exit 2; }
      if [[ "$MODE" == --create-first-item ]]; then
        run_publisher "$MODE" --plan "$PLAN" --expected-plan-sha256 "$PLAN_HASH" --confirm-first-create-once
      elif [[ "$MODE" == --verify-first-item ]]; then
        run_publisher "$MODE" --plan "$PLAN" --expected-plan-sha256 "$PLAN_HASH"
      else
        run_publisher "$MODE" --plan "$PLAN" --expected-plan-sha256 "$PLAN_HASH" --confirm-first-publication
      fi ;;
    *) echo "No publishedfileid is configured; use a first-item stage" >&2; exit 2 ;;
  esac
else
  [[ "$MODE" == --update-existing ]] || { echo "Configured Workshop ID found; choose --update-existing" >&2; exit 2; }
  run_publisher --vdf "$VDF"
fi
