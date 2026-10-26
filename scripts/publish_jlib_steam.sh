#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ "${1:-}" == "--add-jlib-dependency" || "${1:-}" == "--verify-jlib-dependency" ]]; then
  JLIB_PUBLISH_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
  JLIB_PUBLISH_PROJECT="$JLIB_PUBLISH_ROOT/tools/OniMods.SteamPublisher/OniMods.SteamPublisher.csproj"
  dotnet build "$JLIB_PUBLISH_PROJECT" -c Release --disable-build-servers >/dev/null
  exec env SteamAppId=457140 SteamGameId=457140 \
    ONIM_PUBLISH_CREATOR_APP_ID=636750 ONIM_PUBLISH_CONSUMER_APP_ID=457140 \
    ONIM_PUBLISH_EXPECTED_OWNER=76561199137573787 ONIM_PUBLISH_NAME=Jlib \
    ONIM_PUBLISH_WORKSHOP_ID=0 ONIM_PUBLISH_TITLE_CONTAINS=Jlib \
    dotnet "$JLIB_PUBLISH_ROOT/tools/OniMods.SteamPublisher/bin/Release/net10.0/OniMods.SteamPublisher.dll" "$@"
fi
export ONIM_PUBLISH_MOD="Jlib"
exec "$SCRIPT_DIR/publish_cycletrim_steam.sh" "$@"
