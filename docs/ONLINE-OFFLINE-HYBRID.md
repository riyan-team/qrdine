# Dine In: online, offline and hybrid

How the app can run, what is ready today, how to set each one up, and how each one works.
Written 2026-11-19, for branch `hybrid-sync-foundation`.

No passwords or keys belong in this file. Where a setting needs one, it says `<make a strong one>`.

---

## 1. The short answer

| Version | Ready? | In one line |
|---|---|---|
| **Fully online** | **Yes. It runs today** on the Hostworld server (`qrdine.mystay.live`). | One server in the cloud. Every phone, tablet and PC opens the website. |
| **Fully offline** | **Yes, it can be set up today**, with the limits in section 4. | The same app on one PC inside the restaurant. Everyone connects to that PC over the restaurant Wi-Fi. No internet needed for daily work. |
| **Hybrid** (PC in the restaurant **and** the cloud, kept in step) | **No. Only the first part is built**, and it is switched off. | The PC keeps working when the internet goes. When it comes back, the PC and the cloud swap their changes. |

**What to use now:**
- Most restaurants: **online**. If the internet is weak, add a backup connection (a 4G device or a second line), because online needs the internet at the counter.
- A restaurant with no dependable internet at all: **offline** on its own PC.
- **Hybrid** is the long-term answer for Pakistan (load-shedding, weak lines). It needs the work in section 5.4 first.

---

## 2. The parts (the same in every version)

| Part | What it is |
|---|---|
| **The app** | One program (`QrDine.Api`). It serves the screens (owner, reception, kitchen, waiter, guest menu) and does all the work: orders, bills, tax, stock, khata, reports. It must run as **one copy only** per database, because it lets one change through at a time per restaurant (`RestaurantGate`). |
| **The database** | **PostgreSQL** for real use. SQLite only for trying it on a laptop, because SQLite can't be upgraded in place (it is rebuilt from scratch). |
| **The screens** | Any browser: phone, tablet or PC. Nothing to install on staff devices. |
| **Printing** | Receipts and kitchen slips print from the browser to the printer on that device (80 mm). It works the same online and offline. |
| **Tax office (FBR / PRA)** | Optional, per branch: FBR or PRA, and Practice, Test or Live (section 7). FBR "Online" files each bill over the internet; FBR "On this computer" talks to FBR's own IMS program on the counter PC (`http://localhost:8524`, PC edition only). PRA is online only. If filing fails, the bill still settles and prints, and waits in **Tax office invoices** to be sent again. |

---

## 3. Fully online (what runs today)

### How it works

```
 Guests' phones ─┐
 Waiter phones ──┤                    ┌──────────────────────────────┐
 Kitchen tablet ─┼──── internet ────► │ Hostworld server              │
 Reception PC ───┤                    │  app (one copy) + PostgreSQL  │
 Owner at home ──┘                    │  backups twice a day          │
                                      └──────────────────────────────┘
```

- Every screen talks to the one server. Orders, the kitchen and bills update live on every screen.
- The owner sees reports from anywhere.
- Backups, updates and FBR filing are all on the server.
- **If the restaurant's internet goes down, the counter stops** until it comes back (or until it switches to a backup connection).

### Setup

The exact commands are in **`deploy/hostworld/README.md`**. In short:

1. **On the server:** PostgreSQL on its own port (5433), the app as `qrdine.service` on 127.0.0.1:5100, and the public address through the Cloudflare tunnel.
2. **Settings** live in `/etc/qrdine/qrdine.env` on the server (never in Git). The names:
   - `ASPNETCORE_ENVIRONMENT=Production`
   - `Database__Provider=Postgres`
   - `ConnectionStrings__Default=Host=127.0.0.1;Port=5433;Database=qrdine;Username=qrdine;Password=<make a strong one>`
   - `Jwt__Secret=<64 random characters>`
   - `PlatformAdmin__Username`, `PlatformAdmin__Password` (the platform operator's sign-in)
   - `Fiscal__Simulate` (`true` = every branch is held in Practice, nothing reaches FBR or PRA; `false` = each branch files where its owner set it: Practice, Test or Live). Whether a restaurant files at all is the operator's plan switch plus the owner's own switch.
   - `Fiscal__AutoRetryMinutes` (default 3): how often waiting bills are tried again automatically. 0 turns the automatic resend off for the whole server.
   - `Sync__Enabled` **left out** (sync stays off until hybrid is finished)
3. **Updating:** build on the PC, upload, restart. The database upgrades itself on start (migrations). Keep the old build folder for rollback, and take a database dump first.
4. **A new restaurant:** the owner signs up at `/signup`. The platform operator sets the plan and paid date at `/platform/login`. The owner prints table QR codes from **QR codes** (they point at the address the page was opened from).

---

## 4. Fully offline (one PC in the restaurant)

### How it works

```
 Guests' phones ─┐
 Waiter phones ──┤   restaurant Wi-Fi   ┌───────────────────────────────┐
 Kitchen tablet ─┼────── (no internet ─►│ Counter PC                     │
 Reception PC ───┘        needed)       │  app (one copy) + PostgreSQL   │
                                        │  receipt printer on USB/LAN    │
                                        └───────────────────────────────┘
```

- The same app runs on a PC inside the restaurant. Every device joins the restaurant Wi-Fi and opens the PC's address, for example `http://192.168.1.10:5100`.
- Orders, kitchen, waiter, bills, receipts, stock, khata, payroll and reports all work **with no internet**.
- **Needs the internet for:** FBR "Cloud" filing (use FBR's "Local" IMS on the PC instead, or file later), reading a menu from a photo (AI), WhatsApp and Google review links, and the owner checking from home.
- **Guests must be on the restaurant Wi-Fi** to order from the QR code. Their mobile data can't reach a PC inside the restaurant. Print the Wi-Fi name and password on the table card.
- **The PC is the only copy.** If its disk dies, the data dies with it, unless it is backed up (step 9). Keep it on a UPS.

### What you need

- A Windows 10/11 PC: 8 GB RAM, SSD, always on, on a UPS.
- A Wi-Fi router. Give the PC a **fixed address** in the router (a "DHCP reservation"), so the QR codes keep working.
- On the PC: the **ASP.NET Core 9 runtime** and **PostgreSQL 16 or newer** (both free).
- The receipt printer set as the PC's default printer.

### Setup, step by step

1. **Install PostgreSQL** on the PC. Make a database `qrdine` and a user `qrdine` with a strong password. Let it listen on this PC only (`listen_addresses = 'localhost'`).
2. **Build the app** on the developer PC, the same way as for online (`npm run build` in `frontend`, then `dotnet publish`, then copy `frontend/dist` into `wwwroot`). Copy the finished folder to the restaurant PC, for example `C:\DineIn\app`.
3. **Make a start file** `C:\DineIn\start.cmd`. Keep it readable by the PC's admin only, since it holds the passwords:
   ```bat
   @echo off
   set ASPNETCORE_ENVIRONMENT=Production
   set ASPNETCORE_URLS=http://0.0.0.0:5100
   set Database__Provider=Postgres
   set ConnectionStrings__Default=Host=127.0.0.1;Port=5432;Database=qrdine;Username=qrdine;Password=<make a strong one>
   set Jwt__Secret=<64 random characters>
   set PlatformAdmin__Username=<operator name>
   set PlatformAdmin__Password=<make a strong one>
   set Fiscal__Simulate=true
   cd /d C:\DineIn\app
   dotnet QrDine.Api.dll
   ```
   - Leave `Sync__Enabled` out.
   - For real FBR filing with no internet, use FBR "On this computer": install FBR's IMS program on this PC, add `set Fiscal__Simulate=false` and `set Fiscal__AllowLocal=true`, and choose "On this computer" in the branch's tax office setup. PRA has no offline program, so PRA bills wait (they still print) and are sent when the internet is back. Otherwise leave filing switched off.
4. **Start it with Windows:** Task Scheduler → Create Task → "Run whether user is logged on or not" → trigger "At startup" → action `C:\DineIn\start.cmd`. Start it once now.
5. **Open the firewall** for TCP port 5100 on the **Private** network only (Windows Defender Firewall → Inbound rule).
6. **First start** builds the database by itself. Check `http://localhost:5100/health` answers.
7. **Make the restaurant:**
   - Sign in at `http://<PC address>:5100/platform/login` with the operator name and password from step 3.
   - Sign up the restaurant at `/signup`.
   - In the platform console, set its paid-until date far ahead, so the restaurant never locks while offline.
8. **QR codes:** open the app from the **PC's address** (not `localhost`) before printing QR codes, so the codes point at the PC.
9. **Backups (do not skip):** a second Task Scheduler job each night copies the database to a USB drive or a second disk:
   ```bat
   powershell -NoProfile -Command "& 'C:\Program Files\PostgreSQL\18\bin\pg_dump.exe' -h 127.0.0.1 -U qrdine -Fc qrdine -f ('E:\DineIn-backup\qrdine-' + (Get-Date -Format yyyy-MM-dd) + '.dump')"
   ```
   (Change `18` to the PostgreSQL version you installed, and `E:` to the backup drive.)
   Put the database password in `%APPDATA%\postgresql\pgpass.conf`, so the job doesn't ask for it. Check once a week that a backup restores.
10. **Updating later:** take a backup first. Stop the task, keep the old `app` folder as `app.prev`, copy in the new build, then start the task. The database upgrades itself on start. To roll back, put `app.prev` back.

---

## 5. Hybrid (PC in the restaurant + the cloud)

### How it will work

```
 Restaurant (works with or without internet)                Cloud (Hostworld)
 ┌──────────────────────────────────┐   when internet    ┌─────────────────────────┐
 │ Counter PC: app + PostgreSQL     │ ◄─── is up: ─────► │ app + PostgreSQL         │
 │ staff and guests on the Wi-Fi    │   send my changes, │ owner, reports, backups, │
 │ keeps a list of its changes      │   get the cloud's  │ FBR, online orders       │
 └──────────────────────────────────┘                    └─────────────────────────┘
```

- In the restaurant everything runs on the PC, exactly like offline. Load-shedding or a dead line doesn't stop the counter.
- When the internet is up, the PC sends the cloud what changed and takes what changed in the cloud (for example, the owner edited the menu from home).
- The owner sees sales online, a little behind when the internet was down.

### What is built today (phase 1, on the server, switched off)

Turned on by `Sync__Enabled=true`. While it is off (the default, and how the server runs), every sync address answers 404 and nothing extra is written.

- Every row that should travel has a world-wide id (`SyncId`) and a "last changed" time, so the PC and the cloud can both make "order 105" without a clash.
- A **change log**: each save writes which rows changed, in the same save.
- **Pairing:** the owner pairs a PC and gets a device key once (only its hash is kept). At most 5 devices. A device can be removed at once.
- **Download:** a first full copy (snapshot) and then "what changed since number N" (pull). Secrets never leave the server: staff password hashes, FBR tokens, and riders' and members' private links.
- **Upload:** the PC's changes are received and stored, once each, **but not applied yet**.
- **Rules for clashes** are written and tested, ready for phase 3:
  - Menu, prices, tables, staff and other settings: **the newest change wins**.
  - Orders, bills, payments, khata and other money records: **owned by the place that made them**. The other side never changes them.
  - Stock and loyalty points: **lines are added, never overwritten**, so "+5 here and −2 there" add up.
  - Plan, paid dates and feature switches: **cloud only**. A PC can never change them.
  - Bill numbers: once a restaurant runs from a PC, only that PC gives out bill numbers.

### What is missing before hybrid can be used

| Phase | Work | Why it is needed |
|---|---|---|
| **2: the PC side** | A PC edition: the offline setup in section 4, plus a sync worker that pairs, takes the snapshot, then pulls and pushes every few seconds and retries when the internet comes back. Also staff sign-in on the PC while offline (password hashes never leave the server, so the PC needs its own sign-in), and an installer. | Without it there is nothing in the restaurant to sync. |
| **3: applying uploads** | The server applies what PCs send, with the clash rules above. Bill numbers come from the PC. Each PC's own changes come back to it once, not twice. | Today uploads are only stored. |
| **4: proving it** | Real tests with the internet pulled out mid-service: two cashiers, a menu edit at home, a day close, FBR filing late. | Money must never double or go missing. |

### How it will be set up (once phases 2–4 are done)

1. Deploy the new server build. Set `Sync__Enabled=true` in `/etc/qrdine/qrdine.env` and restart.
2. The owner pairs the counter PC and copies the key shown **once**. Today pairing is an owner-only address (`/api/r/{slug}/admin/sync/devices`) with no screen. A Devices screen in Settings is part of phase 2.
3. Install the PC edition (section 4) and give it the server address and the device key.
4. The PC downloads its first copy, then stays in step on its own.
5. If sync is ever switched off and on again, or the server is rolled back to an older build, every paired PC must download a fresh copy.

---

## 6. Owner settings for bills and cancelling (all versions)

Added 2026-11-19, in **Settings → Billing, tax & receipt**. Each switch starts the way bills and cancelling always worked, so nothing changes for a restaurant until its owner flips one.

**Bill options** (all start **off**):

| Setting | Off (the default) | On |
|---|---|---|
| Charge tax on the service charge | The service charge has no tax. | The service charge is taxed at the bill's rate (the card rate when paid by card). Shown as "GST on service". Filed with FBR on the service line. |
| Work out the service charge after the discount | Service is on the full food price. | Service is on the food after the discount. |
| Round each bill to the nearest rupee | Bills keep their paisa. | The total is rounded (at most 50 paisa) and shown as "Rounding". Change is given on the rounded total. FBR still gets the exact amounts. |
| Card rate replaces an item's own tax rate too | Items with their own rate keep it when paid by card. | Paid by card or wallet, every taxed item uses the card rate. Tax-free items stay tax-free. |

**Cancelling orders:**

| Setting | Starts | On | Off |
|---|---|---|---|
| Allow cancelling once cooking has started | **On** | Staff with the cancel right can cancel food that is cooking or ready. Guests can cancel until it is ready. | Until the kitchen presses "start cooking", cancelling works as normal. After that, only the owner can cancel the order or take a dish off it. Guests can't cancel it, and their screen hides the button. |
| Cashier can cancel at any stage | **Off** | The counter (cashier, manager, owner) can cancel any unpaid order or take off any dish: placed, cooking, ready or served, even when the rule above is off. Cashiers don't need the "Cancel orders" right for this. | Cashiers cancel only if their role has the "Cancel orders" right, and the rule above applies to them. |
| Put ingredients back in stock when cooking food is cancelled | **On** | Cancelled food goes back into stock, cooked or not. | Food cancelled after cooking started counts as used up. Food the kitchen had not started always goes back. |

- Only the owner can change the three cancelling switches. A manager can save the card without changing them.
- A paid bill can never be cancelled this way. Staff must give a reason for every cancel, and the report shows it with who did it.

**Tax on items** (added 2026-11-20, under Charge Tax / GST):

| Choice | What it does |
|---|---|
| **Each item's own rate** (the default) | An item with its own Tax % in the menu uses it (0 = tax-free). Every other item uses the bill's rate. |
| **One rate for all items** | Every item is charged the bill's rate (the card rate when paid by card), whatever its own Tax % says, tax-free items included. The menu's item editor says the item's own rate isn't used. |

---

## 7. Tax office filing: FBR and PRA (all versions)

Added 2026-11-20. **Settings → Tax office (FBR / PRA)**, and the list in **Tax office invoices**.

**Who switches it on:** the platform operator puts filing in the restaurant's plan; the owner then turns on "File bills with the tax office" and adds the NTN (and STRN). Bills settled while filing is off are never sent later.

**Each branch chooses:**

| Setting | Choices |
|---|---|
| Tax office | **FBR** (federal sales tax) or **PRA** (Punjab Revenue Authority). |
| How bills reach FBR | **Online** (FBR's internet gateway, with the token) or **On this computer** (FBR's IMS program on the counter PC; PC edition only). PRA is always online. |
| Where bills go | **Practice**: nothing is sent; bills get a made-up `PRACTICE-…` number and the receipt says "not a tax invoice". **Test**: the office's test (sandbox) server with the test POSID and token; the receipt says "TEST: not a tax invoice". **Live**: real tax invoices. |
| POSID, token | From the office's portal. The token is never shown again after saving (only "saved, ends …1234"); typing a new one replaces it, "Remove saved token" clears it. A cashier's own POSID/token, when set, wins over the branch's. |
| Registered IP | The address the office has on record. The office refuses bills from any other address ("didn't accept the token"). |
| Test server address | Only if the office gave a different test address. Never allowed for Live. |

- **Live needs** the POSID and (online) the token first, and asks "File real tax invoices?" before saving.
- **Check setup** lists, per branch, what stops filing (in red) and what is worth fixing (menu items without a PCT code, no NTN, no registered IP).
- **Send a test bill** sends a made-up Rs 100 bill to Test (or Practice) and shows the office's answer. Nothing is saved. It is never allowed on Live.
- A server started with `Fiscal__Simulate=true` holds every branch in Practice, whatever it says, and the settings page says so.

**When bills are sent** (restaurant switches, both start on):

| Switch | On | Off |
|---|---|---|
| Send each bill as soon as it is settled | The bill is filed at settle and the receipt prints with the office's number and QR. | Bills wait in Tax office invoices until someone presses Send. |
| Keep trying waiting bills automatically | Every few minutes (`Fiscal__AutoRetryMinutes`), bills that couldn't reach the office are sent again: first after 2 minutes, then less often, up to every 30 minutes. Refused bills are tried 3 more times, every 30 minutes, in case the setup was fixed. | Waiting bills go only when someone presses Send. |

**Tax office invoices:** "Waiting / refused" (with the office's reason and the number of tries) and "Filed" (with the office's number and POSID). Send one ("Send now"), the ticked ones ("Send picked"), or every waiting one ("Send all waiting", 25 a press, oldest first). A press stops early when the office can't be reached three times in a row, and says so.

**Never filed twice:** a filed bill is never sent again. Before sending, a bill is claimed in the database for 2 minutes; a second sender at the same moment (another cashier, the automatic resend, a second server) steps back with "This bill is being sent right now". A bill refused for a missing PCT code can be fixed in the menu and sent again: lines with no PCT code take the dish's code then; prices, tax and totals stay as settled.

**What is sent:** FBR's invoice model (POSID, USIN, date, buyer, items with PCT code, rate, sale value, tax, discount, further tax, totals; the service charge as its own SERVICE line). PRA gets the same with `BuyerPNTN` instead of `BuyerNTN` and the time with milliseconds; its receipt QR opens PRA's invoice page. Addresses: FBR test `esp.fbr.gov.pk:8244`, FBR live `gw.fbr.gov.pk`, PRA test and live on `ims.pral.com.pk`; each can be changed in the server settings (`Fiscal__CloudSandboxUrl`, `Fiscal__CloudProductionUrl`, `Fiscal__PraSandboxUrl`, `Fiscal__PraProductionUrl`).

**Testing without the real offices:** `deploy/hostworld/e2e/mock_tax_office.py` is a stand-in FBR/PRA server that checks the token, the format and every sum, and refuses a USIN filed twice. `tax_check.py` files real bills through the app against it (Practice, Test, Live, outage, refusal, resend, eight presses at once, PRA, one tax for all items, the automatic resend). Point the four `Fiscal__…Url` settings at the stand-in before running it.
