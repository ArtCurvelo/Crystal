#!/usr/bin/env bash
# Benchmark de desempenho (vistas fixas por região). Uso: ./tools/run_bench.sh [qualidade 0-2]
cd "$(dirname "$0")/.."
source /home/user/tools/env.sh 2>/dev/null || true
timeout 300 xvfb-run -a -s "-screen 0 1280x720x24" "$GODOT" --path . --resolution 1280x720 -- --bench --bench-quality=${1:-0} 2>&1 | grep "\[Bench\]"
