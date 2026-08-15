using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace InvisiblePlayer.Core.Analysis
{
    /// <summary>Jeden lokální vrchol ve spektru.</summary>
    public readonly record struct SpectralPeak(double FrequencyHz, double Db);

    /// <summary>
    /// Čistá analytická logika spektrálního analyzátoru.
    /// </summary>
    /// <remarks>
    /// VYTAŽENO Z CODE-BEHIND (nález P11): tyhle metody byly privátní v
    /// Analyzer/MainWindow.xaml.cs, tedy uvnitř WPF okna. Přitom nezávisejí
    /// na ničem z UI - berou double[] a vracejí text. V code-behind byly
    /// netestovatelné, protože Analyzer je net8.0-windows projekt a jeho instanci
    /// nelze v testech vytvořit.
    ///
    /// Zde jde o čistý net8.0 kód bez závislostí, takže se dá testovat proti
    /// synteticky vyrobenému spektru se ZNÁMOU odpovědí.
    /// </remarks>
    public static class SpectrumAnalysis
    {
        // OPRAVA P12: čísla se formátují INVARIANTNĚ, tedy s desetinnou TEČKOU.
        // Není to nedbalost vůči české lokalizaci - výstup analyzátoru slouží
        // k odečtení hodnot, které se ručně přepisují do VoicePreset v C# kódu.
        // "0,5000" by se do zdrojáku přepsat nedalo, "0.5000" ano.
        // Popisky a hlášky zůstávají česky; invariantní je jen ČÍSLO.

        /// <summary>
        /// Koherentní zisk Hannova okna. Okno signál v průměru zeslabí na polovinu,
        /// takže se jím musí naměřená amplituda vydělit - jinak vyjde o ~6 dB nižší.
        /// </summary>
        public const double HannCoherentGain = 0.5;

        /// <summary>Obdélníkové okno (žádné) - zisk 1,0.</summary>
        public const double RectangularCoherentGain = 1.0;

        /// <summary>Spodní hranice dynamiky. −100 dB odpovídá ose grafu do −90 dB.</summary>
        public const double DefaultFloorDb = -100.0;

        /// <summary>
        /// Převede magnitudu jedné FFT přihrádky na dBFS.
        /// </summary>
        /// <param name="magnitude">|X[k]| z FFT (konvence bez škálování při forward).</param>
        /// <param name="fftSize">Počet vzorků okna.</param>
        /// <param name="windowCoherentGain">
        /// Koherentní zisk použitého okna - viz <see cref="HannCoherentGain"/>.
        /// </param>
        /// <remarks>
        /// OPRAVA S9: původní výpočet zněl <c>mag = |X[k]| * 2 / n</c> a koherentní
        /// zisk okna neřešil. Amplituda sinusovky tak vycházela asi o 6 dB nižší,
        /// než ve skutečnosti byla. Relativních odečtů (rozdíl dvou píků) se to
        /// netýkalo - posun se vyrušil - ale absolutní údaj "DOMINANTA = … dBFS"
        /// byl špatně. U nástroje, jehož účelem je odměřovat amplitudy partiálů
        /// pro tvorbu presetů, to není kosmetika.
        /// </remarks>
        public static double MagnitudeToDbFs(
            double magnitude,
            int fftSize,
            double windowCoherentGain = HannCoherentGain,
            double floorDb = DefaultFloorDb)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fftSize);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowCoherentGain);

            // *2 = součet kladné a zrcadlové záporné frekvence, /n = normalizace FFT,
            // /gain = kompenzace útlumu okna.
            double amplitude = magnitude * 2.0 / (fftSize * windowCoherentGain);

            double floorAmplitude = Math.Pow(10.0, floorDb / 20.0);
            return 20.0 * Math.Log10(Math.Max(amplitude, floorAmplitude));
        }

        /// <summary>
        /// Najde lokální maxima spektra - bod vyšší než oba sousedi po obou stranách.
        /// </summary>
        /// <param name="ignoreBelowHz">
        /// Spodní hranice; síťový brum a stejnosměrná složka pod ní se ignorují.
        /// </param>
        public static IReadOnlyList<SpectralPeak> FindPeaks(
            double[] freqsHz,
            double[] db,
            double minDbThreshold,
            double ignoreBelowHz = 35.0)
        {
            ArgumentNullException.ThrowIfNull(freqsHz);
            ArgumentNullException.ThrowIfNull(db);

            var peaks = new List<SpectralPeak>();
            int n = Math.Min(freqsHz.Length, db.Length);

            for (int i = 2; i < n - 2; i++)
            {
                if (freqsHz[i] < ignoreBelowHz) continue;

                if (db[i] > minDbThreshold &&
                    db[i] > db[i - 1] && db[i] > db[i - 2] &&
                    db[i] > db[i + 1] && db[i] > db[i + 2])
                {
                    peaks.Add(new SpectralPeak(freqsHz[i], db[i]));
                }
            }

            return peaks;
        }

        /// <summary>
        /// VARHANY: píky seřazené podle síly, s poměrem vůči nejsilnější čáře.
        /// Poměr pod 1,0 znamená subharmonickou (nižší než dominanta).
        /// </summary>
        public static string DescribeOrganPartials(double[] freqsHz, double[] db, double minDb = -75.0)
        {
            var peaks = FindPeaks(freqsHz, db, minDb);
            if (peaks.Count == 0) return "[VARHANY] Žádný výrazný tón nenalezen (nízký signál).";

            var sorted = peaks.OrderByDescending(p => p.Db).ToList();
            var main = sorted[0];

            var lines = sorted.Take(12).Select(p => string.Format(
                CultureInfo.InvariantCulture,
                "{0:F1}Hz ({1:F4}x | {2:F1}dB)", p.FrequencyHz, p.FrequencyHz / main.FrequencyHz, p.Db - main.Db));

            return string.Format(CultureInfo.InvariantCulture,
                "[VARHANY] DOMINANTA = {0:F1} Hz ({1:F1} dBFS)\nTop Čáry (Hz | Násobek | Rel dB): {2}",
                main.FrequencyHz, main.Db, string.Join(" | ", lines));
        }

        /// <summary>
        /// ZVON: dominantní inharmonické čáry. U zvonu nejsou partiály celočíselné
        /// násobky základní frekvence, takže poměry jsou to podstatné.
        /// </summary>
        public static string DescribeBellPartials(double[] freqsHz, double[] db, double minDb = -70.0)
        {
            var peaks = FindPeaks(freqsHz, db, minDb);
            if (peaks.Count == 0) return "[ZVON] Žádný úder nenalezen.";

            var sorted = peaks.OrderByDescending(p => p.Db).ToList();
            var main = sorted[0];

            var lines = sorted.Take(10).Select(p => string.Format(
                CultureInfo.InvariantCulture,
                "{0:F1}Hz ({1:F4}x | {2:F1}dB)", p.FrequencyHz, p.FrequencyHz / main.FrequencyHz, p.Db));

            return string.Format(CultureInfo.InvariantCulture,
                "[ZVON] Hlavní pík: {0:F1} Hz\nInharmonická řada čár: {1}",
                main.FrequencyHz, string.Join(" | ", lines));
        }

        /// <summary>
        /// ŠUM / FUK: popis "kopce" - střední kmitočet, činitel jakosti a mezní
        /// kmitočty při poklesu o 20 dB. Výstup je předpis pro nastavení filtru.
        /// </summary>
        public static string DescribeNoiseShape(double[] freqsHz, double[] db, double minPeakDb = -75.0)
        {
            ArgumentNullException.ThrowIfNull(freqsHz);
            ArgumentNullException.ThrowIfNull(db);

            int n = Math.Min(freqsHz.Length, db.Length);
            int maxIdx = -1;
            double maxDb = double.NegativeInfinity;

            for (int i = 0; i < n; i++)
            {
                if (freqsHz[i] >= 50.0 && db[i] > maxDb) { maxDb = db[i]; maxIdx = i; }
            }

            if (maxIdx < 0 || maxDb < minPeakDb)
                return "[ŠUM] Žádný výrazný šumový profil nenalezen.";

            double f0 = freqsHz[maxIdx];

            double fLow3 = ScanForDrop(freqsHz, db, maxIdx, maxDb - 3.0, forward: false);
            double fHigh3 = ScanForDrop(freqsHz, db, maxIdx, maxDb - 3.0, forward: true);
            double bandwidth = Math.Max(1.0, fHigh3 - fLow3);
            double q = f0 / bandwidth;

            double fLow20 = ScanForDrop(freqsHz, db, maxIdx, maxDb - 20.0, forward: false);
            double fHigh20 = ScanForDrop(freqsHz, db, maxIdx, maxDb - 20.0, forward: true);

            return string.Format(CultureInfo.InvariantCulture,
                "[ŠUM / FILTR] Předpis pro filtr bílého šumu:\n" +
                "1. PÁSMOVÁ PROPUST (BPF 2.řád, 20dB/dek): Střed f0 = {0:F0} Hz | Jakost Q ≈ {1:F2}\n" +
                "2. KASKÁDA (HP + LP 20dB/dek): HP Cutoff (-20dB) = {2:F0} Hz | LP Cutoff (-20dB) = {3:F0} Hz",
                f0, q, fLow20, fHigh20);
        }

        /// <summary>
        /// Hledá od vrcholu jedním směrem první přihrádku pod zadanou úrovní.
        /// Když ji nenajde (kopec sahá až na kraj spektra), vrátí krajní kmitočet -
        /// dřív se v takovém případě vracel kmitočet vrcholu, což dávalo nulovou
        /// šířku pásma a nesmyslně vysoké Q.
        /// </summary>
        private static double ScanForDrop(double[] freqsHz, double[] db, int fromIdx, double targetDb, bool forward)
        {
            int n = Math.Min(freqsHz.Length, db.Length);
            int step = forward ? 1 : -1;

            for (int i = fromIdx; i >= 0 && i < n; i += step)
            {
                if (db[i] <= targetDb) return freqsHz[i];
            }

            return forward ? freqsHz[n - 1] : freqsHz[0];
        }
    }
}
