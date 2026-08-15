using System.Globalization;
using InvisiblePlayer.Core.Analysis;

namespace InvisiblePlayer.Core.Tests;

/// <summary>
/// P11 + S9. Analytika byla uvězněná v code-behind WPF okna a nešla testovat.
/// Po vytažení do Core ji lze konfrontovat se synteticky vyrobeným spektrem,
/// u kterého správnou odpověď ZNÁME.
/// </summary>
public class SpectrumAnalysisTests
{
    /// <summary>
    /// Naformátuje číslo stejně, jako to udělá SpectrumAnalysis - tedy podle
    /// AKTUÁLNÍ kultury. Bez toho by testy prošly jen tam, kde je desetinná
    /// čárka stejná jako na stroji autora (v ČR ",", v invariantní kultuře ".").
    /// Kulturně závislý výstup je záměr: je to text pro uživatele.
    /// </summary>
    private static string Num(double value, string format)
        => value.ToString(format, CultureInfo.CurrentCulture);

    /// <summary>Spektrum s píky na zadaných frekvencích (v dB), jinak podlaha.</summary>
    private static (double[] Freqs, double[] Db) MakeSpectrum(
        (double Hz, double Db)[] peaks, int bins = 1024, double stepHz = 10.0, double floorDb = -100.0)
    {
        var freqs = new double[bins];
        var db = new double[bins];
        for (int i = 0; i < bins; i++) { freqs[i] = i * stepHz; db[i] = floorDb; }

        foreach (var (hz, level) in peaks)
        {
            int idx = (int)Math.Round(hz / stepHz);
            if (idx > 1 && idx < bins - 2) db[idx] = level;
        }
        return (freqs, db);
    }

    // ---------------------------------------------------------------- S9 ----

    /// <summary>
    /// Sinusovka o amplitudě 1,0 musí po Hannově okně vyjít jako 0 dBFS.
    /// Před opravou S9 vycházela o ~6 dB níž, protože se nekompenzoval
    /// koherentní zisk okna.
    /// </summary>
    [Fact]
    public void MagnitudeToDbFs_PlnaAmplitudaSHannovymOknem_DavaNulaDbFs()
    {
        const int n = 4096;

        // FFT sinusovky o amplitudě A po okně s koherentním ziskem g dá v přihrádce
        // magnitudu A * g * n / 2. Pro A = 1 a Hann (g = 0,5) tedy n/4.
        double magnitude = n / 4.0;

        double db = SpectrumAnalysis.MagnitudeToDbFs(magnitude, n, SpectrumAnalysis.HannCoherentGain);

        Assert.Equal(0.0, db, precision: 6);
    }

    [Fact]
    public void MagnitudeToDbFs_BezKompenzaceOkna_JeVysledekO6dbNiz()
    {
        // Dokládá velikost chyby, kterou S9 opravilo: 20*log10(0.5) = -6,02 dB.
        const int n = 4096;
        double magnitude = n / 4.0;

        double correct = SpectrumAnalysis.MagnitudeToDbFs(magnitude, n, SpectrumAnalysis.HannCoherentGain);
        double uncompensated = SpectrumAnalysis.MagnitudeToDbFs(magnitude, n, SpectrumAnalysis.RectangularCoherentGain);

        Assert.Equal(-6.0206, uncompensated - correct, precision: 3);
    }

    [Fact]
    public void MagnitudeToDbFs_PolovicniAmplituda_DavaMinus6dB()
    {
        const int n = 4096;
        double db = SpectrumAnalysis.MagnitudeToDbFs(n / 8.0, n, SpectrumAnalysis.HannCoherentGain);
        Assert.Equal(-6.0206, db, precision: 3);
    }

    [Fact]
    public void MagnitudeToDbFs_NulovaMagnituda_VraciPodlahu()
    {
        Assert.Equal(-100.0, SpectrumAnalysis.MagnitudeToDbFs(0.0, 4096), precision: 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MagnitudeToDbFs_NeplatnyFftSize_Vyhodi(int fftSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpectrumAnalysis.MagnitudeToDbFs(1.0, fftSize));
    }

    // --------------------------------------------------------- FindPeaks ----

    [Fact]
    public void FindPeaks_NajdeVsechnyPikyNadPrahem()
    {
        var (freqs, db) = MakeSpectrum([(100, -10), (200, -20), (300, -30)]);

        var peaks = SpectrumAnalysis.FindPeaks(freqs, db, minDbThreshold: -75.0);

        Assert.Equal(3, peaks.Count);
        Assert.Contains(peaks, p => Math.Abs(p.FrequencyHz - 100) < 1e-9 && Math.Abs(p.Db + 10) < 1e-9);
    }

    [Fact]
    public void FindPeaks_IgnorujePikyPodPrahem()
    {
        var (freqs, db) = MakeSpectrum([(100, -10), (200, -80)]);

        var peaks = SpectrumAnalysis.FindPeaks(freqs, db, minDbThreshold: -75.0);

        Assert.Single(peaks);
        Assert.Equal(100, peaks[0].FrequencyHz);
    }

    [Fact]
    public void FindPeaks_IgnorujeBrumPodDolniHranici()
    {
        // Síťový brum na 50 Hz nesmí být hlášen jako tón.
        var (freqs, db) = MakeSpectrum([(20, -5), (500, -15)]);

        var peaks = SpectrumAnalysis.FindPeaks(freqs, db, minDbThreshold: -75.0, ignoreBelowHz: 35.0);

        Assert.Single(peaks);
        Assert.Equal(500, peaks[0].FrequencyHz);
    }

    [Fact]
    public void FindPeaks_PrazdneSpektrum_VraciPrazdno()
    {
        var (freqs, db) = MakeSpectrum([]);
        Assert.Empty(SpectrumAnalysis.FindPeaks(freqs, db, -75.0));
    }

    // -------------------------------------------------------- popis --------

    [Fact]
    public void DescribeOrganPartials_NajdeDominantuAPomery()
    {
        // Harmonická řada 200 / 400 / 600 Hz, nejsilnější je základ.
        var (freqs, db) = MakeSpectrum([(200, -10), (400, -16), (600, -22)]);

        string text = SpectrumAnalysis.DescribeOrganPartials(freqs, db);

        Assert.Contains($"DOMINANTA = {Num(200.0, "F1")} Hz", text, StringComparison.Ordinal);
        Assert.Contains($"{Num(2.0, "F4")}x", text, StringComparison.Ordinal);   // 400 / 200
        Assert.Contains($"{Num(3.0, "F4")}x", text, StringComparison.Ordinal);   // 600 / 200
    }

    [Fact]
    public void DescribeOrganPartials_ZachytiSubharmonickou()
    {
        // Nejsilnější je 400 Hz, takže 200 Hz vyjde jako poměr 0,5x.
        var (freqs, db) = MakeSpectrum([(200, -20), (400, -10)]);

        string text = SpectrumAnalysis.DescribeOrganPartials(freqs, db);

        Assert.Contains($"DOMINANTA = {Num(400.0, "F1")} Hz", text, StringComparison.Ordinal);
        Assert.Contains($"{Num(0.5, "F4")}x", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeOrganPartials_TicheSpektrum_HlasiZeNicNenasel()
    {
        var (freqs, db) = MakeSpectrum([]);
        Assert.Contains("Žádný výrazný tón", SpectrumAnalysis.DescribeOrganPartials(freqs, db), StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeBellPartials_ZachytiNecelociselnePomery()
    {
        // Typické poměry zvonu: hum 0,5x, prime 1x, tercie ~1,2x, nominál 2x.
        var (freqs, db) = MakeSpectrum([(250, -20), (500, -12), (600, -18), (1000, -10)]);

        string text = SpectrumAnalysis.DescribeBellPartials(freqs, db);

        Assert.Contains($"Hlavní pík: {Num(1000.0, "F1")} Hz", text, StringComparison.Ordinal);
        Assert.Contains($"{Num(0.6, "F4")}x", text, StringComparison.Ordinal);   // 600 / 1000
    }

    [Fact]
    public void DescribeNoiseShape_UrciStredniKmitocet()
    {
        // "Kopec" se středem na 1000 Hz.
        var (freqs, db) = MakeSpectrum([]);
        for (int i = 0; i < freqs.Length; i++)
        {
            double d = Math.Abs(freqs[i] - 1000.0) / 200.0;
            db[i] = -10.0 - 20.0 * d;
        }

        string text = SpectrumAnalysis.DescribeNoiseShape(freqs, db);

        Assert.Contains($"Střed f0 = {Num(1000.0, "F0")} Hz", text, StringComparison.Ordinal);
        Assert.Contains("Jakost Q", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeNoiseShape_TicheSpektrum_HlasiZeNicNenasel()
    {
        var (freqs, db) = MakeSpectrum([]);
        Assert.Contains("Žádný výrazný šumový profil",
            SpectrumAnalysis.DescribeNoiseShape(freqs, db), StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeNoiseShape_KopecSahajiciNaKrajSpektra_NedavaNesmyslneQ()
    {
        // Monotónně klesající spektrum: pokles o 3 dB doprava existuje, doleva ne.
        // Dřív se v takovém případě vracel kmitočet vrcholu -> nulová šířka pásma
        // -> Q = f0 / 1 (uměle vysoké). Test hlídá, že výsledek zůstane konečný.
        var (freqs, db) = MakeSpectrum([]);
        for (int i = 0; i < freqs.Length; i++) db[i] = -10.0 - freqs[i] / 100.0;

        string text = SpectrumAnalysis.DescribeNoiseShape(freqs, db);

        Assert.Contains("Střed f0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("∞", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NaN", text, StringComparison.Ordinal);
    }
}
