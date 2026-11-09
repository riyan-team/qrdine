#!/bin/bash
# Full and partial backups for QrDine, print and the storefront, on the Hostworld server.
#
#   qrdine-backup full         the whole QrDine database, a portable dump, print data, settings
#   qrdine-backup partial      only what changed since the last full
#   qrdine-backup set-offsite  store the Oracle upload link (asks for it silently)
#   qrdine-backup status       what ran last, sizes, what is waiting to upload
#
# Local copies stay on this server. Off-site copies are encrypted with a public key before
# they leave, so neither this server nor Oracle can read them: only the private key kept on
# the owner's PC can. deploy/hostworld/README.md explains how to restore.
set -Eeuo pipefail

PGBIN=/usr/pgsql-18/bin
PG=(-h /run/postgresql -p 5433 -U postgres)
ROOT=/var/backups/qrdine
CHAINS=$ROOT/chains
OUTGOING=$ROOT/outgoing
LOG=$ROOT/backup.log
PUBKEY=/etc/qrdine-backup/public-key.asc
URLFILE=/etc/qrdine-backup/oracle-url
STOREFRONT=/var/backups/storefront
KEEP_CHAINS=2
MIN_FREE_GB=10

MODE=${1:-}
log() { printf '%s [%s] %s\n' "$(date '+%F %T')" "$MODE" "$*" | tee -a "$LOG"; }
trap 'log "FAILED at line $LINENO"' ERR
as_pg() { runuser -u postgres -- "$@"; }
size() { du -sh "$1" | cut -f1; }

encrypt_to() {
  local gpghome
  gpghome=$(mktemp -d)
  gpg --batch --yes --quiet --homedir "$gpghome" --trust-model always --compress-algo none \
      --recipient-file "$PUBKEY" --output "$1.tmp" --encrypt
  rm -rf "$gpghome"
  mv "$1.tmp" "$1"
}

# The server is shared with the live shops, so never let a backup fill its disk.
check_space() {
  local free
  free=$(df --output=avail -BG "$ROOT" | tail -n1 | tr -dc 0-9)
  if [ "$free" -lt "$MIN_FREE_GB" ]; then
    log "FAILED: only $free GB free on the disk, need $MIN_FREE_GB"
    exit 1
  fi
}

# Newest full backup that finished properly.
latest_chain() {
  local c latest=""
  for c in "$CHAINS"/*/; do
    if [ -e "${c}ok" ]; then latest=${c%/}; fi
  done
  printf '%s' "$latest"
}

# Keeps the newest finished chains; also clears anything an interrupted run left behind.
prune() {
  local dirs=("$CHAINS"/*/) kept=0 i c
  for (( i=${#dirs[@]}-1; i>=0; i-- )); do
    c=${dirs[i]%/}
    if [ -e "$c/ok" ] && [ "$kept" -lt "$KEEP_CHAINS" ]; then
      kept=$((kept+1))
    else
      rm -rf -- "$c"
      log "removed old local backup $(basename "$c")"
    fi
  done
}

upload_all() {
  if [ ! -s "$URLFILE" ]; then
    log "off-site link not set yet: $(find "$OUTGOING" -name '*.gpg' | wc -l) file(s) waiting here"
    return 0
  fi
  local base f name
  base=$(cat "$URLFILE")
  for f in "$OUTGOING"/*.gpg; do
    [ -e "$f" ] || continue
    name=$(basename "$f")
    curl --fail --silent --show-error --retry 3 --retry-delay 30 --connect-timeout 30 \
         --max-time 7200 -o /dev/null -T "$f" "$base/$name"
    log "uploaded $name ($(size "$f"))"
    rm -f -- "$f"
  done
}

do_full() {
  check_space
  local stamp day chain latest
  stamp=$(date +%Y%m%d-%H%M)
  day=$(date +%a)
  chain=$CHAINS/$stamp
  install -d -m 700 -o postgres -g postgres "$chain"

  as_pg "$PGBIN/pg_basebackup" "${PG[@]}" -D "$chain/full" -Fp -Xs -c fast
  as_pg "$PGBIN/pg_verifybackup" -q "$chain/full"
  # The portable copy: this is the one that can move to Supabase later.
  as_pg "$PGBIN/pg_dump" "${PG[@]}" -Fc -f "$chain/qrdine.dump" qrdine

  install -d -m 700 "$chain/settings"
  cp -p /etc/qrdine/qrdine.env /etc/qrprint/qrprint.env "$chain/settings/"
  cp -p /var/lib/qrprint/db.json "$chain/print-db.json"
  echo "$day" > "$chain/day"

  tar -C "$chain" -czf - full qrdine.dump settings print-db.json \
    | encrypt_to "$OUTGOING/qrdine-$day-full.tar.gz.gpg"

  # The shops' own nightly backup only lives on this server, so send its newest copy too.
  latest=$(find "$STOREFRONT" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | sort | tail -n1)
  if [ -n "$latest" ]; then
    tar -C "$latest" -cf - . | encrypt_to "$OUTGOING/storefront-$day.tar.gpg"
  fi

  touch "$chain/ok"
  log "full backup done: $(size "$chain") as $stamp"
  prune
  upload_all
}

do_partial() {
  local chain
  chain=$(latest_chain)
  if [ -z "$chain" ]; then
    log "no full backup yet, taking one first"
    do_full
    return
  fi
  check_space
  local stamp hour day name
  stamp=$(date +%Y%m%d-%H%M)
  hour=$(date +%H)
  day=$(cat "$chain/day")
  name=partial-$stamp

  as_pg "$PGBIN/pg_basebackup" "${PG[@]}" -D "$chain/$name" -Fp -Xs -c fast \
        --incremental="$chain/full/backup_manifest"
  as_pg "$PGBIN/pg_verifybackup" -q "$chain/$name"
  cp -p /var/lib/qrprint/db.json "$chain/print-db-$stamp.json"

  # Named after the day of its full backup: restoring needs that full plus this partial.
  tar -C "$chain" -czf - "$name" "print-db-$stamp.json" \
    | encrypt_to "$OUTGOING/qrdine-$day-partial-$hour.tar.gz.gpg"

  log "partial backup done: $(size "$chain/$name") on top of $(basename "$chain")"
  upload_all
}

do_set_offsite() {
  local url probe code
  read -rsp 'Paste the Oracle upload link (nothing will show), then press Enter: ' url
  echo
  url=${url%%[[:space:]]*}
  url=${url%/}
  case "$url" in
    https://*/p/*/o) ;;
    *) echo "That doesn't look right. It should start with https:// and end with /o/"; exit 1 ;;
  esac
  probe=$(mktemp)
  echo "connection test $(date '+%F %T')" > "$probe"
  code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 60 -T "$probe" "$url/connection-test.txt" || true)
  rm -f -- "$probe"
  if [ "$code" != 200 ]; then
    echo "Oracle refused the test upload (answer: $code). Check the link allows object writes."
    exit 1
  fi
  ( umask 077; printf '%s\n' "$url" > "$URLFILE" )
  log "off-site link saved and tested"
  upload_all
  echo "Done. Off-site backups are switched on."
}

do_status() {
  local c
  echo "Local backups:"
  for c in "$CHAINS"/*/; do
    [ -e "${c}ok" ] || continue
    echo "  $(basename "$c")  $(size "$c")  with $(find "$c" -mindepth 1 -maxdepth 1 -type d -name 'partial-*' | wc -l) partial"
  done
  echo "Waiting to upload: $(find "$OUTGOING" -name '*.gpg' | wc -l) file(s), $(size "$OUTGOING")"
  if [ -s "$URLFILE" ]; then echo "Off-site link: set"; else echo "Off-site link: NOT set yet"; fi
  echo
  systemctl list-timers 'qrdine-backup-*' --no-pager | head -n 4 | cut -c1-110
  echo
  tail -n 10 "$LOG"
}

cd /
install -d -m 750 -o root -g postgres "$ROOT" "$CHAINS"
install -d -m 700 "$OUTGOING"
touch "$LOG"
chmod 600 "$LOG"

case "$MODE" in
  full|partial|set-offsite)
    exec 9>/run/qrdine-backup.lock
    if ! flock -n 9; then
      log "another backup is still running, skipping this one"
      exit 0
    fi
    ;;
esac

case "$MODE" in
  full) do_full ;;
  partial) do_partial ;;
  set-offsite) do_set_offsite ;;
  status) do_status ;;
  *) echo "Use: qrdine-backup full | partial | set-offsite | status"; exit 2 ;;
esac
