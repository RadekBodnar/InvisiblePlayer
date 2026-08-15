using InvisiblePlayer.Core.Filters;
using InvisiblePlayer.Core.Generators;

namespace InvisiblePlayer.Core.Tests;

public class BandPassFilterTests
{
    /// <summary>
    /// NÁLEZ S4: q = 0 dřív vedlo k dělení nulou -> alpha = Infinity -> NaN
    /// koeficienty. NaN přežije i Math.Clamp v mixu, takže by filtr otrávil
    /// celý výstup natrvalo. Tohle je ten nejzákeřnější druh vady: neshodí to,
    /// jen se zvuk "ztratí".
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.Epsilon)]
    public void SetParams_NuloveNeboZaporneQ_NevyrobiNaN(double q)
    {
        var filter = new BandPassFilter();
        filter.SetParams(800.0, q, 44100.0);

        for (int i = 0; i < 1000; i++)
        {
            double y = filter.Process(i % 2 == 0 ? 1.0 : -1.0);
            Assert.False(double.IsNaN(y), $"NaN po {i} vzorcích při q={q}");
            Assert.False(double.IsInfinity(y), $"Infinity po {i} vzorcích při q={q}");
        }
    }

    [Theory]
    [InlineData(0.0)]              // pod dolní mezí
    [InlineData(100_000.0)]        // vysoko nad Nyquistem
    [InlineData(-500.0)]
    public void SetParams_FrekvenceMimoRozsah_ZustaneStabilni(double centerFreq)
    {
        var filter = new BandPassFilter();
        filter.SetParams(centerFreq, 1.0, 44100.0);

        for (int i = 0; i < 5000; i++)
        {
            double y = filter.Process(Math.Sin(i * 0.1));
            Assert.False(double.IsNaN(y) || double.IsInfinity(y));
            Assert.InRange(y, -100.0, 100.0);   // nesmí divergovat
        }
    }

    [Fact]
    public void Reset_VynulujeVnitrniStav()
    {
        var filter = new BandPassFilter();
        filter.SetParams(800.0, 1.2, 44100.0);
        for (int i = 0; i < 100; i++) filter.Process(1.0);

        filter.Reset();

        // Po resetu musí být první výstup stejný jako u čerstvé instance.
        var fresh = new BandPassFilter();
        fresh.SetParams(800.0, 1.2, 44100.0);
        Assert.Equal(fresh.Process(1.0), filter.Process(1.0), precision: 12);
    }
}

public class AdsrEnvelopeTests
{
    private const int SampleRate = 44100;

    [Fact]
    public void Process_NabehneNaPlnouUroven()
    {
        var env = new AdsrEnvelope { AttackTime = 0.01f, SustainLevel = 1.0f };
        env.TriggerGate(true);

        for (int i = 0; i < SampleRate / 50; i++) env.Process(SampleRate);

        Assert.True(env.CurrentLevel > 0.99f, $"Úroveň po attacku: {env.CurrentLevel}");
    }

    [Fact]
    public void Process_PoReleaseDojedeDoIdle()
    {
        var env = new AdsrEnvelope { ReleaseTime = 0.05f };
        env.TriggerGate(true);
        for (int i = 0; i < 1000; i++) env.Process(SampleRate);

        env.TriggerGate(false);
        for (int i = 0; i < SampleRate; i++) env.Process(SampleRate);

        Assert.Equal(EnvelopeState.Idle, env.State);
        Assert.False(env.IsActive);
    }

    [Fact]
    public void Process_NulovySampleRate_Neshodi()
    {
        var env = new AdsrEnvelope();
        env.TriggerGate(true);
        Assert.Equal(0.0f, env.Process(0));
    }

    /// <summary>
    /// CHARAKTERIZAČNÍ TEST ke známé vadě S5 (viz TODO.md).
    ///
    /// Zachycuje SOUČASNÉ chování, ne to správné: hlas se SustainLevel = 0
    /// (CembaloVoice, BellVoice) po doznění decay uvázne ve stavu Sustain
    /// s úrovní 0 - je trvale němý, ale IsActive zůstává true, takže se
    /// v ToneEngine nikdy neodstraní ze seznamu a přes voiceCount zbytečně
    /// tlumí ostatní tóny.
    ///
    /// AŽ SE S5 OPRAVÍ, TENHLE TEST ZAČNE PADAT. To je záměr - připomene, že
    /// se má přepsat na Assert.Equal(EnvelopeState.Idle, env.State).
    /// </summary>
    [Fact]
    public void ZNAMA_VADA_S5_SustainNula_UvizneVeStavuSustain()
    {
        var env = new AdsrEnvelope
        {
            AttackTime = 0.001f,
            DecayTime = 0.05f,
            SustainLevel = 0.0f,
        };
        env.TriggerGate(true);

        for (int i = 0; i < SampleRate; i++) env.Process(SampleRate);

        Assert.Equal(EnvelopeState.Sustain, env.State);
        Assert.Equal(0.0f, env.CurrentLevel);
        Assert.True(env.IsActive, "Aktuálně zůstává 'aktivní', ačkoli je němý - to je ta vada.");
    }
}

public class TemperamentTests
{
    [Fact]
    public void CentOffset_VychoziTemperaturaJeRovnomerna()
    {
        var t = new Temperament();
        for (int note = 0; note < 128; note++)
            Assert.Equal(0.0, t.CentOffset(note));
    }

    [Theory]
    [InlineData(60, 0)]     // C4  -> pitch class 0 (C)
    [InlineData(69, 9)]     // A4  -> pitch class 9 (A)
    [InlineData(0, 0)]
    public void CentOffset_MapujeNaSpravnouTridu(int midiNote, int expectedPitchClass)
    {
        var t = new Temperament();
        t.CentOffsets[expectedPitchClass] = 42.0;

        Assert.Equal(42.0, t.CentOffset(midiNote));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-12)]
    [InlineData(-13)]
    public void CentOffset_ZapornaCisla_Neshodi(int midiNote)
    {
        // Modulo v C# vrací u záporných čísel záporný zbytek - kód to řeší
        // dvojitým modulem. Bez něj by tu byl IndexOutOfRangeException.
        var t = new Temperament();
        t.CentOffset(midiNote);
    }
}
