#!/bin/bash
# Runs the freshly built DineIn.exe from a throwaway data folder on port 5310, checks it, restarts it, checks again,
# and checks a second copy only points at the first. Licence keys are made with the seller's tool and never printed.
set -u
# Usage: bash deploy/pc/run-pc-check.sh <DineIn.exe> <work folder> <dinein-licence.dll> <seller private key .pem>
EXE="$1"; T="$2"; TOOL="dotnet $3"; KEY="$4"
HERE="$(cd "$(dirname "$0")" && pwd -W 2>/dev/null || pwd)"
export MSYS_NO_PATHCONV=1 PYTHONIOENCODING=utf-8 PYTHONUTF8=1
export DINEIN_DATA="$T/data" Pc__Port=5310 Pc__OpenBrowser=false Pc__FirstBackupMinutes=0.1
export PC_BASE=http://127.0.0.1:5310 PC_STATE="$T/state.json"
taskkill //F //IM DineIn.exe >/dev/null 2>&1; sleep 1
rm -rf "$T/data" "$T/state.json"; mkdir -p "$T/data"
[ -e "$T/data/dinein.db" ] && { echo "old data still there"; exit 1; }

start() {
  "$EXE" > "$T/$1.log" 2>&1 &
  PID=$(cat /proc/$!/winpid)   # the Windows process id, for taskkill
  for _ in $(seq 1 60); do curl -fsS --max-time 2 "$PC_BASE/health" >/dev/null 2>&1 && return 0; sleep 1; done
  echo "DID NOT START"; tail -30 "$T/$1.log"; return 1
}
stop() { taskkill //F //T //PID "$PID" >/dev/null 2>&1; for _ in $(seq 1 20); do curl -s --max-time 1 "$PC_BASE/health" >/dev/null 2>&1 || break; sleep 1; done; }

echo "=== first start"
start run1 || exit 1
MACHINE=$(curl -s "$PC_BASE/api/licence" | python -c "import json,sys;print(json.load(sys.stdin)['machineCode'])")
export PC_LICENCE=$($TOOL issue --key "$KEY" --restaurant "PC Grill" --machine "$MACHINE" --days 365 | tail -1)
export PC_LICENCE_ENDED=$($TOOL issue --key "$KEY" --restaurant "PC Grill" --machine "$MACHINE" --until 2026-01-01 | tail -1)
python "$HERE/pc_check.py"
sleep 8   # the first backup (0.1 min after start in this test)
echo "backups: $(ls "$T/data/backups" 2>/dev/null | grep -c '^dinein-.*\.db$')"
echo "a second copy: $(DINEIN_DATA="$T/data" timeout 60 "$EXE" 2>&1 | grep -c 'already running') (1 = it only pointed at the first)"
echo "errors in the log: $(grep -cE ' (ERR|FTL)\] ' "$T/run1.log")"
grep -E ' (ERR|FTL)\] ' "$T/run1.log" | cut -c1-200 | head -5
stop

echo "=== after a restart"
start run2 || exit 1
python "$HERE/pc_check.py" --after-restart
echo "errors in the log: $(grep -cE ' (ERR|FTL)\] ' "$T/run2.log")"
grep -iE 'Database upgraded|New database' "$T/run1.log" "$T/run2.log" | cut -c1-160
echo "data folder: $(ls "$T/data" | tr '\n' ' ')"
stop
taskkill //F //IM DineIn.exe >/dev/null 2>&1 || true
