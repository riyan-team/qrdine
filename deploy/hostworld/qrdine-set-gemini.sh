#!/bin/bash
# Sets the Gemini key that reads menus from photos.
#   ssh -t hostworld /usr/local/sbin/qrdine-set-gemini
#
# The key is never shown, never put on a command line, and written only to /etc/qrdine/qrdine.env.
# It is saved only if Gemini can actually read the small test menu below.
# Optional first argument: a model name. The default follows Google's current Flash model.
set -euo pipefail

ENV_FILE=/etc/qrdine/qrdine.env
MODEL="${1:-gemini-flash-latest}"
API="https://generativelanguage.googleapis.com/v1beta/models/${MODEL}:generateContent"
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

jq -n --arg png "$TEST_MENU_PNG" '{
  contents: [{ role: "user", parts: [
    { text: "This is a photo of a restaurant menu. Return ONLY JSON: {\"items\":[{\"name\":\"string\",\"price\":0}]}" },
    { inline_data: { mime_type: "image/png", data: $png } }
  ]}],
  generationConfig: { temperature: 0.1, responseMimeType: "application/json", maxOutputTokens: 8192 }
}' > "$WORK/request.json"

echo "Testing it on a small sample menu with $MODEL ..."
STATUS=000
for attempt in 1 2 3; do
  # The header reaches curl through stdin, so the key never shows in the process list.
  STATUS="$(printf 'header = "x-goog-api-key: %s"\n' "$KEY" | curl -sS -K - --max-time 90 \
    -H 'Content-Type: application/json' --data @"$WORK/request.json" \
    -o "$WORK/reply.json" -w '%{http_code}' "$API" 2>"$WORK/curl.err" || true)"
  case "$STATUS" in 500|502|503|504|000) sleep 5 ;; *) break ;; esac
done

why() { jq -r '.error.message // empty' "$WORK/reply.json" 2>/dev/null || true; }

case "$STATUS" in
  200)
    READ="$(jq -r '[.candidates[0].content.parts[]?.text // empty] | join("")' "$WORK/reply.json")"
    case "$READ" in
      *Chicken*350*) echo "It works. Gemini read the sample menu: $(printf '%s' "$READ" | tr -d '\n' | cut -c1-200)" ;;
      *) echo "Gemini answered but didn't read the sample menu properly. Nothing was changed."
         printf 'It said: %s\n' "$(printf '%s' "$READ" | tr -d '\n' | cut -c1-200)"
         exit 1 ;;
    esac
    ;;
  429)
    echo "The key is accepted, but Google says its limit is used up for now. Saving it anyway."
    echo "Google said: $(why)"
    ;;
  400|401|403)
    echo "Google refused this key. Nothing was changed."
    echo "Google said: $(why)"
    exit 1
    ;;
  404)
    echo "Google doesn't offer the model '$MODEL' to this key. Nothing was changed."
    echo "Google said: $(why)"
    exit 1
    ;;
  *)
    echo "Couldn't get an answer from Google (status $STATUS). Nothing was changed."
    [ -s "$WORK/curl.err" ] && cat "$WORK/curl.err"
    why
    exit 1
    ;;
esac

sed -i '/^Gemini__ApiKey=/d;/^Gemini__Model=/d' "$ENV_FILE"
printf 'Gemini__ApiKey=%s\nGemini__Model=%s\n' "$KEY" "$MODEL" >> "$ENV_FILE"
unset KEY

echo "Saved. Restarting the app ..."
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
