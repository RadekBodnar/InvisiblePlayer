# InvisiblePlayer

Sada projektů okolo přehrávání a syntézy zvuku pro Windows, s výhledem na budoucí hardwarové
rozšíření (elektronické varhany na Raspberry Pi).

## Projekty v tomto řešení

- **InvisiblePlayer.UI.Windows** – přehrávač audio/video souborů (mp3, wav, flac, MIDI, video),
  napojení na MIDI-IN. Zatím ve fázi provizorního textového rozhraní.
- **InvisiblePlayer.Core** – SW syntezátor zvuku (oscilátory, obálky, filtry, hlasy nástrojů).
- **InvisiblePlayer.Analyzer** – SW analyzátor zvuku s 2D grafy (WPF).
- **InvisiblePlayer.Raspi** – plánovaná hardwarová větev (Raspberry Pi, HiFiBerry DAC8x,
  MIDI in/out, GPIO ovládání kláves).

## Stav projektu

⚠️ Projekt je ve **rané fázi vývoje**. Struktura i API se mohou často měnit, řada částí
je provizorní nebo nedokončená. Zpětná vazba a nápady vítány.

## Licence

Vlastní kód je licencován pod [MIT licencí](LICENSE).
Licence použitých knihoven třetích stran: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

