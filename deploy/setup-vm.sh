#!/usr/bin/env bash
# One-time setup for a fresh Oracle Cloud Ubuntu VM.
# Usage (on the VM):  curl -fsSL https://raw.githubusercontent.com/ValeriiOnyshchenko/pan-prodan-bot/main/deploy/setup-vm.sh | bash
set -euo pipefail

echo "==> Updating packages"
sudo apt-get update -y && sudo apt-get upgrade -y

echo "==> Installing Docker"
if ! command -v docker >/dev/null; then
  curl -fsSL https://get.docker.com | sudo sh
fi
sudo usermod -aG docker "$USER"
sudo systemctl enable --now docker

# The free AMD micro VM has only 1 GB RAM — add swap so it stays stable.
if [ ! -f /swapfile ]; then
  echo "==> Adding 2 GB swap"
  sudo fallocate -l 2G /swapfile
  sudo chmod 600 /swapfile
  sudo mkswap /swapfile
  sudo swapon /swapfile
  echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
fi

echo "==> Done. Log out and back in once so the docker group applies."
