#!/bin/bash
# Puts qrdine.mystay.live and print.mystay.live online through a Cloudflare Tunnel.
# Both apps listen only on this server's own address, so nothing else opens to the internet.
#
# Run it from Windows Command Prompt:
#   cd /d "C:\Users\SCS\Desktop\qrdriven\deploy\hostworld" && scp hostworld-tunnel-mystay.sh hostworld:/root/ && ssh -t hostworld bash /root/hostworld-tunnel-mystay.sh
#
# It stops at the first error and is safe to run again. The existing "storefront" tunnel
# (beads, nutfira, admin) is a separate service and is not touched.
#
# To undo: systemctl disable --now cloudflared-mystay

set -euo pipefail

TUNNEL=mystay
ZONE=mystay.live
CONFIG=/etc/cloudflared/mystay.yml
SERVICE=cloudflared-mystay
trap 'echo; echo "STOPPED at line $LINENO. beads, nutfira and admin are not affected."' ERR

say() { printf '\n=== %s\n' "$1"; }
tunnel_id() { cloudflared tunnel list --name "$TUNNEL" 2>/dev/null | awk -v t="$TUNNEL" '$2==t{print $1; exit}' || true; }

say "1/5 Check both apps are running"
for port in 5100 3100; do
  code=$(curl -s -m 10 -o /dev/null -w '%{http_code}' "http://127.0.0.1:$port/" || true)
  echo "port $port -> $code"
  [ "$code" = 200 ] || { echo "The app on port $port is not answering."; false; }
done

say "2/5 Sign in to Cloudflare"
if [ ! -f /root/.cloudflared/cert.pem ]; then
  echo "A link appears below. Open it in the browser where you use Cloudflare,"
  echo "choose the site $ZONE (not roomcalcs.com), and click Authorize."
  echo "If $ZONE is not in the list, you are signed in to the other Cloudflare account:"
  echo "sign in with the account that holds $ZONE and open the link again."
  echo "This window then carries on by itself."
  echo
  cloudflared tunnel login
else
  echo "Already signed in."
fi

say "3/5 Create the tunnel"
ID=$(tunnel_id)
if [ -z "$ID" ]; then
  cloudflared tunnel create "$TUNNEL"
  ID=$(tunnel_id)
fi
[ -n "$ID" ] || { echo "Could not find the tunnel id."; false; }
echo "Tunnel id: $ID"
mkdir -p /etc/cloudflared
if [ ! -f "/etc/cloudflared/$ID.json" ]; then
  cp "/root/.cloudflared/$ID.json" "/etc/cloudflared/$ID.json"
fi
chmod 600 "/etc/cloudflared/$ID.json"
cat > "$CONFIG" <<CONF
tunnel: $ID
credentials-file: /etc/cloudflared/$ID.json
metrics: 127.0.0.1:20242
ingress:
  - hostname: qrdine.$ZONE
    service: http://127.0.0.1:5100
  - hostname: print.$ZONE
    service: http://127.0.0.1:3100
  - service: http_status:404
CONF
cloudflared tunnel --config "$CONFIG" ingress validate

say "4/5 Start the tunnel"
cat > "/etc/systemd/system/$SERVICE.service" <<UNIT
[Unit]
Description=Cloudflare Tunnel for $ZONE (QrDine and print)
After=network-online.target
Wants=network-online.target

[Service]
ExecStart=$(command -v cloudflared) --no-autoupdate --config $CONFIG tunnel run
Restart=on-failure
RestartSec=5
TimeoutStartSec=0

[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload
systemctl enable "$SERVICE" >/dev/null 2>&1
systemctl restart "$SERVICE"
connected=no
for _ in $(seq 1 30); do
  log=$(journalctl -u "$SERVICE" --since "2 min ago" --no-pager 2>/dev/null || true)
  if grep -q "Registered tunnel connection" <<<"$log"; then
    connected=yes
    break
  fi
  sleep 2
done
if [ "$connected" != yes ]; then
  echo "The tunnel did not connect. Recent log:"
  journalctl -u "$SERVICE" -n 15 --no-pager
  false
fi
echo "Tunnel connected."

say "5/5 Point the two addresses at the tunnel"
for h in "qrdine.$ZONE" "print.$ZONE"; do
  out=$(cloudflared tunnel route dns --overwrite-dns "$TUNNEL" "$h" 2>&1) || { echo "$out"; false; }
  echo "$out"
  if grep -qE "CNAME $h\.[a-z]" <<<"$out"; then
    echo
    echo "WRONG SITE: the sign-in in step 2 chose a domain other than $ZONE."
    echo "Delete the record named above in Cloudflare, then run:"
    echo "  rm /root/.cloudflared/cert.pem"
    echo "and run this script again, choosing $ZONE."
    false
  fi
done

sleep 8
for h in "qrdine.$ZONE/health" "print.$ZONE/"; do
  code=$(curl -sS -m 25 -o /dev/null -w '%{http_code}' "https://$h" 2>/dev/null || true)
  echo "$h -> ${code:-no answer}"
done

# The running tunnel only needs its own credentials file. This sign-in can change the whole
# zone's DNS, so it shouldn't stay on the server. Sign in again to add an address later.
rm -f /root/.cloudflared/cert.pem

echo
echo "DONE. Tell Claude so it can test both sites from Pakistan and abroad."
