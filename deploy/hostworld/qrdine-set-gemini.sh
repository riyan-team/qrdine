#!/bin/bash
# Sets the Gemini key that reads menus from photos.
#   ssh -t hostworld /usr/local/sbin/qrdine-set-gemini
#
# The key is never shown, never put on a command line, and written only to the app's settings file.
# 1. It asks Google whether the key is valid (this works even when Google is busy).
# 2. It has Google's Flash models read a small test menu, trying several in turn.
# 3. It saves the key with the model that worked first, and the others as fallbacks.
#    If the key is valid but every model is too busy to test, it still saves it: the app
#    retries and switches models by itself when a restaurant reads a photo.
# Optional first argument: one model name to use instead of choosing automatically.
set -euo pipefail

ENV_FILE="${QRDINE_ENV_FILE:-/etc/qrdine/qrdine.env}"
PAUSE="${QRDINE_RETRY_PAUSE:-6}"
MODEL_ARG="${1:-}"
BASE="https://generativelanguage.googleapis.com/v1beta"
TEST_MENU_PNG="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"

[ "$(id -u)" -eq 0 ] || { echo "Please run this as root." >&2; exit 1; }
[ -t 0 ] || { echo "Please run it with: ssh -t hostworld /usr/local/sbin/qrdine-set-gemini" >&2; exit 1; }
[ -f "$ENV_FILE" ] || { echo "$ENV_FILE is missing." >&2; exit 1; }

read -rsp "Paste your Gemini API key (nothing will show), then press Enter: " KEY
echo
KEY="${KEY//[[:space:]]/}"
[ -n "$KEY" ] || { echo "Nothing entered. Nothing was changed."; exit 1; }
case "$KEY" in
  *[!A-Za-z0-9_.-]*) echo "That doesn't look like an API key. Nothing was changed."; exit 1 ;;
esac

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# The key reaches curl through stdin, so it never shows in the process list.
call() {  # call OUTFILE URL [curl args...]  -> prints the HTTP status
  local out="$1" url="$2"; shift 2
  printf 'header = "x-goog-api-key: %s"\n' "$KEY" | curl -sS -K - --max-time 90 "$@" \
    -o "$out" -w '%{http_code}' "$url" 2>"$WORK/curl.err" || true
}
why() {  # what Google (or curl) said about the last call
  local m
  m="$(jq -r '.error.message // empty' "$1" 2>/dev/null || true)"
  [ -n "$m" ] || m="$(head -c 200 "$WORK/curl.err" 2>/dev/null || true)"
  printf '%s' "$m" | tr -d '\n' | cut -c1-200
}

# ---- 1. Is the key valid?
echo "Checking the key with Google ..."
KEY_OK=0
for attempt in 1 2 3; do
  STATUS="$(call "$WORK/models.json" "$BASE/models?pageSize=1000")"
  case "$STATUS" in 500|502|503|504|000) sleep "$PAUSE" ;; *) break ;; esac
done
case "$STATUS" in
  200) KEY_OK=1; echo "The key is valid." ;;
  400|401|403)
    echo "Google refused this key. Nothing was changed."
    echo "Google said: $(why "$WORK/models.json")"
    exit 1 ;;
  *) echo "Couldn't check the key with Google just now (status $STATUS): $(why "$WORK/models.json")"
     echo "Carrying on with the menu test." ;;
esac

# ---- 2. Which models to try, best first.
AVAILABLE=()
if [ "$KEY_OK" = 1 ]; then
  mapfile -t AVAILABLE < <(jq -r '.models[]? | select((.supportedGenerationMethods // []) | index("generateContent")) | .name | sub("^models/"; "")' "$WORK/models.json" 2>/dev/null || true)
fi
has() { local m; for m in "${AVAILABLE[@]}"; do [ "$m" = "$1" ] && return 0; done; return 1; }
CANDIDATES=()
add() { local m; for m in "${CANDIDATES[@]}"; do [ "$m" = "$1" ] && return 0; done; CANDIDATES+=("$1"); }
if [ -n "$MODEL_ARG" ]; then
  add "$MODEL_ARG"
else
  for m in gemini-flash-latest gemini-flash-lite-latest; do has "$m" && add "$m"; done
  while read -r m; do [ -n "$m" ] && add "$m"; done < <(printf '%s\n' "${AVAILABLE[@]}" | grep -E '^gemini-[0-9][0-9.]*-flash(-lite)?$' | sort -Vr || true)
  [ "${#CANDIDATES[@]}" -gt 0 ] || { add gemini-flash-latest; add gemini-flash-lite-latest; }
fi
CANDIDATES=("${CANDIDATES[@]:0:4}")

# ---- 3. Read the test menu.
jq -n --arg png "$TEST_MENU_PNG" '{
  contents: [{ role: "user", parts: [
    { text: "This is a photo of a restaurant menu. Return ONLY JSON: {\"items\":[{\"name\":\"string\",\"price\":0}]}" },
    { inline_data: { mime_type: "image/png", data: $png } }
  ]}],
  generationConfig: { temperature: 0.1, responseMimeType: "application/json", maxOutputTokens: 8192 }
}' > "$WORK/request.json"

WORKING=""; BUSY=0; READ=""
for model in "${CANDIDATES[@]}"; do
  echo "Testing $model on a small sample menu ..."
  for attempt in 1 2; do
    STATUS="$(call "$WORK/reply.json" "$BASE/models/${model}:generateContent" -H 'Content-Type: application/json' --data @"$WORK/request.json")"
    case "$STATUS" in 429|500|502|503|504|000) [ "$attempt" = 1 ] && sleep "$PAUSE" ;; *) break ;; esac
  done
  case "$STATUS" in
    200)
      READ="$(jq -r '[.candidates[0].content.parts[]?.text // empty] | join("")' "$WORK/reply.json" 2>/dev/null || true)"
      case "$READ" in
        *Chicken*350*) WORKING="$model"; break ;;
        *) echo "  It answered but misread the sample: $(printf '%s' "$READ" | tr -d '\n' | cut -c1-120)" ;;
      esac ;;
    429|500|502|503|504|000) BUSY=1; echo "  Busy right now ($STATUS): $(why "$WORK/reply.json")" ;;
    404) echo "  Google doesn't offer this model to your key." ;;
    400|401|403)
      echo "Google refused this key for reading menus. Nothing was changed."
      echo "Google said: $(why "$WORK/reply.json")"
      exit 1 ;;
    *) echo "  Unexpected answer ($STATUS): $(why "$WORK/reply.json")" ;;
  esac
done

if [ -n "$WORKING" ]; then
  echo "It works. $WORKING read the sample menu: $(printf '%s' "$READ" | tr -d '\n' | cut -c1-160)"
  PRIMARY="$WORKING"
elif [ "$BUSY" = 1 ] && [ "$KEY_OK" = 1 ]; then
  echo
  echo "Your key is valid, but Google's models are too busy to read a test menu right now."
  echo "Saving it anyway. When a restaurant reads a photo, the app retries and switches models"
  echo "by itself, so photo reading works as soon as Google catches up."
  PRIMARY="${CANDIDATES[0]}"
else
  echo "None of Google's models read the sample menu, so nothing was changed. Please try again later."
  exit 1
fi
FALLBACKS="$(printf '%s\n' "${CANDIDATES[@]}" | grep -vxF "$PRIMARY" | paste -sd, - || true)"

sed -i '/^Gemini__ApiKey=/d;/^Gemini__Model=/d;/^Gemini__FallbackModels=/d' "$ENV_FILE"
printf 'Gemini__ApiKey=%s\nGemini__Model=%s\nGemini__FallbackModels=%s\n' "$KEY" "$PRIMARY" "$FALLBACKS" >> "$ENV_FILE"
unset KEY
echo "Saved: $PRIMARY first${FALLBACKS:+, then ${FALLBACKS//,/, }}."

[ -n "${QRDINE_NO_RESTART:-}" ] && exit 0
echo "Restarting the app ..."
systemctl restart qrdine
for _ in $(seq 1 30); do
  if curl -fsS --max-time 2 http://127.0.0.1:5100/health >/dev/null 2>&1; then
    echo "Done. Menu > Import menu > Photo now reads printed menus."
    exit 0
  fi
  sleep 2
done
echo "The key is saved, but the app hasn't come back yet. Check: journalctl -u qrdine -n 50" >&2
exit 1
