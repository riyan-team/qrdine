#!/bin/bash
# Builds the restaurant PC edition: one DineIn.exe for Windows 10/11 (64-bit). Nothing else is installed on the
# PC: .NET, the web pages and the database engine (SQLite) are all inside the exe.
#   bash deploy/pc/build-pc.sh [output folder]      (default: dist-pc at the repo root)
# The folder gets DineIn.exe, BUILD_COMMIT and READ-ME.txt for the restaurant.
set -euo pipefail
cd "$(dirname "$0")/../.."
OUT="${1:-dist-pc}"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
(cd frontend && npm run build)
dotnet publish src/QrDine.Api/QrDine.Api.csproj -c Release -r win-x64 -p:PcEdition=true -p:DebugType=none -p:DebugSymbols=false -o "$TMP"
rm -rf "$OUT"; mkdir -p "$OUT"
cp "$TMP/DineIn.exe" "$OUT/"
git rev-parse HEAD > "$OUT/BUILD_COMMIT" 2>/dev/null || true
cp deploy/pc/READ-ME.txt "$OUT/"
ls -la "$OUT"
