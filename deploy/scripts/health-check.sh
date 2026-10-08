#!/usr/bin/env bash
set -euo pipefail

live_url=http://127.0.0.1:5100/health/live
ready_url=http://127.0.0.1:5100/health/ready

for attempt in $(seq 1 60); do
  if live=$(curl --fail --silent --show-error --connect-timeout 2 "$live_url" 2>/dev/null) \
    && ready=$(curl --fail --silent --show-error --connect-timeout 2 "$ready_url" 2>/dev/null); then
    printf '%s\n%s\n' "$live" "$ready"
    exit 0
  fi

  sleep 1
done

echo 'FlowHearth health checks did not become ready within 60 attempts.' >&2
exit 1
