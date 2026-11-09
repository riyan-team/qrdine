#!/bin/bash
# Removes everything e2e-up.sh created, then proves production wasn't touched.
D=/var/tmp/qrdine-e2e-pg
PGBIN=/usr/pgsql-18/bin
cd /
systemctl stop qrdine-e2e 2>/dev/null
systemctl reset-failed qrdine-e2e 2>/dev/null
[ -d "$D/data" ] && sudo -u postgres "$PGBIN/pg_ctl" -D "$D/data" -m fast -w stop >/dev/null
rm -f /run/qrdine-e2e.env
rm -rf /var/tmp/qrdine-e2e-pg /var/tmp/qrdine-e2e-app
echo "--- after teardown"
echo "temporary service: $(systemctl is-active qrdine-e2e 2>/dev/null || true)"
echo "port 5199 or 5434 listening: $(ss -ltn | grep -cE ':(5199|5434)\b' || true)"
echo "temporary files left: $(ls -d /var/tmp/qrdine-e2e-pg /var/tmp/qrdine-e2e-app /run/qrdine-e2e.env 2>/dev/null | wc -l)"
echo "production: qrdine=$(systemctl is-active qrdine) health=$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5100/health) restaurants=$(sudo -u postgres $PGBIN/psql -p 5433 -d qrdine -Atc 'select count(*) from "Restaurants"')"
