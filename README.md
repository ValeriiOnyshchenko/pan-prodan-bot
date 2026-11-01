# PanProdanBot — «Пан Продан»

Telegram bot (C# / .NET 10). Пан Продан checks Ukrainian auctions every hour, picks new lots that pass his filters (region/price, no partial shares) and broadcasts them — one message per lot with photos, price, address, dates and a link — to everyone who pressed **Start**, all at the same time at the start of the hour (xx:00). He speaks **Ukrainian only**.

- **Start** → user is subscribed: welcome message (sources and filters) + the **latest lot from each auction** that passed the filters
- **/stop** or blocking the bot → user is unsubscribed
- Any other message → polite Ukrainian "I don't chat yet" reply

## How it works

```
xx:45  collect ──► Galcontract list ──► new lot? ──► Prozorro.Sale API (details) ──┐
               └─► SETAM list (all pages) ──► new lot? ──► lot page (details) ─────┤
                                                                                   ▼
                             partial-share filter + region/price filter ──► batch of matches
xx:00  send    ──► every lot to every subscriber, all at once (one message per lot)
```

The schedule is aligned to the clock, not to the deploy time: with `IntervalMinutes = 60` the sites are read and photos downloaded at xx:45 (`PrefetchMinutes`) and the batch is delivered at xx:00 sharp. After a deploy the first check happens at the next full hour.

### Sources

| Source | What is read | Details from |
|---|---|---|
| **Біржа «Галконтракт»**, мала приватизація | [list](https://sales.tsbgalcontract.org.ua/PositionList.aspx?&page=1&rows=1000&sidx=3&sord=0&filter_type=filter&fvAuctionDir_0=small_privatization&action=Y&status=all), only lots with status *«Прийняття заяв на участь»* | Public Prozorro.Sale API — `procedure.prozorro.sale/api/procedures/{id}` (JSON) |
| **СЕТАМ**, нерухомість | [setam.net.ua/neruhomist](https://setam.net.ua/neruhomist), all pages of upcoming auctions (*«Реєстрація учасників»*), ~12 pages | Lot page `setam.net.ua/auction/{id}` (HTML) |

### Filter 0 — real estate only

Galcontract's small privatization sells any municipal property — besides buildings also animals (sheep, mouflons, deer), cars, share packages, whole enterprises, building materials. Every lot's main item has a Prozorro classifier code; only these prefixes pass (`RealEstateCodePrefixes`):

| Code | Meaning | Sent |
|---|---|---|
| `04…` | Нерухомість (buildings, premises, unfinished construction) | ✅ |
| `06…` | Земельні ділянки | ✅ |
| `70…` | Продаж нерухомості (CPV, used for some buildings) | ✅ |
| `03…` animals, `34…` cars, `08…` shares, `05…` enterprises, `44…` materials, … | — | ❌ |

SETAM lots have no code; the bot only reads SETAM's «Нерухомість» section, so they always pass this filter.

### Filter 1 — partial lots are skipped

Neither site has a structured "share" field (checked: SETAM lot attributes, Prozorro.Sale `items`), so the **title** is checked with a regex (`LotFilter.IsPartial`, case-insensitive). Skipped when the title contains a fraction followed by:

- `частка / частки / частку / часток / частина / частини / частину / частин / ч.` — «1/2 частка квартири», «1/2 ч. житлового будинку», «7/25 часток домоволодіння», «14/100 ід. частини»
- or directly the object — «1/2 Чотирикімнатної квартири», «1/3 квартири», «1/2 нежитлової будівлі»

Not treated as partial: address fractions («буд. 5/7», «кв. 167/1», «№ 140/1») and «що становить / складає 1/10 частину…» (a separate premise described relative to the building). On 14.10.2026 this skipped 65 of 224 SETAM lots and 5 of 139 Galcontract lots. Switch off with `SkipPartialLots = false`.

### Filter 2 — start price by region (UAH)

| Region | Passes if price is below |
|---|---|
| м. Київ і Київська область | 2 000 000 |
| Львівська область | 2 000 000 |
| Тернопільська, Івано-Франківська, Закарпатська області | 1 000 000 |
| All other regions | **not sent** (`IncludeOtherRegions = false`) |

Configured in `appsettings.json` → `Monitor` (`RegionRules`, `IncludeOtherRegions`, `DefaultMaxPrice`). Region rules match by substring (`"Київ"`, `"Львів"`, `"Тернопіл"`, `"Франків"`, `"Закарпат"`), so they work with both sites' spellings («Львівська обл.» / «Львівська область», «м.Київ» / «Київ»). The welcome message and bot description are generated from these settings — change the config and texts follow.

### One-time announcements

`BotTexts.Announcements` is a list of `(Id, Text)`. On start, every entry whose id isn't yet in `bot_state` is sent once to all subscribers, then its id is stored — restarts never repeat it. To send a message to everyone: add an entry with a **new** id and push. Sent so far: `2026-11-real-estate-fix` («Пан Продан трохи дав у штангу…»).

### Filter-change announcement

On every start the bot compares the current filters (regions, limits, partial-lot rule — not display names) with the ones stored in `bot_state`. If they differ, every subscriber gets one «📢 Пан Продан оновив фільтри!» message listing the new rules, and the new signature is stored. Restarts and deploys that don't touch the filters stay silent. To announce a change: edit the filters in `appsettings.json`, push — that's it.

### New-lot detection

- Every lot ever looked at is stored in SQLite (`seen_lots` table) → each lot is sent **at most once**.
- **First run** of a source: all currently open lots (~250) are remembered silently so nobody gets flooded; only `FirstRunSampleCount` (1) matching lot per source is sent as a sample.
- Safety cap: at most `MaxLotsPerRun` (30) lots per check; the rest go out next hour.
- If a site is down or its layout changes, the check is skipped and logged.
- **No history, ever:** only lots published within `MaxLotAgeHours` (24 h) are sent. Older new-to-the-bot lots (e.g. after an outage, a stuck connection or a filter change) are remembered as `stale` and skipped.
- HTTP clients come fresh from `IHttpClientFactory` on every request with a 5-minute connection lifetime, so a broken connection can't silently block a source for days (that happened on 29.10–01.11 with a long-lived client; restarting the container released ~80 lots at once).
- **Cloudflare on procedure.prozorro.sale** (Galcontract details + photos) blocks quick bursts from the bot with `403 cf-mitigated: challenge` for minutes, but reliably accepts one request every ~6 s (verified from the server). `HostThrottle` paces all requests to `*.prozorro.sale` accordingly (≈40 s per lot with 6 photos) and, on a challenge, pauses 2 min and retries once. Photos are downloaded in the prefetch window (xx:45), so delivery at xx:00 isn't delayed. If photos still can't be downloaded, Telegram is given the URLs to fetch itself; only if that fails too, the lot goes out as text. Every skipped photo is logged with its HTTP status.
- Failures to load lot details are logged as errors (`docker logs panprodanbot | grep "could not load"`).

### Latest lots for new subscribers

The last 20 broadcast lots of each auction are kept (`recent_lots` table, incl. Telegram photo ids). Right after the welcome, a new subscriber gets «Ось останні пропозиції…» and, **per auction, the newest of those lots that still passes the current filters, hasn't expired and wasn't delivered to this user yet**. If an auction has no such lot, it's simply left out. Photos are re-sent by Telegram file id, so nothing is downloaded again.

### No duplicates — per-user delivery log

Every lot sent to a user is recorded in `deliveries` (chat id + lot key). The hourly broadcast skips users who already have the lot (e.g. got it from /start, or a broadcast was interrupted and retried), and /start never shows a lot the user already received. `/stop` (or blocking the bot) deletes the user together with their delivery log, so a later /start starts from scratch. Records older than 90 days are pruned.

### Message format — one lot = one message

- **Photos + caption:** up to `MaxImages` (6) photos as one album with the details as its caption. Telegram limits captions to 1024 characters, so the description is shortened to fit (title, price and dates are always kept). Photos are downloaded and uploaded once, then re-sent by Telegram file id.
- **Caption contents:** title, start price (± ПДВ), guarantee fee, auction type, address, area, category, application deadline, auction date, encumbrance warning (SETAM), description, map link (Galcontract), «Відкрити лот» link.
- **Button «Відкрити лот ↗»:** only on single-photo and text-only messages — Telegram doesn't allow buttons on albums, so there the link is in the caption.
- **No photos** (or Telegram rejects them): a text message (up to 4096 chars) with the button.

### Settings (`appsettings.json` → `Monitor`, or env vars like `Monitor__IntervalMinutes=30`)

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Turn monitoring on/off |
| `MaxLotAgeHours` | `24` | Never send lots published earlier than this (0 = no limit) |
| `IntervalMinutes` | `60` | Check frequency, aligned to the clock (60 → every hour at xx:00) |
| `PrefetchMinutes` | `15` | Read the sites and download photos this many minutes early, so delivery is exactly at xx:00 |
| `ProzorroRequestIntervalMs` | `6000` | Min pause between requests to procedure.prozorro.sale (Cloudflare) |
| `CloudflareCooldownSeconds` | `120` | Pause before one retry after a Cloudflare 403 |
| `SkipPartialLots` | `true` | Skip lots that are only a share of an object |
| `MaxImages` | `6` | Photos per lot (Telegram max 10) |
| `MaxLotsPerRun` | `30` | Safety cap per check |
| `FirstRunSampleCount` | `1` | Sample lots sent on the very first check |
| `RequestDelayMs` | `1000` | Pause between requests to a site |
| `RealEstateCodePrefixes` | `04`, `06`, `70` | Real-estate-only filter (Galcontract) |
| `RegionRules` / `IncludeOtherRegions` / `DefaultMaxPrice` | see above | Region/price filter |

`Bot:AnnounceOnStartup` (default `false`) — send "Пан Продан знову на зв'язку" to everyone after each deploy.

## Project layout

| Path | What |
|---|---|
| `src/PanProdanBot/BotTexts.cs` | All Ukrainian texts: profile name, descriptions, chat messages |
| `src/PanProdanBot/Auctions/` | Monitor service, filter, message formatter, models |
| `src/PanProdanBot/Auctions/Sources/` | `GalcontractSource`, `SetamSource` — one class per site |
| `src/PanProdanBot/Bot/` | Polling service, update handler, broadcaster |
| `src/PanProdanBot/Storage/` | SQLite: subscribers + delivery log, seen lots, recent lots per source, bot state |
| `src/PanProdanBot/DryRun.cs` | `--dry-run` preview mode |
| `assets/avatar.png` / `.svg` | Bot avatar (upload via BotFather) |
| `deploy/` | VM setup script and the remote deploy script used by CI |
| `.github/workflows/deploy.yml` | Build image → push to GHCR → deploy to Oracle VM |

## Bot profile

Name, short description ("About") and description are **synced from `BotTexts.cs` on every start** — edit them there, push, done.

Set once manually in @BotFather → `/mybots` → @PanProdanBot → Edit Bot:

| Setting | Value |
|---|---|
| Edit Botpic | upload `assets/avatar.jpg` |
| Bot Settings → Allow Groups? | **Turn groups off** (bot works in private chats only) |

## Preview without Telegram (dry run)

Reads both sites live, applies the filters and prints the first N matching lots per source exactly as they'd be sent. No token, no database, nothing is sent.

```powershell
cd src/PanProdanBot
dotnet run -- --dry-run            # 3 lots per source
dotnet run -- --dry-run --show 10  # 10 lots per source
```

## Run locally

```powershell
cd src/PanProdanBot
dotnet user-secrets set "Bot:Token" "<your BotFather token>"
dotnet run
```

> Only one copy of the bot can poll Telegram at a time. Stop the server container first (see below), otherwise you get `409 Conflict`. Start it again when done.

## Deployment

Every push to `main` → GitHub Actions builds a Docker image (amd64 + arm64), pushes it to GitHub Container Registry, SSHes into the Oracle Cloud VM and restarts the container. Subscribers live in SQLite in the `panprodanbot-data` Docker volume and survive redeploys.

Repository secrets (Settings → Secrets and variables → Actions):

| Secret | Value |
|---|---|
| `BOT_TOKEN` | Token from @BotFather |
| `SSH_HOST` | `79.72.81.132` |
| `SSH_USER` | `ubuntu` |
| `SSH_PRIVATE_KEY` | Full contents of `ssh-key-*.key` (incl. `BEGIN`/`END` lines) |

## Useful commands

All commands are PowerShell, run from the repo root. Set these once per terminal session:

```powershell
$KEY = ".\ssh-key-2026-10-11.key"
$VM  = "ubuntu@79.72.81.132"
```

### Logs & status

```powershell
ssh -i $KEY $VM "docker logs --tail 100 panprodanbot"        # last 100 lines
ssh -i $KEY $VM "docker logs -f panprodanbot"                # live (Ctrl+C to exit)
ssh -i $KEY $VM "docker logs --since 1h panprodanbot"        # last hour
ssh -i $KEY $VM "docker ps -a --filter name=panprodanbot"    # running? since when?
ssh -i $KEY $VM "docker stats --no-stream panprodanbot"      # CPU / RAM
```

### Control

```powershell
ssh -i $KEY $VM "docker restart panprodanbot"
ssh -i $KEY $VM "docker stop panprodanbot"     # before running locally
ssh -i $KEY $VM "docker start panprodanbot"    # after running locally
```

### Subscribers & lots

```powershell
# How many subscribers
"SELECT COUNT(*) FROM subscribers;" | ssh -i $KEY $VM "docker run --rm -i -v panprodanbot-data:/data alpine sh -c 'apk add -q sqlite && sqlite3 /data/bot.db'"

# List subscribers
"SELECT * FROM subscribers;" | ssh -i $KEY $VM "docker run --rm -i -v panprodanbot-data:/data alpine sh -c 'apk add -q sqlite && sqlite3 -header -column /data/bot.db'"

# Lots: how many seen / sent / rejected
"SELECT source, outcome, COUNT(*) FROM seen_lots GROUP BY source, outcome;" | ssh -i $KEY $VM "docker run --rm -i -v panprodanbot-data:/data alpine sh -c 'apk add -q sqlite && sqlite3 -header -column /data/bot.db'"

# Last 10 lots that were broadcast
"SELECT key, seen_at FROM seen_lots WHERE outcome='broadcast' ORDER BY seen_at DESC LIMIT 10;" | ssh -i $KEY $VM "docker run --rm -i -v panprodanbot-data:/data alpine sh -c 'apk add -q sqlite && sqlite3 -header -column /data/bot.db'"

# Recent lots per source (new subscribers get the newest valid one of each)
"SELECT source, substr(json_extract(lot_json, '$.Title'), 1, 60) AS title, sent_at FROM recent_lots ORDER BY sent_at DESC;" | ssh -i $KEY $VM "docker run --rm -i -v panprodanbot-data:/data alpine sh -c 'apk add -q sqlite && sqlite3 -header -column /data/bot.db'"

# Monitor activity in the logs
ssh -i $KEY $VM "docker logs --since 3h panprodanbot 2>&1 | grep -E 'Galcontract|SETAM|match|Lot '"

# Back up the database to your PC
ssh -i $KEY $VM "docker run --rm -v panprodanbot-data:/data -v /home/ubuntu:/out alpine cp /data/bot.db /out/bot-backup.db"
scp -i $KEY "${VM}:~/bot-backup.db" .\bot-backup.db
```

### Deployments (GitHub CLI)

```powershell
gh run list --limit 5          # recent runs
gh run watch                   # follow the current run
gh run view --log-failed       # logs of a failed run
gh workflow run deploy.yml     # redeploy without pushing
```

### Server

```powershell
ssh -i $KEY $VM                                 # interactive shell
ssh -i $KEY $VM "df -h /; free -h; uptime"      # disk, memory, load
ssh -i $KEY $VM "sudo apt-get update && sudo apt-get upgrade -y && sudo reboot"   # OS updates
```

The container has `--restart unless-stopped`, so it comes back automatically after a reboot.
