# Code review — InvisiblePlayer

**Datum:** 2026-08-15 · **Commit:** `3ea67f2` · **Rozsah:** celé řešení (4 projekty, 36 `.cs`, ~3 290 řádků)
**Metoda:** statické čtení zdrojů podle `skills/code-quality` (checklist + klasifikace závažnosti)
a `skills/csharp-standards` (`references/code-review.md` — pořadí čtení, C#-specifický checklist).

> **Omezení platnosti — čti dřív, než z reportu uděláš závěr.**
> Na tomto stroji **není .NET SDK** (`dotnet` chybí), review tedy **neprošlo kompilací
> ani spuštěním**. Všechny nálezy jsou z četby kódu. U nálezů, kde by překladač nebo
> běh dal definitivní odpověď, je uvedena **falzifikační podmínka** — jak si to ověřit.
> Nálezy typu „chybí test" / „chybí konfigurace" jsou ověřené (soubor buď je, nebo není).

---

## Souhrn

| Závažnost | Počet | Význam |
|---|---|---|
| 🔴 Blocker | 2 | Slyšitelná/viditelná vada funkce, pád, ztráta dat |
| 🟠 Major | 8 | Reálná vada na běžné cestě, race, únik zdroje, měřicí chyba |
| 🟡 Medium | 9 | Latentní vada, mrtvý kód, nekonzistentní metadata, chybějící pojistky |
| 🔵 Low | 6 | Hygiena, styl, drobnosti |

**Celkové hodnocení.** Na projekt v deklarované „rané fázi" je jádro syntézy překvapivě
solidní: `ToneEngine` má korektní zamykání mezi MIDI a audio vláknem, biquad koeficienty
podle RBJ cookbooku jsou správně, fyzikální modely (inharmonicita klavíru, partiály zvonu,
√N kompenzace polyfonie) jsou promyšlené a dobře okomentované. Komentáře jsou nadprůměrné
— vysvětlují *proč*, ne *co*.

Slabá místa nejsou v DSP, ale **na hranicích**: mezi vlákny (Analyzer), mezi presetem
a generátorem (`InstrumentType` se nikde nevyhodnocuje), a mezi tím, co komentář slibuje,
a co kód dělá. Nejzávažnější nález je banální copy-paste v `App.xaml.cs`, který zdvojuje
každou notu.

**Největší strukturální mezera: nula testů.** V řešení není testovací projekt. Přitom
`AudioMeter`, `Temperament`, `AdsrEnvelope`, `LowPassFilter`, `BandPassFilter`
a `DirectoryNavigator` jsou čisté, deterministické funkce — ideální kandidáti, kde by
test stál 10 řádků a chytil polovinu nálezů z této tabulky.

---

## 🔴 Blocker

### B1 — Každá MIDI nota se zpracuje dvakrát (copy-paste)

**`InvisiblePlayer.UI.Windows/App.xaml.cs:82-99`**

Celý dispatch blok je v handleru dvakrát — jednou před `Debug.WriteLine`, podruhé za ním:

```csharp
if (evt.Type == InputEventType.NoteOn && evt.Velocity > 0)
    _toneEngine?.NoteOn(evt.Note.Number);
else
    _toneEngine?.NoteOff(evt.Note.Number);

System.Diagnostics.Debug.WriteLine(...);

if (evt.Type == InputEventType.NoteOn && evt.Velocity > 0)   // ← duplikát řádků 84-90
    _toneEngine?.NoteOn(evt.Note.Number);
else
    _toneEngine?.NoteOff(evt.Note.Number);
```

**Následek — je slyšet.** Druhé `NoteOn` spadne v `ToneEngine.NoteOn` do retrigger větve
(`ToneEngine.cs:69-75`) → `existing.Voice.NoteOn()` → `AdsrEnvelope.TriggerGate(true)`
restartuje obálku do stavu `Attack` **a** `OrganVoice.NoteOn()` nastaví `_chiffEnvelope = 1.0`.
Chiff (zapraskání píšťaly při náběhu) tedy zazní dvakrát a náběhová obálka se uprostřed
restartuje. Projeví se to jako „dvojité cvaknutí" / nečistý nástup u každé zahrané noty.

**Oprava:** smazat řádky 95-98.

---

### B2 — `ClipDetected` prakticky nikdy neohlásí ořez

**`InvisiblePlayer.Core/02_ToneEngine/ToneEngine.cs:150`**

```csharp
ClipDetected = gained > 1.0 || gained < -1.0;
```

Přiřazuje se **při každém vzorku**, tj. 44 100× za sekundu. Konzumní strana
(`VgaEngine.Run`) čte stav jednou za 30 ms, tedy jednou na ~1 323 vzorků. Aby se ořez
zobrazil, musel by clipovat **právě ten poslední vzorek** před čtením — pravděpodobnost
u krátkého transientu (útok, chiff, fázová shoda) je ~1/1323.

Komentář na `ToneEngine.cs:42-44` přitom tvrdí, že příznak slouží jako podklad pro
„clip" indikátor a je „přesnější než jen sledovat dB hodnotu okem". Kód to nedělá —
je to přesně případ z checklistu `csharp-standards/references/code-review.md` §9:
*„Confident comments describing behaviour the code does not have."*

**Následek:** clip indikátor je falešně negativní. Uživatel v dobré víře přebudí výstup
a nedozví se to. Ironií je, že správný vzor je o 80 řádků výš ve stejném repu —
`AudioEngine.ReadPeak()` (`Audio.cs:84-89`) drží maximum a nuluje ho až při čtení.

**Oprava — latching se stejnou sémantikou jako `ReadPeak()`:**

```csharp
private bool _clipSinceLastRead;

// v GenerateNextMixSample:
if (gained > 1.0 || gained < -1.0) _clipSinceLastRead = true;

/// <summary>Byl od posledního volání oříznut alespoň jeden vzorek? Čtení příznak nuluje.</summary>
public bool ReadClipDetected()
{
    bool clipped = _clipSinceLastRead;
    _clipSinceLastRead = false;
    return clipped;
}
```

Volající (`Audio.cs:95` → `AudioEngine.ClipDetected`) se upraví na průchod na `ReadClipDetected()`.
Pozn.: `_clipSinceLastRead` se zapisuje z audio vlákna a čte z UI — u `bool` je čtení
i zápis atomický, ale kvůli viditelnosti mezi vlákny patří k poli `volatile`
(viz M6).

---

## 🟠 Major

### M1 — Analyzer: záměna bufferu za běhu → `IndexOutOfRangeException` v audio callbacku

**`InvisiblePlayer.Analyzer/MainWindow.xaml.cs:262-263` vs `322-329`**

UI vlákno při změně velikosti FFT přealokuje buffer:

```csharp
_sampleBuffer = new float[_fftSize];   // řádek 262
_bufferIndex = 0;                      // řádek 263
```

Audio vlákno (`OnAudioDataAvailable`) mezitím do stejného pole zapisuje:

```csharp
if (_bufferIndex < _sampleBuffer.Length)   // řádek 322 — čtení reference #1
{
    _sampleBuffer[_bufferIndex] = floatSample;   // řádek 324 — čtení reference #2
```

Mezi kontrolou a zápisem se reference může vyměnit. Přepnutí z **262 144 → 8 192**
(položka „Sub-Bass" → „Fuk") je přesně ten případ: `_bufferIndex` může být např. 100 000,
kontrola proběhne proti starému poli (délka 262 144), zápis do nového (délka 8 192)
→ `IndexOutOfRangeException` na vlákně NAudio. Neodchycená výjimka v callbacku ukončí
capture, v horším případě proces.

Stejný závod je mezi `_fftSize` (řádek 329) a `_sampleBuffer.Length` (řádek 322) —
mohou se dočasně rozejít.

**Oprava:** buffer neměnit z UI vlákna. Nastavit jen `_pendingFftSize` a přealokaci
provést **v audio vlákně** na hranici okna (tam, kde už `_bufferIndex` stejně nulujete,
řádek 331), nebo celý přístup k bufferu obalit `lock`em.

---

### M2 — Analyzer: blokující `Dispatcher.Invoke` z audio callbacku

**`InvisiblePlayer.Analyzer/MainWindow.xaml.cs:386` (a `346`)**

`ProcessFFT` běží synchronně v `OnAudioDataAvailable`, tedy na vlákně NAudio capture.
Uvnitř volá `Dispatcher.Invoke(...)`, což **blokuje** capture vlákno, dokud UI vlákno
lambdu nedokončí — a ta lambda dělá `WpfPlot1.Plot.Clear()`, `Add.Scatter(...)`
a `Refresh()` nad polem o `_fftSize/2` bodech (u 262 144 to je **131 072 bodů**).

**Následek:** vypadávající vstupní bloky (a tedy díry v analyzovaném signálu),
trhaná odezva; při souběhu s modálním dialogem nebo jiným `Invoke` reálné riziko
deadlocku.

**Oprava:** `Dispatcher.InvokeAsync(...)` (neblokující) místo `Invoke`. Vykreslení navíc
throttlovat — pole `_lastRenderTime` (řádek 26) je pro to zjevně určené, ale **nikde se
nepoužívá**; throttling byl zamýšlen a nedokončen.

**Falzifikace:** nastavit FFT na 262 144, sledovat, zda `WaveInEvent` hlásí vypadlé buffery,
nebo změřit dobu strávenou v `OnAudioDataAvailable` (`Stopwatch`) — pokud přesáhne délku
jednoho vstupního bloku, nález je potvrzen.

---

### M3 — Analyzer: `WaveInEvent` se nikdy nezastaví ani neuvolní

**`InvisiblePlayer.Analyzer/MainWindow.xaml.cs:18, 292-299`**

`_waveIn` je vytvořen v konstruktoru, ale ve třídě není `OnClosed`, `Dispose`, ani
`StopRecording()`. Nahrávací zařízení zůstane obsazené, callback běží dál nad oknem,
které se zavírá.

Porušuje checklist `code-quality` → *Correctness → „Resources properly closed/released"*
i `csharp-standards` → *Resources and lifetime*.

**Oprava:**

```csharp
protected override void OnClosed(EventArgs e)
{
    if (_waveIn != null)
    {
        _waveIn.DataAvailable -= OnAudioDataAvailable;
        _waveIn.StopRecording();
        _waveIn.Dispose();
        _waveIn = null;
    }
    base.OnClosed(e);
}
```

---

### M4 — Analyzer: výběr mikrofonu nic nedělá

**`InvisiblePlayer.Analyzer/MainWindow.xaml:23` + `MainWindow.xaml.cs:291`**

`ComboMicrophones` nemá v XAML žádný `SelectionChanged` handler (na rozdíl od
`ComboFftSize` na řádku 26). `StartAudioCapture()` přečte `SelectedIndex` **jednou**
při startu a `_waveIn != null` pak návrat ukončí (řádek 289). Změna vstupu za běhu
je bez efektu.

UI prvek, který vypadá funkčně a není — u měřicího nástroje to znamená, že uživatel
může měřit ze špatného vstupu a myslet si opak.

**Oprava:** handler `ComboMicrophones_SelectionChanged`, který zavolá stop + dispose +
`_waveIn = null` + `StartAudioCapture()`.

---

### M5 — VgaEngine: `Console.KeyAvailable` pravděpodobně zahazuje události myši

**`InvisiblePlayer.UI.Windows/VgaEngine.cs:139` vs `150-172`**

Smyčka kombinuje dvě API nad **stejným** vstupním handlem: .NET `Console.ReadKey`/
`KeyAvailable` (řádky 139-145) a nativní `PeekConsoleInput`/`ReadConsoleInput`
(řádky 150-153).

Implementace `Console.KeyAvailable` na Windows peekuje záznamy a každý, který **není**
key-down, z fronty **odebere** (`ReadConsoleInput`) a pokračuje dál — jinak by se na
neklávesovém záznamu zasekla. `MOUSE_EVENT` je přesně takový záznam. Protože
`while (Console.KeyAvailable)` se vyhodnotí na začátku každé iterace, události myši
jsou spotřebovány dřív, než se k nim dostane blok B.

**Následek:** kolečko myši (hlasitost) a klikání na `[<<] [►] [▄] [>>]` v dashboardu
nefunguje — kód na řádcích 150-172 je fakticky nedosažitelný.

**Confidence: MEDIUM-HIGH.** Mechanismus vychází ze znalosti implementace
`ConsolePal.Windows.KeyAvailable` v .NET runtime; bez SDK jsem to zde nemohl ověřit během.

**Falzifikace (30 sekund):** spustit, zatočit kolečkem myši nad konzolí. Pokud se
`Vol:` v hlavičce nemění, nález platí.

**Oprava:** nemíchat obě API. Buď vše přes `ReadConsoleInput` (a `KEY_EVENT` dekódovat
ručně), nebo klávesnici i myš číst jedním nativním čtením. První varianta je konzistentnější
s tím, že P/Invoke deklarace už v souboru jsou.

---

### M6 — Analyzer: sdílené `bool` příznaky mezi vlákny bez `volatile`

**`InvisiblePlayer.Analyzer/MainWindow.xaml.cs:27-29`**

`_isFrozen`, `_waitForSnap`, `_isMeasuringSnap` se zapisují z UI vlákna
(řádky 214-216, 433-435, 441-442) a čtou z audio vlákna (316, 334, 400).

Kritický je `_waitForSnap` — čte se **uvnitř těsné per-sample smyčky** (řádek 316).
To je učebnicový případ, kdy JIT smí hodnotu pole nacachovat do registru a zápis
z druhého vlákna nikdy neuvidět. Tlačítko SNAP by pak fungovalo „někdy".

**Oprava:** `private volatile bool _waitForSnap;` (a totéž pro zbylé dva).
Komentář na řádku 333 („Bezpečně zjišťujeme stav z naší C# proměnné") je v tomto
smyslu nepřesný — bezpečné je to vůči WPF affinity, ne vůči paměťovému modelu.

---

### M7 — `PlayMidiFileAsync`: fire-and-forget spolkne chybu poškozeného souboru

**`InvisiblePlayer.UI.Windows/App.xaml.cs:38` + `InvisiblePlayer.Core/01_Input/InputManager.cs:80-119`**

```csharp
_ = inputManager.PlayMidiFileAsync(filePath);   // App.xaml.cs:38
```

`MidiFile.Read(filePath)` (`InputManager.cs:90`) na poškozeném nebo nepodporovaném
souboru vyhodí. Task není nikde awaitován ani observován → výjimka zmizí, aplikace
otevře VGA konzoli a **tiše nic nehraje**. Uživatel nedostane žádnou informaci.

`csharp-standards` → *Async, bod 6*: „Do not fire-and-forget. `_ = DoWorkAsync();`
loses the exception."

**Oprava:** buď `try/catch` uvnitř `PlayMidiFileAsync` s událostí `OnError`, nebo
minimálně:

```csharp
_ = inputManager.PlayMidiFileAsync(filePath).ContinueWith(
        t => MessageBox.Show($"Nelze přehrát MIDI: {t.Exception?.GetBaseException().Message}"),
        TaskContinuationOptions.OnlyOnFaulted);
```

---

### M8 — `DirectoryNavigator`: `IndexOf` selže při jiné velikosti písmen v cestě

**`InvisiblePlayer.UI.Windows/DirectoryNavigator.cs:31`**

```csharp
_currentIndex = _playlist.IndexOf(initialFilePath);
```

`List<string>.IndexOf` porovnává **ordinálně, case-sensitive**. `_playlist` obsahuje
cesty tak, jak je vrátil `Directory.GetFiles` — tedy s velikostí písmen podle disku.
`initialFilePath` přichází z příkazové řádky přes `Path.GetFullPath`
(`App.xaml.cs:119`), který normalizuje oddělovače, ale **ne velikost písmen**.

Na Windows (case-insensitive FS) tedy stačí, aby uživatel/Průzkumník předal
`C:\hudba\SONG.MP3` proti souboru `Song.mp3` na disku → `IndexOf` vrátí `-1`,
`CurrentFile` je `null` a `StartPlayingCurrentFile()` (`MainWindow.xaml.cs:56`)
tiše nic neudělá. Video se neotevře a nikde není chyba.

**Oprava:**

```csharp
_currentIndex = _playlist.FindIndex(
    f => string.Equals(f, initialFilePath, StringComparison.OrdinalIgnoreCase));
```

---

## 🟡 Medium

### S1 — Presety `_300_Cembalo` a `_400_Bell` mají `Harmonics == null` → latentní NRE

**`InvisiblePlayer.Core/04_Generators/02_WaveType.cs:33`, `03_Tones/_300_*.cs`, `03_Tones/_400_*.cs`**

`VoicePreset.Harmonics` je deklarované jako **non-nullable** pole bez inicializátoru
a bez `required`:

```csharp
public (double FrequencyMultiplier, double Amplitude)[] Harmonics { get; set; }
```

Projekt má `<Nullable>enable</Nullable>`, takže překladač na to hlásí `CS8618` —
anotace lže. Presety `_300_Cembalo_RandallHopkirk` a `_400_Bell_Zikmund` `Harmonics`
nenastavují. Jakmile kdokoli předá takový preset do `OrganVoice`, konstruktor
(`08_OrganVoice.cs:21`) udělá `new double[_preset.Harmonics.Length]`
→ **NullReferenceException**.

Dnes to nepadá jen proto, že `ToneEngine` používá napevno jediný preset (viz S2).

**Oprava:** `required` člen (C# 11+) nebo výchozí hodnota:
```csharp
public (double FrequencyMultiplier, double Amplitude)[] Harmonics { get; set; } = [(1.0, 1.0)];
```

---

### S2 — `InstrumentType` se nikde nevyhodnocuje; polovina Core je mrtvý kód

**`InvisiblePlayer.Core/02_ToneEngine/ToneEngine.cs:78`**

```csharp
var voice = new OrganVoice(_001_Bombard16Preset.Preset, _sampleRate);
```

`ToneEngine` vytváří vždy `OrganVoice` s jediným, natvrdo zadrátovaným presetem.
Enum `InstrumentType` (`02_WaveType.cs:14`) se v presetech **nastavuje**, ale nikde
**nečte** — chybí dispatch `switch (preset.Instrument) → OrganVoice / PianoVoice /
CembaloVoice / BellVoice`.

Ověřeno grepem — bez jediné reference (mimo vlastní definici a komentáře):

| Typ | Stav |
|---|---|
| `WavetableOscillator` | mrtvý |
| `LowPassFilter` | mrtvý |
| `PianoVoice` | mrtvý |
| `CembaloVoice` | mrtvý |
| `BellVoice` | mrtvý |
| `RegisterNumbers` | mrtvý |
| `Temperament.MelzerGeorgKratkyI` | mrtvý (default temperatura = samé nuly) |
| `_085_Aeolus`, `_200_Piano_Petrof`, `_300_Cembalo`, `_400_Bell` | mrtvé |
| `OrganHardwareManager` | mrtvý (celý projekt Raspi nemá vstupní bod) |

To samo o sobě není chyba — u projektu v rané fázi je to připravená infrastruktura.
Je to ale **největší dluh v architektuře**: chybí jediný `switch`, který by ty tři
hotové generátory zpřístupnil, a bez něj nejde nic z toho otestovat ani slyšet.

**Doporučení:** factory metoda v `ToneEngine`:
```csharp
private static SynthVoice CreateVoice(VoicePreset preset, double sampleRate) => preset.Instrument switch
{
    InstrumentType.Piano   => new PianoVoice(sampleRate),
    InstrumentType.Cembalo => new CembaloVoice(sampleRate),
    InstrumentType.Bell    => new BellVoice(preset, sampleRate),
    _                      => new OrganVoice(preset, sampleRate),
};
```
Vyžaduje změnit `ActiveNote.Voice` z `OrganVoice` na `SynthVoice` (`ToneEngine.cs:13`) —
polymorfismus už v `SynthVoice` připravený je.

---

### S3 — Preset „Bombard 16'" se jmenuje Aeolus a nese cizí číslo; `ModType` se ignoruje

**`InvisiblePlayer.Core/03_Tones/_001_199_organ.cs:5-21`**

Třída se jmenuje `_001_Bombard16Preset`, komentář v `ToneEngine.cs:77` mluví
o „presetu Bombard 16'", ale data uvnitř říkají `Name = "Aeolus"`, `Number = 85`.
Číslo 85 přitom patří presetu `_085_Aeolus` o pár řádků níž — **dva různé presety
se hlásí ke stejnému číslu rejstříku** a jeden z nich ke špatnému jménu.

Podle `RegisterNumbers.cs:15-17` má být č. 85 zvukově zvonkohra (`BellVoice`),
zatímco `_001_Bombard16Preset` je vedený jako `InstrumentType.Organ` (default).

Navíc `ModType = ModulationType.AM` (řádek 18) **nemá žádný efekt** — `OrganVoice`
pole `ModType`/`ModSpeedHz`/`ModDepth` vůbec nečte; jediný generátor, který je
respektuje, je `BellVoice` (a jen pro `FM`). Preset tedy slibuje tremolo, které nezazní.

**Oprava:** srovnat jméno/číslo s realitou (buď přejmenovat třídu na `_085_...`,
nebo opravit `Name`/`Number`), a buď implementovat AM v `OrganVoice`, nebo `ModType`
z tohoto presetu odstranit.

---

### S4 — `BandPassFilter` nemá žádné pojistky na vstupní parametry

**`InvisiblePlayer.Core/05_Filters/02_BandPassFilter.cs:8-19`**

```csharp
double alpha = System.Math.Sin(w0) / (2.0 * q);
```

`q == 0` → dělení nulou → `alpha = Infinity` → koeficienty `NaN` → filtr už **navždy**
vrací `NaN`, které se přes `chiff` propaguje do mixu a otráví celý výstup (`NaN` přežije
i `Math.Clamp`). Podobně `centerFreqHz` nad Nyquistem dá nestabilní odezvu.

Sesterský `LowPassFilter` to dělá správně — `Math.Clamp` v `SetCutoff`
(`01_LowPassFilter.cs:32`) i `SetResonance` (řádek 39). Nekonzistence mezi dvěma
filtry ve stejné složce.

**Oprava:** stejné klampování jako v `LowPassFilter`:
```csharp
q = Math.Clamp(q, 0.1, 20.0);
centerFreqHz = Math.Clamp(centerFreqHz, 20.0, sampleRate * 0.45);
```

---

### S5 — Ztichlé hlasy zůstávají v seznamu a zbytečně tlumí mix

**`InvisiblePlayer.Core/04_Generators/06_SynthVoice.cs:12, 49` + `02_ToneEngine/ToneEngine.cs:116, 144`**

`IsFinished => HasStarted && !NoteEnvelope.IsActive`, přičemž
`IsActive => State != EnvelopeState.Idle`.

U hlasu se `SustainLevel = 0.0f` (`CembaloVoice`, `BellVoice`) obálka po doznění decay
skončí ve stavu **`Sustain` s úrovní 0**, ne v `Idle`. Hlas je tedy trvale tichý,
`GenerateSample` se hned na řádku 49 vrátí nulou — ale `IsFinished` je `false`,
takže se ze seznamu **neodstraní**.

Následek v `ToneEngine`: `voiceCount` (řádek 116) započítá i tyto němé hlasy
a kompenzace `1/√N` (řádek 144) zbytečně stáhne hlasitost skutečně znějících tónů.
Deset dohraných cembalových not utlumí jedenáctou o ~10 dB.

Dnes se to neprojeví, protože se `CembaloVoice`/`BellVoice` nepoužívají (S2) —
je to vada, která vybuchne až při zapojení factory.

**Oprava:** doplnit do `AdsrEnvelope` přechod `Sustain → Idle`, když
`SustainLevel <= 0`, nebo upravit `IsFinished`:
```csharp
public bool IsFinished => HasStarted && (!NoteEnvelope.IsActive
    || (NoteEnvelope.State == EnvelopeState.Sustain && NoteEnvelope.CurrentLevel <= 0f));
```

---

### S6 — VgaEngine: poslední skladba ve složce se přehrává donekonečna

**`InvisiblePlayer.UI.Windows/VgaEngine.cs:175-184`**

Na konci skladby se zavolá `_navigator.GetNextFile()`. Když už jsme na posledním
souboru a neexistuje sousední složka, `GetNextFile` (`DirectoryNavigator.cs:50`)
nastaví `_currentIndex = _playlist.Count - 1`, tedy **vrátí tentýž soubor**.
`UpdateFileTypeState()` ho znovu načte a spustí → smyčka.

Uživatelsky to vypadá jako „poslední skladba se zacyklila", což nikde není deklarované
chování. Buď to je záměr (pak patří do dokumentace a `README`), nebo má přehrávání
skončit.

**Oprava:** `GetNextFile()` ať vrátí `null`, když už není kam jít, a volající to ošetří.

---

### S7 — Hardcoded souřadnice konzole spadnou v malém okně

**`InvisiblePlayer.UI.Windows/VgaEngine.cs:398, 422` + `404-405`**

`Console.SetCursorPosition(0, 7)` vyhodí `ArgumentOutOfRangeException`, pokud má okno
méně než 8 řádků. Podobně `RenderBar(_leftDb, 80)` vypíše 80 znaků + rámování;
v užším okně se řádky zalomí a celý dashboard se rozpadne (a další `SetCursorPosition`
píše přes obsah).

Výjimka není nikde odchycená → pád aplikace při zmenšení konzole.

**Oprava:** ověřit `Console.WindowWidth`/`WindowHeight` před kreslením, šířku metru
odvodit z `Console.WindowWidth - 12`.

---

### S8 — VgaEngine slibuje ovládání kanálů, které neexistuje

**`InvisiblePlayer.UI.Windows/VgaEngine.cs:20, 425, 429`**

`RenderMidiStaffOnly` vypisuje „Staff Attenuation Keys [1-0]:" a stavy
`[Ch01:ON] … [Ch10[DRUM]:ON]`, ale `HandleInput` (řádky 273-328) nemá pro číslice
žádný `case`. Pole `_channelMuted` se nikdy nezapisuje — čte se jen na řádku 429.

UI tvrdí funkci, kterou kód nemá. (Řádek 445 to částečně přiznává slovem
„Placeholder", ale legenda kláves ne.)

**Oprava:** buď doplnit `case ConsoleKey.D1..D0`, nebo legendu označit jako
„(připravováno)".

---

### S9 — Analyzer: absolutní dBFS je systematicky posunuté o ~6 dB

**`InvisiblePlayer.Analyzer/MainWindow.xaml.cs:357-380`**

Signál se násobí Hannovým oknem (řádek 357), ale při normalizaci magnitudy
(řádek 379) se nekompenzuje **koherentní zisk okna** (Hann = 0,5):

```csharp
double mag = (buffer[i].Magnitude * 2.0) / n;      // chybí / 0.5
```

Amplituda sinusovky tak vyjde asi o **6 dB nižší**, než ve skutečnosti je.
Relativní odečty (`relDb = p.Db - mainPeak.Db` na řádku 84) jsou v pořádku —
posun se vyruší. Ale hodnota „DOMINANTA = … (−32,4 dBFS)" v `AnalyzeOrganSubAndHarmonics`
je absolutní údaj a je špatně.

U nástroje, jehož účel je odměřovat amplitudy partiálů pro tvorbu presetů, to není
kosmetika.

**Vedlejší nález:** práh na řádku 380 (`Math.Max(mag, 1e-4)`) usekává dynamiku na
−80 dB, zatímco osa grafu jde do −90 dB (řádek 283) a `AnalyzeNoiseShape` hledá pokles
o −20 dB, který na floor snadno narazí. Sjednotit floor s rozsahem osy.

**Oprava:** `double mag = (buffer[i].Magnitude * 2.0) / (n * 0.5);` (a floor na `1e-5` = −100 dB).

---

## 🔵 Low

| # | Soubor:řádek | Nález |
|---|---|---|
| L1 | `Core/04_Generators/08_OrganVoice.cs:44` | `double harmonicFreq = …` se nikde nepoužije (`CS0219`) — pozůstatek refaktoru; smazat. |
| L2 | `Core/04_Generators/08_OrganVoice.cs:2` | `using NAudio.SoundFont;` je nepoužitý a matoucí (soundfonty se nikde nepoužívají). |
| L3 | `Core/02_ToneEngine/Temperament.cs:8` | `public class Temperament` je v **globálním namespace** — jako jediný typ v repu. Znečišťuje jmenný prostor všech konzumentů. Přesunout do `InvisiblePlayer.Core.ToneEngine`. |
| L4 | `UI.Windows/MainWindow.xaml.cs:119-123` | Redundantní podmínky: `(e.Key == Key.Return && isAltPressed)` je pohlcené následujícím `(e.Key == Key.Return)`, stejně `(Key.F && isCtrlPressed)` vs `(Key.F)`. Obě proměnné `isAltPressed`/`isCtrlPressed` jsou tím fakticky nepoužité. Zjednodušit na `Key.Return \|\| Key.F \|\| (Key.L && Ctrl)`. |
| L5 | `UI.Windows/DirectoryNavigator.cs:28, 92, 106` | `OrderBy(f => f)` nad `string` používá **aktuální kulturu** (v češtině `ch` řadí za `h`). Pro stabilní pořadí playlistu `OrderBy(f => f, StringComparer.Ordinal)`. Viz `csharp-standards` → *LINQ traps*. |
| L6 | `Raspi/Hardware/Mcp23016Controller.cs:46` | `_i2cDevice?.Dispose()` — `_i2cDevice` je `readonly` non-nullable přiřazené v konstruktoru, `?.` je mrtvý. Chybí guard proti dvojímu `Dispose`. (Adresy registrů MCP23016 jsou jinak **správně** — GP0=0x00, IODIR0=0x06; pozor jen na záměnu s MCP23017, kde IODIRA=0x00.) |

---

## Architektura, procesy a nástroje

Tohle nejsou nálezy v kódu, ale v tom, co kolem kódu chybí. Řadím podle poměru
přínos/náklad.

### P1 — 🟠 Nula testů (P1: nízká pracnost, vysoký dopad)

V řešení není testovací projekt. Přitom tyto typy jsou **čisté funkce bez závislostí**
a otestovat je je otázka desítek řádků:

| Typ | Co testovat | Který nález by to chytilo |
|---|---|---|
| `AudioMeter` | `LinearToDecibels` na hranicích (0, 1e-7, 1.0), `RenderBar` šířka | — |
| `Temperament.CentOffset` | záporná MIDI čísla, pitch class wrap | — |
| `AdsrEnvelope` | doběh do `Idle` při `SustainLevel = 0` | **S5** |
| `BandPassFilter` | `q = 0`, `centerFreq > Nyquist` → není `NaN` | **S4** |
| `LowPassFilter` | impulsní odezva je konečná (stabilita) | — |
| `DirectoryNavigator` | jiná velikost písmen v cestě; konec playlistu | **M8, S6** |
| `ToneEngine` | `NoteOn` 2× → jen jeden hlas; `ClipDetected` po ořezu | **B1, B2** |
| `VoicePreset` presety | každý preset má `Harmonics != null` | **S1** |

Doporučení: `tests/InvisiblePlayer.Core.Tests` (xUnit). Test na `ToneEngine`, který
zavolá `NoteOn(60)` dvakrát a ověří, že chiff zazní jednou, by B1 chytil okamžitě.

**Charakterizační testy** (`code-quality` → *Characterization Tests*): u DSP kódu
zachytit současný výstup pro pevný vstup jako baseline — pak lze refaktorovat
(např. zapojení factory z S2) bez obav, že se změní zvuk.

### P2 — 🟡 Chybí `Directory.Build.props` a analyzátory

`TargetFramework`, `Nullable` a `ImplicitUsings` jsou zduplikované ve všech čtyřech
`.csproj`. Chybí `TreatWarningsAsErrors`, `AnalysisLevel`, `EnforceCodeStyleInBuild`.

To je přímý důvod, proč nálezy **S1** (`CS8618`) a **L1** (`CS0219`) v repu přežily —
překladač je nahlásil, ale nikdo je nemusel vidět.

```xml
<!-- Directory.Build.props v kořeni -->
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

### P3 — 🔵 .NET 8 → plánovat migraci na .NET 10

Všechny projekty cílí `net8.0`. .NET 8 je předchozí LTS, **podpora končí 11/2026**
(za ~15 měsíců). Aktuální LTS je .NET 10 (C# 14). Není to urgentní, ale patří to do
plánu — u `net8.0-windows` (WPF) je migrace obvykle jen změna TFM.

### P4 — 🟡 Chybí `CLAUDE.md`, `FEATURES.md`, `TODO.md` a pre-commit hook

Podle tvé globální konfigurace má každý projekt v kořeni `CLAUDE.md` (konvence, quirky),
`FEATURES.md` (registr funkcí s mapováním na testy — ochrana proti regresi),
`TODO.md` + `RESOLVED.md`. Ani jeden neexistuje. `TODO.md` zakládám tímto review
(viz vedlejší soubor); zbytek dává smysl založit až s testy (`FEATURES.md` bez sloupce
`Tests` je jen seznam přání).

Pre-commit hook (`.git/hooks/pre-commit`) neexistuje. Až bude testovací projekt, patří
sem **celá** sada testů, fail-closed — nikdy podmnožina podle změněných souborů.

### P5 — 🔵 Commit messages

18 commitů, z toho 12 se zprávou `rr`, `e`, `d`, `ee`, `dd`, `efd`, `f` a jeden prázdný.
Historie je tím pro `git bisect` i pro review nepoužitelná — což je poznat i na tomto
review: nemohl jsem využít krok „čtení git blame pro historický kontext", protože
zprávy nenesou žádnou informaci.

Není to formalita: až se objeví regrese ve zvuku, `git log --oneline` na 12 stejných
písmenech nepomůže.

---

## Co je v projektu dobře (a proč to sem patří)

Review, který jmenuje jen vady, dává zkreslený obraz priorit.

- **Zamykání v `ToneEngine`** je správné: `_lock` chrání seznam mezi MIDI a audio
  vláknem, `GenerateNextMixSample` iteruje pozpátku, aby mohl bezpečně mazat
  (`ToneEngine.cs:118`). To je přesně ta část, kde by chyba znamenala náhodné pády.
- **Retrigger místo druhé instance hlasu** (`ToneEngine.cs:69-75`) — komentář popisuje
  konkrétní pozorovaný jev (fázové rušení při rychlém opakování) a řeší ho správně.
  Takhle má vypadat komentář.
- **√N kompenzace polyfonie** místo tvrdého limiteru, s vysvětlením proč
  (`ToneEngine.cs:137-143`) — fyzikálně podložené a záměrně zvolené kvůli spektrální
  analýze bez zkreslení.
- **Biquad koeficienty** v obou filtrech odpovídají RBJ Audio EQ Cookbooku,
  `LowPassFilter` používá Direct Form II Transposed (numericky stabilnější).
- **Fyzikální modely** — inharmonicita `f_n = n·f0·√(1+B·n²)` u klavíru, partiály zvonu
  včetně rozladěného nominálu pro beating, nezávislé doznívání harmonických u cembala.
  To není generický „synth kód", to je odvedená rešerše.
- **`Note` jako `readonly struct`** — správná volba typu podle `csharp-standards`.
- **`AudioEngine.ReadPeak()`** — správně latching. Škoda, že `ClipDetected` ne (B2).

---

## Doporučené pořadí prací

1. **B1** — smazat 4 řádky v `App.xaml.cs` (5 minut, okamžitě slyšitelný rozdíl)
2. **B2** — latching `ClipDetected`
3. **M3, M4** — Analyzer: dispose `_waveIn`, zapojit výběr mikrofonu
4. **M1, M6** — Analyzer: odstranit závody (přealokace v audio vlákně, `volatile`)
5. **M2** — `InvokeAsync` + zapojit `_lastRenderTime` throttling
6. **P2** — `Directory.Build.props` s `TreatWarningsAsErrors` → vypadnou S1, L1, L2
7. **P1** — testovací projekt, začít od `AudioMeter` + `AdsrEnvelope` + `DirectoryNavigator`
8. **S2** — factory podle `InstrumentType` (oživí polovinu Core)
9. Zbytek podle tabulky výše

---

## Pre-mortem: tři nejpravděpodobnější důvody, proč tento projekt narazí

1. **Zvuk se rozbije a nikdo nepozná kdy.** DSP kód nemá testy ani baseline, historie
   commitů je nečitelná. První refaktor `ToneEngine` (nutný pro S2) může nepozorovaně
   změnit zvuk a nebude se k čemu vrátit. → **Charakterizační testy dřív než S2.**
2. **Analyzer začne měřit špatně a bude tomu věřit.** M4 (výběr vstupu nefunguje)
   + S9 (−6 dB) + M1 (pád při změně FFT) dohromady znamenají, že presety odvozené
   z měření mohou být systematicky vedle. → **Ověřit analyzátor proti známému signálu**
   (generovaný sinus o známé amplitudě) dřív, než z něj vzniknou další presety.
3. **Raspi větev se nikdy nespojí s Core.** `OrganHardwareManager.UpdateHardwareState`
   má prázdná těla cyklů, projekt nemá vstupní bod, `Core` cílí `net8.0` (dobře), ale
   `WaveOutEvent` v `Audio.cs` je Windows-only — na Raspberry Pi bude potřeba jiný
   výstup (ALSA). → **Rozhodnout hranici Core/platforma dřív, než přibude další kód.**
