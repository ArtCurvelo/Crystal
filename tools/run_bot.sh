#!/usr/bin/env bash
# Executa o bot de QA que joga a partida real (headless, simulação acelerada).
# Uso: ./tools/run_bot.sh [minutos_de_jogo]
set -e
cd "$(dirname "$0")/.."
source /home/user/tools/env.sh 2>/dev/null || true
GODOT_BIN="${GODOT:-godot}"
dotnet build -nologo -v q 2>&1 | grep -E "error|Build succeeded" | sort -u
timeout 1200 "$GODOT_BIN" --headless --fixed-fps 60 --path . -- --bot=${1:-120} 2>&1 | grep -E "^\[|Tempo de jogo|Vendas|Upgrades|Renascimento|Dias completos|Miras perdidas|BOT:|Save|ERRO|EXCE"
exit ${PIPESTATUS[0]}
