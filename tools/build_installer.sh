#!/usr/bin/env bash
# Exporta o jogo para Windows e gera o instalador build/CrystalExcavator-Setup.exe (NSIS).
set -e
cd "$(dirname "$0")/.."
source /home/user/tools/env.sh 2>/dev/null || true
NSIS_ROOT="${NSIS_ROOT:-/home/user/tools/nsis}"
export NSISDIR="$NSIS_ROOT/usr/share/nsis"
VERSION=$(grep -oP 'config/version="\K[^"]+' project.godot)
rm -rf build/windows && mkdir -p build/windows
"$GODOT" --headless --path . --export-release "Windows Desktop" build/windows/CrystalExcavator.exe 2>&1 | grep -E "^ERROR" || true
cp README.md build/windows/LEIA-ME.md
"$NSIS_ROOT/usr/bin/makensis" -V2 -DVERSION="$VERSION" installer/CrystalExcavator.nsi
ls -la build/CrystalExcavator-Setup.exe
