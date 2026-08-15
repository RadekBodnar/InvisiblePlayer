#!/bin/bash
#
# Nainstaluje git hooky do .git/hooks/.
#
# Proč skript a ne prostě soubor v repu: .git/hooks/ se NEVERZUJE, takže na každém
# čerstvém klonu je hook potřeba nainstalovat ručně. Kdo to neudělá, commituje
# bez ochrany a nic mu to neřekne.
#
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
SRC="$ROOT/scripts/pre-commit-hook.sh"
DST="$ROOT/.git/hooks/pre-commit"

[ -f "$SRC" ] || { echo "❌ Chybí $SRC"; exit 1; }

# POZOR: v .git/hooks/pre-commit může už něco být — typicky wrapper pro sken
# tajemství (gitleaks apod.). Přepsat ho naslepo znamená ten sken tiše vypnout.
if [ -e "$DST" ] && ! grep -q 'InvisiblePlayer' "$DST" 2>/dev/null; then
    echo "⚠️  V .git/hooks/pre-commit už je JINÝ hook:"
    echo "───────────────────────────────────────────"
    head -20 "$DST" | sed 's/^/  /'
    echo "───────────────────────────────────────────"
    echo "Nepřepisuji. Zálohuj a slouč ručně, nebo smaž a spusť znovu:"
    echo "  mv $DST $DST.backup && $0"
    exit 1
fi

cp "$SRC" "$DST"
chmod +x "$DST"
echo "✅ Nainstalováno: $DST"
echo "   Obejití v nouzi: PRE_COMMIT_SKIP=1 git commit ..."
