#!/usr/bin/env bash
# Publica uma atualização automática:
#   ./tools/publish_update.sh "Notas da versão"
# 1) testes (aborta se falharem)  2) sobe a versão (patch)  3) exporta para Windows
# 4) gera update.json (SHA-256)  5) envia o pacote ao ramo "updates" (substitui o anterior)
# 6) commita e envia a nova versão no main.
set -euo pipefail
cd "$(dirname "$0")/.."
source /home/user/tools/env.sh 2>/dev/null || true
NOTES="${1:-Melhorias e correções.}"
REPO_RAW="https://raw.githubusercontent.com/ArtCurvelo/Crystal/updates"
DATA="data_CrystalExcavator_windows_x86_64"

echo "== Testes"
./tools/run_tests.sh > /tmp/ce_publish_tests.log 2>&1 || { grep -E "FALHA|RESULTADO" /tmp/ce_publish_tests.log; echo "Testes falharam: publicação cancelada."; exit 1; }
grep RESULTADO /tmp/ce_publish_tests.log

echo "== Versão"
OLD=$(grep -oP 'config/version="\K[^"]+' project.godot)
IFS=. read -r MA MI PA <<< "$OLD"
NEW="$MA.$MI.$((PA + 1))"
sed -i "s/^config\/version=\"$OLD\"/config\/version=\"$NEW\"/" project.godot
echo "$OLD -> $NEW"

echo "== Exportação"
rm -rf build/windows && mkdir -p build/windows
"$GODOT" --headless --path . --export-release "Windows Desktop" build/windows/CrystalExcavator.exe 2>&1 | grep -E "^ERROR" || true
test -f build/windows/CrystalExcavator.pck

echo "== Pacote"
PAY=build/update_payload
rm -rf "$PAY" && mkdir -p "$PAY/$DATA"
cp build/windows/CrystalExcavator.pck "$PAY/"
for f in CrystalExcavator.dll CrystalExcavator.deps.json CrystalExcavator.runtimeconfig.json; do cp "build/windows/$DATA/$f" "$PAY/$DATA/"; done
ENGINE=$("$GODOT" --version | grep -oP '^\d+\.\d+(\.\d+)?' | head -1)
[[ "$ENGINE" =~ ^[0-9]+\.[0-9]+$ ]] && ENGINE="$ENGINE.0"
python3 -I - "$PAY" "$NEW" "$ENGINE" "$NOTES" "$REPO_RAW" "$DATA" <<'PY'
import sys, json, hashlib, os
pay, ver, engine, notes, raw, data = sys.argv[1:7]
files = []
for rel in ["CrystalExcavator.pck", f"{data}/CrystalExcavator.dll", f"{data}/CrystalExcavator.deps.json", f"{data}/CrystalExcavator.runtimeconfig.json"]:
    p = os.path.join(pay, rel)
    b = open(p, "rb").read()
    files.append({"path": rel, "url": f"{raw}/{rel}", "sha256": hashlib.sha256(b).hexdigest(), "size": len(b)})
m = {"version": ver, "engine": engine, "notes": notes,
     "setupUrl": "https://github.com/ArtCurvelo/Crystal/raw/main/dist/CrystalExcavator-Setup.exe", "files": files}
open(os.path.join(pay, "update.json"), "w").write(json.dumps(m, ensure_ascii=False, indent=2))
print(json.dumps(m, ensure_ascii=False, indent=2))
PY

echo "== Envio do pacote (ramo updates)"
export GIT_INDEX_FILE="$PWD/.git/updates-index"
rm -f "$GIT_INDEX_FILE"
git --work-tree="$PAY" add -A -f .
TREE=$(git write-tree)
COMMIT=$(git commit-tree "$TREE" -m "Atualização v$NEW")
unset GIT_INDEX_FILE
rm -f .git/updates-index
for i in 1 2 3 4; do git push -f origin "$COMMIT:refs/heads/updates" && break || sleep $((i * 2)); done

echo "== Versão no main"
git add project.godot
git commit -q -m "Publica atualização v$NEW: $NOTES

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01VNDxi8LJdZjd7RsBuVevc4"
for i in 1 2 3 4; do git push && break || sleep $((i * 2)); done
echo "Publicado: v$NEW"
