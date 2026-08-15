# TODO — InvisiblePlayer

Pouze **otevřené** problémy. Vyřešené jsou v `RESOLVED.md` s datem a hashem commitu.
Zdroj: `CODE_REVIEW_2026-08-15.md`.

Legenda: 🔴 Blocker · 🟠 Major · 🟡 Medium · 🔵 Low

> **Stav ověření (2026-08-15):** .NET SDK 8.0.424 (`~/.dotnet`, bez sudo).
> Build: `dotnet build <projekt>.csproj --artifacts-path ~/build/InvisiblePlayer`
> Testy: `dotnet test tests/InvisiblePlayer.Core.Tests/…` — **114 testů, 0 selhání**.
> Hooky: `./scripts/install-hooks.sh` (na čerstvém klonu nutné ručně).
> WPF projekty se na Linuxu překládají (`EnableWindowsTargeting`), spustit je nelze.
> `.slnx` SDK 8 neumí (`MSB4068`) — stavět je nutné po projektech.

> ⚠️ **Co Linux ověřit NEMŮŽE — nutná kontrola na Windows:**
> - **M5** (`Console.KeyAvailable` vs myš) — vyžaduje Windows konzoli.
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
  **Confidence MEDIUM-HIGH — NEOVĚŘENO.**
  *Falzifikace (30 s): spustit, zatočit kolečkem nad konzolí, sledovat `Vol:`.*
  Oprava: nemíchat obě API — číst klávesnici i myš jedním `ReadConsoleInput`
  (P/Invoke deklarace už v souboru jsou).

## 🟡 Medium


- [ ] **S8** — `UI.Windows/VgaEngine.cs` — mutování MIDI kanálů neexistuje.
  Legenda už nelže (opraveno), ale `_channelMuted` se pořád nikde nezapisuje
  a `HandleInput` nemá `case` pro číslice.
  **Vyžaduje rozhodnutí:** implementovat? Znamená to filtrování podle kanálu
  v `ToneEngine` (dnes se `ConsoleInputEvent.Channel` zahazuje), tedy funkci,
  ne opravu.


## 🔵 Low

- [ ] **L7** — zbylá varování analyzátorů (nezastavují build, jsou reálná):
  `CA1051` ×6 (veřejná instanční pole, např. `SynthVoice.SampleRate`),
  `CA1816` ×3 (`Dispose` bez `GC.SuppressFinalize`), `CA1014` ×2,
  `CA1307` ×3 (v testech, `Assert.Contains` bez `StringComparison`).

## 🏗 Infrastruktura a proces

- [ ] **P3** 🔵 — migrace `net8.0` → `net10.0` (podpora .NET 8 končí 11/2026).
  Vyřešilo by i `.slnx` (SDK 8 formát neumí).
- [ ] **P5** 🔵 — commit messages: 12 z 18 původních commitů má zprávu `rr`/`e`/`d`/`ee`,
  jeden je prázdný. Historie je nepoužitelná pro `git bisect`.
- [ ] **P7** 🟡 — rozhodnout hranici Core/platforma: `Audio.cs` používá `WaveOutEvent`
  (Windows-only), ale `Core` cílí `net8.0` a má ho konzumovat i Raspi (ALSA).
  Souvisí: `Raspi` nemá vstupní bod a `OrganHardwareManager.UpdateHardwareState`
  má prázdná těla cyklů.
- [ ] **P8** 🔵 — `NU1701`: `ScottPlot.WPF` táhne tranzitivně `SkiaSharp.Views.WPF`
  bez `net8.0-windows` assetů (restore přes .NET Framework fallback). Zatím tlumeno
  v `Directory.Build.props`; prověřit při upgradu ScottPlotu.
- [ ] **P13** 🔵 — bez testů zůstávají `InputManager` (vyžaduje MIDI zařízení nebo
  testovací soubor), `AudioEngine` / `AudioPlayer` (vyžadují zvukovou kartu) a UI
  vrstva `Analyzeru`. Čistá analytika už testovatelná je — viz `SpectrumAnalysis`.
- [ ] **P12** 🔵 🆕 — analyzátor formátuje čísla podle aktuální kultury (v ČR
  desetinná čárka), ale slouží k odečítání hodnot přepisovaných do C# presetů,
  kde `0,5000` neprojde. Zvážit invariantní formátování numerických výstupů.
  **Produktové rozhodnutí, ne vada.**