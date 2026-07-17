#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAUNCHER="$SCRIPT_DIR/launch_oni_mcp.sh"
TEST_ROOT="$(mktemp -d)"
trap 'rm -rf "$TEST_ROOT"' EXIT

FAKE_BIN="$TEST_ROOT/bin"
mkdir -p "$FAKE_BIN"

cat >"$FAKE_BIN/fake-command" <<'FAKE'
#!/bin/bash
set -euo pipefail

command_name="${0##*/}"
state="${ONI_TEST_STATE:?}"
case "$command_name" in
  pgrep)
    exit 0
    ;;
  pidof)
    [ -s "$state/pids" ] || exit 1
    cat "$state/pids"
    ;;
  steam)
    : >"$state/steam-requested"
    printf '%s\n' "$*" >"$state/steam-args"
    exit 0
    ;;
  curl)
    if [ -f "$state/healthy" ]; then
      printf '{"status":"ok"}\n'
      exit 0
    fi
    exit 22
    ;;
  sleep)
    /bin/sleep 0.02
    count=0
    [ ! -f "$state/sleeps" ] || count="$(cat "$state/sleeps")"
    count=$((count + 1))
    printf '%s\n' "$count" >"$state/sleeps"
    case "${ONI_TEST_MODE:?}" in
      delayed_success)
        if [ ! -f "$state/steam-requested" ]; then
          if [ "$count" -eq 1 ]; then
            printf '102\n' >"$state/pids"
          elif [ "$count" -ge 2 ]; then
            : >"$state/pids"
          fi
        else
          printf '202\n' >"$state/pids"
          : >"$state/healthy"
        fi
        ;;
      reused_old)
        if [ ! -f "$state/steam-requested" ]; then
          : >"$state/pids"
        else
          printf '101\n' >"$state/pids"
          : >"$state/healthy"
        fi
        ;;
      old_timeout|no_launch) ;;
    esac
    ;;
  *)
    echo "unexpected fake command: $command_name" >&2
    exit 127
    ;;
esac
FAKE
chmod +x "$FAKE_BIN/fake-command"
for command_name in pgrep pidof steam curl sleep; do
  ln -s fake-command "$FAKE_BIN/$command_name"
done

run_case() {
  local name="$1" initial_pids="$2" expected_status="$3" expected_output="$4" case_path="${5:-$FAKE_BIN:$PATH}"
  local state="$TEST_ROOT/$name" output="$TEST_ROOT/$name.out" status
  mkdir -p "$state/home" "$state/game" "$state/log"
  printf '%s' "$initial_pids" >"$state/pids"
  if [ "$name" = "healthy_fast" ]; then
    : >"$state/healthy"
    : >"$state/game/unity.lock"
  fi

  set +e
  PATH="$case_path" \
    HOME="$state/home" \
    ONI_STEAM_APP_ID=999999 \
    ONI_TEST_STATE="$state" \
    ONI_TEST_MODE="$name" \
    ONI_GAME_DIR="$state/game" \
    ONI_LAUNCH_LOG_DIR="$state/log" \
    ONI_OLD_PROCESS_EXIT_WAIT_SECONDS=3 \
    ONI_STEAM_LAUNCH_WAIT_SECONDS=3 \
    ONI_MCP_WAIT_SECONDS=3 \
    /bin/bash "$LAUNCHER" >"$output" 2>&1
  status=$?
  set -e

  if [ "$status" -ne "$expected_status" ]; then
    cat "$output" >&2
    echo "$name: expected status $expected_status, got $status" >&2
    return 1
  fi
  if ! grep -F "$expected_output" "$output" >/dev/null; then
    cat "$output" >&2
    echo "$name: missing output: $expected_output" >&2
    return 1
  fi
}

HEALTHY_BIN="$TEST_ROOT/healthy-bin"
mkdir -p "$HEALTHY_BIN"
ln -s "$FAKE_BIN/fake-command" "$HEALTHY_BIN/pidof"
ln -s "$FAKE_BIN/fake-command" "$HEALTHY_BIN/curl"
ln -s /usr/bin/mkdir "$HEALTHY_BIN/mkdir"
ln -s /usr/bin/cat "$HEALTHY_BIN/cat"

run_case healthy_fast '303
' 0 'mcp=ready url=http://localhost:8788/mcp/ pid=303 existing_process=true' "$HEALTHY_BIN"
[ ! -f "$TEST_ROOT/healthy_fast/steam-requested" ]
[ -f "$TEST_ROOT/healthy_fast/game/unity.lock" ]

run_case delayed_success '101 102
' 0 'mcp=ready url=http://localhost:8788/mcp/ pid=202 postlaunch_process=true'
[ -f "$TEST_ROOT/delayed_success/steam-requested" ]
grep -Fx 'steam://run/457140' "$TEST_ROOT/delayed_success/steam-args" >/dev/null
if grep -F 'ONI_STEAM_APP_ID' "$LAUNCHER" >/dev/null; then
  echo "launcher must not accept ONI_STEAM_APP_ID overrides" >&2
  exit 1
fi

run_case old_timeout '101 102
' 1 'oni_process=stale_timeout pids=101 102'
[ ! -f "$TEST_ROOT/old_timeout/steam-requested" ]

run_case no_launch '' 1 'oni_process=launch_timeout prelaunch_pids=none'
[ -f "$TEST_ROOT/no_launch/steam-requested" ]

run_case reused_old '101
' 1 'oni_process=launch_timeout prelaunch_pids=101 current=101'
[ -f "$TEST_ROOT/reused_old/steam-requested" ]

echo "launch_oni_mcp race verification passed"
