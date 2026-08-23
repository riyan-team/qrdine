#!/usr/bin/env bash
# One-command setup for QrDine on an Oracle Cloud (Oracle Linux / Ubuntu) VM.
# Installs Docker, opens the host firewall, generates secrets, builds and starts.
set -euo pipefail
cd "$(dirname "$0")"

echo "==> 1/4 Docker"
if ! command -v docker >/dev/null 2>&1; then
  # Oracle Linux ('ol') isn't supported by get.docker.com — use the CE repo directly.
  if [ -f /etc/oracle-release ] || grep -qi '^ID="\?ol"\?' /etc/os-release 2>/dev/null; then
    sudo dnf install -y dnf-plugins-core
    sudo dnf config-manager --add-repo https://download.docker.com/linux/centos/docker-ce.repo
    sudo dnf install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin --allowerasing
  else
    curl -fsSL https://get.docker.com | sudo sh
  fi
  sudo systemctl enable --now docker || true
fi

echo "==> 2/4 Host firewall (open 80 + 443)"
if command -v firewall-cmd >/dev/null 2>&1 && sudo systemctl is-active --quiet firewalld; then
  sudo firewall-cmd --permanent --add-port=80/tcp
  sudo firewall-cmd --permanent --add-port=443/tcp
  sudo firewall-cmd --reload
else
  sudo iptables -I INPUT -p tcp --dport 80  -j ACCEPT || true
  sudo iptables -I INPUT -p tcp --dport 443 -j ACCEPT || true
  { sudo netfilter-persistent save || sudo bash -c 'mkdir -p /etc/iptables && iptables-save > /etc/iptables/rules.v4'; } 2>/dev/null || true
fi

echo "==> 3/4 Secrets (.env)"
if [ ! -f .env ]; then
  DOMAIN_VALUE="${1:-qrdine.mystay.live}"
  cat > .env <<EOF
DOMAIN=${DOMAIN_VALUE}
DB_PASSWORD=$(openssl rand -hex 16)
JWT_SECRET=$(openssl rand -base64 32)
PLATFORM_ADMIN_USER=superadmin
PLATFORM_ADMIN_PASSWORD=$(openssl rand -hex 12)
GEMINI_API_KEY=
EOF
  echo "    wrote .env"
fi

echo "==> 4/4 Build & start (first build takes a few minutes)"
sudo docker compose up -d --build

echo
echo "======================================================================"
echo " QrDine is starting. Site:  https://$(grep '^DOMAIN=' .env | cut -d= -f2)"
echo " (allow ~1 minute for the HTTPS certificate on first run)"
echo
echo " Platform operator login (/platform/login):"
grep -E '^PLATFORM_ADMIN_' .env | sed 's/^/   /'
echo
echo " Logs:     sudo docker compose logs -f app"
echo " Restart:  sudo docker compose restart"
echo " Update:   git pull && sudo docker compose up -d --build"
echo "======================================================================"
