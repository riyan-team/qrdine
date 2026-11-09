# QrDine and print on the Hostworld server

Free production setup, November 2026. No secrets belong in this folder: the repo is public.

## What runs where

| Piece | Where | Notes |
|---|---|---|
| QrDine app | `qrdine.service`, 127.0.0.1:5100, `/opt/qrdine/app` | settings in `/etc/qrdine/qrdine.env` |
| QrDine database | `postgresql-qrdine.service`, 127.0.0.1:5433, `/var/lib/pgsql/qrdine` | its own server, separate from the storefront's on 5432 |
| Print app | `qrprint.service`, 127.0.0.1:3100, `/opt/qrprint/app` | settings in `/etc/qrprint/qrprint.env`, data in `/var/lib/qrprint` |
| Public addresses | `cloudflared-mystay.service` | see `hostworld-tunnel-mystay.sh` |
| Backups | `qrdine-backup-full.timer`, `qrdine-backup-partial.timer` | see below |

## Updating the apps

There is no auto-deploy. Build on the PC, because `NuGet.Config` points at an offline source.

QrDine, from Git Bash at the repo root:

```bash
(cd frontend && npm ci && npm run build)
OUT=/tmp/qrdine-publish; rm -rf "$OUT"
dotnet publish src/QrDine.Api/QrDine.Api.csproj -c Release -o "$OUT" -p:UseAppHost=false
rm -f "$OUT/appsettings.Development.json"; mkdir -p "$OUT/wwwroot/uploads"; cp -r frontend/dist/. "$OUT/wwwroot/"
(cd "$OUT" && tar -czf - .) | ssh hostworld 'rm -rf /opt/qrdine/app/wwwroot/assets && tar -xzf - -C /opt/qrdine/app && chown -R root:root /opt/qrdine/app && chown -R qrdine:qrdine /opt/qrdine/app/wwwroot/uploads && systemctl restart qrdine'
```

Print, from Git Bash in the print repo:

```bash
git archive HEAD server package.json package-lock.json | ssh hostworld 'tar -xf - -C /opt/qrprint/app && cd /opt/qrprint/app && npm ci --omit=dev && chown -R root:root . && systemctl restart qrprint'
```

## Reading menus from a photo

Uses Google's Gemini API. The key lives only in `/etc/qrdine/qrdine.env`. To set or change it, get a key at https://aistudio.google.com/apikey, then run this from the PC:

```bash
ssh -t hostworld /usr/local/sbin/qrdine-set-gemini
```

It asks for the key without showing it, has Gemini read a small test menu, and saves the key only if that worked. The model defaults to `gemini-flash-latest`, because Google retires dated model names. `qrdine-set-gemini.sh` in this folder is the source; copy it to `/usr/local/sbin/qrdine-set-gemini` if it changes.

## What each plan unlocks

The platform console sets a restaurant's plan. Staff screens hide what the plan leaves out, and pick up a change next time a screen loads. The server also refuses those back-office routes (`RequiresPlan` in `src/QrDine.Api/Auth/PlanGate.cs`). Order, table and bill routes are deliberately not gated that way, so a plan change in the middle of service can't strand food on the floor. Guest ordering is gated separately, in `OrderService`.

## Paid-date rules

`Restaurant.PlanExpiresAt` is the paid-through date. Sign-up gives 14 days.

- 5 days before it, owners and managers see a yellow banner.
- After it, a red banner, and everything still works for `Subscription:GraceDays` (default 3).
- After that the restaurant is paused until a payment is recorded in the platform console. Guests see "This menu isn't available right now".
- `Subscription:Enforce=false` turns all of this off.

## Backups

`qrdine-backup status` shows what ran last.

- **Full:** 04:00 server time (03:30 Pakistan), every night. Holds the whole QrDine database, a portable dump, print's data, both settings files, and the newest storefront backup.
- **Partial:** 00:00, 08:00, 12:00, 16:00, 20:00 server time. Holds only what changed since that day's full, plus print's data. The most that can be lost is about 4 hours.
- **On this server:** `/var/backups/qrdine/chains`, the newest 2 fulls with their partials. Not encrypted.
- **Off-site:** the Oracle bucket `mystay-backups`, encrypted before upload. Names repeat by weekday, so the last 7 days overwrite themselves. `qrdine-Mon-partial-16` belongs with `qrdine-Mon-full`.

The private key is at `C:\Users\SCS\.mystay-backup-key\PRIVATE-KEY-keep-safe.asc`. The server holds only the public key, so it can lock the files but not open them. **If the private key is lost, the off-site files can never be opened.**

A failed backup is only recorded in the log and as a failed `qrdine-backup@` service. Nobody is alerted yet.

## Restoring

Tested 2026-11-09 from the encrypted files, with marked rows, on a temporary server.

1. In the Oracle console, open the bucket and download the full and the newest partial with the same weekday. Check their dates: for a few hours after a new full, the partials with that weekday's name are still last week's and won't combine with it.
2. Decrypt each on the PC, in Git Bash, and send it to the server:

   ```bash
   export GNUPGHOME=/c/Users/SCS/.mystay-backup-key
   ssh hostworld 'install -d -m 700 -o postgres -g postgres /var/tmp/restore'
   gpg --decrypt qrdine-Mon-full.tar.gz.gpg | ssh hostworld 'tar -xzf - -C /var/tmp/restore'
   gpg --decrypt qrdine-Mon-partial-16.tar.gz.gpg | ssh hostworld 'tar -xzf - -C /var/tmp/restore'
   ```

   If the PC lost its key folder, first run `gpg --import PRIVATE-KEY-keep-safe.asc` with `GNUPGHOME` pointing at a new empty folder.

3. On the server, combine them and swap the database in:

   ```bash
   chown -R postgres:postgres /var/tmp/restore
   cd /var/tmp/restore
   sudo -u postgres /usr/pgsql-18/bin/pg_combinebackup full partial-* -o combined
   sudo -u postgres /usr/pgsql-18/bin/pg_verifybackup -q combined
   systemctl stop qrdine postgresql-qrdine
   mv /var/lib/pgsql/qrdine /var/lib/pgsql/qrdine.broken
   mv combined /var/lib/pgsql/qrdine
   chmod 700 /var/lib/pgsql/qrdine
   systemctl start postgresql-qrdine qrdine
   ```

   With no partial, use `mv full /var/lib/pgsql/qrdine` and skip the combine.

4. To move to Supabase instead, restore `qrdine.dump` from the full with `pg_restore --no-owner`, then change `ConnectionStrings__Default` in `/etc/qrdine/qrdine.env`.

A full rebuild on a new server also needs the settings files, which are in `settings/` inside every full.
