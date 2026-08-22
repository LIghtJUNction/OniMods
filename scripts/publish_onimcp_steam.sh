#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export ONIM_PUBLISH_MOD="OniMcp"
exec "$SCRIPT_DIR/publish_cycletrim_steam.sh" "$@"
