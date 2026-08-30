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
`reports.view`, `invoice.sync`.

Defaults: Owner/Manager = all; Cashier = create+approve+pay+invoice.sync;
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
- `frontend/src/api.js`, `frontend/src/auth.js`, `frontend/src/useStaffOrders.js`, `frontend/src/useHub.js`
- `frontend/src/styles.css` — design system (cream/maroon POS theme) + animations (bottom of file)

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

## Deploy (Oracle Cloud free VM)
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
- **Platform operator** (`/platform/login`): `grep -E '^PLATFORM_ADMIN_' ~/qrdine/deploy/oracle/.env`
- **Restaurant owner**: whatever was set at **sign-up** (hashed; not recoverable from DB).
  No demo accounts are seeded in production.
- **Local dev test accounts** (SQLite only, NOT on the server): owner/`owner123`,
  cashier/`cash123`, kitchen/`kit123`, waiter/`wait123`; dev platform `superadmin`/`super123`.

## Known quirks
- SQLite/Npgsql return `DateTime` as `Unspecified`; JSON must emit UTC `Z` or clients
  ahead of UTC (PKT) mis-parse timers. Order timestamps are forced UTC in `OrderDto.From`.
  Apply the same care to any new time field the UI counts on.
- SignalR hub reconnect errors in the browser console during a redeploy are transient/expected.

## Change log (most recent first)
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
