#!/usr/bin/env bash
# Checks GitHub for new commits and redeploys only when something changed.
# Enable it to run every few minutes via cron (see README) for auto-deploy.
set -e
cd "$(dirname "$0")/../.."          # repo root
git fetch origin main -q
before=$(git rev-parse HEAD)
after=$(git rev-parse origin/main)
if [ "$before" != "$after" ]; then
  echo "$(date): new code $before -> $after, redeploying"
  git reset --hard origin/main      # .env is untracked, so it is never touched
  cd deploy/oracle
  sudo docker compose up -d --build
  echo "$(date): done"
fi
