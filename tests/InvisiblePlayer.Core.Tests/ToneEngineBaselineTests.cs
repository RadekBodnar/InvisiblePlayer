namespace InvisiblePlayer.Core.Tests;

using ToneEngine = InvisiblePlayer.Core.ToneEngine.ToneEngine;

/// <summary>
/// CHARAKTERIZAČNÍ TESTY — zachycují, jak engine zní DNES, ne jak by měl znít.
///
/// Účel: umožnit refaktor S2 (factory podle InstrumentType) s důkazem, že se zvuk
/// nezměnil. Hodnoty NEJSOU vypočítané z teorie, jsou ODEČTENÉ z běžícího kódu
/// (2026-08-15, commit před zavedením factory) a ověřené na dvou po sobě jdoucích
/// bězích.
///
/// PODMÍNKA, BEZ KTERÉ BY TENHLE SOUBOR NEMOHL EXISTOVAT: pevné semínko šumu.
/// NoiseGenerator dřív používal new Random() bez semínka, takže chiff byl pokaždé
/// jiný a dvojí spuštění téhož kódu dalo jiné vzorky (peak se lišil v řádu 1e-3).
/// Bez reprodukovatelnosti nelze regresi zvuku prokázat — jen poslouchat a doufat.
///
/// KDYŽ TENHLE SOUBOR ZAČNE PADAT:
///   - po zásahu, který zvuk měnit NEMĚL  -> regrese, opravit kód
///   - po zásahu, který zvuk měnit MĚL    -> přepočítat hodnoty a v commitu
///                                            vysvětlit, co a proč se změnilo
///
/// Neasertujeme na hash celého bufferu: Math.Sin se může mezi platformami lišit
/// v posledních bitech. Měříme veličiny odolné vůči takovému šumu, ale citlivé
/// na skutečnou změnu zvuku (jiný hlas, obálka, harmonické, kompenzace polyfonie).
/// </summary>
public class ToneEngineBaselineTests
{
    private const int SampleRate = 44100;

    /// <summary>Libovolná pevná hodnota; důležité je, že se nemění.</summary>
    private const int NoiseSeed = 12345;

    private static ToneEngine NewEngine() => new(SampleRate, temperament: null, noiseSeed: NoiseSeed);

    private static double[] Render(ToneEngine engine, int count)
    {
        var buffer = new double[count];
        for (int i = 0; i < count; i++) buffer[i] = engine.GenerateNextMixSample();
        return buffer;
    }

    private static (double Peak, double Rms, int ZeroCrossings) Describe(double[] s)
    {
        double peak = 0, sumSq = 0;
        int crossings = 0;
        for (int i = 0; i < s.Length; i++)
        {
            double a = Math.Abs(s[i]);
            if (a > peak) peak = a;
            sumSq += s[i] * s[i];
            if (i > 0 && Math.Sign(s[i]) != Math.Sign(s[i - 1]) && s[i] != 0) crossings++;
        }
        return (peak, Math.Sqrt(sumSq / s.Length), crossings);
    }

    /// <summary>
    /// Nejdůležitější test celého souboru: bez determinismu jsou všechny ostatní
    /// baseline hodnoty bezcenné.
    /// </summary>
    [Fact]
    public void SeSeminkem_JeVystupReprodukovatelny()
    {
        var a = NewEngine(); a.NoteOn(60);
        var b = NewEngine(); b.NoteOn(60);

        double[] first = Render(a, 4096);
        double[] second = Render(b, 4096);

        Assert.Equal(first, second);
    }

    [Fact]
    public void BezSeminka_JeVystupNahodny()
    {
        // Ověřuje, že výchozí (neseedované) chování zůstalo zachováno — pro běžné
        // přehrávání chceme šum, ne opakující se vzorec.
        var a = new ToneEngine(SampleRate); a.NoteOn(60);
        var b = new ToneEngine(SampleRate); b.NoteOn(60);

        Assert.NotEqual(Render(a, 4096), Render(b, 4096));
    }

    [Fact]
    public void BASELINE_JedenTon_C4()
    {
        var engine = NewEngine();
        engine.NoteOn(60);
        var (peak, rms, crossings) = Describe(Render(engine, 4096));

        Assert.Equal(0.673489, peak, precision: 6);
        Assert.Equal(0.295004, rms, precision: 6);
        Assert.Equal(48, crossings);
    }

    [Theory]
    [InlineData(0, 0.000117)]
    [InlineData(1, 0.000458)]
    [InlineData(100, -0.010126)]
    [InlineData(1000, -0.626249)]
    [InlineData(4095, 0.161713)]
    public void BASELINE_KonkretniVzorky_C4(int index, double expected)
    {
        // Nejcitlivější kontrola — odhalí posun fáze nebo změnu tvaru vlny,
        // které by se v peak/RMS ztratily.
        var engine = NewEngine();
        engine.NoteOn(60);

        Assert.Equal(expected, Render(engine, 4096)[index], precision: 6);
    }

    [Fact]
    public void BASELINE_Akord_CEG()
    {
        // Ověřuje i kompenzaci polyfonie 1/sqrt(N). Peak = 1.0 znamená, že akord
        // na špičkách naráží na Math.Clamp — což je stav, který ClipDetected hlásí.
        var engine = NewEngine();
        engine.NoteOn(60);
        engine.NoteOn(64);
        engine.NoteOn(67);
        var (peak, rms, crossings) = Describe(Render(engine, 4096));

        Assert.Equal(1.000000, peak, precision: 6);
        Assert.Equal(0.314304, rms, precision: 6);
        Assert.Equal(120, crossings);
    }

    [Fact]
    public void BASELINE_Dozneni_PoNoteOff()
    {
        var engine = NewEngine();
        engine.NoteOn(60);
        Render(engine, 2000);
        engine.NoteOff(60);

        double[] tail = Render(engine, 2000);
        var (peak, rms, _) = Describe(tail);

        Assert.Equal(0.661322, peak, precision: 6);
        Assert.Equal(0.152799, rms, precision: 6);

        // Release varhanní obálky je 0,03 s = 1323 vzorků -> po 1500 už ticho.
        Assert.All(tail[1500..], v => Assert.Equal(0.0, v, precision: 12));
    }
}
