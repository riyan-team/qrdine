# QrDine — restaurant QR ordering (production build)

Cloud, multi-tenant QR ordering + kitchen-timing platform for the Pakistan market.
Built on **ASP.NET Core 9 + SignalR + EF Core** (Clean Architecture) with a **React** frontend.

> **Design first:** the full architecture, flows, data model and roadmap live in
> [docs/blueprint.html](docs/blueprint.html) (also published as an interactive page).

---

## What runs today (Phase 1 foundation)

A working, tenant-scoped backend on clean architecture, with the complete order
lifecycle enforced by a guarded domain aggregate:

- Multi-tenant: every request resolves a restaurant (tenant); EF Core global query
  filters auto-scope all data. Unknown tenant → 404.
- **Order aggregate owns its state machine** — illegal moves (e.g. serve before
  cook) are rejected with a clean 400, not a corrupt state.
- **Table sessions** — orders attach to an open seating; paying the last order
  closes the session so the next guest starts clean.
- Live updates over SignalR to staff screens and each customer's table.
- Runs on **SQLite locally with zero setup**; one config line switches to
  **PostgreSQL** for the cloud.

The existing React screens (menu, reception, kitchen, waiter, owner/QR) run against
this backend unchanged.

### Auth & roles (added)

- **Staff login** issues a role-carrying JWT; staff endpoints are **role-gated**
  (reception approves/pays, kitchen cooks/readies, waiter assigns/serves,
  manager/owner cancels). Wrong role → 403, no token → 401.
- **Guest table-session tokens**: scanning a table issues a short-lived token
  scoped to that table, so a phone can only order for the table it scanned
  (wrong table → 400). A waiter can order on behalf of any table.
- A token issued for one restaurant is rejected on another's routes (403).

**Demo staff logins** (at `/login`, or one-tap buttons):

| Role | Username | Password |
|---|---|---|
| Owner | `owner` | `owner123` |
| Reception (Cashier) | `cashier` | `cash123` |
| Kitchen | `kitchen` | `kit123` |
| Waiter | `waiter` | `wait123` |

> The dev JWT secret is in `appsettings.json`. **In production set `Jwt__Secret`
> via an env var / secret** (≥ 32 chars) — never ship the dev value.

## Solution layout (Clean Architecture)

```
QrDine.sln
src/
  QrDine.Domain/          # entities, Order aggregate + state machine, events — no dependencies
  QrDine.Application/     # use-case services (MenuService, OrderService), DTOs, interfaces
  QrDine.Infrastructure/  # EF Core DbContext + tenant filter, SignalR, seeder, provider switch
  QrDine.Api/             # endpoints, middleware (tenant, errors), Program.cs
frontend/                 # React (Vite) — the five screens
tools/QrDine.PrintBridge/ # on-site print bridge (SignalR client → ESC/POS)
deploy/                   # Dockerfile.api + docker-compose.yml (API + Postgres)
docs/blueprint.html       # architecture & flow blueprint
backend/                  # ⚠ the original throwaway demo (superseded by src/) — safe to delete
```

Dependency rule: `Api → Application/Infrastructure → Domain`. The Domain depends on nothing.

---

## Run it locally (two terminals)

**1. Backend** — http://localhost:5099 (SQLite, auto-created & seeded)
```bash
cd src/QrDine.Api
dotnet run --launch-profile http
```

**2. Frontend** — http://localhost:5173
```bash
cd frontend
npm install   # first time only
npm run dev
```

Open **http://localhost:5173**, go to **Owner / QR codes**, open a table's menu,
place an order, and watch it flow across the **Reception → Kitchen → Waiter** tabs live.

**3. (optional) Print bridge** — prints kitchen tickets + receipts
```bash
cd tools/QrDine.PrintBridge
dotnet run
```
Leave it running; it prints to the console and to `printouts/*.txt`. Approve an order
(kitchen ticket) or settle a table (receipt) to see jobs arrive.

## Switch to PostgreSQL (cloud)

1. Set config (env vars in production):
   ```
   Database__Provider=Postgres
   ConnectionStrings__Default=Host=...;Database=qrdine;Username=...;Password=...
   ```
2. Migrations are provider-specific. Generate the Postgres set once:
   ```bash
   dotnet ef migrations add InitialPg --project src/QrDine.Infrastructure \
     --startup-project src/QrDine.Api -o Persistence/Migrations -- --provider Postgres
   ```
3. Or just use Docker (does Postgres for you):
   ```bash
   docker compose -f deploy/docker-compose.yml up --build
   ```

---

## What's next (from the blueprint roadmap)

- ✅ **Auth & roles** — done (JWT staff login + role gating, guest table-session tokens).
- ✅ **Billing** — done. Per-table itemized bill (aggregated lines, GST %, service %,
  discount), **settle → numbered invoice** (`INV-00001`…), closes the session, and a
  **printable receipt** in the cashier UI. Groundwork for FBR/PRA fiscal invoicing.
- ✅ **Variants & modifiers** — done. Items can have **variants** (Half/Full portions,
  own price) and **modifier groups** (spice level, add-ons) with min/max rules. The
  guest customizes via a modal; prices resolve server-side with validation; selections
  show on the kitchen ticket, cashier view, and printed bill.
- ✅ **Hardening** — done. FluentValidation on every request (400 `ValidationProblem`
  with per-field errors), Serilog structured + request logging, `/health` with a DB
  check, and split-query behaviour for multi-collection loads.
- ✅ **FBR/PRA fiscal invoicing** — done as a pluggable seam. On settle, the invoice
  is registered with the tax authority and gets a **fiscal number + scannable QR**
  printed on the receipt. A **Sandbox** provider simulates this for the demo (config
  `Fiscal:Provider`); **None** turns it off; **FBR/PRA** are scaffolded and go live once
  you set `Fiscal:Endpoint/Token/PosId` from enrolment. A failed post marks the invoice
  `Pending` (the sale still completes).
- ✅ **Thermal print bridge** — done. A small on-site console app
  (`tools/QrDine.PrintBridge`) connects to the API over SignalR, joins the
  restaurant's "printers" group, and prints **kitchen tickets on approve** and the
  **fiscal receipt on settle**. It "prints" to console + `printouts/*.txt` so it's
  testable without hardware; the real 80mm ESC/POS output (bytes + QR command) is
  written and isolated in `EscPos.cs`, needing only your printer's transport.
- **Payments** — by design there is **no online payment gateway**: the customer pays
  the assigned **waiter in cash**, who **deposits to the main cashier**. The next slice
  adds waiter cash-collection + deposit **reconciliation** (theft control). Online
  methods (JazzCash/Easypaisa/Raast) remain optional *recorded* methods only.
- ✅ **Menu management (owner self-service)** — done. Owner/Manager get a **Menu**
  screen to add/edit/delete categories and items — prices, description, emoji,
  availability (out-of-stock), **sizes/variants** and **option groups** (spice, add-ons).
  Changes are live for customers instantly. Role-gated (waiter → 403).
- ✅ **Owner sales dashboard** — done. Today's sales & bills, 7-day trend chart,
  avg bill, GST collected, **Cash-vs-Card payment split**, and **top items** — built
  from settled invoices, bucketed in Pakistan time. Role-gated (Owner/Manager).
- ✅ **Self-serve onboarding + tables/staff management** — done. Public **sign-up**
  creates a new restaurant + owner + starter tables and logs the owner straight in.
  A **Settings** screen manages **tables** (add/rename/delete) and **staff**
  (add/edit role/reset password/delete, with a last-owner guard). The frontend is now
  **multi-tenant**: staff calls use the signed-in restaurant, guests use the QR's slug.
- ✅ **Inventory** — done. Track ingredients (stock items, units, low-stock alerts),
  set a **recipe** per dish, and stock **auto-deducts when an order is approved** (and
  restores on cancel). Full **movement audit log**. Owner/Manager only.
- ✅ **Platform super-admin + subscriptions** — done. A separate **operator console**
  (`/platform`) for the SaaS owner: platform stats across all restaurants, and per-restaurant
  **subscription plan** (Trial/Menu/Ordering/Operations/Full → feature flags) and
  **suspend/activate**. Plans are **enforced** (menu-only blocks ordering; suspended blocks
  the whole restaurant). Separate platform login (`superadmin` / `super123`).
- ✅ **Dynamic subscription plans + signup banners + payment** — done. The operator
  **creates plans** (name, price, multi-select features) in the console; new plans appear
  **automatically as banner cards** on sign-up. A new restaurant **picks a plan**, goes
  through a **(mock) payment** step, and gets exactly that plan's features. Assigning a plan
  (signup or operator) sets the restaurant's feature flags. Real gateways
  (JazzCash/Easypaisa/Safepay) plug into the payment step.
- ✅ **Owner home dashboard + responsive + print** — done. The dashboard is now a
  role-home: KPI tiles with **hover lift + % badges**, **clickable quick-action tiles**
  (Menu/Inventory/Reception/Kitchen/QR/Settings), a **low-stock** tile, a **Print report**
  button (clean printable sales report), the 7-day chart, payment split and top items.
  The whole app is **responsive** (mobile / tablet / desktop) with hover effects.
- ✅ **Editable permissions** — done. The owner opens **Settings → Role permissions** and
  ticks, per role, exactly what it can do (approve, cook, serve, bill, cancel, manage menu /
  inventory / settings, view reports). The server enforces the **permission**, not the role
  (permissions ride in the JWT; changes apply on next sign-in). Owner is always full;
  editing permissions is **owner-only** so no one can escalate themselves. The nav adapts to
  each user's permissions.
- ✅ **Real photo uploads** — done. Owners upload a **JPG/PNG/WebP** per menu item
  (≤5 MB); photos show on the guest menu and admin. Stored under `wwwroot/uploads`
  (swap `IFileStorage` for S3/R2 in the cloud).
- ✅ **JazzCash payment wiring** — done as a pluggable gateway. Sign-up takes payment
  via `IPaymentGateway`: a **Mock** gateway (default, dev) and a **JazzCash** hosted-checkout
  gateway that builds the real `pp_*` fields + HMAC-SHA256 secure hash. Set
  `Payments:Provider=JazzCash` + your `MerchantId/Password/IntegritySalt` to go live
  (the return-URL verification handler is the remaining step once you have credentials).
- ✅ **Multi-branch** — done. A restaurant can have several **branches** (locations);
  each has its own **tables** and **QR codes**, and every order/session/invoice is
  **stamped with its branch**. The owner manages branches in Settings, assigns tables to
  branches, and the **dashboard filters by branch** (or All). Menu, staff and inventory
  stay shared at the restaurant level (per-branch versions are a later option).
- ✅ **Split-bill** — done. The cashier can take **partial payments** toward a table —
  **split evenly** (into N shares) or by **custom amount**, each with its own method —
  with a live paid/remaining bar. When the bill is covered it **auto-settles** into one
  invoice tagged e.g. `Split (Cash+JazzCash)`. "Settle full/rest" still available.
- WhatsApp receipts, automated tests, then cloud deploy.
- Menu variants/modifiers, billing + thermal print bridge, then FBR/PRA, payments,
  WhatsApp, inventory, multi-branch.
- Offline/local-box + cloud sync as a later, self-contained phase.
