#!/usr/bin/env bash
# Compila e executa a suíte de testes automatizados em modo headless.
set -e
cd "$(dirname "$0")/.."
source /home/user/tools/env.sh 2>/dev/null || true
GODOT_BIN="${GODOT:-godot}"
dotnet build -nologo -v q 2>&1 | grep -E "error|Build succeeded" | sort -u
timeout 300 "$GODOT_BIN" --headless --path . -- --run-tests 2>&1 | grep -E "^\[OK\]|^\[FALHA\]|RESULTADO|FALHA:|^      |ERROR|SCRIPT ERROR|at:" | grep -v "Save de versão mais nova"
exit ${PIPESTATUS[0]}
