# PanProdanBot — «Пан Продан»

Telegram bot (C# / .NET 10). Пан Продан watches auctions and broadcasts useful lots to subscribers. He speaks **Ukrainian only** — all user-facing texts live in [`src/PanProdanBot/BotTexts.cs`](src/PanProdanBot/BotTexts.cs).

- **Start** → user is subscribed
- **/stop** or blocking the bot → user is unsubscribed
- Any other message → polite Ukrainian "I don't chat yet" reply

## Project layout

| Path | What |
|---|---|
| `src/PanProdanBot/BotTexts.cs` | All Ukrainian texts: profile name, descriptions, chat messages |
| `src/PanProdanBot/Bot/` | Polling service, update handler, broadcaster |
| `src/PanProdanBot/Storage/` | SQLite subscriber store |
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

### Subscribers

```powershell
# How many subscribers
"SELECT COUNT(*) FROM subscribers;" | ssh -i $KEY $VM "docker run --rm -i -v panprodanbot-data:/data alpine sh -c 'apk add -q sqlite && sqlite3 /data/bot.db'"

# List subscribers
"SELECT * FROM subscribers;" | ssh -i $KEY $VM "docker run --rm -i -v panprodanbot-data:/data alpine sh -c 'apk add -q sqlite && sqlite3 -header -column /data/bot.db'"

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
