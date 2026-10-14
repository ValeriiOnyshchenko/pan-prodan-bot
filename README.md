# PanProdanBot — «Пан Продан»

Telegram bot (C# / .NET 10). Пан Продан checks Ukrainian auctions every hour, picks new lots that pass his filters (region/price, no partial shares) and broadcasts them — one message per lot with photos, price, address, dates and a link — to everyone who pressed **Start**, all at the same time at the start of the hour (xx:00). He speaks **Ukrainian only**.

- **Start** → user is subscribed: welcome message (sources and filters) + the **latest lot from each auction** that passed the filters
- **/stop** or blocking the bot → user is unsubscribed
- Any other message → polite Ukrainian "I don't chat yet" reply

## How it works

```
xx:55  collect ──► Galcontract list ──► new lot? ──► Prozorro.Sale API (details) ──┐
               └─► SETAM list (all pages) ──► new lot? ──► lot page (details) ─────┤
                                                                                   ▼
                             partial-share filter + region/price filter ──► batch of matches
xx:00  send    ──► every lot to every subscriber, all at once (one message per lot)
```

The schedule is aligned to the clock, not to the deploy time: with `IntervalMinutes = 60` the sites are read at xx:55 (`PrefetchMinutes`) and the batch is delivered at xx:00 sharp. After a deploy the first check happens at the next full hour.

### Sources

| Source | What is read | Details from |
|---|---|---|
| **Біржа «Галконтракт»**, мала приватизація | [list](https://sales.tsbgalcontract.org.ua/PositionList.aspx?&page=1&rows=1000&sidx=3&sord=0&filter_type=filter&fvAuctionDir_0=small_privatization&action=Y&status=all), only lots with status *«Прийняття заяв на участь»* | Public Prozorro.Sale API — `procedure.prozorro.sale/api/procedures/{id}` (JSON) |
| **СЕТАМ**, нерухомість | [setam.net.ua/neruhomist](https://setam.net.ua/neruhomist), all pages of upcoming auctions (*«Реєстрація учасників»*), ~12 pages | Lot page `setam.net.ua/auction/{id}` (HTML) |

### Filter 1 — partial lots are skipped

Neither site has a structured "share" field (checked: SETAM lot attributes, Prozorro.Sale `items`), so the **title** is checked with a regex (`LotFilter.IsPartial`, case-insensitive). Skipped when the title contains a fraction followed by:

- `частка / частки / частку / часток / частина / частини / частину / частин / ч.` — «1/2 частка квартири», «1/2 ч. житлового будинку», «7/25 часток домоволодіння», «14/100 ід. частини»
- or directly the object — «1/2 Чотирикімнатної квартири», «1/3 квартири», «1/2 нежитлової будівлі»

Not treated as partial: address fractions («буд. 5/7», «кв. 167/1», «№ 140/1») and «що становить / складає 1/10 частину…» (a separate premise described relative to the building). On 14.10.2026 this skipped 65 of 224 SETAM lots and 5 of 139 Galcontract lots. Switch off with `SkipPartialLots = false`.

### Filter 2 — start price by region (UAH)

| Region | Passes if price is below |
|---|---|
| Львівська область | 2 000 000 |
| Київська область **and м. Київ** | 2 000 000 |
| All other regions | 1 000 000 |

Configured in `appsettings.json` → `Monitor` (`DefaultMaxPrice`, `RegionRules`). Region rules match by substring (`"Львів"`, `"Київ"`), so they work with both sites' spellings. The welcome message and bot description are generated from these settings — change the config and texts follow.

### New-lot detection

- Every lot ever looked at is stored in SQLite (`seen_lots` table) → each lot is sent **at most once**.
- **First run** of a source: all currently open lots (~250) are remembered silently so nobody gets flooded; only `FirstRunSampleCount` (1) matching lot per source is sent as a sample.
- Safety cap: at most `MaxLotsPerRun` (30) lots per check; the rest go out next hour.
- If a site is down or its layout changes, the check is skipped and logged; nothing is marked as seen, so nothing is lost.

### Latest lots for new subscribers

Every broadcast lot is saved as the latest lot of its source (`latest_lots` table, incl. Telegram photo ids). Right after the welcome, a new subscriber gets «Ось останні пропозиції…» followed by those lots — one per auction. Lots whose application deadline has passed are skipped; if none are left, only the welcome is sent. Photos are re-sent by Telegram file id, so nothing is downloaded again.

### Message format — one lot = one message

- **Photos + caption:** up to `MaxImages` (6) photos as one album with the details as its caption. Telegram limits captions to 1024 characters, so the description is shortened to fit (title, price and dates are always kept). Photos are downloaded and uploaded once, then re-sent by Telegram file id.
- **Caption contents:** title, start price (± ПДВ), guarantee fee, auction type, address, area, category, application deadline, auction date, encumbrance warning (SETAM), description, map link (Galcontract), «Відкрити лот» link.
- **Button «Відкрити лот ↗»:** only on single-photo and text-only messages — Telegram doesn't allow buttons on albums, so there the link is in the caption.
- **No photos** (or Telegram rejects them): a text message (up to 4096 chars) with the button.

### Settings (`appsettings.json` → `Monitor`, or env vars like `Monitor__IntervalMinutes=30`)

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Turn monitoring on/off |
| `IntervalMinutes` | `60` | Check frequency, aligned to the clock (60 → every hour at xx:00) |
| `PrefetchMinutes` | `5` | Read the sites this many minutes early, so delivery is exactly at xx:00 |
| `SkipPartialLots` | `true` | Skip lots that are only a share of an object |
| `MaxImages` | `6` | Photos per lot (Telegram max 10) |
| `MaxLotsPerRun` | `30` | Safety cap per check |
| `FirstRunSampleCount` | `1` | Sample lots sent on the very first check |
| `RequestDelayMs` | `1000` | Pause between requests to a site |
| `DefaultMaxPrice` / `RegionRules` | see above | Filters |

`Bot:AnnounceOnStartup` (default `false`) — send "Пан Продан знову на зв'язку" to everyone after each deploy.

## Project layout

| Path | What |
|---|---|
| `src/PanProdanBot/BotTexts.cs` | All Ukrainian texts: profile name, descriptions, chat messages |
| `src/PanProdanBot/Auctions/` | Monitor service, filter, message formatter, models |
| `src/PanProdanBot/Auctions/Sources/` | `GalcontractSource`, `SetamSource` — one class per site |
| `src/PanProdanBot/Bot/` | Polling service, update handler, broadcaster |
| `src/PanProdanBot/Storage/` | SQLite: subscribers, seen lots, latest lot per source |
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

# Latest lot per source (what new subscribers get)
"SELECT source, json_extract(lot_json, '$.Title') AS title, sent_at FROM latest_lots;" | ssh -i $KEY $VM "docker run --rm -i -v panprodanbot-data:/data alpine sh -c 'apk add -q sqlite && sqlite3 -header -column /data/bot.db'"

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
