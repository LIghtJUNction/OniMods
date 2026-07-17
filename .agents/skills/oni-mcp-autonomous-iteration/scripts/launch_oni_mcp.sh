#!/usr/bin/env bash
set -euo pipefail

readonly APP_ID=457140
GAME_DIR="${ONI_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/OxygenNotIncluded}"
MCP_URL="${ONI_MCP_URL:-http://localhost:8788/mcp/}"
WAIT_SECONDS="${ONI_MCP_WAIT_SECONDS:-240}"
LOG_DIR="${ONI_LAUNCH_LOG_DIR:-/tmp}"
STEAM_WAIT_SECONDS="${ONI_STEAM_LAUNCH_WAIT_SECONDS:-90}"
STEAM_CLIENT_WAIT_SECONDS="${ONI_STEAM_CLIENT_WAIT_SECONDS:-120}"
OLD_PROCESS_WAIT_SECONDS="${ONI_OLD_PROCESS_EXIT_WAIT_SECONDS:-30}"
PROBE_OUTPUT="$LOG_DIR/oni-mcp-probe.txt"
PROBE_ERROR="$LOG_DIR/oni-mcp-probe.err"

oni_pids() {
  pidof OxygenNotIncluded 2>/dev/null || true
}

pid_in_list() {
  case " $2 " in
    *" $1 "*) return 0 ;;
    *) return 1 ;;
  esac
}

old_pids_still_running() {
  local current old_pid
  current="$(oni_pids)"
  for old_pid in $1; do
    pid_in_list "$old_pid" "$current" && return 0
  done
  return 1
}

new_oni_pids() {
  local current pid result=""
  current="$(oni_pids)"
  for pid in $current; do
    if ! pid_in_list "$pid" "$1"; then
      result="${result:+$result }$pid"
    fi
  done
  printf '%s' "$result"
}

probe_mcp() {
  curl -fsS --max-time 2 "$MCP_URL" >"$PROBE_OUTPUT" 2>"$PROBE_ERROR"
}

mkdir -p "$LOG_DIR"

# A healthy running instance is already the requested outcome. Check it before
# touching unity.lock or inspecting/starting Steam so this path is read-only.
prelaunch_pids="$(oni_pids)"
if [ -n "$prelaunch_pids" ] && probe_mcp; then
  echo "mcp=ready url=$MCP_URL pid=$prelaunch_pids existing_process=true"
  cat "$PROBE_OUTPUT"
  exit 0
fi

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

prelaunch_pids="$(oni_pids)"
if [ -n "$prelaunch_pids" ] && probe_mcp; then
  echo "mcp=ready url=$MCP_URL pid=$prelaunch_pids existing_process=true"
  cat "$PROBE_OUTPUT"
  exit 0
fi

if [ -n "$prelaunch_pids" ]; then
  echo "oni_process=stale_wait pids=$prelaunch_pids timeout=$OLD_PROCESS_WAIT_SECONDS"
  for _ in $(seq 1 "$OLD_PROCESS_WAIT_SECONDS"); do
    old_pids_still_running "$prelaunch_pids" || break
    sleep 1
  done
  if old_pids_still_running "$prelaunch_pids"; then
    echo "oni_process=stale_timeout pids=$prelaunch_pids current=$(oni_pids)"
    exit 1
  fi
fi

# Exclude every PID visible before the URI, including one that appeared while
# the previous process set was draining.
current_pids="$(oni_pids)"
for pid in $current_pids; do
  if ! pid_in_list "$pid" "$prelaunch_pids"; then
    prelaunch_pids="${prelaunch_pids:+$prelaunch_pids }$pid"
  fi
done

echo "oni_process=launching_via_steam app_id=$APP_ID prelaunch_pids=${prelaunch_pids:-none}"
nohup steam "steam://run/$APP_ID" >"$LOG_DIR/oni-steam-uri.log" 2>&1 </dev/null &
echo "steam_uri_pid=$!"

launched_pids=""
for _ in $(seq 1 "$STEAM_WAIT_SECONDS"); do
  launched_pids="$(new_oni_pids "$prelaunch_pids")"
  [ -n "$launched_pids" ] && break
  sleep 1
done
if [ -z "$launched_pids" ]; then
  echo "oni_process=launch_timeout prelaunch_pids=${prelaunch_pids:-none} current=$(oni_pids)"
  tail -80 "$LOG_DIR/oni-steam-uri.log" 2>/dev/null || true
  exit 1
fi

for _ in $(seq 1 "$WAIT_SECONDS"); do
  launched_pids="$(new_oni_pids "$prelaunch_pids")"
  if [ -n "$launched_pids" ] && probe_mcp; then
    echo "mcp=ready url=$MCP_URL pid=$launched_pids postlaunch_process=true"
    cat "$PROBE_OUTPUT"
    exit 0
  fi
  sleep 1
done

echo "mcp=timeout url=$MCP_URL prelaunch_pids=${prelaunch_pids:-none} current=$(oni_pids)"
echo "--- steam uri log ---"
tail -80 "$LOG_DIR/oni-steam-uri.log" 2>/dev/null || true
echo "--- steam recent launch lines ---"
rg '457140|LaunchGameAction|AppError|CreatingProcess|ShowInterstitials|OxygenNotIncluded' \
  "$HOME/.local/share/Steam/logs/console-linux.txt" 2>/dev/null | tail -120 || true
echo "--- curl error ---"
cat "$PROBE_ERROR" 2>/dev/null || true
exit 1
