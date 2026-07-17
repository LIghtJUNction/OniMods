#!/bin/sh
set -eu

old_pid="${1:-}"
steam_bin="${2:-}"
case "$old_pid" in
  ''|*[!0-9]*) exit 64 ;;
esac
case "$steam_bin" in
  /*) ;;
  *) exit 64 ;;
esac
[ -x "$steam_bin" ] || exit 69

while kill -0 "$old_pid" 2>/dev/null; do
  sleep 1
done

exec "$steam_bin" steam://run/457140
