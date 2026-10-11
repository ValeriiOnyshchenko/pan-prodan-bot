#!/usr/bin/env bash
# Runs ON THE VM, piped in by the GitHub Actions deploy job.
# Expects BOT_TOKEN, IMAGE, GHCR_USER, GHCR_TOKEN to be defined before this script.
set -euo pipefail

APP_DIR="$HOME/panprodanbot"
NAME=panprodanbot

mkdir -p "$APP_DIR"
( umask 077; printf 'Bot__Token=%s\n' "$BOT_TOKEN" > "$APP_DIR/bot.env" )

echo "$GHCR_TOKEN" | docker login ghcr.io -u "$GHCR_USER" --password-stdin
docker pull "$IMAGE"
docker logout ghcr.io

docker rm -f "$NAME" 2>/dev/null || true
docker run -d \
  --name "$NAME" \
  --restart unless-stopped \
  --env-file "$APP_DIR/bot.env" \
  -v panprodanbot-data:/data \
  --log-opt max-size=10m --log-opt max-file=3 \
  "$IMAGE"

docker image prune -af >/dev/null
sleep 5
docker ps --filter "name=$NAME" --format 'table {{.Names}}\t{{.Status}}\t{{.Image}}'
docker logs --tail 20 "$NAME"
