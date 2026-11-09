#!/bin/bash
# Starts a throwaway copy of Dine In on 127.0.0.1:5199 with its own Postgres cluster on 127.0.0.1:5434.
# Production's Postgres (5433), its access rules and its data are not touched.
# It shares no secrets with production: the database password, JWT secret and operator password are all fresh.
set -euo pipefail
D=/var/tmp/qrdine-e2e-pg
PGBIN=/usr/pgsql-18/bin
# Runs whatever build is in /var/tmp/qrdine-e2e-app if there is one, so a new build can be checked before it goes live.
APP=/opt/qrdine/app
[ -f /var/tmp/qrdine-e2e-app/QrDine.Api.dll ] && APP=/var/tmp/qrdine-e2e-app
cd /
[ -e "$D" ] && { echo "$D already exists; tear down first"; exit 1; }
rand() { python3 -c "import secrets,sys;print(secrets.token_hex(int(sys.argv[1])))" "$1"; }

install -d -o postgres -g postgres -m 700 "$D"
PW="$(rand 24)"
( umask 077; printf '%s\n' "$PW" > "$D/pw" ); chown postgres:postgres "$D/pw"
sudo -u postgres "$PGBIN/initdb" -D "$D/data" -U postgres -A scram-sha-256 --pwfile="$D/pw" -E UTF8 >/dev/null
sudo -u postgres "$PGBIN/pg_ctl" -D "$D/data" -l "$D/log" -w \
  -o "-p 5434 -c listen_addresses=127.0.0.1 -c unix_socket_directories=$D" start >/dev/null
PGPASSWORD="$PW" "$PGBIN/psql" -q -h 127.0.0.1 -p 5434 -U postgres -d postgres \
  -c "CREATE ROLE qrdine LOGIN PASSWORD '$PW'" -c "CREATE DATABASE qrdine_e2e OWNER qrdine"

( umask 077
  sed -E \
    -e "s#^ConnectionStrings__Default=.*#ConnectionStrings__Default=Host=127.0.0.1;Port=5434;Database=qrdine_e2e;Username=qrdine;Password=$PW;Maximum Pool Size=20#" \
    -e "s#^ASPNETCORE_URLS=.*#ASPNETCORE_URLS=http://127.0.0.1:5199#" \
    -e "s#^Jwt__Secret=.*#Jwt__Secret=$(rand 48)#" \
    -e "s#^PlatformAdmin__Password=.*#PlatformAdmin__Password=$(rand 16)#" \
    /etc/qrdine/qrdine.env > /run/qrdine-e2e.env )
unset PW

systemd-run --quiet --unit=qrdine-e2e -p EnvironmentFile=/run/qrdine-e2e.env -p User=qrdine -p Group=qrdine \
  -p WorkingDirectory="$APP" -p ProtectHome=true -p MemoryMax=1200M \
  /bin/dotnet "$APP/QrDine.Api.dll"

for _ in $(seq 1 45); do
  if curl -fsS --max-time 2 http://127.0.0.1:5199/health >/dev/null 2>&1; then
    echo "temporary copy is up, running $APP"
    PGPASSWORD="$(cat "$D/pw")" "$PGBIN/psql" -h 127.0.0.1 -p 5434 -U qrdine -d qrdine_e2e -Atc \
      'select count(*) || $$ migrations, newest $$ || max("MigrationId") from "__EFMigrationsHistory"'
    exit 0
  fi
  sleep 2
done
echo "did not come up"; journalctl -u qrdine-e2e --no-pager -n 30 | sed -E 's/(Password|Pwd)=[^;]*/\1=***/I'
exit 1
