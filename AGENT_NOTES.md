# QrDine — Agent & Operator Notes

> Persistent context for AI agent sessions and the operator. If you're an AI
> assistant picking this project up: **read this file first**, then the code.
> Nothing here is a secret (real credentials live only in `deploy/oracle/.env`
> on the server, which is git-ignored).

## What this is
Multi-tenant **QR ordering + kitchen-timing** SaaS for the Pakistan market.
**ASP.NET Core 9 (Clean Architecture) + SignalR + EF Core** backend, **React (Vite)**
frontend. SQLite locally (zero setup), **Postgres** in production. One flag switches
providers (`Database:Provider`).

## Repo layout
```
src/QrDine.Domain/          entities, Order aggregate + state machine (no deps)
src/QrDine.Application/      use-case services, DTOs, interfaces
src/QrDine.Infrastructure/   EF Core DbContext + tenant filter, SignalR, seeder, migrations
src/QrDine.Api/              minimal-API endpoints, middleware, Program.cs
frontend/                    React SPA (the customer + staff screens)
tools/QrDine.PrintBridge/    on-site ESC/POS print bridge
deploy/oracle/               one-VM deploy: app + Postgres + Caddy + backup + auto-update
backend/                     ⚠ old throwaway demo, superseded by src/ — ignore
```

## Roles & permissions
Roles: **Owner, Manager, Cashier(=Reception), Kitchen, Waiter**. Permissions are
**configurable per role** by the owner (Settings → Role permissions), stored in
`RolePermissions`, and ride in the JWT (apply on next sign-in). Owner is always full.

Permission keys (`src/QrDine.Domain/Identity/Permissions.cs`):
`orders.create` (take order), `orders.approve`, `orders.cook`, `orders.serve`,
`orders.pay`, `orders.cancel`, `menu.manage`, `inventory.manage`, `settings.manage`,
`reports.view`, `invoice.sync`, `expenses.manage`, `suppliers.manage`, `orders.discount`,
`customers.manage`, `payroll.manage`.

Defaults: Owner/Manager = all; Cashier = create+approve+pay+invoice.sync+discount+customers;
Kitchen = cook; Waiter = create+serve.

## Order lifecycle (one central state machine)
`Placed → Approved → Cooking → Ready → Served → Paid` (+ `Cancelled` early). Rules
are enforced in the `Order` aggregate (`src/QrDine.Domain/Ordering/Order.cs`); illegal
moves throw a clean 400. Both **QR** and **Waiter** orders use the same pipeline.

- **Order source**: `Order.Source` = `Qr` (customer scanned the table) or `Waiter`
  (a waiter took it). Shown as a badge on Reception.
- **Timer**: anchored to `AcceptedAt` (set once when cooking starts) + `EtaMinutes`.
  It never resets when another role advances the order.
- **Kitchen-unavailable fallback**: grant Reception or Waiter `orders.cook`/`orders.serve`
  and they can advance orders from their own screen — one shared `OrderActions`
  component (`frontend/src/ordering.jsx`) reused by Reception + Waiter, no duplicated logic.

## Frontend map
- `frontend/src/ordering.jsx` — shared cart/menu/customizer + `OrderActions` (status buttons)
- `frontend/src/pages/Menu.jsx` — guest QR menu (uses shared ordering)
- `frontend/src/pages/Waiter.jsx` — Board + **Take order** (search, category nav, bottom-sheet cart)
- `frontend/src/pages/Cashier.jsx` — Reception (approve, bill/settle, source badge, fallback controls)
- `frontend/src/pages/Kitchen.jsx` — KDS (cook time → countdown)
- `frontend/src/pages/Landing.jsx` — public marketing homepage at `/` (hero, features, about, contact)
- `frontend/src/pages/Settings.jsx` — account, **Receipt & branding** (logo/header/footer), branches
  (edit + FBR-gated POS), tables, staff (edit incl. username), role permissions
- `frontend/src/icons.jsx` — thin-line SVG icon set (replaced the app's decorative emoji)
- `frontend/src/loyalty.jsx` — guest **Loyalty & reviews**: rewards strip under the menu header, the Rewards & Reviews
  bottom sheet (join, points ring, rewards, redemption code, history, ratings, Google review buttons), `useLoyalty`
- `frontend/src/pages/Loyalty.jsx` — owner/manager **Loyalty** page (overview, programme & Google links, rewards,
  members + adjust, reward requests); also exports `Requests` (compact strip) + `BillMember` used on Reception
- `frontend/src/api.js`, `frontend/src/auth.js`, `frontend/src/useStaffOrders.js`, `frontend/src/useHub.js`
- `frontend/src/styles.css` — design system (cream/maroon POS theme) + animations + 80mm receipt
  print rules (`@page { size: 80mm auto }`) + the `.lp-*` landing styles (bottom of file)

## Run locally (two terminals)
```bash
# backend → http://localhost:5099 (SQLite, auto-created & seeded)
cd src/QrDine.Api && dotnet run --launch-profile http
# frontend → http://localhost:5173
cd frontend && npm install && npm run dev
```
Dev DB is `src/QrDine.Api/qrdine.db` (git-ignored). It is built from the model via
`EnsureCreated` and **does not auto-migrate** — to pick up a schema change locally,
**delete/rename `qrdine.db`** and restart (it re-seeds). There is no demo restaurant;
create one via **Sign up** (`/signup`).

## Deploy (Hostworld, current)
Since 2026-11-09 production runs on the Hostworld server without Docker, deployed by hand from
the PC, so **pushing to `main` no longer deploys**. The Oracle VM described below is gone.
Services, updating, paid-date rules, backups and restoring: **`deploy/hostworld/README.md`**.

## Deploy (Oracle Cloud free VM, old)
Repo is cloned at **`~/qrdine`** on the VM (user `opc`). Stack: app + Postgres + Caddy
(auto-HTTPS) via `deploy/oracle/docker-compose.yml`. Secrets in **`~/qrdine/deploy/oracle/.env`**
(git-ignored; created by `setup.sh`): `DOMAIN, DB_PASSWORD, JWT_SECRET,
PLATFORM_ADMIN_USER (=superadmin), PLATFORM_ADMIN_PASSWORD, GEMINI_API_KEY`.

**Auto-deploy**: `deploy/oracle/auto-update.sh` (run by cron every ~5 min) fetches
`origin/main`; if changed it does `git reset --hard origin/main` + `docker compose up -d --build`.
So **pushing to `main` deploys automatically**.

Manual deploy:
```bash
cd ~/qrdine && git pull && cd deploy/oracle && sudo docker compose up -d --build
```

### Database on deploy — nothing manual
Production is Postgres, so `Program.cs` runs `db.Database.MigrateAsync()` on startup:
**EF migrations apply automatically** on every container rebuild. The startup seeder
also runs an **idempotent backfill** that grants newly-added default permissions
(e.g. `orders.create`) to existing roles without overriding owner edits. Data lives
in the `pgdata` docker volume; rebuilds don't touch it. Nightly backups → `deploy/oracle/backups/`.

**Adding a migration** (run on a dev machine, they are Postgres-native):
```bash
dotnet ef migrations add <Name> --project src/QrDine.Infrastructure --startup-project src/QrDine.Api
```
Commit the generated files; they apply on the next deploy.

## Verify a deploy landed
```bash
cd ~/qrdine
git log -1 --oneline                 # should show the newest pushed commit
cd deploy/oracle
sudo docker compose ps               # app/db/caddy Up
sudo docker compose logs --tail=60 app   # look for migration + "Now listening"
```
Then in the browser (hard-refresh): log in → owner sees **Settings → Role permissions**
with a **“Take orders (waiter/reception)”** row; waiter sees a **➕ Take order** tab.

## Credentials
- **Platform operator** (`/platform/login`): `grep '^PlatformAdmin__' /etc/qrdine/qrdine.env` on Hostworld. Written to the database on every start, so edit the file and restart to change it.
- **Restaurant owner**: whatever was set at **sign-up** (hashed; not recoverable from DB).
  No demo accounts are seeded in production.
- **Local dev test accounts** (SQLite only, NOT on the server): owner/`owner123`,
  cashier/`cash123`, kitchen/`kit123`, waiter/`wait123`; dev platform `superadmin`/`super123`.

## Sync with a restaurant's own PC (hybrid)
For the owner-level picture (what is ready online, offline and hybrid, and how to set each up) see
**`docs/ONLINE-OFFLINE-HYBRID.md`** (section 5 is hybrid).
A restaurant's counter PC (`DineIn.exe`) runs the counter with or without internet and keeps the restaurant's online copy
in step. **The server side is off until `Sync:Enabled=true`** (`Sync__Enabled=true` in `/etc/qrdine/qrdine.env`).
While it is off, the sync routes aren't mapped at all: every sync address answers 404 to everyone, and no change lines
are written. The switch is read once at start, so changing it needs a restart. Code: `Domain/Sync` (`SyncRules`,
`SyncRecords`), `Application/Sync` (`SyncService`, DTOs), `Infrastructure/Sync` (`SyncModel`, `SyncTracker`, `EfSyncStore`,
`SyncKeys`, `SyncApplier`), `Api/Endpoints/SyncEndpoints.cs`, the PC side `Api/Pc/PcSync.cs`, screens `pages/Connect.jsx`,
`CounterPc` and `PcSyncCard` in `Settings.jsx`. Tests: `SyncTests`, `SyncHttpTests`, `HybridSyncTests` (server + PC in one
process over HTTP), and `deploy/pc/run-hybrid-check.sh` (real processes; 2026-11-20: 54 + 11 + 6 checks, 0 failed).

**What every synced row gets** (migrations `AddSyncFoundation`, `HybridSyncApply`, additive only):
- `SyncId` (uuid, unique): the row's id on every copy. On Postgres the column defaults to `gen_random_uuid()`.
- `LastChangedAt` (nullable): when the row last changed, for "newest change wins".
- Both are EF shadow properties (`SyncModel.cs`). `SyncTracker` stamps them in `SaveChanges`.
- **Roots** are the restaurant-owned tables plus `Restaurant`. **Children** (order items and modifiers, invoice lines, variants, modifier groups and options, recipe lines, supplier entry lines) travel inside their root, each with its own `SyncId`.
- **Never synced:** platform admins, plans, subscription payments and the sync tables. Photos travel as files (`/api/files/{id}` down, `PUT /api/sync/files/{id}` up).

**Change log:** `SyncChanges` (Seq, RestaurantId, Type, SyncId, Op `Upsert`/`Delete`, ChangedAt, `OriginDeviceId`), written in the same save. `OriginDeviceId` is the device whose push made the change (server) or `-1` = from the server (PC); a device never pulls back rows whose lines all came from itself, and the PC pushes only lines with no origin. On the PC, `SyncSettings.RecordOnThisPc` turns the log on once connected.

**Device API** (key in `X-Device-Key`):

| Route | What it does |
|---|---|
| `GET/POST /api/r/{slug}/admin/sync/devices`, `DELETE …/devices/{id}` | Owner only. Pairing (body `{name}`) returns the key once (`dk_…`); only its hash is stored. **One active PC per restaurant.** The list shows `counterSince`, `waiting`, `failed`, `lastFailure`. Removing stops the key at once and frees the online counter. |
| `GET /api/sync/hello` | The restaurant, the device, `head`, `counterSince`, the synced types **in dependency order**, batch limits. |
| `GET /api/sync/snapshot?type=…&after=0&limit=…` | A new PC's download, one type at a time. |
| `GET /api/sync/pull?since=N&limit=…` | Changes after N with the row as it is now. Lines from the last `Sync:SettleSeconds` wait. |
| `POST /api/sync/push` | Stored in `SyncInbox` (idempotent by `changeId`), then **applied** by `SyncApplier`: newest copy per row, older ones `Skipped`; each row `Applied`, `Skipped` or `Failed` with a note. |
| `POST /api/sync/takeover[?rejoin=true]` | The PC runs the counter from now on (`SyncDevice.CounterSince`). `rejoin` (a removed PC back with a new key) is refused if floor records were made online since the last PC was removed. |

**Rules** (`SyncRules.cs`, applied by `SyncApplier`):
- Rows travel without either side's own ids: references as the target's `SyncId` (`SyncRules.References`), children matched by `SyncId`. A row whose reference hasn't arrived waits for the batch, then goes in with the link empty and a note.
- **Settings-like rows** (menu, prices, tables, staff, branches, stock items, suppliers, customers, loyalty set-up…): newest change wins, both ways.
- **Floor rows** (orders, seatings, bills, payments, voids, returns, calls, day closings, khata lines, expenses, feedback, redemptions, stock movements, points lines): made **only on the PC** after takeover. `CounterOnPcRule` in `AppDbContext` refuses them on the server (and changes to device-owned fields) while a PC runs the counter, except while applying a push. The PC never takes the server's floor rows after its download.
- **Device-owned fields** (`DeviceOwnedFields`): stock `Quantity`/`LastCost`, loyalty `Points`/`LifetimePoints`/`LifetimeSpend`/`Visits`, table `GuestPassesFrom`: the PC's value always wins.
- **Server only** (`ServerOnlyFields`): the restaurant's plan, status, paid dates, features, slug. On the PC, features come from its licence.
- **Never travel** (`SecretFields`): FBR tokens (staff and branch), a loyalty member's phone pass, a rider's link key. **Staff password hashes do travel** (PBKDF2, salted), only to the paired PC, so staff sign in there offline.
- `FiscalAutoRetryService` skips restaurants whose counter is on a PC (the PC files).
- If the switch is ever turned off after a PC connected, or the server is restored from an older backup, the PC must be removed and a fresh PC connected.

**The PC side** (`PcSync`, a hosted service in the PC edition): `POST /api/pc/sync/connect` (empty PC: hello, then the download in the background; the licence gate answers 503 to everything but `/api/licence` and `/api/pc/sync` until done; connected PC: a new key for the same restaurant, owner/manager only), `GET /api/pc/sync` (status, no secrets), `POST /api/pc/sync/now`. State in `DineIn-data/sync.json`. Every 5 s: push, then pull; prunes pushed lines older than 3 days.

**Deploy checklist** (the migrations only add columns, tables and indexes, plus small data fix-ups: item tax 0 becomes blank, see the 2026-11-19 QA entry; tax office settings, see the 2026-11-20 03:18 entry). The scripts used for the 2026-11-20 deploys (`run.sh` with its tax stage, `rehearse.sh` + `prodcopy.sh`, `deploy-live.sh`) follow these steps:
1. **Quiet hour:** pick a time with no restaurant open. After the 03:30 Pakistan full backup works well. Tell the owner first.
2. **Backup first:** run `qrdine-backup status` and check the last full is good. Then take a dump just before the deploy: `sudo -u postgres /usr/pgsql-18/bin/pg_dump -p 5433 -Fc qrdine > /var/backups/qrdine/pre-sync-$(date +%F-%H%M).dump`.
3. **Keep the running build:** `cp -a /opt/qrdine/app /opt/qrdine/app.prev-$(cat /opt/qrdine/app/DEPLOYED_COMMIT | cut -c1-7)` (now `app.prev-f9770ce`).
4. **Deploy** with the README command. `Sync__Enabled` stays as it is in `/etc/qrdine/qrdine.env` (on since 2026-11-20 05:24; switching it on is step 8).
5. **Check:**
   - `/health` returns 200.
   - The newest `__EFMigrationsHistory` row is `20261119223509_HybridSyncApply` (17 in all).
   - `curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5100/api/sync/hello` returns 404 with sync off, 401 with it on (no key).
   - `journalctl -u qrdine -n 50` is clean.
   - One test bill on the demo or own restaurant settles.
6. **Rollback, app only (normal case):** `systemctl stop qrdine && rm -rf /opt/qrdine/app && cp -a /opt/qrdine/app.prev-f9770ce /opt/qrdine/app && systemctl start qrdine` (and `cp -p /etc/qrdine/qrdine.env.before-cef360f /etc/qrdine/qrdine.env` to put `Sync__Enabled` back off; f9770ce itself runs fine with sync on). If a PC had already taken over the counter, remove it online first (Settings > Restaurant PC) or the old build will keep taking orders online while the PC also does. Older: `app.prev-18c989e` + `qrdine.env.before-f9770ce` (`Fiscal__Simulate=true`). The old build runs on the new schema (tested: see the change log), and its new rows still get a `SyncId` from the database default. Leave the new columns and tables in place. If sync was ever on, every paired PC must take a fresh snapshot after a rollback, because the old build writes no change lines and doesn't update `LastChangedAt`.
7. **Rollback, schema too (only if the database itself is wrong):** stop the app, then restore the step-2 dump into a fresh database. This loses everything after the dump. Or, with no data loss, run the migration's Down: on the PC, `dotnet ef migrations script 20261119210227_TaxOfficeGuestPassesGiveBack 20261119183302_BillOptionsAndCancelRules -p src/QrDine.Infrastructure -s src/QrDine.Api -o down.sql`, then run `down.sql` with psql. Either way, then start the old build.
8. **Switching sync on:** set `Sync__Enabled=true`, restart (one process), check `hello` answers 401 without a key. Owners then pair from Settings > Restaurant PC. Every save writes change lines from then on.

## Tax office filing (FBR / PRA)
How it works for owners: `docs/ONLINE-OFFLINE-HYBRID.md` section 7. For agents:
- **Code:** `FiscalSyncService` (settle, resend one / picked / all, the 2-minute database claim `Invoice.FiscalClaimUntil`, the setup check, the test bill, missing PCT codes filled from the menu on resend), `FiscalAutoRetryService` (every `Fiscal:AutoRetryMinutes`), `FiscalServices.cs` (`CloudFiscalTransport`: FBR and PRA formats, addresses, error wording), `FiscalEndpoints.cs`, `Settings.jsx` (Tax office tab, Billing → Tax on items) and `FbrInvoices.jsx` (Tax office invoices).
- **Server:** `Fiscal__Simulate=false` since 2026-11-20 03:18 (before that `true`, which holds every branch in Practice). Nothing is filed unless the operator puts filing in the plan, the owner switches it on, and a branch is set to Test or Live with its POSID and token.
- **Never file a test bill with the real FBR or PRA production servers.** To test, point all four `Fiscal__…Url` settings at the stand-in (`deploy/hostworld/e2e/mock_tax_office.py`); `tax_check.py` refuses to run otherwise. Real Test (sandbox) or Live credentials come only from the owner.
- FBR checks the registered IP: the owner must give FBR the Hostworld server's address for online filing.

## PC edition (`DineIn.exe`, fully offline)
How it works for owners: `docs/ONLINE-OFFLINE-HYBRID.md` section 4 and `deploy/pc/READ-ME.txt`. For agents:
- **One codebase.** The PC edition is the same `QrDine.Api`, started by `PcEdition.IsOn(args)`: built with `-p:PcEdition=true` (constant `PC_EDITION`, exe name `DineIn`), or a normal build run with `--pc` / `DINEIN_EDITION=pc`. Everything PC-only in `Program.cs` sits behind `pcPaths is not null`, so the server is unchanged.
- **Code:** `src/QrDine.Api/Pc/PcEdition.cs` (data folder, config defaults, port, single copy, start banner), `PcLicence.cs` (seller public key, licence state, `LicenceGateMiddleware`, `/api/licence`, features applied to the restaurant), `src/QrDine.Application/Pc/Licence.cs` (key format, states, issue/verify, PC code), `src/QrDine.Infrastructure/Pc/` (`MachineCode`, `SqliteSchemaUpgrader`, `PcBackupService`), `tools/QrDine.Licence` (the seller's `dinein-licence` tool), `frontend/src/pcEdition.js`, `pages/Activate.jsx`, `PcGate` and `LicenceBanner` in `App.jsx`.
- **Database:** SQLite in `DineIn-data/dinein.db` (WAL). No EF migrations on SQLite: `SqliteSchemaUpgrader` compares with the model and adds missing tables, columns and indexes after a `VACUUM INTO` copy; it never drops or changes a column. So on the PC a **renamed or retyped column is not carried over**: add a new one instead, as the Postgres migrations already do.
- **Web pages** are linked into the exe as `web/` (`ExcludeFromSingleFile=false`), because `wwwroot` is never bundled into a single file. `IncludeAllContentForSelfExtract` unpacks them on first start.
- **Licence:** `DINEIN1.<payload>.<signature>`, ECDSA P-256; payload `{v, id, r, m, i, e, f}`. The seller's **private key is never committed, printed or shared**; only `SellerPublicKey` is in the code. Changing that key invalidates every licence issued.
- **Gate:** no key, a bad key or another PC's key → 402 on every `/api` and `/hub` address except `/api/licence`; expired → reads only (GET, `/hub`, sign-in); a second `POST /api/onboard` → 403.
- **Build and check:** `bash deploy/pc/build-pc.sh [out]`, then `bash deploy/pc/run-pc-check.sh <exe> <work folder> <dinein-licence.dll> <private key .pem>` (port 5310; on 2026-11-20: 101/0 first start, 6/0 after restart). Licence keys made by the check are never printed.

## Known quirks
- SQLite/Npgsql return `DateTime` as `Unspecified`; JSON must emit UTC `Z` or clients
  ahead of UTC (PKT) mis-parse timers. Order timestamps are forced UTC in `OrderDto.From`.
  Apply the same care to any new time field the UI counts on.
- SignalR hub reconnect errors in the browser console during a redeploy are transient/expected.

## Change log (most recent first)
- **2026-11-20 05:44 PKT**: **Deployed `26b5ca8` (frontend only).** On a phone, the tax office set-up's dropdowns ran past their card; fields and selects now stop at the card's width. The server code is the same as `cef360f`, so only `wwwroot` was replaced (the README tar step, applied to `wwwroot`), then a restart. Dump `/var/backups/qrdine/pre-26b5ca8-2026-11-20-0613.dump` (server time), old build kept as `/opt/qrdine/app.prev-cef360f`. After: active, home 200, guest menu 200, the public site serves `index-CQuA6Fmr.css` with the fix, no errors in the journal.
- **2026-11-20 05:24 PKT**: **Deployed `cef360f` (hybrid) to Hostworld with `Sync__Enabled=true`, and merged `hybrid-sync` into `main`.** Restaurants can now pair a counter PC from Settings > Restaurant PC; nothing changes for a restaurant until its owner does.
  - Before, on the server's throwaway copy (Postgres) with the exact package (sha256 `eaa54f77…`): old build f9770ce 561/0; new build migrating the filled database 559/2, rollback build on the new schema 559/2, sync on 559/2 (the two known slug false failures each time: the reused database already has that restaurant name); tax stage against the stand-in 255/0; on a fresh database with sync on 561/0; the sync smoke (now also: a PC's newer row applied, an older one passed over, the takeover refusing orders online with its message, the menu still editable from home, removal giving the counter back) 47/0; the real `DineIn.exe` built from cef360f connected to that copy through an SSH tunnel (`run-hybrid-remote.sh` in the session scratchpad: 54 + 11 + 6, 0 failed, the tunnel closed for "no internet"), server log clean, inbox 14 applied / 2 passed over / 0 failed. No app errors in any stage. The rehearsal on a copy of production: data unchanged (1 restaurant, 0 orders), 17 migrations, guest menu 200, hello 401 with sync on, no errors. Production untouched by all of it.
  - Found and fixed on the way (test only, e96afa4): the hybrid check ordered on the PC with the server's item ids, which matched only because both local databases were fresh.
  - Deploy: dump `/var/backups/qrdine/pre-cef360f-2026-11-20-0524.dump` (94 tables), old build kept as `/opt/qrdine/app.prev-f9770ce`, settings kept as `/etc/qrdine/qrdine.env.before-cef360f`, then `Sync__Enabled=true` and the README tar steps.
  - After: one process, health 200; 17 migrations, newest `HybridSyncApply`; `/api/sync/hello` and `/push` 401 without a key; the devices list and pairing 401 without sign-in; guest menu 200; the public site serves `index-byu6B8zi.js`; no errors in the journal.
  - The nightly full backup at 04:00 failed again ("FAILED at line 84", the off-site problem since 2026-11-16); the pre-deploy dump above is local only.
- **2026-11-20 04:30 PKT**: **Hybrid (branch `hybrid-sync`)**, see "Sync with a restaurant's own PC" above and the guide's section 5. The server applies what a PC sends (`SyncApplier`), rows travel by `SyncId` with their links, one counter PC per restaurant takes over the counter (`takeover`, `CounterOnPcRule`), the PC side (`PcSync`: connect, download, push/pull every 5 s, photos, offline catch-up, a new key after removal), and the screens (PC `/connect`, PC Settings > Your account > Online copy, online Settings > Restaurant PC). Migration `HybridSyncApply` (additive). Checked: 338 unit tests (5 new hybrid ones over real HTTP plus removal and reconnect), `run-hybrid-check.sh` with the real exe and server (54 + 11 + 6, 0 failed, no log errors), the offline PC check again (101/0, 6/0), and every new screen in the browser pane (desktop and phone width). Also fixed in the PC check scripts: `taskkill` flags (`//F` reached Windows as `//F` with path conversion off, so processes were never stopped) and finding the exe's process (it listens on all addresses).
- **2026-11-20 03:20 PKT**: **PC edition (branch `pc-edition`)**, see "PC edition" above. One `DineIn.exe` (about 54 MB, win-x64, nothing to install) with SQLite, the licence, the activation screen, backups and the restaurant's READ-ME. Checked with the real exe (101/0 and 6/0 after a restart) and in the browser pane (activation, sign-up without the plan step, the licence reminder, Settings > Your account, phone width). 330 unit tests. The server is not changed by it and was not redeployed.
- **2026-11-20 03:18 PKT**: **Deployed `f9770ce` (branch `hybrid-sync-foundation`) to Hostworld, and merged the branch into `main`.** Tax office filing with FBR and PRA, Practice / Test / Live per branch, send at settle or later, resend one / picked / all, the automatic resend, one tax rate for all items (see "Tax office filing" above and the guide's section 7). Also the guest pass fix: a guest who scans in the same second the table's bill settles now gets a pass that works (it waits for the next second; before, every order from that pass got 401).
  - Before, on the server's throwaway copy with the exact package: old build 551/0; new build migrated 559/2, rollback 549/2, sync on 559/2 (the two known slug false failures each time); smoke 34/0; the new tax stage (`tax_check.py` against the stand-in FBR/PRA on 127.0.0.1:8599, never the real offices) 255/0; no app errors; production untouched. Locally: 317 unit tests. The rehearsal on a copy of production's data: 16 migrations, the one branch became FBR Practice Online (it was "On this computer", the old default, which can't work online), filing off, guest menu 200, sync 404, no errors.
  - Deploy: dump `/var/backups/qrdine/pre-f9770ce-2026-11-20-0318.dump`, old build kept as `/opt/qrdine/app.prev-18c989e`, settings kept as `/etc/qrdine/qrdine.env.before-f9770ce`, then `Fiscal__Simulate=false` and the README tar steps.
  - After: health 200; 16 migrations, newest `TaxOfficeGuestPassesGiveBack`; sync 404; guest menu 200; the public site serves `index-CKmtFYt7.js`; no errors in the journal.
  - **The owner must reprint the table QR codes.** The production restaurant's 4 tables had no QR key, so their old printed codes can't open a table any more ("This QR code doesn't work any more"). A table gets its key when staff open the tables list; print the codes from there.
  - Off-site backups are still failing (the Oracle bucket is gone, see the 00:52 entry).
- **2026-11-20 00:52 PKT**: **Deployed `18c989e` (branch `hybrid-sync-foundation`) to Hostworld.** It carries the sync foundation (off), the full QA pass and the owner settings below. `main` is still `b264329`, so until the branch is merged, the branch is what runs.
  - Before: the whole-day check on the server, on the exact package that went live, passed **552/552** (no slug failures there). The new build also ran on a copy of production's data in the throwaway cluster: 15 migrations, guest menu 200, sync 404, no errors.
  - Deploy: dump `/var/backups/qrdine/pre-18c989e-2026-11-20-0052.dump`, old build kept as `/opt/qrdine/app.prev-b264329`, then the README tar steps from the uploaded package. Each package now carries a `DEPLOYED_COMMIT` file.
  - After: health 200; 15 migrations, newest `BillOptionsAndCancelRules`; `/api/sync/hello` 404; the guest menu 200; the public site serves `index-gCCQmY3r.js`; every new switch is off; no errors in the journal. No test bill was made on production: its only restaurant has no orders or bills yet.
  - **Off-site backups have been failing since 2026-11-16 20:00:** the Oracle bucket `mystay-backups` answers `BucketNotFound` (404) to the stored upload link, so files pile up in `/var/backups/qrdine/outgoing` (23 files, 481 MB on 2026-11-20). Local backups still run. The owner must set up a new off-site place and write its link to `/etc/qrdine-backup/oracle-url`.
- **2026-11-19**: **Owner settings for bills and cancelling, plus the online/offline/hybrid guide.** Branch `hybrid-sync-foundation`; deployed 2026-11-20 (entry above). Every switch starts the way the app always worked, so no restaurant's bills or cancelling change until its owner flips one. Settings → Billing, tax & receipt, blocks "Bill options" and "Cancelling orders".
  - **Bill options** (`Invoice.Charges`, built by `BillingService.ChargesFor`; every price, split, khata and floor total goes through it):
    - `ServiceChargeTaxable`: the service charge is taxed at the bill's default rate (the card rate when cashless). Kept apart as `Invoice.ServiceTaxAmount` with its rate in `ServiceTaxPercent`. Reports count it as tax. FBR gets it on the SERVICE line (TaxRate, TaxCharged, TotalAmount = service + tax), so the totals still reconcile.
    - `ServiceAfterDiscount`: service on `max(0, subtotal − discount)`. The discount report's "Before" adds back the service (and its tax) that came off.
    - `RoundTotals`: the last step rounds to a whole rupee; `Invoice.RoundingAmount` keeps the difference (−0.50 to +0.50). Change is worked out on the rounded total. **FBR gets the exact, unrounded amounts.** The CSV export gains a `Rounding` column at the end (the old columns keep their places).
    - `CardRateForAllItems`: paid cashless, an item's own rate above 0 is replaced by the card rate; 0 stays tax-free.
    - The bill, receipt (`Receipt.jsx`) and printed slip show "GST on service" and "Rounding". The printed slip also gains its missing "Further tax" line.
  - **Cancelling** (`OrderService.Cancel` / `VoidItem`, who is asking comes from `Access.CancellerOf`):
    - Before the kitchen starts cooking (Placed, Approved) cancelling works exactly as before.
    - `NoCancelOnceCooking` (screen: "Allow cancelling once cooking has started", on by default): when locked, food that is cooking or ready (and dishes on a served order) can only be cancelled or removed by the owner. A guest gets "Your food is already being made…". The guest's order screen asks the server (`OrderDto.GuestCanCancel`, filled only for guests) and hides the button.
    - `CashierCancelAnyStage` (off by default): the counter (Owner, Manager, Cashier roles) can cancel any unpaid order, served included (`Order.Cancel(evenServed)`), or remove any dish, whatever the lock says, even without `orders.cancel`. Without it, cashiers still need `orders.cancel`, which their role doesn't have by default. The void route moved from the `CanCancel` policy to `StaffAny` plus this check. `GET /orders/cancel-rules` gives staff screens the two rules, so Reception shows only buttons that will work.
    - `KeepStockOnCookedCancel` (screen: "Put ingredients back…", on by default): when off, food cancelled while Cooking/Ready stays used up. Approved food always goes back; served food never did.
    - Only the owner can change the three cancelling switches (`SetBillingSettings(callerIsOwner)`). The new inputs are nullable, so an older screen sending none of them changes nothing.
  - Column names are chosen so that false is the old behaviour: EF can't store false in a bool whose database default is true. Migration `BillOptionsAndCancelRules` adds 7 bools to `Restaurants` and 3 decimals to `Invoices`, all defaulting to 0, so the old build inserts fine.
  - Tests: 284 pass (`BillOptionsTests.cs` adds 19). Checked in the browser on the QA restaurant: the toggles save; a 750 bill with 5% service and 16% GST shows 37.50 service, GST on service 6, rounding +0.50, total 914 (card: 827). Settled at 1000 cash, it gave 86 change. Over real HTTP, a guest is refused once cooking starts, the owner isn't, and a cashier is allowed with the any-stage switch on (403 with it off). The settings page has no side scroll at 375 px.
  - **Whole-day check on the PC** (same four stages, throwaway Postgres 18 on 127.0.0.1:5434, Release builds, Production mode): old `b264329` fresh 544/544; new build migrating it (15 migrations, newest `BillOptionsAndCancelRules`) 550 pass; old build on the new schema 542 pass (the rollback case); new build with sync on 550 pass, then the sync smoke 34/34. Only the two known slug failures; no app errors in any stage. Still worth rerunning on the server before deploying.
  - **Local dev DB:** `EnsureCreated` never adds columns. Either delete `qrdine.db`, or add the 10 columns by hand with `ALTER TABLE … ADD COLUMN … DEFAULT 0`.
  - `docs/ONLINE-OFFLINE-HYBRID.md` is new: what is ready (online yes, offline yes with limits, hybrid phase 1 only), how each works and how to set each up, and the new settings.
- **2026-11-19**: **Full QA pass: bills, tax and counts (commit `839c1ef`).** Branch `hybrid-sync-foundation`; not deployed. A read-only audit plus hands-on tests on phone, tablet and PC sizes found money that was shown, stored or counted wrong. Each fix has a test in `MoneyLogicTests.cs`.
  - **Split bills.** A part can't be more than what is left (it used to count the extra as drawer cash: day close showed a false shortage). Change on a part-paid bill is worked out against what was left. A bill can't be discounted below what is already paid. Settle sends the amount the cashier saw (`ExpectedTotal`) and is refused if an order landed since. The split screen divides the standard-rate total (`BillDto.SplitRemaining`), because parts are always booked at the standard rate.
  - **Received and change.** `AmountTendered` is everything received for the bill, earlier parts included, so received minus change is always the total. The receipt says "Received" (not "Cash received") on a split bill.
  - **Discount and tax.** The discount comes off the food only, never the delivery charge (it used to soak up part of the discount, so GST was charged on more food). The printed rate is the tax over the taxed lines (`Invoice.DisplayTaxRate`). **An item at 0% is now tax-free; blank still means the restaurant rate.** Migration `OrderCancelledByAndItemTaxZero` turns existing 0s into blank, so no bill changes, and the old build treats both alike. Further tax is printed as its own line (`FurtherTaxAmount` on the bill and the invoice).
  - **One way of counting.** Reports' payment methods, the dashboard's "how people paid" and day closing all use `MoneyTaken.ByMethod`: each part by its own method, the rest by the bill's. Older overpaid parts count only what the bill kept (the extra comes off the cash parts). A branch's report holds only that branch's money: supplier bills, khata payments and expenses with no branch count under All branches only. Best sellers leave out the delivery charge. The discount report's "Before" is the real bill before the discount (it adds back the tax the discount removed).
  - **Bill totals on the floor.** `GET /api/r/{slug}/floor/bill-totals` prices every open table and ticket like its bill. Reception's tiles, the table sheet and the rider's "collect" (Deliveries card and WhatsApp text) use it instead of the food alone (they used to leave out tax, service and discount: a rider could under-collect).
  - **Payroll.** A bonus is paid out when recorded (it writes an expense), so it is no longer added to "still due" as well (it was being paid twice). The salary slip shows it as earned and paid.
  - **Cancelling.** The last order on a table with a part paid can't be cancelled (the money stayed on the table and the next guests got it). Staff must give a reason; `Order.CancelledBy` and `CancelledAt` are recorded and the cancelled-orders report shows who. Cooking straight from Placed takes the stock, so a cancel no longer gives back stock that never left.
  - **Smaller.** Order history counts Pakistan days. Amounts with paisa show two places (Rs 2,625.60). Switching Cash to Card clears the typed cash. A cleared discount box means no discount for a split part too. Discount box messages for over the food or below zero. Split people stay 1–50. Payroll month buttons are 40 px. The removed `POST /orders/{id}/pay` marked an order paid with no invoice (nothing used it).
  - **Not changed, owner's call:** the service charge is still on the subtotal before discount and untaxed (so a 100%-discounted bill still pays service), totals still keep paisa, and items with their own rate keep it when paid by card. These became owner settings the same day (see the bill options entry above).
- **2026-11-19**: **Full QA pass: forms and screens (commits `dbd058e`, merged in `5acb988`, then `a8605b0` and `46a3aaa`).** Same branch, not deployed. Tests: 265 pass (`FormRulesTests.cs` adds 49).
  - **Refused, not quietly changed.** Bill settings (tax, card tax, service 0–100, discount ≥ 0 and ≤ 100 when a %), item tax and further tax 0–100, and a deal price at or above the price now come back with a reason. They used to be clamped or dropped without a word. `BillingRules` holds the limits and messages.
  - **One phone rule** (`Common/Phone.cs`, `frontend/src/phone.js`): digits with spaces, dashes, brackets or a leading +, and 7 to 15 digits. It applies to tickets, guest orders, customers, suppliers, staff mobiles, employees and riders. A running delivery can't blank out its phone or address.
  - **Other server checks:** a table code is letters, numbers, `-` and `_`; a staff username is a mobile number or `[a-zA-Z0-9_.-]`, unique across all restaurants (staff sign in without naming the restaurant); FBR NTN/STRN lengths; branch FBR endpoint and IP; purchase and return lines; a subscription payment's kind and months; qty 1–50 and a 200-character note on every order line; a delivery charge of 0–100,000. The staff edit sends the username only when it was changed, so an owner who signed up with a space can still edit their name.
  - **Every form:** required fields get a red * from one CSS rule (`:has(:required)`); optional ones say "(optional)". Inline messages keep the dialog open, busy guards stop double saves (Loyalty approve could double-approve), and `ui.prompt` can validate. Settings shows "Service charge % (dine-in)" even with tax off. The item tax hint says "Leave blank for the restaurant rate; 0 means no tax". Guest takeaway/delivery says "Subtotal, tax is added on the bill" when tax is on (`PickupDto.TaxOnBill`).
  - **Screens:** tap targets are at least 36 px (qty −/+, `.ts-x`, the password eye, landing links). At 360 px the Inventory add form and the Menu and Inventory row buttons wrap instead of pushing the page sideways. `.sx-main` has `overflow-x: clip` because the page's opening tilt is briefly wider than the frame, which flashed a sideways scrollbar on a PC. All 20 routes are clean at 360, 768 and 1280 px.
  - **Left as is:** menu import stays lenient (the page warns), the recipe service still drops lines of 0 (the page refuses them), and FeedbackEndpoints/LoyaltyEndpoints have no ValidationFilter (the Loyalty service checks its own input).
  - **Whole-day check, on the PC this time** (2026-11-19 evening; the link to Hostworld was losing half its packets, so uploads kept failing). It was the same four stages as `run.sh`, on a throwaway Postgres 18 cluster (127.0.0.1:5434, data in the scratch folder, removed afterwards) with the Release builds in Production mode, `Fiscal__Simulate=true` and fresh secrets:
    - Old build `b264329` on a fresh database: 544/544.
    - New build `46a3aaa` migrating that data: 550 pass. Every SyncId is filled, sync-off answers 404, no change lines, no item left at tax 0.
    - Old build again on the new schema (the rollback case): 542 pass.
    - New build with sync on: 549 pass, then the smoke test 34/34.
    - The only failures are the two known slug ones (`e2e-karachi-grill-N`, from signing the same name up again). No app errors in any stage.
    - Worth running once more on the server before deploying, when the link is good.
- **2026-11-19**: **Sync foundation for running on a restaurant's own PC (phase 1, server side, off by default).** Branch `hybrid-sync-foundation`; not deployed. See "Sync with a restaurant's own PC" above for the API, the rules, the switch and the deploy checklist.
  - Migration `AddSyncFoundation`, additive only:
    - 38 tables get `SyncId` and `LastChangedAt`.
    - New tables `SyncDevices`, `SyncChanges` and `SyncInbox`.
    - No existing column, answer or rule changes.
  - It differs from the first plan in three ways:
    - Deletes are tombstone lines in the change log, not a soft-delete column.
    - The column is `LastChangedAt`, because several tables already have an `UpdatedAt`.
    - Push only stores changes; applying them waits for the PC app (phase 3).
  - Tests: 30 new (25 in `SyncTests`, 5 over real HTTP in `SyncHttpTests`).
    - The HTTP tests start the sync routes on a local port, so the test project now references the API and the ASP.NET framework.
    - All 197 pass, and they also pass with sync forced on.
  - Whole-day check on the throwaway copy at Hostworld, in four stages on one database:
    1. Old build `b264329` on a fresh database: 545/545.
    2. New build migrating that filled database: 38 tables got unique SyncIds, and the sync addresses answered 404. 542/544.
    3. Old build again on the migrated database: 543/545. Its 49 new orders all got their own SyncId from the database default.
    4. New build with sync on: 543/545, plus a 34-check sync smoke test on Postgres. The smoke test covered pairing, isolation between two restaurants, pull paging, snapshots without secrets, delete lines, 8 same-moment pushes stored once, and removing a device.
    - The 2 failures in stages 2–4 are the check signing up "E2E Karachi Grill" again: the address gets a `-2`, `-3`, and the lookup finds the first one.
    - Production stayed untouched, and the teardown left nothing behind.
  - After that run, a restaurant's pushes take turns (`PushTurns`), so a batch sent twice at once no longer logs a database error.
- **2026-11-13**: **On a phone, the bill shows the ways to pay before Settle.** The bill sheet's pinned bar (Back, Print bill, WhatsApp, then a full-width Settle) was two rows tall on a phone and covered "Paid by", so a cashier could settle without seeing that the bill would be recorded as Cash. On phones narrower than 460 px it also hung 4 px past the sheet and clipped Settle.
  - On a phone the bar is now one row: Back, Print and WhatsApp show as icons (their names stay as labels), and Settle takes the rest of the row.
  - Settle names the way it records: "Settle · JazzCash" (`.settle-way`).
  - "Paid by" now comes before the loyalty box.
  - The bar lines up with the sheet's 16 px padding on small phones.
  - Checked with headless Chrome at 360×740, 390×844 and 1280×800: all five ways are tappable when the bill opens, and a JazzCash settle on a 360 px phone closed the bill. Frontend only (`Cashier.jsx`, `design/staff.css`).
- **2026-11-13**: **The sales list and invoices CSV use Pakistan days.** `/admin/reports/sales` and `invoices.csv` read plain `from`/`to` dates as UTC days (`Window` in `MenuAdminEndpoints.cs`), while the main report and day closing use Pakistan days. So a bill made between 00:00 and 05:00 Pakistan time showed under the previous date in the sales list.
  - `BusinessDay.RangeUtc(from, to)` turns a plain date into the whole Pakistan day: 19:00 UTC the day before, to just before 19:00 UTC. A value with a time is used as it is; no dates means the last 31 days.
  - 2 new tests in `TimeTests`. The whole-day check, run after midnight Pakistan time, passed 545 of 545.
- **2026-11-12**: **Takeaway and delivery from the guest's own phone, with a map, a rider screen and live tracking.** Deployed to Hostworld on 2026-11-13. It adds migrations `AddGuestPickupDelivery` and `AddDeliveryTracking`, both additive.
  - **Order QR.** One restaurant-wide "order" QR, separate from the table QRs (Owner → QR codes). A guest picks Takeaway or Delivery and gives a name, number and address. For delivery they drop a map pin or tap "Use my location".
    - The order reaches reception as a Parcel or Delivery ticket that needs approving.
    - Off by default: `Restaurant.GuestTakeaway` / `GuestDelivery`.
    - Related settings: `DeliveryFee`, `DeliveryNote`, the restaurant's own pin (`Latitude`/`Longitude`), and `DeliveryRadiusKm` (0 means no limit).
  - **Deliveries board** (`pages/Deliveries.jsx`, staff). It shows each ticket on the map.
    - Approve the ticket and set a promised time (`POST /tickets/{code}/promise`). For a delivery that is "at the door by", for a takeaway "ready by".
    - Send a rider link (`/rider-link`, with the rider's name and phone) and share it on WhatsApp.
  - **Rider screen** (`pages/Ride.jsx`, `/r/{slug}/ride/{code}?k=…`). There's no account: the private `RiderKey` in the link is the only pass, and a new link replaces it.
    - It shows the address, distance, the bag's contents, the cash to collect and the promised time.
    - The rider taps "Picked up" and then "Delivered". While on the road the phone posts its GPS to `/ride/{code}/location`.
  - **Guest tracking** (`TrackCard` in `Menu.jsx`, `GET /tickets/{code}/track`, `DeliveryService.Track`). It shows the stage, the rider on the map, "Arrives by" / "Ready at" with minutes left, and how early or late it arrived.
    - A takeaway's ETA is the kitchen's ready time, otherwise the promised time.
    - The pass for a takeaway or delivery ticket is handed out once, when the order is placed, and kept on that phone (`auth.keepTicket`). Another phone can't follow the order.
  - **Map:** `MapView.jsx` (Leaflet, loaded only when a map shows) on OpenStreetMap's public tiles, which need no key. OSM's tile policy suits light use only. If traffic grows, move to a tile provider with a key.
  - **Landing page and plans** list takeaway and delivery QR, map tracking and the rider screen. There's a new FAQ entry.
  - 13 new tests in `FloorTests`. Before deploying, the whole-day check ran 545 checks and 543 passed.
    - The 2 failures were an older bug this change didn't touch; it was fixed on 2026-11-13 (the entry above).
  - **Promo videos** for these features live in the shopify repo, under `qrdine-videos/`. They are recorded on the local copy with the demo restaurant, which has only tables T1–T4.
- **2026-11-09** — **Sign-in and access checks tightened** (no migration; every existing pass stops working once):
  - Found: anyone with an order number could read the order, and anyone could list a table's live orders. The live feed let anyone listen to a whole restaurant's orders. A reset password, removed staff member or role change didn't end an existing pass. Passwords could be guessed without limit by changing address. The print bridge was handed a long-lived pass.
  - Staff and platform passes carry `stf` and `stm` (a stamp of the password hash). `TenantResolutionMiddleware` checks them against the database on every request (`Auth/StaffSessions.cs`) and uses the current role and permissions, not the ones in the pass.
  - Guest passes carry `iat` and belong to one table (`Auth/Access.cs`). Reading another table's order answers like a missing one. `GET /tables` needs staff.
  - `OrderHub` is `[Authorize]`, takes the restaurant from the pass, and checks each feed it's asked to join. It closes when the pass runs out.
  - `LoginGuard`: 10 wrong passwords in 15 minutes rest that account for 15 minutes (staff and platform; in memory, one process). Staff passwords are 6+ characters. Changing your own password returns a new pass.
  - `SecurityHeadersMiddleware` adds CSP, frame denial, HSTS and the rest. Rate limits count IPv6 per /64.
  - The platform operator account is synced from the env file on every start (`DbSeeder`). `/admin` in the browser goes to sign-in, or to your own home if you're signed in. The browser checks a saved pass once before showing staff screens.
  - The print bridge signs in as a staff account (`QRDINE_PASSWORD` or a hidden prompt) and signs in again before the pass ends.
  - 18 new tests (`SecurityTests`). The whole-day check now also tries reading orders without a pass and with another table's pass.
- **2026-11-09** — **Photo reading copes with Google being busy.** The owner's first key setup hit Gemini 503 "high demand" on every try. `GeminiMenuExtractor` now retries a busy first model once, then tries `Gemini:FallbackModels` (default `gemini-flash-lite-latest`) within a 90 s budget, and stops at once on a refused key or unusable photo. `qrdine-set-gemini.sh` checks the key via `models.list`, tests up to four models, and saves a valid key even when all of them are busy. 10 new tests (`GeminiMenuExtractorTests`).
- **2026-11-09** — **One change at a time per restaurant** (migration `OneOpenSeatingOneBill`, adds two unique indexes):
  - A whole-day test on real Postgres with requests sent at the same moment found: two cashiers settling one table made 4 bills, a double approve took stock twice, orders from ten phones at once landed on a seating nobody billed, part payments could pass the bill, and two tables moved onto one lost a bill.
  - Fix: `RestaurantGate` / `FloorTurn` (Application/Common) and `FloorGateFilter` (Api/Middleware) make each restaurant's writes take turns. They're applied to the `floor` route group in `Program.cs` (orders, billing, floor, inventory, business, staff, loyalty). GETs are never queued. Settling hands back its turn before FBR filing. The order, table and bill rules themselves are unchanged.
  - Database backstop: one open seating per table (`IX_Sessions_OneOpenPerTable`, filtered on `IsOpen`) and one bill per seating (`IX_Invoices_OnePerSeating`).
  - **Single process only.** A second app instance would need a Postgres advisory lock instead. New write routes for orders, bills, stock or khatas go in the `floor` group.
  - Whole-day check scripts: `deploy/hostworld/e2e/` (see the README).
- **2026-11-09** — **Dine In: redesign, back office and floor features** (branch `hostworld-production`, migrations `AddSuppliersExpenses`, `AddFloorTicketsKhataPayroll`, both additive):
  - The user-facing name is **Dine In**. New look in `frontend/src/design/*.css`, CSS-only 3D (`Diorama.jsx`, `motion.js`). Never put a transform on a scroll-snap element, and don't use a forwards animation fill on a container with `position: fixed` children.
  - **Sign-in** is `POST /api/auth/login`: mobile or username plus password, and the account says which restaurant (`Auth/LoginName.cs`). Sign-up has no address field.
  - **Guest menu** opens on category cards, with a swipe pager, deals, most ordered and a suggestion before sending.
  - **Floor:** parcel and delivery tickets (`TicketService`), table shift and merge, guests calling a waiter or the bill (`ServiceRequestService`), item void with a reason, part payments, card and cash tax rates, day closing (`DayCloseService`).
  - **Back office:** supplier khata and purchases, customer khata with limits, expenses, salaries and attendance, reports by section (`Reports.jsx`), bulk price change.
  - **Permissions added:** `expenses.manage`, `suppliers.manage`, `orders.discount`, `customers.manage`, `payroll.manage`.
  - **Plans:** back-office routes are refused on the server when the plan leaves them out (`Auth/PlanGate.cs`). Order, table and bill routes deliberately aren't. `GET /staff/plan` lets a signed-in screen notice a plan change.
  - **Times:** `UtcDateTimeConverter` marks every API time as UTC, because the database hands them back without a kind. Printed slips, CSVs and FBR use `BusinessDay.ToPkt`, since the server's clock is IST.
  - **Gemini:** `gemini-2.0-flash` was shut down, so the default is `gemini-flash-latest`. Set the key with `qrdine-set-gemini` (see `deploy/hostworld/README.md`).
- **2026-11-09** — **Moved to Hostworld; photo shrinking; unpaid restaurants lock**:
  - Item photos and logos shrink in the browser before upload (`frontend/src/imageShrink.js`): photos 1000 px JPEG, logos 512 px keeping transparency. The server's limits and content checks are unchanged.
  - `PlanExpiresAt` is now enforced in `TenantResolutionMiddleware` (`Domain/Restaurants/SubscriptionAccess.cs`): warning 5 days before, 3 grace days after, then 403 with `expired=true` on all `/api/r/{slug}` routes until a payment is recorded. Owners see it through `GET /admin/subscription` and `PlanBanner` in `App.jsx`; guests get a neutral "menu isn't available" screen. Suspended restaurants now get a friendly message too.
- **2026-10-21** — **Loyalty, rewards & Google reviews on the table QR** (branch `feature/loyalty`, migration `AddLoyalty`):
  - **Off by default**: a restaurant sees nothing new until the owner switches it on (Loyalty → Program & reviews).
    New tables `LoyaltyPrograms` (one per restaurant), `LoyaltyMembers` (unique phone per restaurant), `LoyaltyRewards`,
    `LoyaltyTransactions` (the ledger behind every balance), `LoyaltyRedemptions`; one nullable column
    `Sessions.LoyaltyMemberId`. All tenant-filtered. Service `src/QrDine.Application/Loyalty/LoyaltyService.cs`,
    endpoints `src/QrDine.Api/Endpoints/LoyaltyEndpoints.cs`, tests `tests/QrDine.Tests/LoyaltyTests.cs`.
  - **Joining**: guests join on the QR page with name + mobile (Pakistani numbers normalised to 03xxxxxxxxx); the phone
    keeps a member token (`X-Member-Token`). No SMS/OTP exists, so **points only come off when staff approve** a reward
    request (code shown on the guest's phone; approve on Reception or Loyalty → Reward requests).
  - **Earning**: `BillingService.Finalize` calls `AwardForSettledSession` **after** the sale is saved, in a try/catch
    (a loyalty error is logged, never fails a settle). Once per seating (an Earn ledger line marks it). The seating is
    linked to a member when the guest joins/orders (`/loyalty/link`) or when Reception adds a phone on the bill.
    Points = floor(bill total ÷ PerAmount) × EarnPoints, above MinBillAmount. Bill amounts are never changed.
  - **Reviews**: in-house rating card (same `/feedback` endpoint; optional `memberToken` earns the rating bonus at most
    once per 20 h) + **Review us on Google / See our Google reviews** buttons from the owner's links. Per Google's
    rules the button shows to every guest and is never rewarded with points.
  - Permissions reuse existing keys: programme/rewards/members = `settings.manage`; reward requests + bill member =
    `orders.pay`. Guest endpoints use the `public` rate limit.
  - Local dev DB must be recreated for the new tables (EnsureCreated): the old one was kept as
    `src/QrDine.Api/qrdine.db.bak-before-loyalty`.
- **2026-09-01** — Marketing landing page + waiter "add items to a running tab":
  - **Landing page** (`frontend/src/pages/Landing.jsx`, styles under the `.lp-*` block in
    `styles.css`): the app's public front door at **`/`** (was a redirect to `/login`).
    Brand hero with an animated phone + receipt mock, features, how-it-works, about,
    contact and footer; scroll-reveal via IntersectionObserver (JS-added class so no-JS
    stays visible). CTAs go to `/signup` (Get started) and `/login` (Sign in). `/` is now
    in `App.jsx`'s `isBare` list (no staff topbar).
  - **Waiter add-items**: the Board's *My tables* cards gained an **Add items** button
    that opens Take-order pre-selected to that table (`TakeOrder initialTable`). Placing
    reuses the table's **open session** (already how `OrderService.Place` works), so the
    extra items reach the kitchen as their own ticket but **bill together as one tab**.
- **2026-09-01** — Receipt branding, edit-in-place, 80mm printing & professional icon pass:
  - **Receipt branding**: new `Restaurant.LogoUrl/ReceiptHeader/ReceiptFooter` (migration
    `AddReceiptBranding`). Settings → **Receipt & branding** card uploads a centred logo
    (`POST /admin/logo`, PNG/JPG/WebP ≤2 MB, stored via `IFileStorage`) and edits the
    printed header/footer (`GET/PUT /admin/receipt-settings`). Threaded into `BillDto` +
    `InvoiceDto`; `Receipt.jsx` renders logo-top-centre, header line, custom footer.
  - **80mm thermal printing**: print CSS now `@page { size: 80mm auto; margin: 0 }` +
    `.receipt { width: 80mm }` so bills/kitchen tickets print at 80 mm and the roll
    auto-cuts at content end (was full-page A4). Reception incoming cards gained direct
    **Kitchen** / **Bill** print buttons (open the slip pre-set + auto-fire print).
  - **Edit-in-place**: branches and staff are now editable (were create/delete only) via an
    inline panel + **double-click** a row. Staff edit can rename the sign-in username
    (`StaffUpdateInput.Username`, unique-checked). Tables keep rename+delete.
  - **FBR POS gating**: the per-branch/-staff FBR POSID fields only show when FBR is on
    (`featureFbr && fbrEnabled`) — hidden otherwise.
  - **Restaurant name in header**: `AuthResponse.RestaurantName` (login + onboard); the
    staff topbar shows the **DineIn logo** (same as the login page, not an emoji) + the
    restaurant name.
  - **Professional icon pass**: new `frontend/src/icons.jsx` thin-line SVG set replaces all
    decorative emoji across every screen (food-item emoji *data* + the emoji picker kept).
  - **Login (unchanged, answer to a FAQ)**: admin-created staff sign in on the **same**
    `/login` page (restaurant name/address + their username + password); `HOME_FOR[role]`
    routes them to their own screen and the nav is permission-gated, so they see only what
    they need. They now see the restaurant name in the header.
- **2026-08-31** — Security + auth/UX hardening pass:
  - **Login fixed**: name→slug lookup is now normalisation-tolerant (type the name OR
    the address, any spacing/casing), fields are clearly labelled with guidance, and a
    freshly-registered restaurant logs in immediately. Root cause was an exact-match-only
    lookup with no UI guidance (`OnboardingEndpoints.cs` lookup, `pages/Login.jsx`).
  - **Friendly errors everywhere**: `ValidationFilter` emits camelCase field keys + a
    human `detail`; all FluentValidation rules got friendly `.WithMessage()`; the SPA's
    `errors.js` (`ApiError` + `parseError`) maps every 4xx/5xx to a friendly line and per
    -field messages — the old `j()`/`pj()` raw-JSON leak is gone. 500s carry a support
    `reference` (logged server-side). Added an `ErrorBoundary` + toast host (`ui.jsx`).
  - **Inline validation**: `forms.jsx` (`useForm` + `FormField`) — validate on blur+submit,
    clear-on-fix, preserve values, focus first invalid, subtle red border + message below.
    Applied to Signup, Login, PlatformLogin.
  - **Rate limiting** expanded + configurable (`RateLimits` in appsettings): `login`,
    `register` (onboard), `orders`, `public` (guest session/feedback). Friendly 429 body.
  - **Security fixes**: owner-only gating on owner-role staff create/update/delete/password
    (was Manager-escalatable — H1); reject non-guest/non-staff tokens on order placement;
    `nosniff` on served files; 1 MB CSV-import cap; startup guard rejecting a weak/default
    `Jwt:Secret` in production; FBR TLS bypass now gated by `Fiscal:AllowInvalidCertificate`.
- **2026-08-31** — Waiter ordering (take order on a table's behalf), `Order.Source`
  (QR vs Waiter) + Reception badge, `orders.create` permission (configurable + enforced),
  role-gated `OrderActions` for kitchen-unavailable fallback (Reception & Waiter),
  UTC timer fix, and a polished responsive/animated Take-order UI. Migration `AddOrderSource`.
