#!/bin/bash
# Hybrid with real processes: an online server (this repo's API on SQLite, restaurant PCs switched on) on 127.0.0.1:5331
# and a freshly built DineIn.exe on 127.0.0.1:5321, each with a throwaway database in the work folder.
#   1. The owner signs up online, sells one bill, pairs a PC. The PC connects, downloads, takes over the counter and sells.
#   2. The server is stopped (the internet is out): the PC keeps selling.
#   3. The server is started again: the PC catches up by itself.
# Licence keys are made with the seller's tool and never printed.
# Usage: bash deploy/pc/run-hybrid-check.sh <DineIn.exe> <server publish folder> <work folder> <dinein-licence.dll> <private key .pem>
#   The server folder is a plain `dotnet publish src/QrDine.Api -c Release -o <folder>`.
set -u
EXE="$1"; SRV="$2"; T="$3"; TOOL="dotnet $4"; KEY="$5"
HERE="$(cd "$(dirname "$0")" && pwd -W 2>/dev/null || pwd)"
export MSYS_NO_PATHCONV=1 PYTHONIOENCODING=utf-8 PYTHONUTF8=1
export HY_SERVER=http://127.0.0.1:5331 HY_PC=http://127.0.0.1:5321 HY_STATE="$T/state.json"
SECRET=$(python -c "import secrets;print(secrets.token_hex(32))")
SPID=""; PPID_=""

# Any address: the server listens on 127.0.0.1, the exe on all addresses (for the phones on the Wi-Fi).
listener() { netstat -ano | grep -E "^ *TCP +[^ ]+:$1 .*LISTENING" | awk '{print $NF}' | head -1; }
up() { for _ in $(seq 1 60); do curl -fsS --max-time 2 "$1/health" >/dev/null 2>&1 && return 0; sleep 1; done; return 1; }
down() { [ -n "$2" ] && taskkill /F /T /PID "$2" >/dev/null 2>&1; for _ in $(seq 1 20); do curl -s --max-time 1 "$1/health" >/dev/null 2>&1 || break; sleep 1; done; }

start_server() {
  ( export ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS="$HY_SERVER" Database__Provider=Sqlite \
      "ConnectionStrings__Default=Data Source=$T/server.db" Jwt__Secret="$SECRET" Sync__Enabled=true Fiscal__Simulate=true
    cd "$SRV" && exec dotnet QrDine.Api.dll ) > "$T/server-$1.log" 2>&1 &
  up "$HY_SERVER" || { echo "SERVER DID NOT START"; tail -30 "$T/server-$1.log"; exit 1; }
  SPID=$(listener 5331)
}
start_pc() {
  DINEIN_DATA="$T/pcdata" Pc__Port=5321 Pc__OpenBrowser=false "$EXE" > "$T/pc-$1.log" 2>&1 &
  up "$HY_PC" || { echo "PC DID NOT START"; tail -30 "$T/pc-$1.log"; exit 1; }
  PPID_=$(listener 5321)
}
cleanup() { down "$HY_PC" "$PPID_"; down "$HY_SERVER" "$SPID"; }
trap cleanup EXIT

down "$HY_PC" "$(listener 5321)"; down "$HY_SERVER" "$(listener 5331)"
rm -rf "$T/pcdata" "$T/state.json" "$T"/server.db*; mkdir -p "$T/pcdata"

echo "=== both up"
start_server run1
start_pc run1
MACHINE=$(curl -s "$HY_PC/api/licence" | python -c "import json,sys;print(json.load(sys.stdin)['machineCode'])")
export HY_LICENCE=$($TOOL issue --key "$KEY" --restaurant "Hybrid Grill" --machine "$MACHINE" --days 365 | tail -1)
python "$HERE/hybrid_check.py" connect || exit 1

echo "=== the server is gone (no internet)"
down "$HY_SERVER" "$SPID"; SPID=""
python "$HERE/hybrid_check.py" offline || exit 1

echo "=== the server is back"
start_server run2
python "$HERE/hybrid_check.py" back
STATUS=$?
for f in "$T"/server-run*.log "$T"/pc-run*.log; do
  # The check's own request while the PC downloads is refused with 503 on purpose.
  errs=$(grep -E ' (ERR|FTL)\] |fail: ' "$f" | grep -v 'GET /api/plans responded 503')
  echo "errors in $(basename "$f"): $(printf '%s' "$errs" | grep -c .)"
  printf '%s\n' "$errs" | cut -c1-220 | head -6
done
exit $STATUS
