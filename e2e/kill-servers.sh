#!/usr/bin/env bash
# Frees the given dev-server port(s) by killing whatever LISTENS on them.
# Port-based (not name-based): a pkill -f 'Booking.Host' here would also match
# the spawning webServer shell's own cmdline and kill it. Call with ONLY the
# port(s) this webServer owns — entries spawn concurrently, and killing a
# sibling's port would murder the other server right after it binds.
# Usage: kill-servers.sh <port> [port...]
for port in "$@"; do
  pids=$(ss -ltnp 2>/dev/null | awk -v p=":${port}" '$4 ~ p' | grep -o 'pid=[0-9]*' | cut -d= -f2 | sort -u)
  if [ -n "$pids" ]; then
    kill -9 $pids 2>/dev/null
  fi
done
sleep 0.5
i=0
for port in "$@"; do
  while ss -ltn | grep -q ":${port} "; do
    sleep 0.5
    i=$((i + 1))
    [ "$i" -ge 30 ] && break 2
  done
done
exit 0
