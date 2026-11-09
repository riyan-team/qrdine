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
- **Platform operator** (`/platform/login`): `grep '^PlatformAdmin__' /etc/qrdine/qrdine.env` on Hostworld
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
