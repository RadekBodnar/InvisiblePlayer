# TODO — InvisiblePlayer

Pouze **otevřené** problémy. Vyřešené jsou v `RESOLVED.md` s datem a hashem commitu.
Zdroj: `CODE_REVIEW_2026-08-15.md`.

Legenda: 🔴 Blocker · 🟠 Major · 🟡 Medium · 🔵 Low

> **Stav ověření (2026-08-15):** .NET SDK 8.0.424 (`~/.dotnet`, bez sudo).
> Build: `dotnet build <projekt>.csproj --artifacts-path ~/build/InvisiblePlayer`
> Testy: `dotnet test tests/InvisiblePlayer.Core.Tests/…` — **125 testů, 0 selhání**.
> Build je bez jediného varování (CS i CA).
> Hooky: `./scripts/install-hooks.sh` (na čerstvém klonu nutné ručně).
> WPF projekty se na Linuxu překládají (`EnableWindowsTargeting`), spustit je nelze.
> `.slnx` SDK 8 neumí (`MSB4068`) — stavět je nutné po projektech.

> ⚠️ **Co Linux ověřit NEMŮŽE — nutná kontrola na Windows:**
> - **M5** (`Console.KeyAvailable` vs myš) — **POTVRZENO** externím review proti
>   zdrojáku .NET runtime (`ConsolePal.Windows.cs`): `KeyAvailable` neklávesové
>   záznamy z fronty skutečně odebírá. Není to hypotéza. K opravě navíc patří
>   vypnout `ENABLE_QUICK_EDIT_MODE`, jinak myš přebere výběr textu.
> - **M8** (case-insensitive hledání souboru) — na case-sensitive FS selže
>   `File.Exists` dřív, než se k porovnání dojde. Test to detekuje za běhu
>   a degraduje se na kontrolu přesné shody, takže oprava zůstává neověřená.
> - **S7** (odolnost konzole vůči malému oknu) — opraveno, ale neodzkoušeno.
> - Cokoli běhového ve WPF projektech — překlad ano, spuštění ne.

---

## 🟠 Major

- [ ] **M5** — `UI.Windows/VgaEngine.cs:139` vs `150-172` — `Console.KeyAvailable`
  a `ReadConsoleInput` nad stejným vstupním handlem. `KeyAvailable` odebírá z fronty
  neklávesové záznamy, tedy i `MOUSE_EVENT` → obsluha kolečka a kliků na
  `[<<] [►] [▄] [>>]` je nejspíš nedosažitelný kód.
  **POTVRZENO** statickou analýzou zdrojáku .NET runtime (externí review).
  Oprava: nemíchat obě API — číst klávesnici i myš jedním `ReadConsoleInput`
  (P/Invoke deklarace už v souboru jsou).

## 🟡 Medium



## 🏗 Infrastruktura a proces

- [ ] **P3** 🟡 — migrace `net8.0` → `net10.0` (podpora .NET 8 končí **10. 11. 2026**;
  .NET 10 je aktivní LTS do 2028). Externí review doporučuje povýšit z „low" mezi
  nejbližší práci.
  Vyřešilo by i `.slnx` (SDK 8 formát neumí). **Vyžaduje doinstalovat SDK 10**
  — na stroji je jen 8.0.424 a disk je zaplněný z 92 %.
- [ ] **P5** 🔵 — commit messages: 12 z 18 commitů PŘED review má zprávu `rr`/`e`/`d`/`ee`,
  jeden je prázdný. **Nelze opravit** — je to historie upstreamu (`antonio-nv`),
  přepsat by šla jen za cenu rozejití s fork-network. Bereme jako daný stav;
  od `ee2b6fe` dál je historie čitelná.

- [ ] **P8** 🔵 — `NU1701`: `ScottPlot.WPF` táhne tranzitivně `SkiaSharp.Views.WPF`
  bez `net8.0-windows` assetů (restore přes .NET Framework fallback). Zatím tlumeno
  v `Directory.Build.props`; prověřit při upgradu ScottPlotu.
- [ ] **P13** 🔵 — bez testů zůstávají `InputManager` (vyžaduje MIDI zařízení nebo
  testovací soubor), `AudioEngine` / `AudioPlayer` (vyžadují zvukovou kartu) a UI
  vrstva `Analyzeru`. Čistá analytika už testovatelná je — viz `SpectrumAnalysis`.

---

## 🔎 Z externího review (Codex, 2026-08-15) — zbývá

- [ ] **X1** 🟠 — **`VgaEngine` neumí přepnout typ média.** PageUp/PageDown umí
  jen audio: MIDI nespustí, běžící MIDI nezastaví, na video nepřejde. Při přechodu
  z audia na MIDI může dál hrát staré audio. Pauza, seek a hlasitost navíc v MIDI
  režimu ovládají jen `_audioPlayer`, takže nemají účinek.
  **Návrh:** `PlaybackCoordinator` v Core — při každém přechodu nejdřív zastavit
  starou session, pak vytvořit správný backend; společné `Play/Pause/Stop/Seek/Volume`.
  Klasifikace už zdroj pravdy má (`MediaTypes`).

- [ ] **X2** 🟠 — **Zastavení MIDI zanechá visící tóny.** `StopFilePlayback` jen
  zruší playback; nesleduje aktivní noty a neposílá jim NoteOff. Zastavení uprostřed
  držené varhanní noty (Sustain = 1.0) hlas nechá znít.
  **Návrh:** evidovat aktivní `(channel, note)` a při stop/cancel poslat „all notes off".

- [ ] **X3** 🟡 — **Selhání zvukového zařízení shodí i to, co ho nepotřebuje.**
  `App.OnStartup` otevírá `WaveOutEvent` vždy, ještě než se rozhodne, jestli půjde
  o video. Výjimka tak může shodit i video přehrávač. Analyzer obdobně otevírá
  zařízení 0 bez ošetření.
  **Návrh:** inicializovat podle režimu, chybu ukázat uživateli.

- [ ] **X4** 🟡 — **`VgaEngine.Run` blokuje WPF dispatcher.** Volá se synchronně
  z UI vlákna, takže `Dispatcher.InvokeAsync` (např. hláška o chybě MIDI) se nemá
  kdy zobrazit, dokud konzolová smyčka neskončí.

- [ ] **X5** 🟡 — **`ToneEngine.GenerateNextMixSample()` bere zámek na každý vzorek**
  (44 100×/s). Lepší: `Render(Span<float>)` — jednou za buffer vyprázdnit frontu MIDI
  příkazů a pak renderovat bez zámku. Souvisí s tím, že audio callback nesmí blokovat.

- [ ] **X6** 🟡 — **Chybí velocity, sustain pedál (CC64) a pitch bend.**
  `ToneEngine.NoteOn` velocity ani nepřijímá, takže klavír a cembalo hrají bez dynamiky.

- [ ] **X7** 🔵 — **Seedované hlasy sdílejí jedno semínko**, takže v deterministickém
  režimu mají všechny současně znějící hlasy identický, korelovaný šum. Odvozovat
  unikátní semínko z jednoho master RNG.

- [ ] **X8** 🔵 — **`ReadClipDetected()` a `ReadPeak()` dělají neatomické read+clear.**
  Událost se může mezi čtením a nulováním ztratit. `Interlocked.Exchange` (u `bool`
  přes `int`).

- [ ] **X9** 🔵 — **`LowPassFilter.SetSampleRate`** nevaliduje nulu/zápor a po změně
  vzorkovací frekvence znovu neomezí stávající cutoff vůči Nyquistovi.

- [ ] **X10** 🔵 — **Chybí CI.** Lokální hook je opt-in a ověřuje pracovní strom,
  ne přesně obsah indexu (částečně nastagovanou změnu tedy testuje špatně).

- [ ] **X11** 🔵 — **xUnit v2 je deprecated**, tranzitivně přináší dvě „high" položky
  do `dotnet list package --vulnerable` (System.Net.Http, System.Text.RegularExpressions).
  Jde o testovací graf, postižené DLL se do výstupu nekopírují — produktové riziko malé,
  audit ale zůstává červený. Zvážit migraci na xUnit v3.

- [ ] **X12** 🔵 — **`VgaEngine` (523 ř.) a analyzer code-behind (419 ř.)** by měly mít
  stavový automat přehrávání a koordinátor jako čisté třídy v Core, aby šly testovat
  bez Windows.

- [ ] **X13** 🔵 — **`Raspi`:** pokud se větev obnoví, `OrganHardwareManager` musí
  uvolňovat expandéry a `ReadKeyInputs()` nemůže tvrdit, že čte 16 vstupů, když je
  port 1 nakonfigurovaný jako výstup pro LED.
