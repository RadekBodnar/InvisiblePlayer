# TODO — InvisiblePlayer

Pouze **otevřené** problémy. Vyřešené jsou v `RESOLVED.md` s datem a hashem commitu.
Zdroj: `CODE_REVIEW_2026-08-15.md`.

Legenda: 🔴 Blocker · 🟠 Major · 🟡 Medium · 🔵 Low

> **Stav ověření (2026-08-15):** na stroji je .NET SDK 8.0.424 (`~/.dotnet`, bez sudo).
> Build: `dotnet build <projekt>.csproj --artifacts-path ~/build/InvisiblePlayer`
> Testy: `dotnet test tests/InvisiblePlayer.Core.Tests/…` — aktuálně **57 testů, 0 selhání**.
> WPF projekty se na Linuxu překládají (`EnableWindowsTargeting`), spustit je nelze.

---

## 🟠 Major

- [ ] **M5** — `UI.Windows/VgaEngine.cs:139` vs `150-172` — `Console.KeyAvailable`
  a `ReadConsoleInput` nad stejným vstupním handlem. `KeyAvailable` odebírá z fronty
  neklávesové záznamy, tedy i `MOUSE_EVENT` → obsluha kolečka a kliků na
  `[<<] [►] [▄] [>>]` je nejspíš nedosažitelný kód.
  **Confidence MEDIUM-HIGH — NEOVĚŘENO**, vyžaduje běh na Windows.
  *Falzifikace: spustit, zatočit kolečkem nad konzolí, sledovat `Vol:` v hlavičce.*

## 🟡 Medium

- [ ] **S2** — `Core/02_ToneEngine/ToneEngine.cs:78` — `InstrumentType` se nastavuje,
  ale nikde nevyhodnocuje; `NoteOn` vždy vytvoří `OrganVoice` s jediným zadrátovaným
  presetem. Mrtvý kód: `WavetableOscillator`, `LowPassFilter`, `PianoVoice`,
  `CembaloVoice`, `BellVoice`, `RegisterNumbers`, `Temperament.MelzerGeorgKratkyI`,
  4 z 5 presetů, celý `Raspi`.
  **Vyžaduje rozhodnutí.** Návrh: factory `switch` + změna `ActiveNote.Voice`
  z `OrganVoice` na `SynthVoice`. Testy `SynthVoiceTests` už na to čekají.
  ⚠️ Před refaktorem doplnit charakterizační testy zvuku (baseline výstupu).

- [ ] **S3** — `Core/03_Tones/_001_199_organ.cs:5-21` — třída `_001_Bombard16Preset`
  má `Name = "Aeolus"` a `Number = 85`, což koliduje s `_085_Aeolus`.
  `ModType = AM` nemá efekt — `OrganVoice` modulační pole vůbec nečte.
  **Vyžaduje rozhodnutí:** přejmenovat třídu, nebo opravit data?

- [ ] **S5** — `Core/04_Generators/06_SynthVoice.cs:12` — hlas se `SustainLevel = 0`
  uvázne ve stavu `Sustain` s úrovní 0; `IsFinished` zůstane `false` → němé hlasy
  se nemažou a přes `voiceCount` tlumí znějící tóny.
  ✅ Zachyceno charakterizačním testem `ZNAMA_VADA_S5_SustainNula_UvizneVeStavuSustain`
  — **až se vada opraví, test začne padat** (to je záměr, připomene přepis aserce).
  Latentní: projeví se až se zapojí S2.

- [ ] **S6** — `UI.Windows/VgaEngine.cs:175-184` — na konci playlistu vrátí
  `GetNextFile()` tentýž soubor → poslední skladba se přehrává donekonečna.
  **Vyžaduje rozhodnutí:** je to zamýšlené zacyklení, nebo má přehrávání skončit?

- [ ] **S7** — `UI.Windows/VgaEngine.cs:398, 422` — `SetCursorPosition(0, 7)`
  a 80znakový VU metr natvrdo; v menším okně `ArgumentOutOfRangeException` → pád.

- [ ] **S8** — `UI.Windows/VgaEngine.cs:425` — legenda „Staff Attenuation Keys [1-0]"
  slibuje mutování kanálů, `HandleInput` pro číslice nemá `case`, `_channelMuted`
  se nikdy nezapisuje.

- [ ] **S9** — `Analyzer/MainWindow.xaml.cs:379` — nekompenzovaný koherentní zisk
  Hannova okna (0,5) → absolutní dBFS o ~6 dB nižší. Relativní odečty jsou OK.
  Floor `1e-4` (−80 dB) navíc nesedí s osou grafu do −90 dB.
  ⚠️ Než z analyzátoru vzniknou další presety, ověřit ho proti generovanému sinu
  o známé amplitudě.

- [ ] **S10** 🆕 — `Core/02_ToneEngine/` — **`ToneEngine` je zároveň namespace i třída.**
  Odhaleno při psaní testů (CS0118). Důsledek: každé použití vyžaduje plnou
  kvalifikaci `InvisiblePlayer.Core.ToneEngine.ToneEngine` (viz `Audio.cs:12, 20`),
  nebo alias umístěný *uvnitř* deklarace namespacu.
  Návrh: přejmenovat namespace na `InvisiblePlayer.Core.Synthesis`.

## 🔵 Low

- [ ] **L3** — `Core/02_ToneEngine/Temperament.cs:8` — `public class Temperament`
  v globálním namespace (jediný typ v repu). Potvrzeno analyzátorem: **CA1050**.
- [ ] **L4** — `UI.Windows/MainWindow.xaml.cs:119-123` — redundantní podmínky kláves;
  `isAltPressed` / `isCtrlPressed` jsou fakticky nepoužité.
- [ ] **L6** — `Raspi/Hardware/Mcp23016Controller.cs:46` — mrtvý `?.` na non-nullable
  `readonly` poli; chybí guard proti dvojímu `Dispose`. Potvrzeno: **CA1816**.
- [ ] **L7** 🆕 — zbylá varování analyzátorů (nezastavují build, ale jsou reálná):
  `CA1051` ×6 (veřejná instanční pole, např. `SynthVoice.SampleRate`),
  `CA1816` ×4 (`Dispose` bez `GC.SuppressFinalize`), `CA1050` ×2, `CA1014` ×2.

## 🏗 Infrastruktura a proces

- [ ] **P3** 🔵 — migrace `net8.0` → `net10.0` (podpora .NET 8 končí 11/2026).
  Souvisí: **SDK 8 neumí formát `.slnx`** (`error MSB4068`), stavět jde jen po
  projektech. SDK 9+ by vyřešilo obojí.
- [ ] **P4** 🟡 — založit `CLAUDE.md` (konvence projektu) a `FEATURES.md`
  (registr funkcí se sloupcem `Tests` — teď už je na co odkazovat).
  Pre-commit hook: **celá** sada testů, fail-closed, nikdy podmnožina.
- [ ] **P5** 🔵 — commit messages: 12 z 18 původních commitů má zprávu `rr`/`e`/`d`/`ee`,
  jeden je prázdný. Historie je nepoužitelná pro `git bisect`.
- [ ] **P6** 🔵 — `Core/InvisiblePlayer.Core.csproj:17` — zbytečný `<Folder Include="bin\" />`.
- [ ] **P7** 🟡 — rozhodnout hranici Core/platforma: `Audio.cs` používá `WaveOutEvent`
  (Windows-only), ale `Core` cílí `net8.0` a má ho konzumovat i Raspi (ALSA).
- [ ] **P8** 🔵 🆕 — `NU1701`: `ScottPlot.WPF` táhne tranzitivně `SkiaSharp.Views.WPF`
  bez `net8.0-windows` assetů (restore přes .NET Framework fallback). Zatím tlumeno
  v `Directory.Build.props`; prověřit při upgradu ScottPlotu.
- [ ] **P9** 🟡 🆕 — rozšířit testy na `DirectoryNavigator` (potřebuje dočasný adresář —
  ověřit opravu M8 na case-insensitive cestě) a `LowPassFilter` (stabilita).
  Ty dvě třídy jsou zatím bez pokrytí.
