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
| **M8** | `9ab0142` | `DirectoryNavigator.LoadDirectory` hledal soubor přes case-sensitive `IndexOf`, zatímco Windows FS je case-insensitive → `FindIndex` + `OrdinalIgnoreCase`. ⚠️ Na Linuxu **nelze ověřit testem** (`File.Exists` selže dřív) — zbývá kontrola na Windows. |
| **M9** | `fb64758` | 🆕 **Nález odhalený testem, ne review ani překladačem.** `TryToNavigateToNeighborFolder` nemělo ošetření kolem `GetDirectories()`/`GetFiles()`; jediná nečitelná sousední složka vyhodila `UnauthorizedAccessException`, která propadla až do `VgaEngine.Run` / `MainWindow_KeyDown` → pád. Na Windows skoro zaručené: každý NTFS svazek má v kořeni `System Volume Information` se zamítnutým ACL, takže stačilo přehrávat soubor z kořene disku a stisknout PageDown. Ověřeno oběma směry — bez opravy test padá právě touto výjimkou. |

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
| **P9** | `fb64758` | 11 testů pro `DirectoryNavigator` + přesun souboru z projektu `UI.Windows` do `Core` (hlásil se do namespace `InvisiblePlayer.Core`, ležel jinde a neměl WPF závislosti — kvůli tomu nešel testovat). Celkem **68 testů**. |
| **P2** | `088caaf` | `Directory.Build.props` + `.editorconfig`. `TreatWarningsAsErrors=true` (CS* = chyba) **a zároveň** `CodeAnalysisTreatWarningsAsErrors=false` (CA* = varování). `EnableWindowsTargeting` mimo Windows. |

### Druhá vlna (po instalaci SDK, vše ověřeno sestavením i testy)

| # | Commit | Popis |
|---|---|---|
| **S2** | `04b3ca7` | `VoicePreset.Instrument` se nastavovalo, ale nikde nevyhodnocovalo — `NoteOn` vždy vytvořil `OrganVoice` a Piano/Cembalo/Bell byly nedosažitelné. Doplněna factory `CreateVoice` + nastavitelný `CurrentPreset`; `ActiveNote.Voice` z `OrganVoice` na `SynthVoice`. |
| **předpoklad S2** | `04b3ca7` | 🆕 **Nález objevený při psaní charakterizačních testů:** `NoiseGenerator` používal `new Random()` bez semínka → engine nebyl reprodukovatelný a regresní test zvuku nešlo napsat vůbec. `NoiseGenerator(int? seed = null)`, semínko prochází až do `ToneEngine`. Výchozí chování beze změny. |
| **S3** | `8bd80c1` | `_001_Bombard16Preset` měl `Name = "Aeolus"`, `Number = 85` — kolize s `_085_Aeolus`. Že jsou špatně data (ne název třídy) plyne z harmonických: celočíselná řada 1-2-3-4-5 je spektrum píšťaly, zvon má partiály neceločíselné. Odstraněn i `ModType = AM`, který `OrganVoice` nikdy nečetl. Test `CislaRejstriku_JsouJedinecna` hlídá návrat kolize. |
| **S6** | `8bd80c1` | `DirectoryNavigator` pletl „další neexistuje" s „tady máš zase ten samý" → poslední skladba se opakovala donekonečna. Opraven **kontrakt** (`GetNextFile`/`GetPreviousFile` vracejí `null`), politiku volí volající. |
| **S7** | *tento commit* | `SetCursorPosition(0, 7)` a 80znakový VU metr natvrdo → `ArgumentOutOfRangeException` v malém okně. Doplněno `TrySetCursor()` (3 místa) a `MeterWidth()` odvozená od `Console.WindowWidth`. |
| **S8 (část)** | *tento commit* | Legenda „Staff Attenuation Keys [1-0]" slibovala funkci, kterou `HandleInput` nemá. Text opraven na pravdivý; vlastní mutování zůstává otevřené jako funkce, ne oprava. |
| **S10** | `b6ce211` | `InvisiblePlayer.Core.ToneEngine` byl namespace i třída (CS0118) — `Audio.cs` proto psalo plnou kvalifikaci všude a testy potřebovaly alias za deklarací namespace. Přejmenováno na `…​.Synthesis`, složka `02_ToneEngine` → `02_Synthesis`. |
| **L3** | `b6ce211` | `Temperament` byl jako jediný typ v repu v globálním namespace (CA1050). Přesunut do `InvisiblePlayer.Core.Synthesis`; ověřeno 0 výskytů CA1050. |
| **L4** | *tento commit* | `MainWindow_KeyDown` měl dvě mrtvé větve podmínky (`Key.Return && Alt` pohlceno `Key.Return`, `Key.F && Ctrl` pohlceno `Key.F`) — `isAltPressed` tím pádem nebyla použitá. |
| **L6** | *tento commit* | `Mcp23016Controller.Dispose` měl mrtvý `?.` na non-nullable readonly poli a chyběl guard proti dvojímu Dispose + `GC.SuppressFinalize`. |
| **P6** | *tento commit* | Odstraněn zbytečný `<Folder Include="bin\" />` z `Core.csproj`. |
| **P10** | `cc42a6e` | 8 testů pro `LowPassFilter` (konvergence impulsní odezvy, hodnoty mimo rozsah, potlačení výšek, `Reset`, přepočet při změně vzorkovací frekvence). Celkem **95 testů**. |
| **S5** | `b39d219` | Obálka se `SustainLevel = 0` uvázla ve stavu `Sustain` na nulové úrovni: němá, ale `IsActive` hlásilo `true`, takže `IsFinished` zůstalo `false` a `ToneEngine` hlas nikdy neuklidil. Němé hlasy se hromadily a přes `voiceCount` tlumily kompenzací `1/√N` tóny, které skutečně zněly. Do zavedení factory (S2) latentní — pak se stalo aktuálním. Regresní test přes **důsledek**: po deseti doznělých cembalových notách má jedenáctá stejnou amplitudu jako sólový tón. |
| **S9** | `e127d57` | Absolutní dBFS bylo o ~6 dB nižší — nekompenzoval se koherentní zisk Hannova okna (0,5). Relativní odečty byly v pořádku, ale údaj „DOMINANTA = … dBFS" byl špatně, a analyzátor slouží právě k odměřování amplitud pro presety. Podlaha dynamiky sjednocena na −100 dB (dřív −80 dB proti ose grafu do −90 dB). Chyba doložena testem: rozdíl kompenzovaného a nekompenzovaného výpočtu = 6,0206 dB. |
| **P11** | `e127d57` | `AnalyzeOrganSubAndHarmonics`, `AnalyzeBellPikes`, `AnalyzeNoiseShape` a `FindAllPikes` byly privátní metody uvnitř WPF okna — nezávislé na UI, ale netestovatelné (Analyzer je `net8.0-windows`). Vytaženo do `Core/07_Analysis/SpectrumAnalysis.cs` + 19 testů proti syntetickému spektru se známou odpovědí. Code-behind zkrátil z 455 na 419 řádků. |
| **navíc** | `e127d57` | 🆕 `ScanForDrop`: když šumový kopec sahá až na kraj spektra, vracel se kmitočet vrcholu → nulová šířka pásma → uměle vysoké Q. Odhaleno při psaní testu na monotónně klesající spektrum. |
| **P4** | `8211f4b` | Založeny `CLAUDE.md` (konvence + Gotchas), `FEATURES.md` (22 položek F-001..F-022 se sloupcem `Tests`) a pre-commit hook: celá sada testů, fail-closed bez SDK, únikový východ `PRE_COMMIT_SKIP=1`. Včetně `scripts/test-precommit-hook.sh`, který **testuje sám hook** — 4 scénáře, všechny prošly. |

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
