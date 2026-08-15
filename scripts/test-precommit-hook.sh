#!/bin/bash
#
# Testuje SÁM HOOK — tedy že opravdu umí commit zablokovat.
#
# Ochranná vrstva, kterou nikdo nezkusil rozbít, je jen dekorace. Hook, který
# vždycky vypíše zelenou, se od funkčního hooku nedá odlišit jinak než tím,
# že mu podstrčíš rozbitý stav a ověříš, že se ozve.
#
# Spuštění:  ./scripts/test-precommit-hook.sh
#
set -uo pipefail

ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT"
HOOK="$ROOT/scripts/pre-commit-hook.sh"
BROKEN="tests/InvisiblePlayer.Core.Tests/_HookProbe.cs"

PASS=0
FAIL=0

cleanup() {
    git reset -q HEAD "$BROKEN" 2>/dev/null || true
    rm -f "$BROKEN"
}
trap cleanup EXIT

check() { # $1=popis  $2=očekávaný exit  $3=skutečný exit
    if [ "$2" -eq "$3" ]; then
        echo "  ✅ $1 (exit $3)"; PASS=$((PASS+1))
    else
        echo "  ❌ $1 — očekáván exit $2, byl $3"; FAIL=$((FAIL+1))
    fi
}

echo "=== 1) Únikový východ PRE_COMMIT_SKIP=1 ==="
PRE_COMMIT_SKIP=1 "$HOOK" >/dev/null 2>&1
check "hook se přeskočí" 0 $?

echo
echo "=== 2) FAIL CLOSED: bez SDK musí skončit CHYBOU, ne zeleně ==="
# PATH musí zůstat funkční (hook potřebuje git, grep, dirname) — jinak by umřel
# na exit 127 dřív, než se ke své vlastní kontrole SDK vůbec dostane, a test by
# ověřoval pád skriptu místo zamýšlené větve.
# Nedosažitelné SDK simulujeme přes DOTNET_ROOT + HOME do prázdného adresáře.
FAKE_HOME="$(mktemp -d)"
echo '// probe' > "$BROKEN"
git add "$BROKEN"
env HOME="$FAKE_HOME" PATH="/usr/bin:/bin" DOTNET_ROOT="/nonexistent" \
    bash "$HOOK" >/dev/null 2>&1
check "chybějící SDK commit ZABLOKUJE" 1 $?
rm -rf "$FAKE_HOME"
git reset -q HEAD "$BROKEN"; rm -f "$BROKEN"

echo
echo "=== 3) Padající test musí commit ZABLOKOVAT ==="
cat > "$BROKEN" <<'EOF'
namespace InvisiblePlayer.Core.Tests;

// Dočasný soubor vytvořený scripts/test-precommit-hook.sh.
// Ověřuje, že pre-commit hook padající test skutečně zachytí.
public class _HookProbe
{
    [Fact]
    public void ZamerneSelze() => Assert.True(false, "Záměrné selhání pro test hooku.");
}
EOF
git add "$BROKEN"
"$HOOK" >/dev/null 2>&1
check "padající test commit ZABLOKUJE" 1 $?
git reset -q HEAD "$BROKEN"; rm -f "$BROKEN"

echo
echo "=== 4) Čistý strom musí projít ==="
# Nastagujeme skutečný soubor s kódem, ale bez jakékoli změny obsahu.
git add InvisiblePlayer.Core/AudioMeter.cs
"$HOOK" >/dev/null 2>&1
rc=$?
git reset -q HEAD InvisiblePlayer.Core/AudioMeter.cs
check "zelený stav commit PUSTÍ" 0 $rc

echo
echo "───────────────────────────────"
echo "  prošlo: $PASS   selhalo: $FAIL"
[ "$FAIL" -eq 0 ] || exit 1
echo "  Hook prokazatelně umí zablokovat commit."
