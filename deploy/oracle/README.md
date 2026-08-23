# Deploy QrDine on an Oracle Cloud Free VM

Runs the whole stack on one always-free Ampere A1 VM: the app, Postgres, automatic
HTTPS (Caddy + Let's Encrypt), and a nightly database backup. No Render, no Neon.

## Prerequisites (already done during VM setup)
- VM running, with a **public IP**.
- Security List ingress rules for **TCP 80** and **443** (and 22 for SSH).
- A DNS **A record** for your domain → the VM's public IP, **DNS-only (grey cloud)** in Cloudflare.

## One-time deploy
SSH into the VM (Oracle Linux default user is `opc`):

```bash
ssh -i /path/to/your-private-key.key opc@<PUBLIC_IP>
```

Then:

```bash
sudo dnf install -y git || sudo apt-get update && sudo apt-get install -y git
git clone https://github.com/riyan-epk/qrdine.git
cd qrdine/deploy/oracle
chmod +x setup.sh backup.sh
./setup.sh                     # installs Docker, opens firewall, builds, starts
```

The script prints your generated **superadmin** password — save it. In ~1 minute
`https://<your-domain>` is live with a valid certificate.

> If you enabled photo-import: edit `.env`, set `GEMINI_API_KEY=<key>`, then
> `sudo docker compose up -d`.

## Everyday commands (run in `deploy/oracle`)
```bash
sudo docker compose logs -f app     # watch app logs
sudo docker compose restart         # restart everything
git pull && sudo docker compose up -d --build   # deploy latest code
sudo docker compose down            # stop
```

## Backups
Nightly gzipped dumps land in `deploy/oracle/backups/` (newest 14 kept).
Restore one:
```bash
gunzip -c backups/qrdine-YYYYMMDD-HHMM.sql.gz | sudo docker compose exec -T db psql -U qrdine qrdine
```
For off-site safety, copy `backups/` to Oracle Object Storage or download it periodically.

## Notes
- First boot runs EF migrations automatically against the fresh Postgres — no reset needed.
- Ports: only 80/443 are public (Caddy); Postgres is internal to the compose network.
- Cloudflare: keep the record **grey (DNS-only)** so Caddy can obtain the certificate.
  After it's working you may switch to orange (proxied) with SSL mode **Full (strict)**.
