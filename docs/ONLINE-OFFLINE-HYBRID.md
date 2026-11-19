# Dine In: online, offline and hybrid

How the app can run, what is ready today, how to set each one up, and how each one works.
Updated 2026-11-20.

No passwords or keys belong in this file. Where a setting needs one, it says `<make a strong one>`.

---

## 1. The short answer

| Version | Ready? | In one line |
|---|---|---|
| **Fully online** | **Yes. It runs today** on the Hostworld server (`qrdine.mystay.live`). | One server in the cloud. Every phone, tablet and PC opens the website. |
| **Fully offline** | **Yes. `DineIn.exe`**, one file with its own database and licence (section 4). | The same app on one PC inside the restaurant. Everyone connects to that PC over the restaurant Wi-Fi. No internet needed for daily work. |
| **Hybrid** (PC in the restaurant **and** the cloud, kept in step) | **Yes.** `DineIn.exe` connected to the online restaurant (section 5). The server needs `Sync__Enabled=true`. | The counter runs on the PC and keeps working when the internet goes. Every few seconds the PC and the cloud swap their changes. |

**What to use now:**
- Most restaurants: **online**. If the internet is weak, add a backup connection (a 4G device or a second line), because online needs the internet at the counter.
- A restaurant with no dependable internet at all: **offline** on its own PC.
- A restaurant online today whose internet drops: **hybrid**. The counter moves to a PC and the online copy keeps up whenever it can.

---

## 2. The parts (the same in every version)

| Part | What it is |
|---|---|
| **The app** | One program (`QrDine.Api`). It serves the screens (owner, reception, kitchen, waiter, guest menu) and does all the work: orders, bills, tax, stock, khata, reports. It must run as **one copy only** per database, because it lets one change through at a time per restaurant (`RestaurantGate`). |
| **The database** | **PostgreSQL** on the server. **SQLite** inside the PC edition, brought up to date on every start (new tables and columns added, nothing dropped, a copy first). |
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
   - `Sync__Enabled=true` (restaurant PCs, section 5; on at `qrdine.mystay.live` since 2026-11-20). Left out or `false`, every sync address answers 404.
3. **Updating:** build on the PC, upload, restart. The database upgrades itself on start (migrations). Keep the old build folder for rollback, and take a database dump first.
4. **A new restaurant:** the owner signs up at `/signup`. The platform operator sets the plan and paid date at `/platform/login`. The owner prints table QR codes from **QR codes** (they point at the address the page was opened from).

---

## 4. Fully offline: the PC edition (`DineIn.exe`)

### How it works

```
 Guests' phones ─┐
 Waiter phones ──┤   restaurant Wi-Fi   ┌───────────────────────────────────┐
 Kitchen tablet ─┼────── (no internet ─►│ Counter PC                         │
 Reception PC ───┘        needed)       │  DineIn.exe (app + pages + SQLite) │
                                        │  DineIn-data\ (database, backups)  │
                                        │  receipt printer on USB/LAN        │
                                        └───────────────────────────────────┘
```

- **One file, nothing to install.** `DineIn.exe` (about 55 MB, Windows 10/11 64-bit) holds .NET, the web pages and the database engine (SQLite). Double-click it: a window shows the addresses and the browser opens.
- Every device joins the restaurant Wi-Fi and opens the PC's address, for example `http://192.168.1.10:5100`.
- Orders, kitchen, waiter, bills, receipts, stock, khata, payroll and reports all work **with no internet**.
- **Needs the internet for:** FBR "Online" and PRA filing (bills wait and go when the internet is back; or use FBR "On this computer" with FBR's IMS program), reading a menu from a photo (AI), WhatsApp and Google review links, and the owner checking from home.
- **Guests must be on the restaurant Wi-Fi** to order from the QR code. Their mobile data can't reach a PC inside the restaurant. Print the Wi-Fi name and password on the table card.
- **The PC is the only copy.** If its disk dies, the data dies with it, unless the second backup folder is set (below). Keep it on a UPS.

### What is on the PC

Everything lives in `DineIn-data` next to the exe (another folder with `--data <folder>` or `DINEIN_DATA`):

| File | What it is |
|---|---|
| `dinein.db` | The database (SQLite, WAL mode). On every start it is brought up to date by itself: new tables, columns and indexes are added (a copy goes into `backups` first). Nothing is ever dropped. |
| `backups\dinein-<date>-<time>.db` | A full copy 3 minutes after each start and every 24 hours. The last 14 are kept. |
| `licence.key` | The licence key, once entered. |
| `secret.key` | The sign-in secret, made on first start. Copy it with the rest when moving PCs, or every staff member signs in again. |
| `clock.txt` | The newest time this PC has seen, to catch the clock being turned back. |
| `dinein-settings.json` | The owner's settings: `Port` (5100), `BackupFolder` (a second backup folder, for example a USB drive) and `OpenBrowser`. |

Only one copy runs per PC: starting it again just opens the browser on the running one.

How the PC edition differs from the server: SQLite instead of PostgreSQL, **one restaurant per PC**, no paid-until lock (the licence takes its place), sync switched off, real filing on (`Fiscal:Simulate=false`) with FBR "On this computer" allowed (`Fiscal:AllowLocal=true`). Each branch still starts in Practice, so nothing is filed until the owner sets it.

### The licence

- On first start the browser opens **Activate**. It shows this PC's code (`XXXX-XXXX-XXXX-XXXX`, made from Windows' own id for the PC) with Copy and WhatsApp buttons.
- The seller makes a key for that code and the restaurant pastes it in. Until then every address answers 402 and the screens stay on Activate.
- A key is **signed** with the seller's private key (ECDSA P-256). The exe has only the public key, so a key can't be made, changed or moved to another PC.
- It holds the restaurant name, the PC code, the issue date, the end date (or lifetime) and the features (ordering, kitchen, waiter, billing, inventory, reports, fbr). The features are applied to the restaurant on every start.
- **Ending:** a reminder from 14 days before the end date; 7 more days of grace after it; after that the PC can still sign in and show every record, but new orders and bills need a renewed key.
- **Clock turned back** by more than a day (against `clock.txt` and the newest bill): the licence stops working until the clock is right.

**For the seller** (the tool is `tools/QrDine.Licence`):

```bash
dotnet run --project tools/QrDine.Licence -- keygen --out C:/Users/<you>/.dinein-licence      # once, ever
dotnet run --project tools/QrDine.Licence -- issue --key <private-key.pem> --restaurant "Name" --machine ABCD-EFGH-JKLM-NPQR --days 365
dotnet run --project tools/QrDine.Licence -- show <licence key>
```

- `issue` also takes `--until 2027-11-30` or `--lifetime`, and `--features ordering,kitchen,...` (default all).
- The public key is `SellerPublicKey` in `src/QrDine.Api/Pc/PcLicence.cs`.
- **The private key never goes into git and is never shared.** Keep two copies off this PC. If it is lost, no more keys can be made: a new pair means a new exe and new keys for every restaurant.

### Build and check

```bash
bash deploy/pc/build-pc.sh            # dist-pc/: DineIn.exe, BUILD_COMMIT, READ-ME.txt
bash deploy/pc/run-pc-check.sh dist-pc/DineIn.exe <empty work folder> <dinein-licence.dll> <private-key.pem>
```

The check starts the exe on port 5310 with its own data folder, makes keys with the tool (never printed), and goes through: the pages, no licence, a made-up key, an ended licence, sign-up (and a second sign-up refused), menu, a guest order to the bill with tax, a busy moment, reports and the first backup (the wrong PC, grace, clock-back and the database upgrade are in `PcEditionTests`). Then it restarts the exe and checks everything is still there, and that a second copy only points at the first.

### Setting up a restaurant

`READ-ME.txt` (in `deploy/pc/`, copied next to the exe) is the restaurant's own guide. In short:

1. Put `DineIn.exe` in its own folder, for example `C:\DineIn`, and start it. Allow **Private networks** when Windows asks (or add an inbound rule for TCP 5100 on the Private network).
2. Send the PC code; paste the key.
3. Sign up the restaurant, then add the menu, tables and staff.
4. Give the PC a **fixed address** in the router ("DHCP reservation"). Open the app from that address (not `localhost`) before printing the QR codes.
5. Start with Windows: a shortcut to `DineIn.exe` in `shell:startup`.
6. Set `BackupFolder` in `dinein-settings.json` to a USB drive or a second disk.
7. **Updating:** close it, replace `DineIn.exe`, start it. The database brings itself up to date (with a copy first). To roll back, put the old exe back and, if needed, the copy from `backups`.
8. **Moving PCs:** copy the whole `DineIn-data` folder. The new PC has a new code, so it needs a new key.

---

## 5. Hybrid (PC in the restaurant + the cloud)

### How it works

```
 Restaurant (works with or without internet)                Cloud (Hostworld)
 ┌──────────────────────────────────┐   every 5 seconds  ┌─────────────────────────┐
 │ Counter PC: DineIn.exe + SQLite  │ ◄── when online: ─► │ app + PostgreSQL         │
 │ staff and guests on the Wi-Fi    │   send my changes, │ owner from home, reports │
 │ keeps a list of its changes      │   take the cloud's │ menu edits, backups      │
 └──────────────────────────────────┘                    └─────────────────────────┘
```

- **The counter runs on the PC.** Orders, bills, payments, cancellations and returns, day closing, khata entries (customers, suppliers, staff), expenses, stock movements, loyalty points and feedback are made there, exactly like offline. Load-shedding or a dead line doesn't stop it.
- **Every 5 seconds while online**, the PC sends what changed and then takes what changed online. With no internet it keeps a list, and sends it all by itself when the line is back. Menu photos travel both ways.
- **Both sides can change the menu, prices, tables, branches, staff, customers, suppliers and settings.** The newest change wins.
- **Counts come from the PC:** stock quantities and loyalty point balances. The owner can rename a stock item from home; the quantity stays the PC's.
- **Online only:** the plan, paid dates, feature switches and the restaurant's address. On the PC, features come from its licence.
- **Never travel:** FBR/PRA tokens, members' phone passes and riders' links (each is entered or made where it is used). Staff passwords travel as salted hashes, only to the paired PC, so staff sign in there with no internet.
- **The online copy steps back.** Once the PC takes over, the online copy refuses new orders, bills, payments, stock counts and khata entries with a clear message (guests are told to scan the QR code on the restaurant's Wi-Fi). Everything the PC makes shows up online within seconds.
- **Tax office:** bills are filed from the PC, with tokens entered on the PC. The server's retry job leaves these restaurants alone.

### Setting it up

1. **Server:** `Sync__Enabled=true` in `/etc/qrdine/qrdine.env`, then restart (one process). While it is off, every sync address answers 404. (Already on at `qrdine.mystay.live`.)
2. **Online, as the owner:** Settings > **Restaurant PC** > **Pair a PC**. Copy the key; it is shown once. One PC per restaurant.
3. **On the PC:** start the new `DineIn.exe` and activate the licence (section 4). On the sign-up page choose **My restaurant is already online**, keep the address `https://qrdine.mystay.live`, and paste the key. The PC downloads the whole restaurant (nothing else opens until it is done), then takes over the counter.
4. Staff sign in on the PC with their usual mobile numbers and passwords.
5. **Print the table QR codes again from the PC** (the QR codes page, opened from the PC's fixed address, not `localhost`). Guests order over the restaurant Wi-Fi.
6. **Tax office:** enter each branch's FBR/PRA token on the PC (Settings > Tax office). Tokens never travel.

### Watching it

- **On the PC:** Settings > Your account > **Online copy**: up to date or offline, what is still waiting to go up, the last sync, **Sync now**, and any change the online copy could not take.
- **Online:** Settings > Restaurant PC: when the PC was last heard from, since when it runs the counter, and anything waiting or refused (with the reason).

### Moving, removing, starting again

- **A new PC:** copy the whole `DineIn-data` folder (it holds `sync.json`, the link to the online copy) and get a key for the new PC's code.
- **Back to online only:** let the PC send everything (0 waiting), then **Remove** it online. The online copy takes orders and bills again.
- **Removed by mistake, or the key leaked:** the PC keeps working and says it was removed. Pair again online and enter the new key on the PC (Settings > Your account > Online copy, as the owner or a manager). It keeps everything and sends what waited. This is refused if orders, bills or khata entries were made online in between, because their numbers would clash; then connect a fresh PC.
- **Never delete the PC's data folder while changes are waiting.** They exist only there.
- If sync is ever switched off and on again, or the server is restored from an older backup, remove the PC online and connect a fresh PC data folder.

### Limits (this version)

| Limit | Why |
|---|---|
| One counter PC per restaurant | Two PCs would both give out bill numbers. |
| Connecting works only on a PC with no restaurant yet | A PC that already sold offline on its own would need its records merged with the online ones. |
| Once the PC runs the counter, orders and khata entries are made only on it | One place gives out bill numbers and owns the money records, so nothing doubles. |
| QR codes printed for the online address stop taking orders | Guests order through the PC. Reprint them from the PC. |
| Members' phone passes and riders' links made online don't work on the PC | They never travel. Staff give the member a new code. |
| The newest change wins by each side's clock | Keep the PC's clock automatic (Windows does this). |

### How it is built (for developers)

- Every synced row has a world-wide id (`SyncId`) and `LastChangedAt`. Each save writes a change log (`SyncChanges`) in the same transaction, with `OriginDeviceId` so a device never gets its own changes back.
- Device API (`/api/sync`, device key): `hello`, `snapshot`, `pull`, `push`, `takeover`, `files/{id}`. Owner API: `/api/r/{slug}/admin/sync/devices`.
- Rows travel without either side's own ids: links to other rows go as that row's `SyncId` (`SyncRules.References`), and children (order items, bill lines, variants…) are matched by their own `SyncId`.
- `SyncApplier` applies a batch in dependency order. A row whose link has not arrived waits for the rest of the batch, then goes in with that link empty and a note. A row that fails is marked **Failed** with the reason and never blocks the others. The server takes the PC's floor records and counts; settings go to the newest. After the download, the PC never takes the server's floor records.
- `CounterOnPcRule` in `AppDbContext` is the guard that makes the online copy step back.
- The PC side is `PcSync` (`src/QrDine.Api/Pc/PcSync.cs`); its state is in `DineIn-data/sync.json`.
- Tests: `SyncTests`, `SyncHttpTests`, `HybridSyncTests` (a server and a PC in one process over real HTTP), and, with both as real processes (the server is stopped and started again mid-way):

```bash
bash deploy/pc/run-hybrid-check.sh <DineIn.exe> <server publish folder> <empty work folder> <dinein-licence.dll> <private-key.pem>
```

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
