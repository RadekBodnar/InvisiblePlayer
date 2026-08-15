# TODO — InvisiblePlayer

Otevřené problémy. Vyřešené se přesouvají do `RESOLVED.md` s datem a hashem commitu.
Zdroj: `CODE_REVIEW_2026-08-15.md` (commit `3ea67f2`).

Legenda závažnosti: 🔴 Blocker · 🟠 Major · 🟡 Medium · 🔵 Low

---

## 🔴 Blocker

- [ ] **B1** — `UI.Windows/App.xaml.cs:82-99` — dispatch blok je zdvojený, každá MIDI nota
  se zpracuje 2×. Druhé `NoteOn` restartuje ADSR obálku a chiff → slyšitelné dvojité
  cvaknutí u každé noty. **Oprava: smazat řádky 95-98.**

- [ ] **B2** — `Core/02_ToneEngine/ToneEngine.cs:150` — `ClipDetected` se přepisuje
  při každém vzorku (44 100×/s), čte se každých 30 ms → ořez se prakticky nikdy
  nezobrazí. Komentář na ř. 42-44 slibuje funkci, kterou kód nemá.
  **Oprava: latching + `ReadClipDetected()` po vzoru `AudioEngine.ReadPeak()`.**

## 🟠 Major

- [ ] **M1** — `Analyzer/MainWindow.xaml.cs:262-263` vs `322-329` — přealokace
  `_sampleBuffer` z UI vlákna při běžícím zápisu z audio vlákna →
  `IndexOutOfRangeException` (nejhůř při 262 144 → 8 192).
  **Oprava: přealokovat v audio vlákně na hranici okna (ř. 331).**

- [ ] **M2** — `Analyzer/MainWindow.xaml.cs:386` — blokující `Dispatcher.Invoke`
  z audio callbacku, uvnitř překreslení až 131 072 bodů → vypadávající vstupní bloky,
  riziko deadlocku. **Oprava: `InvokeAsync` + zapojit nepoužité pole `_lastRenderTime`
  (ř. 26) jako throttle.**

- [ ] **M3** — `Analyzer/MainWindow.xaml.cs:18` — `WaveInEvent` se nikdy nezastaví
  ani neuvolní (chybí `OnClosed`/`Dispose`) → obsazené nahrávací zařízení.

- [ ] **M4** — `Analyzer/MainWindow.xaml:23` — `ComboMicrophones` nemá `SelectionChanged`;
  změna vstupu za běhu nic nedělá. U měřicího nástroje riziko měření ze špatného vstupu.

- [ ] **M5** — `UI.Windows/VgaEngine.cs:139` vs `150-172` — `Console.KeyAvailable`
  a `ReadConsoleInput` nad stejným handlem; `KeyAvailable` odebírá neklávesové záznamy
  → obsluha myši (kolečko, kliky na `[<<] [►] [▄] [>>]`) je nedosažitelná.
  *Confidence MEDIUM-HIGH — falzifikace: zatočit kolečkem, sledovat `Vol:` v hlavičce.*

- [ ] **M6** — `Analyzer/MainWindow.xaml.cs:27-29` — `_isFrozen` / `_waitForSnap` /
  `_isMeasuringSnap` sdílené mezi UI a audio vláknem bez `volatile`; `_waitForSnap`
  se čte v těsné per-sample smyčce (ř. 316) → JIT smí zápis nikdy neuvidět.

- [ ] **M7** — `UI.Windows/App.xaml.cs:38` — `_ = inputManager.PlayMidiFileAsync(...)`
  fire-and-forget; výjimka z `MidiFile.Read` (poškozený soubor) zmizí, aplikace tiše nehraje.

- [ ] **M8** — `UI.Windows/DirectoryNavigator.cs:31` — `IndexOf` je case-sensitive;
  cesta z příkazové řádky s jinou velikostí písmen → `CurrentFile == null` → video se
  tiše neotevře. **Oprava: `FindIndex` + `StringComparison.OrdinalIgnoreCase`.**

## 🟡 Medium

- [ ] **S1** — `Core/04_Generators/02_WaveType.cs:33` — `VoicePreset.Harmonics` je
  non-nullable bez inicializátoru (`CS8618`); presety `_300_Cembalo` a `_400_Bell` ho
  nenastavují → `NullReferenceException` v `OrganVoice.cs:21`, jakmile se zapojí S2.

- [ ] **S2** — `Core/02_ToneEngine/ToneEngine.cs:78` — `InstrumentType` se nastavuje,
  ale nikde nevyhodnocuje; chybí factory `switch`. Mrtvý kód: `WavetableOscillator`,
  `LowPassFilter`, `PianoVoice`, `CembaloVoice`, `BellVoice`, `RegisterNumbers`,
  `Temperament.MelzerGeorgKratkyI`, 4 z 5 presetů, celý `Raspi`.

- [ ] **S3** — `Core/03_Tones/_001_199_organ.cs:5-21` — třída `_001_Bombard16Preset`
  má `Name = "Aeolus"`, `Number = 85` (kolize s `_085_Aeolus`); `ModType = AM` se
  v `OrganVoice` vůbec nečte → slibované tremolo nezazní.

- [ ] **S4** — `Core/05_Filters/02_BandPassFilter.cs:11` — bez pojistek: `q = 0`
  → `NaN` koeficienty → trvale otrávený výstup. `LowPassFilter` klampuje správně.

- [ ] **S5** — `Core/04_Generators/06_SynthVoice.cs:12` — hlas se `SustainLevel = 0`
  uvázne ve stavu `Sustain` s úrovní 0, `IsFinished` zůstane `false` → němé hlasy
  zůstávají v seznamu a přes `voiceCount` (`ToneEngine.cs:116`) tlumí znějící tóny.

- [ ] **S6** — `UI.Windows/VgaEngine.cs:175-184` — na konci playlistu vrátí
  `GetNextFile()` tentýž soubor → poslední skladba se přehrává donekonečna.

- [ ] **S7** — `UI.Windows/VgaEngine.cs:398, 422` — `SetCursorPosition(0, 7)`
  a 80znakový VU metr natvrdo; v menším okně `ArgumentOutOfRangeException` → pád.

- [ ] **S8** — `UI.Windows/VgaEngine.cs:425` — legenda „Staff Attenuation Keys [1-0]"
  slibuje mutování kanálů, `HandleInput` pro číslice nemá `case`; `_channelMuted`
  se nikdy nezapisuje.

- [ ] **S9** — `Analyzer/MainWindow.xaml.cs:379` — nekompenzovaný koherentní zisk
  Hannova okna (0,5) → absolutní dBFS o ~6 dB nižší. Relativní odečty OK.
  Navíc floor `1e-4` (−80 dB) nesedí s osou grafu do −90 dB (ř. 283).

## 🔵 Low

- [ ] **L1** — `Core/04_Generators/08_OrganVoice.cs:44` — nepoužitá lokální proměnná
  `harmonicFreq` (`CS0219`).
- [ ] **L2** — `Core/04_Generators/08_OrganVoice.cs:2` — nepoužitý a matoucí
  `using NAudio.SoundFont;`.
- [ ] **L3** — `Core/02_ToneEngine/Temperament.cs:8` — `public class Temperament`
  v globálním namespace (jediný typ v repu).
- [ ] **L4** — `UI.Windows/MainWindow.xaml.cs:119-123` — redundantní podmínky kláves;
  `isAltPressed` / `isCtrlPressed` jsou fakticky nepoužité.
- [ ] **L5** — `UI.Windows/DirectoryNavigator.cs:28, 92, 106` — `OrderBy` nad `string`
  používá aktuální kulturu; pro stabilní pořadí `StringComparer.Ordinal`.
- [ ] **L6** — `Raspi/Hardware/Mcp23016Controller.cs:46` — mrtvý `?.` na non-nullable
  `readonly` poli; chybí guard proti dvojímu `Dispose`.

## 🏗 Infrastruktura a proces

- [ ] **P1** 🟠 — **Žádné testy.** Založit `tests/InvisiblePlayer.Core.Tests` (xUnit).
  Priorita: `AudioMeter`, `AdsrEnvelope`, `BandPassFilter`, `DirectoryNavigator`,
  `ToneEngine`. Pro DSP navíc charakterizační testy (baseline výstupu) **před** S2.
- [ ] **P2** 🟡 — `Directory.Build.props` v kořeni: `TreatWarningsAsErrors`,
  `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`. Vyřeší S1, L1, L2
  automaticky. Odstranit duplicitu TFM ze 4 `.csproj`.
- [ ] **P3** 🔵 — Naplánovat migraci `net8.0` → `net10.0` (podpora .NET 8 končí 11/2026).
- [ ] **P4** 🟡 — Založit `CLAUDE.md`, `FEATURES.md`, `RESOLVED.md`; pre-commit hook
  (celá sada testů, fail-closed) až po P1.
- [ ] **P5** 🔵 — Commit messages: 12 z 18 commitů má zprávu `rr`/`e`/`d`/`ee`/`f`,
  jeden je prázdný. Historie je nepoužitelná pro `git bisect` i pro review.
- [ ] **P6** 🔵 — `Core/InvisiblePlayer.Core.csproj:17` — zbytečný
  `<Folder Include="bin\" />` (build výstup do projektu nepatří).
- [ ] **P7** 🟡 — Rozhodnout hranici Core/platforma: `Audio.cs` používá `WaveOutEvent`
  (Windows-only), ale `Core` cílí `net8.0` a má ho konzumovat i Raspi (ALSA).
