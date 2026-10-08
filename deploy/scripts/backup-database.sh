#!/usr/bin/env bash
set -euo pipefail

root=/opt/flowhearth
backup_dir="$root/shared/backups"
defaults_file="$root/shared/migrator.my.cnf"
timestamp=$(date -u +%Y%m%dT%H%M%SZ)
backup_path="$backup_dir/flowhearth-$timestamp.sql.gz"

test -r "$defaults_file"
install -d -m 0750 "$backup_dir"

mysqldump \
  --defaults-extra-file="$defaults_file" \
  --single-transaction \
  --routines \
  --triggers \
  --events \
  --set-gtid-purged=OFF \
  flowhearth | gzip -9 > "$backup_path"

test -s "$backup_path"
sha256sum "$backup_path" > "$backup_path.sha256"
chmod 0640 "$backup_path" "$backup_path.sha256"
printf '%s\n' "$backup_path"
