# InvisiblePlayer — poznámky k projektu

Softwarový syntezátor varhan + přehrávač médií + spektrální analyzátor.
Výhledově hardwarová větev na Raspberry Pi.

Registr funkcí: `FEATURES.md` · Otevřené problémy: `TODO.md` · Vyřešené: `RESOLVED.md`
Výchozí audit: `CODE_REVIEW_2026-08-15.md`

---

## Quick Start

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"

# Build — VŽDY po projektech, ne přes .slnx (viz Gotchas)
dotnet build InvisiblePlayer.Core/InvisiblePlayer.Core.csproj \
    --artifacts-path ~/build/InvisiblePlayer

# Testy
dotnet test tests/InvisiblePlayer.Core.Tests/InvisiblePlayer.Core.Tests.csproj \
    --artifacts-path ~/build/InvisiblePlayer

# Hooky (na čerstvém klonu nutné ručně — .git/hooks se neverzuje)
./scripts/install-hooks.sh
./scripts/test-precommit-hook.sh    # ověří, že hook umí commit zablokovat
```

## Struktura

```
InvisiblePlayer.Core/          net8.0  — syntéza, vstup, analýza (jádro, testovatelné)
  01_Input/                    MIDI vstup, Note, čísla rejstříků
  02_Synthesis/                ToneEngine, Temperament
  03_Tones/                    presety rejstříků (VoicePreset)
  04_Generators/               oscilátory, obálka, hlasy nástrojů
  05_Filters/                  biquad dolní a pásmová propust
  06_Output/                   AudioEngine (NAudio WaveOut)
  07_Analysis/                 SpectrumAnalysis (čistá analytika)
  AudioMeter.cs, DirectoryNavigator.cs, Organ.cs
InvisiblePlayer.UI.Windows/    net8.0-windows, WPF — přehrávač, VGA konzole, LibVLC
InvisiblePlayer.Analyzer/      net8.0-windows, WPF — analyzátor s grafem (ScottPlot)
InvisiblePlayer.Raspi/         net8.0  — hardwarová větev (zatím kostra)
tests/InvisiblePlayer.Core.Tests/  xUnit, 114 testů
scripts/                       pre-commit hook + jeho instalace a test
```

## Tech Stack

.NET 8 · WPF · NAudio (audio) · Melanchall.DryWetMidi (MIDI) · LibVLCSharp (video) ·
MathNet.Numerics (FFT) · ScottPlot (grafy) · System.Device.Gpio (I²C) · xUnit

---

## Konvence

- **Číslované složky a soubory** (`04_Generators/08_OrganVoice.cs`) — pořadí odpovídá
  toku signálu, ne abecedě.
- **Podtržítka v názvech presetů** (`_001_Bombard16Preset`, `_085_Aeolus`) jsou
  ZÁMĚRNÉ: číslo odpovídá fyzické klapce na hracím stole nástroje (viz
  `01_Input/RegisterNumbers.cs`). Proto je `CA1707` v `.editorconfig` vypnuté.
- **Komentáře česky**, kód anglicky.
- **ID nálezů** (`B1`, `M9`, `S5`, `P10`) pocházejí z `CODE_REVIEW_2026-08-15.md`
  a používají se v `TODO.md`, `RESOLVED.md` i commit messages. Nepřečíslovávat —
  je na nich postavená dohledatelnost napříč 17 commity.
- Commity: `typ(ID): popis` — např. `fix(B1): …`, `refactor(S10,L3): …`.

---

## Gotchas — přečti dřív, než na to narazíš

### [2026-08-15] `.slnx` nejde přeložit SDK 8
- **Nález:** `dotnet build InvisiblePlayer.slnx` → `error MSB4068: The element
  <Solution> is unrecognized`. Formát `.slnx` umí až SDK 9+.
- **Řešení:** stavět po jednotlivých `.csproj`. Skripty to tak dělají.

### [2026-08-15] Repozitář leží na exFAT — build výstup tam nepatří
- **Nález:** `/media/veracrypt1` je exFAT (bez symlinků, přes FUSE).
- **Řešení:** `--artifacts-path ~/build/InvisiblePlayer` (btrfs). SDK samotné patří
  do `~/.dotnet`, ne na svazek.

### [2026-08-15] WPF projekty se PŘELOŽÍ i na Linuxu
- **Nález:** `net8.0-windows` + `UseWPF` jde na Linuxu přeložit včetně XAML markup
  kompilace, díky `EnableWindowsTargeting=true` v `Directory.Build.props`.
- **Důležité:** překlad ano, **spuštění ne**. Cokoli běhového ve WPF (VgaEngine,
  AudioPlayer, Analyzer) zůstává neověřené — viz blok v `TODO.md`.

### [2026-08-15] Syntéza je bez semínka NEDETERMINISTICKÁ
- **Nález:** chiff a úder kladívka jsou šum. Bez pevného semínka dá dvojí spuštění
  téhož kódu jiné vzorky (peak se liší v řádu 1e-3), takže regresi zvuku nelze prokázat.
- **Řešení:** `new ToneEngine(sampleRate, temperament, noiseSeed: 12345)`.
  Na tom stojí `ToneEngineBaselineTests` — charakterizační baseline zvuku.
- **Důležité:** Když baseline spadne po změně, která zvuk měnit **neměla**, je to
  regrese. Když po změně, která ho měnit **měla**, přepočítej hodnoty a v commitu
  napiš proč.

### [2026-08-15] `TreatWarningsAsErrors` ano, analyzátory ne
- **Nález:** `AnalysisLevel=latest-recommended` + `TreatWarningsAsErrors` rozbije build
  deseti stylovými pravidly. Jediná realistická reakce by byla vypnout obojí — a tím
  přijít i o `CS8618`, které chytá skutečné vady.
- **Řešení:** `CodeAnalysisTreatWarningsAsErrors=false` — překladačová `CS*` jsou chyby,
  analyzátorová `CA*` zůstávají varováními a zavádějí se postupně.

### [2026-08-15] Nečitelná složka shodí navigaci v playlistu
- **Nález:** `DirectoryNavigator` prochází sousední složky v nadřazeném adresáři.
  Na Windows má každý NTFS svazek v kořeni `System Volume Information` se zamítnutým
  ACL — stačilo přehrávat soubor z kořene disku a stisknout PageDown.
- **Řešení:** nečitelná složka se přeskočí, nedostupný nadřazený adresář = konec cesty.
- **Poučení:** testy nad souborovým systémem si vždy izoluj do vlastního podstromu
  (dvě úrovně), jinak jim do cesty leze `/tmp`.

### [2026-08-15] Push na GitHub jde jen přes SSH s hardwarovým tokenem
- **Nález:** `$GITHUB_TOKEN` (fine-grained PAT) má na fork jen `Contents: read`.
  `git-upload-pack` → 200, `git-receive-pack` → **403**. Po rebootu je navíc agent
  prázdný a token hlásí `pin required`.
- **Řešení:** `origin` je SSH URL; klíč odemkni interaktivně (`ssh -T git@github.com`).
- **Pozor:** `GET /repos/…` vrací `permissions: {push: true}` — to jsou práva
  **uživatele**, ne **tokenu**. Nedá se tím řídit.

---

## Testování

- **Testovatelné je jen to, co je v `Core`.** Oba WPF projekty na Linuxu nespustíš,
  takže čistá logika do code-behind nepatří — viz `07_Analysis/SpectrumAnalysis.cs`,
  vytažené z `Analyzer/MainWindow.xaml.cs` právě proto.
- **Charakterizační testy** (`ToneEngineBaselineTests`) zachycují, jak engine zní DNES.
  Píší se tak, že se napíšou, nechají spadnout, a do aserce se zapíše naměřená
  skutečnost — ne hodnota spočítaná z teorie.
- **Testy pojmenované `ZNAMA_VADA_*`** schválně zachycují vadné chování. Když po
  opravě spadnou, je to signál k přepsání testu, ne k opravě kódu.
- **Pre-commit hook spouští CELOU sadu**, nikdy podmnožinu podle změněných souborů.
  Taková mapa se rozbije potichu a hook pak hlásí zeleno, aniž by cokoli spustil.
  Bez SDK hook selže **zavřeně** (exit 1), ne tiše zeleně.

## Známá omezení

Podrobně v `TODO.md`. Nejdůležitější: obsluha myši ve `VgaEngine` je pravděpodobně
nedosažitelná (M5), `InputManager` / `AudioPlayer` / `Analyzer` nemají testy,
a hranice mezi `Core` a platformou není vyřešená — `Audio.cs` používá Windows-only
`WaveOutEvent`, ale `Core` má konzumovat i Raspi (P7).
