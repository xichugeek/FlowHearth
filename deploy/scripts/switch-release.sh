#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 || ! $1 =~ ^[0-9]{14}$ ]]; then
  echo "Usage: switch-release.sh <yyyyMMddHHmmss>" >&2
  exit 2
fi

root=/opt/flowhearth
release="$root/releases/$1"

test -f "$release/api/FlowHearth.Api.dll"
test -f "$release/migrator/FlowHearth.DbMigrator.dll"
test -f "$release/web/index.html"
test -f "$release/SHA256SUMS"

# Archives are built on Windows, where POSIX modes are not authoritative.
# Normalize the immutable release tree before the service account reads it.
chown -R root:root "$release"
find "$release" -type d -exec chmod 0755 {} +
find "$release" -type f -exec chmod 0644 {} +

(
  cd "$release"
  sha256sum --check SHA256SUMS
)

runuser -u flowhearth -- test -x "$release/api"
runuser -u flowhearth -- test -r "$release/api/FlowHearth.Api.dll"

ln -s "$release" "$root/current.next"
mv -Tf "$root/current.next" "$root/current"
systemctl restart flowhearth.service
"$root/deploy/health-check.sh"
