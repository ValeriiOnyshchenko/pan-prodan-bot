# PanProdanBot

Telegram bot (C# / .NET 10). Users press **Start** to subscribe and receive broadcasts; `/stop` or blocking the bot unsubscribes them.

## Run locally

```powershell
cd src/PanProdanBot
dotnet user-secrets set "Bot:Token" "<your BotFather token>"
dotnet run
```

> Only one copy of the bot can poll Telegram at a time. Stop the server container (`docker stop panprodanbot`) before running locally, otherwise you'll get `409 Conflict`.

## Deployment

Every push to `main` → GitHub Actions builds a Docker image (amd64 + arm64), pushes it to GitHub Container Registry, then SSHes into the Oracle Cloud VM and restarts the container. Subscribers are stored in SQLite in the `panprodanbot-data` Docker volume, so they survive redeploys.

Required repository secrets (Settings → Secrets and variables → Actions):

| Secret | Value |
|---|---|
| `BOT_TOKEN` | Token from @BotFather |
| `SSH_HOST` | Public IP of the VM |
| `SSH_USER` | `ubuntu` (for Ubuntu images) |
| `SSH_PRIVATE_KEY` | Full contents of the private key file, including the `BEGIN`/`END` lines |

Useful commands on the VM:

```bash
docker logs -f panprodanbot      # live logs
docker restart panprodanbot
```
