using InvisiblePlayer.Core.Filters;

namespace InvisiblePlayer.Core.Tests;

/// <summary>
/// P10: poslední čistá třída v Core bez pokrytí.
/// Biquad filtr je nestabilní, pokud koeficienty vyjdou špatně — a nestabilita
/// se pozná až tím, že výstup exponenciálně roste (nebo skončí NaN). Testy jdou
/// proto po chování v čase, ne po hodnotách koeficientů.
/// </summary>
public class LowPassFilterTests
{
    [Fact]
    public void Process_ImpulsniOdezva_Konverguje()
    {
        // Stabilní IIR filtr má impulsní odezvu, která doznívá k nule.
        // Kdyby póly ležely vně jednotkové kružnice, hodnoty by rostly.
        var filter = new LowPassFilter(44100f);
        filter.SetCutoff(1000f);

        float first = filter.Process(1.0f);
        float maxTail = 0f;
        for (int i = 0; i < 10_000; i++)
        {
            float y = filter.Process(0.0f);
            if (i > 5000) maxTail = MathF.Max(maxTail, MathF.Abs(y));
            Assert.False(float.IsNaN(y) || float.IsInfinity(y), $"Divergence ve vzorku {i}");
        }

        Assert.True(first > 0f, "Impuls neprošel filtrem vůbec.");
        Assert.True(maxTail < 1e-3f, $"Odezva nedozněla, zbytek po 5000 vzorcích: {maxTail:E3}");
    }

    [Theory]
    [InlineData(0f)]            // pod dolní mezí
    [InlineData(-1000f)]
    [InlineData(1_000_000f)]    // hluboko nad Nyquistem
    public void SetCutoff_HodnotyMimoRozsah_ZustanouStabilni(float cutoff)
    {
        var filter = new LowPassFilter(44100f);
        filter.SetCutoff(cutoff);

        for (int i = 0; i < 5000; i++)
        {
            float y = filter.Process(MathF.Sin(i * 0.05f));
            Assert.False(float.IsNaN(y) || float.IsInfinity(y));
            Assert.InRange(y, -10f, 10f);
        }
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    [InlineData(1000f)]
    public void SetResonance_HodnotyMimoRozsah_ZustanouStabilni(float q)
    {
        var filter = new LowPassFilter(44100f);
        filter.SetResonance(q);
        filter.SetCutoff(2000f);

        for (int i = 0; i < 5000; i++)
        {
            float y = filter.Process(i % 100 == 0 ? 1.0f : 0.0f);
            Assert.False(float.IsNaN(y) || float.IsInfinity(y), $"NaN/Inf při Q={q}, vzorek {i}");
        }
    }

    [Fact]
    public void Process_PotlacujeVysokeFrekvence()
    {
        // Vlastní smysl dolní propusti: signál nad mezní frekvencí musí mít
        // po průchodu výrazně menší amplitudu než signál pod ní.
        static float RmsOfSine(float freqHz, float cutoffHz)
        {
            var f = new LowPassFilter(44100f);
            f.SetCutoff(cutoffHz);

            double sumSq = 0;
            const int n = 44100;
            for (int i = 0; i < n; i++)
            {
                float y = f.Process(MathF.Sin(2f * MathF.PI * freqHz * i / 44100f));
                if (i > 4410) sumSq += y * y;   // prvních 0,1 s vynecháme (přechodový děj)
            }
            return (float)Math.Sqrt(sumSq / (n - 4410));
        }

        float low = RmsOfSine(200f, cutoffHz: 1000f);
        float high = RmsOfSine(8000f, cutoffHz: 1000f);

        Assert.True(high < low * 0.1f,
            $"Dolní propust nepotlačuje výšky dost: 200 Hz RMS={low:F4}, 8 kHz RMS={high:F4}");
    }

    [Fact]
    public void Reset_VynulujeVnitrniStav()
    {
        var filter = new LowPassFilter(44100f);
        filter.SetCutoff(1000f);
        for (int i = 0; i < 100; i++) filter.Process(1.0f);

        filter.Reset();

        var fresh = new LowPassFilter(44100f);
        fresh.SetCutoff(1000f);
        Assert.Equal(fresh.Process(1.0f), filter.Process(1.0f), precision: 6);
    }

    [Fact]
    public void SetSampleRate_PrepocitaKoeficienty()
    {
        // Stejná mezní frekvence při jiné vzorkovací frekvenci musí dát jinou
        // odezvu - jinak by se koeficienty nepřepočítávaly.
        var a = new LowPassFilter(44100f); a.SetCutoff(1000f);
        var b = new LowPassFilter(44100f); b.SetSampleRate(8000f); b.SetCutoff(1000f);

        Assert.NotEqual(a.Process(1.0f), b.Process(1.0f), precision: 6);
    }
}
