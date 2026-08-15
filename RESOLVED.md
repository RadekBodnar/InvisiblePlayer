# RESOLVED — InvisiblePlayer

Archiv vyřešených problémů. **Append-only** — slouží jako audit trail, nemaže se.
Otevřené problémy jsou v `TODO.md`, plné znění nálezů v `CODE_REVIEW_2026-08-15.md`.

---

## 2026-08-15 — code review celého řešení

Statické review 4 projektů (36 `.cs`, ~3 290 řádků) podle skills `code-quality`
a `csharp-standards`. Nálezy původně **neověřené kompilací** (na stroji nebylo SDK);
SDK 8.0.424 doinstalováno až v průběhu, poté vše ověřeno sestavením a testy.

### Blockery

| # | Commit | Popis |
|---|---|---|
| **B1** | `7e5b47a` | `App.xaml.cs:82-99` — dispatch blok byl v handleru dvakrát, každá MIDI nota se zpracovala 2×. Druhé `NoteOn` restartovalo ADSR obálku a znovu spustilo chiff → slyšitelné dvojité cvaknutí. Odstraněny 4 řádky. |
| **B2** | `529b29a` | `ToneEngine.cs:150` — `ClipDetected` se přepisovalo při každém vzorku (44 100×/s), čtení probíhá po ~30 ms → transientní ořez se do okamžiku čtení trefil s pravděpodobností ~1/1300. Nahrazeno latchingem `ReadClipDetected()` (sémantika jako `AudioEngine.ReadPeak()`), pole `volatile`. **BREAKING:** property → metoda. |

### Major

| # | Commit | Popis |
|---|---|---|
| **M1** | `a9248b0` | Analyzer: `_sampleBuffer` se přealokovával z UI vlákna, zatímco do něj zapisovalo audio vlákno → `IndexOutOfRangeException` (typicky 262144 → 8192). Vlastnictví pole přesunuto výhradně na audio vlákno (`_pendingFftSize` + `ApplyPendingFftSize`). |
| **M2** | `a9248b0` | Analyzer: blokující `Dispatcher.Invoke` z audio callbacku (překreslení až 131 072 bodů) → `InvokeAsync` + `_renderPending` jako náhrada za ztracený zpětný tlak. Uvolnění ve `finally`, aby ho nezablokovala větev `return` u `_isFrozen`. |
| **M3** | `a9248b0` | Analyzer: `WaveInEvent` se nikdy nezastavil ani neuvolnil → `StopAudioCapture()` + `OnClosed()`. |
| **M4** | `a9248b0` | Analyzer: `ComboMicrophones` neměl `SelectionChanged`, výběr vstupu neměl efekt → handler + napojení v XAML. |
| **M6** | `a9248b0` | Analyzer: `_isFrozen` / `_waitForSnap` / `_isMeasuringSnap` sdílené mezi vlákny bez `volatile`; `_waitForSnap` se čte v těsné per-sample smyčce. |
| **M7** | `9ab0142` | `PlayMidiFileAsync` se volá fire-and-forget; výjimka z `MidiFile.Read` mizela beze stopy → událost `OnPlaybackError` + handler v `App.OnStartup`. |
| **M8** | `9ab0142` | `DirectoryNavigator.LoadDirectory` hledal soubor přes case-sensitive `IndexOf`, zatímco Windows FS je case-insensitive → `FindIndex` + `OrdinalIgnoreCase`. |

### Medium / Low

| # | Commit | Popis |
|---|---|---|
| **S1** | `9ab0142` | `VoicePreset.Harmonics` non-nullable bez inicializátoru (**CS8618**, potvrzeno překladačem), presety Cembalo/Bell ho nenastavovaly → NRE v `OrganVoice` ctor. Doplněna výchozí hodnota. |
| **S4** | `9ab0142` | `BandPassFilter.SetParams` bez pojistek: `q = 0` → dělení nulou → NaN koeficienty → filtr navždy vrací NaN. Doplněno klampování + chybějící `Reset()`. |
| **L1** | `9ab0142` | Odstraněna nepoužitá lokální proměnná `harmonicFreq`. |
| **L2** | `9ab0142` | Odstraněn nepoužitý `using NAudio.SoundFont`. |
| **L5** | `9ab0142` | Všechna `OrderBy` nad `string` → `StringComparer.Ordinal` (výchozí řazení používá aktuální kulturu; v češtině `ch` za `h`). |

### Infrastruktura

| # | Commit | Popis |
|---|---|---|
| **P1** | `088caaf` | Založen `tests/InvisiblePlayer.Core.Tests` (xUnit) — **57 testů, 0 selhání**. Pokrývá `AudioMeter`, `ToneEngine`, `BandPassFilter`, `AdsrEnvelope`, `Temperament`, `VoicePreset`, `SynthVoice`. |
| **P2** | `088caaf` | `Directory.Build.props` + `.editorconfig`. `TreatWarningsAsErrors=true` (CS* = chyba) **a zároveň** `CodeAnalysisTreatWarningsAsErrors=false` (CA* = varování). `EnableWindowsTargeting` mimo Windows. |

---

## Opravy vlastních chyb v review

Poctivý záznam toho, co v review z 2026-08-15 nesedělo. Zjištěno až po instalaci SDK.

1. **Přehlédnutý druhý výskyt S1.** Review našlo `CS8618` na `VoicePreset.Harmonics`,
   ale ne na `ToneEngine.ActiveNote.Voice` (`ToneEngine.cs:13`) — stejná vada, druhý
   výskyt. Build původního commitu `3ea67f2` hlásil **2× CS8618**, review popsalo jedno.
   Opraveno v `088caaf` pomocí `required`. *Poučení: čtení kódu nenahradí překladač.*

2. **Špatně určený diagnostický kód u L1.** Review tvrdilo, že nepoužitá proměnná
   `harmonicFreq` je `CS0219` a že by ji `TreatWarningsAsErrors` zachytil. **Není to
   pravda:** `CS0219` se týká jen proměnných přiřazených *konstantou*. Zde je
   inicializátor výraz s voláním, takže překladač mlčí. Nález sám (nepoužitá proměnná)
   platí, ale odhalí ho až analyzátor `IDE0059`, ne překladač.

3. **Zbytečně pesimistický odhad u WPF na Linuxu.** Review i následná odpověď uváděly
   „confidence MEDIUM" k tomu, zda na Linuxu projde XAML markup kompilace.
   **Prošla** — `dotnet build -p:EnableWindowsTargeting=true` sestaví oba WPF projekty
   na Ubuntu 26.04 se SDK 8.0.424 bez chyby.

4. **Chybná aserce v prvním návrhu testu k B1.** Test `NoteOn_DvakratZaSebou_…`
   původně tvrdil, že dvojité `NoteOn` dá *bitově totožný* signál. Spadl: rozdíl
   0,0003. Ukázalo se, že retrigger sice nevytvoří druhý hlas (poměr špiček 1,0004,
   ne 1,414), ale **restartuje obálku a chiff** — což je právě ten mechanismus, kterým
   byl B1 slyšet. Test přepsán na dvojici aserce + charakterizace.
   *Poučení: padající test byl přesnější než moje hypotéza.*
