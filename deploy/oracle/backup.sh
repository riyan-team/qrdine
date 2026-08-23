#!/usr/bin/env bash
# Dumps the database once a day, gzipped, keeping the newest 14 files.
set -e
mkdir -p /backups
while true; do
  ts=$(date +%Y%m%d-%H%M)
  if pg_dump -h db -U qrdine qrdine | gzip > "/backups/qrdine-$ts.sql.gz"; then
    echo "backup: wrote qrdine-$ts.sql.gz"
  else
    echo "backup: FAILED at $ts"
  fi
  ls -1t /backups/qrdine-*.sql.gz 2>/dev/null | tail -n +15 | xargs -r rm -f
  sleep 86400
done
