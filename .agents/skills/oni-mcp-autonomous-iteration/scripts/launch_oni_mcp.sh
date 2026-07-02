#!/usr/bin/env bash
set -euo pipefail

APP_ID="${ONI_STEAM_APP_ID:-457140}"
GAME_DIR="${ONI_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/OxygenNotIncluded}"
MCP_URL="${ONI_MCP_URL:-http://localhost:8788/mcp/}"
WAIT_SECONDS="${ONI_MCP_WAIT_SECONDS:-240}"
LOG_DIR="${ONI_LAUNCH_LOG_DIR:-/tmp}"
STEAM_WAIT_SECONDS="${ONI_STEAM_LAUNCH_WAIT_SECONDS:-90}"
STEAM_CLIENT_WAIT_SECONDS="${ONI_STEAM_CLIENT_WAIT_SECONDS:-120}"

mkdir -p "$LOG_DIR"
rm -f "$GAME_DIR/unity.lock"

# Keep process launch Steam-only. The MCP launch action may load saves after
# the game is running, but this script must not start the ONI binary directly.
if ! command -v steam >/dev/null 2>&1; then
  echo "steam=missing steam_only=true"
  exit 1
fi

if ! pgrep -x steam >/dev/null 2>&1; then
  echo "steam_client=absent starting"
  nohup setsid steam -silent >"$LOG_DIR/oni-steam-client.log" 2>&1 </dev/null &
  echo "steam_client_pid=$!"
  for _ in $(seq 1 "$STEAM_CLIENT_WAIT_SECONDS"); do
    pgrep -x steam >/dev/null 2>&1 && break
    sleep 1
  done
fi

if ! pgrep -x steam >/dev/null 2>&1; then
  echo "steam_client=timeout"
  tail -120 "$LOG_DIR/oni-steam-client.log" 2>/dev/null || true
  exit 1
fi

if pidof OxygenNotIncluded >/dev/null 2>&1; then
  echo "oni_process=running pid=$(pidof OxygenNotIncluded)"
else
  echo "oni_process=absent launching_via=steam app_id=$APP_ID"
  nohup steam "steam://run/$APP_ID" >"$LOG_DIR/oni-steam-uri.log" 2>&1 </dev/null &
  echo "steam_uri_pid=$!"
  for _ in $(seq 1 "$STEAM_WAIT_SECONDS"); do
    pidof OxygenNotIncluded >/dev/null 2>&1 && break
    sleep 1
  done
fi

deadline=$((SECONDS + WAIT_SECONDS))
while [ "$SECONDS" -lt "$deadline" ]; do
  if curl -fsS --max-time 2 "$MCP_URL" >/tmp/oni-mcp-probe.txt 2>/tmp/oni-mcp-probe.err; then
    echo "mcp=ready url=$MCP_URL pid=$(pidof OxygenNotIncluded 2>/dev/null || true)"
    cat /tmp/oni-mcp-probe.txt
    exit 0
  fi
  sleep 2
done

echo "mcp=timeout url=$MCP_URL pid=$(pidof OxygenNotIncluded 2>/dev/null || true)"
echo "--- steam uri log ---"
tail -80 "$LOG_DIR/oni-steam-uri.log" 2>/dev/null || true
echo "--- steam recent launch lines ---"
rg '457140|LaunchGameAction|AppError|CreatingProcess|ShowInterstitials|OxygenNotIncluded' \
  "$HOME/.local/share/Steam/logs/console-linux.txt" 2>/dev/null | tail -120 || true
echo "--- curl error ---"
cat /tmp/oni-mcp-probe.err 2>/dev/null || true
exit 1
