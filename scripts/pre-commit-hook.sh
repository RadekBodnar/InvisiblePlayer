#!/bin/bash
#
# Pre-commit hook pro InvisiblePlayer — spouští CELOU testovací sadu.
#
# Instalace:  ./scripts/install-hooks.sh
# Obejití:    PRE_COMMIT_SKIP=1 git commit ...   (jen v nouzi)
#
# ZÁSADA: hook smí být pomalý, nesmí být TIŠE NEPŘÍTOMNÝ.
# Když kontrolu nelze provést (chybí SDK), skončí NENULOVĚ a řekne proč.
# "Nedokážu ověřit" != "nic k hlášení". Selhává tedy ZAVŘENĚ, ne otevřeně.
#
# ZÁSADA: NIKDY nemapovat změněné soubory na "jejich" testy. Taková mapa se
# rozbije potichu a hook pak vesele hlásí zeleno, aniž by cokoli spustil.
# Celá sada je tady levná (114 testů, ~0,4 s).
#
set -euo pipefail

if [ "${PRE_COMMIT_SKIP:-0}" = "1" ]; then
    echo "⚠️  pre-commit PŘESKOČEN (PRE_COMMIT_SKIP=1)"
    exit 0
fi

ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT"

# --- Hrubé síto: jde vůbec o kód? Kontrola TYPU souboru, ne mapa soubor->test.
if ! git diff --cached --name-only --diff-filter=ACMR \
     | grep -qE '(\.cs$|\.xaml$|\.csproj$|\.slnx$|^Directory\.Build\.props$|^\.editorconfig$)'; then
    echo "✅ pre-commit: žádný kód ve stagi, testy přeskočeny"
    exit 0
fi

# --- SDK: bez něj se NEDÁ ověřit nic -> fail closed.
DOTNET=""
for candidate in "${DOTNET_ROOT:-}/dotnet" "$HOME/.dotnet/dotnet" "$(command -v dotnet 2>/dev/null || true)"; do
    if [ -n "$candidate" ] && [ -x "$candidate" ]; then DOTNET="$candidate"; break; fi
done

if [ -z "$DOTNET" ]; then
    echo "❌ pre-commit: .NET SDK nenalezeno — testy NELZE spustit."
    echo "   Hledáno v: \$DOTNET_ROOT/dotnet, ~/.dotnet/dotnet, PATH"
    echo "   Instalace: curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0"
    echo "   Obejití (jen v nouzi): PRE_COMMIT_SKIP=1 git commit ..."
    exit 1
fi

export DOTNET_ROOT="$(dirname "$DOTNET")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# Build výstup mimo strom projektu: repozitář leží na exFAT svazku.
ARTIFACTS="${IP_ARTIFACTS_PATH:-$HOME/build/InvisiblePlayer}"

echo "🔨 pre-commit: build všech projektů…"
# .slnx SDK 8 neumí (MSB4068), proto po projektech.
for proj in InvisiblePlayer.Core InvisiblePlayer.UI.Windows InvisiblePlayer.Analyzer InvisiblePlayer.Raspi; do
    if ! "$DOTNET" build "$proj/$proj.csproj" --artifacts-path "$ARTIFACTS" -v quiet --nologo >/dev/null 2>&1; then
        echo "❌ pre-commit: $proj se nepřeložil."
        echo "   Detail: dotnet build $proj/$proj.csproj --artifacts-path $ARTIFACTS"
        exit 1
    fi
done

echo "🧪 pre-commit: celá testovací sada…"
if ! "$DOTNET" test tests/InvisiblePlayer.Core.Tests/InvisiblePlayer.Core.Tests.csproj \
        --artifacts-path "$ARTIFACTS" -v quiet --nologo; then
    echo "❌ pre-commit: testy neprošly — commit zablokován."
    echo "   Obejití (jen v nouzi): PRE_COMMIT_SKIP=1 git commit ..."
    exit 1
fi

# --- Připomínka FEATURES.md. Záměrně jen upozornění, ne blokace:
#     posoudit dopad na funkci umí člověk, ne grep.
if git diff --cached --name-only | grep -qE '\.cs$' \
   && ! git diff --cached --name-only | grep -q '^FEATURES.md$'; then
    echo "ℹ️  Měněn kód, ale ne FEATURES.md — ověř, že žádná ✅ DONE položka nepadla."
fi

echo "✅ pre-commit: build i testy prošly"
exit 0
