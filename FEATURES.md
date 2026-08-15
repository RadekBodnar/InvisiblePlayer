# FEATURES.md — Funkční registr

> Poslední audit: 2026-08-15
> Zdroje: git historie, zdrojový kód, testy (`tests/InvisiblePlayer.Core.Tests`, 114 testů), README
> Otevřené problémy: `TODO.md` · Vyřešené: `RESOLVED.md` · Původní review: `CODE_REVIEW_2026-08-15.md`

**Před KAŽDÝM commitem** projdi položky se stavem DONE / DEGRADED a ověř, že je změna
nerozbila. Sloupec **Tests** říká, čím se to dá ověřit — když u položky žádný test není,
je to samo o sobě nález do `TODO.md`.

| Stav | Význam |
|---|---|
| ✅ DONE | Implementováno a funkční |
| ⚠️ DEGRADED | Funguje s omezením / známým problémem |
| 🚧 WIP | Rozpracováno |
| 📋 PLANNED | Plánováno, neimplementováno |
| ❌ REMOVED | Záměrně odstraněno (jen se souhlasem vývojáře) |

---

## Syntéza zvuku (InvisiblePlayer.Core)

#### F-001: Polyfonní generování tónů z MIDI not
- **Stav:** ✅ DONE
- **Popis:** `ToneEngine` přijímá NoteOn/NoteOff, drží seznam znějících hlasů a míchá
  je do jednoho mono streamu. Opakované NoteOn téže noty hlas retrigguje, nevytváří druhý.
- **Soubory:** `InvisiblePlayer.Core/02_Synthesis/ToneEngine.cs`
- **Tests:** `ToneEngineTests`, `ToneEngineBaselineTests`
- **Přidáno:** před review — regresní testy 2026-08-15 (`088caaf`)

#### F-002: Kompenzace hlasitosti podle počtu hlasů
- **Stav:** ✅ DONE
- **Popis:** Mix se dělí `√N` podle počtu znějících hlasů — N nekorelovaných zdrojů se
  energeticky sčítá jako √N, ne lineárně. Předchází ořezu dřív, než nastane.
- **Soubory:** `02_Synthesis/ToneEngine.cs` (`PolyphonyCompensationExponent`)
- **Tests:** `ToneEngineBaselineTests.BASELINE_Akord_CEG`
- **Poznámka:** Vyžaduje, aby doznělé hlasy mizely ze seznamu (viz F-004) — jinak tlumí
  zbytečně.

#### F-003: Výběr zvukového generátoru podle presetu
- **Stav:** ✅ DONE
- **Popis:** `VoicePreset.Instrument` řídí, který generátor se pro notu vytvoří —
  varhany, klavír, cembalo, zvon. Preset se nastavuje přes `ToneEngine.CurrentPreset`.
- **Soubory:** `02_Synthesis/ToneEngine.cs` (`CreateVoice`), `04_Generators/*Voice.cs`
- **Tests:** `SynthVoiceTests.ToneEngine_RespektujePresetInstrument`,
  `SynthVoiceTests.ToneEngine_RuzneNastroje_ZniRuzne`
- **Přidáno:** 2026-08-15 — commit `04b3ca7` (nález S2)

#### F-004: ADSR obálka s doběhem do Idle
- **Stav:** ✅ DONE
- **Popis:** Attack / Decay / Sustain / Release. Obálka se `SustainLevel = 0` (cembalo,
  zvon) po doznění decay skončí v Idle, takže hlas jde uklidit ze seznamu.
- **Soubory:** `04_Generators/05_AdsrEnvelope.cs`
- **Tests:** `AdsrEnvelopeTests`, `SynthVoiceTests.ToneEngine_DoznelaCembalovaNota_NetlumiDalsi`
- **Přidáno:** 2026-08-15 — commit `b39d219` (nález S5)

#### F-005: Fyzikální modely nástrojů
- **Stav:** ✅ DONE
- **Popis:** Varhanní píšťala (alikvóty + chiff), klavír (inharmonicita
  `f_n = n·f0·√(1+B·n²)` + úder kladívka), cembalo (nezávislé doznívání harmonických +
  brnknutí), zvon (partiály Hum/Prime/Tierce/Kvinta/Nominál + rozladěný nominál pro
  beating).
- **Soubory:** `04_Generators/07_PianoVoice.cs`, `08_OrganVoice.cs`, `09_CembaloVoice.cs`,
  `10_BellVoice.cs`
- **Tests:** `SynthVoiceTests.VsechnyHlasy_ProduujiKonecneVzorky`
- **Poznámka:** Testy ověřují jen konečnost vzorků, ne věrnost modelu — ta je věcí sluchu.

#### F-006: Reprodukovatelný výstup pro regresní testování
- **Stav:** ✅ DONE
- **Popis:** `ToneEngine(…, noiseSeed:)` a `NoiseGenerator(int? seed)` umožňují pevné
  semínko šumu. Bez něj je výstup nedeterministický (chiff/úder jsou šum) a regresi
  zvuku nelze prokázat.
- **Soubory:** `04_Generators/04_NoiseGenerator.cs`, `02_Synthesis/ToneEngine.cs`
- **Tests:** `ToneEngineBaselineTests.SeSeminkem_JeVystupReprodukovatelny`,
  `…BezSeminka_JeVystupNahodny`
- **Přidáno:** 2026-08-15 — commit `04b3ca7`

#### F-007: Historické temperatury ladění
- **Stav:** ⚠️ DEGRADED
- **Popis:** `Temperament` posouvá ladění jednotlivých tónů o zadaný počet centů.
  Obsahuje temperaturu „Georg Kratky I'" (Melzerovy varhany 1932).
- **Soubory:** `02_Synthesis/Temperament.cs`
- **Tests:** `TemperamentTests`
- **Omezení:** `MelzerGeorgKratkyI` se nikde nepoužívá — chybí způsob, jak temperaturu
  za běhu vybrat. Výchozí je vždy rovnoměrná.

#### F-008: Filtry (dolní propust, pásmová propust)
- **Stav:** ✅ DONE
- **Popis:** Biquad podle RBJ Audio EQ Cookbooku. Dolní propust v Direct Form II
  Transposed. Oba filtry klampují vstupní parametry, takže neplatná hodnota nevyrobí NaN.
- **Soubory:** `05_Filters/01_LowPassFilter.cs`, `02_BandPassFilter.cs`
- **Tests:** `LowPassFilterTests`, `BandPassFilterTests`
- **Poznámka:** `LowPassFilter` se zatím nikde nepoužívá — je připravený, ne zapojený.

#### F-009: Detekce ořezu výstupu
- **Stav:** ✅ DONE
- **Popis:** `ReadClipDetected()` hlásí, jestli od posledního čtení došlo k ořezu
  alespoň jednoho vzorku. Příznak se drží (latching), protože ořez trvá jediný vzorek,
  zatímco VU metr čte po desítkách ms.
- **Soubory:** `02_Synthesis/ToneEngine.cs`, `06_Output/Audio.cs`
- **Tests:** `ToneEngineTests.ReadClipDetected_*`
- **Přidáno:** 2026-08-15 — commit `529b29a` (nález B2)
- **Poznámka:** Zatím to nikdo nekonzumuje — VU metr ve `VgaEngine` clip indikátor nemá.

---

## Vstup (InvisiblePlayer.Core)

#### F-010: Živý MIDI vstup
- **Stav:** ⚠️ DEGRADED
- **Popis:** `InputManager.StartLiveDevice()` se připojí k MIDI zařízení podle části
  názvu a přeposílá NoteOn/NoteOff.
- **Soubory:** `01_Input/InputManager.cs`
- **Tests:** ŽÁDNÉ — vyžaduje fyzické MIDI zařízení (viz `TODO.md` P11)
- **Omezení:** Netestováno. Název zařízení je natvrdo `"USB MIDI"` v `App.OnStartup`.

#### F-011: Přehrávání MIDI souboru
- **Stav:** ⚠️ DEGRADED
- **Popis:** `PlayMidiFileAsync()` přehraje soubor přes DryWetMidi a emituje události
  do stejného kanálu jako živý vstup. Chyba čtení se hlásí událostí `OnPlaybackError`.
- **Soubory:** `01_Input/InputManager.cs`
- **Tests:** ŽÁDNÉ
- **Omezení:** MIDI kanál se v `ToneEngine` zahazuje — všechny stopy hrají jedním
  rejstříkem (souvisí s `TODO.md` S8).

---

## Analýza zvuku (InvisiblePlayer.Core + .Analyzer)

#### F-012: Spektrální analýza a popis partiálů
- **Stav:** ✅ DONE
- **Popis:** Detekce lokálních píků ve spektru a jejich popis ve třech režimech —
  varhany (harmonické i subharmonické poměry), zvon (inharmonická řada), šum
  (střed, jakost Q, mezní kmitočty pro předpis filtru).
- **Soubory:** `07_Analysis/SpectrumAnalysis.cs`
- **Tests:** `SpectrumAnalysisTests`
- **Přidáno:** 2026-08-15 — commit `e127d57` (nález P11, vytaženo z code-behind)

#### F-013: Převod FFT magnitudy na dBFS s kompenzací okna
- **Stav:** ✅ DONE
- **Popis:** `MagnitudeToDbFs()` kompenzuje koherentní zisk okna. Bez toho vycházela
  amplituda o ~6 dB nižší.
- **Soubory:** `07_Analysis/SpectrumAnalysis.cs`
- **Tests:** `SpectrumAnalysisTests.MagnitudeToDbFs_*`
- **Přidáno:** 2026-08-15 — commit `e127d57` (nález S9)

#### F-014: Živý spektrální analyzátor s grafem
- **Stav:** ⚠️ DEGRADED
- **Popis:** WPF okno se záznamem z mikrofonu, FFT (8k–262k), logaritmickým grafem
  (ScottPlot), VU metrem a funkcí SNAP pro zmrazení okamžiku.
- **Soubory:** `InvisiblePlayer.Analyzer/MainWindow.xaml{,.cs}`
- **Tests:** ŽÁDNÉ pro UI vrstvu (čistá analytika je v F-012/F-013)
- **Omezení:** Nelze spustit ani otestovat na Linuxu. Číselný výstup používá aktuální
  kulturu (v ČR desetinná čárka), takže hodnoty nejdou přímo přepsat do C# presetu.

---

## Přehrávač (InvisiblePlayer.UI.Windows)

#### F-015: Navigace v playlistu podle adresáře
- **Stav:** ✅ DONE
- **Popis:** Načte podporované soubory ze složky, umí další/předchozí a přechod
  do sousední složky (včetně přeskočení prázdné nebo nečitelné). Na konci playlistu
  vrací `null`, takže volající pozná „už není kam jít".
- **Soubory:** `InvisiblePlayer.Core/DirectoryNavigator.cs`
- **Tests:** `DirectoryNavigatorTests`
- **Poznámka:** Vyhledání výchozího souboru je case-insensitive kvůli Windows —
  **na Linuxu to nelze otestovat** (`File.Exists` selže dřív).

#### F-016: VGA konzolové rozhraní
- **Stav:** ⚠️ DEGRADED
- **Popis:** Textový dashboard v konzoli: název souboru, čas, hlasitost, VU metry,
  ovládání klávesnicí (mezerník, šipky, PageUp/Down, Esc).
- **Soubory:** `InvisiblePlayer.UI.Windows/VgaEngine.cs`
- **Tests:** ŽÁDNÉ — vyžaduje Windows konzoli
- **Omezení:** Obsluha myši (kolečko, kliky) je pravděpodobně nedosažitelná — viz
  `TODO.md` M5. Mutování MIDI kanálů neexistuje (S8).

#### F-017: Přehrávání audia s VU metrem
- **Stav:** ⚠️ DEGRADED
- **Popis:** WASAPI výstup přes NAudio, měření špiček před fadeem, seek, hlasitost.
- **Soubory:** `InvisiblePlayer.UI.Windows/AudioPlayer.cs`
- **Tests:** ŽÁDNÉ — vyžaduje zvukovou kartu
- **Omezení:** Netestováno.

#### F-018: Přehrávání videa přes LibVLC
- **Stav:** ⚠️ DEGRADED
- **Popis:** Celoobrazovkové WPF okno s LibVLC, ovládání klávesnicí a kolečkem myši,
  přepínání fullscreen, přeskakování souborů.
- **Soubory:** `InvisiblePlayer.UI.Windows/MainWindow.xaml{,.cs}`, `VideoPlayer.cs`
- **Tests:** ŽÁDNÉ — vyžaduje Windows a LibVLC
- **Omezení:** `VideoPlayer.cs` (MediaElement) je mrtvý kód — nahradil ho LibVLC.

#### F-019: Rozpoznání typu média a spuštění správného přehrávače
- **Stav:** ✅ DONE
- **Popis:** `MediaLauncher` podle přípony pustí video (LibVLC okno), MIDI
  (InputManager + VGA konzole) nebo audio (VGA konzole).
- **Soubory:** `InvisiblePlayer.UI.Windows/App.xaml.cs`
- **Tests:** ŽÁDNÉ

---

## Hardware (InvisiblePlayer.Raspi)

#### F-020: Ovladač I²C expandéru MCP23016
- **Stav:** 📋 PLANNED
- **Popis:** Čtení 16 kontaktů (klávesy, sklopky) a řízení LED podsvícení.
  Adresy registrů odpovídají MCP23016 (pozor na záměnu s MCP23017).
- **Soubory:** `InvisiblePlayer.Raspi/Hardware/Mcp23016Controller.cs`
- **Tests:** ŽÁDNÉ — vyžaduje fyzický hardware
- **Omezení:** `OrganHardwareManager.UpdateHardwareState` má prázdná těla cyklů,
  projekt nemá vstupní bod. Viz `TODO.md` P7.

---

## Infrastruktura

#### F-021: Testovací sada jádra
- **Stav:** ✅ DONE
- **Popis:** 114 xUnit testů pro `InvisiblePlayer.Core`. Zahrnuje charakterizační
  baseline zvuku, která umožňuje refaktorovat DSP s důkazem, že se výstup nezměnil.
- **Soubory:** `tests/InvisiblePlayer.Core.Tests/`
- **Tests:** sám sebou
- **Přidáno:** 2026-08-15 — commity `088caaf`, `fb64758`, `04b3ca7`, `e127d57`

#### F-022: Jednotná konfigurace překladu
- **Stav:** ✅ DONE
- **Popis:** `Directory.Build.props` — překladačová varování (CS*) jsou chyby,
  analyzátorová (CA*) zůstávají varováními. `EnableWindowsTargeting` umožňuje přeložit
  WPF projekty i na Linuxu a v CI.
- **Soubory:** `Directory.Build.props`, `.editorconfig`
- **Tests:** ověřuje se každým buildem
- **Přidáno:** 2026-08-15 — commit `088caaf`
