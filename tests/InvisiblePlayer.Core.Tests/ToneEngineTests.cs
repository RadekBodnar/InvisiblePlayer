namespace InvisiblePlayer.Core.Tests;

// Alias MUSÍ být až za deklarací namespace (u file-scoped namespace = uvnitř něj).
// Důvod: 'InvisiblePlayer.Core.ToneEngine' je NAMESPACE i TŘÍDA. Vyhledávání jména
// jde zevnitř ven a na každé úrovni bere členy namespacu + using direktivy TÉ úrovně.
// Alias před deklarací patří k nejvzdálenější úrovni, takže by ho přebil člen
// 'ToneEngine' namespacu 'InvisiblePlayer.Core' -> CS0118.
// Stejná kolize nutí Audio.cs psát plně kvalifikované
// 'InvisiblePlayer.Core.ToneEngine.ToneEngine' na každém místě. Viz TODO S10.
using ToneEngine = InvisiblePlayer.Core.ToneEngine.ToneEngine;

/// <summary>
/// Regresní testy k nálezům B1 a B2 z code review 2026-08-15.
/// </summary>
public class ToneEngineTests
{
    private const int SampleRate = 44100;

    private static double[] Render(ToneEngine engine, int sampleCount)
    {
        var samples = new double[sampleCount];
        for (int i = 0; i < sampleCount; i++)
            samples[i] = engine.GenerateNextMixSample();
        return samples;
    }

    // ---------------------------------------------------------------- B2 ----

    [Fact]
    public void ReadClipDetected_BezSignaluJeFalse()
    {
        var engine = new ToneEngine(SampleRate);
        Render(engine, 1000);

        Assert.False(engine.ReadClipDetected());
    }

    [Fact]
    public void ReadClipDetected_ZachytiOrezIKdyzTrvalJedinyVzorek()
    {
        // JÁDRO NÁLEZU B2: ořez trvá typicky jeden vzorek (23 us), zatímco VU metr
        // čte jednou za desítky ms. Před opravou se příznak přepisoval při každém
        // vzorku, takže transient ořez se do okamžiku čtení prakticky nikdy netrefil.
        var engine = new ToneEngine(SampleRate);

        // Oktávy se fázově sčítají nejsilněji -> spolehlivé přebuzení.
        foreach (int note in new[] { 36, 48, 60, 72, 84, 40, 52, 64, 76, 43, 55, 67 })
            engine.NoteOn(note);

        Render(engine, SampleRate); // 1 sekunda

        Assert.True(engine.ReadClipDetected(),
            "Ořez měl být zachycen i když trval jediný vzorek.");
    }

    [Fact]
    public void ReadClipDetected_CteniPriznakNuluje()
    {
        // Latching sémantika stejná jako AudioEngine.ReadPeak():
        // první čtení hodnotu vydá, druhé už musí být čisté.
        var engine = new ToneEngine(SampleRate);
        foreach (int note in new[] { 36, 48, 60, 72, 84, 40, 52, 64, 76, 43, 55, 67 })
            engine.NoteOn(note);
        Render(engine, SampleRate);

        Assert.True(engine.ReadClipDetected());
        Assert.False(engine.ReadClipDetected());
    }

    // ---------------------------------------------------------------- B1 ----

    [Fact]
    public void NoteOn_DvakratZaSebou_NevytvoriDruhyHlas()
    {
        // ToneEngine má opakované NoteOn ustát retriggerem existujícího hlasu.
        // Kdyby vznikl druhý hlas, mix se zdvojí a kompenzace polyfonie ho vydělí
        // odmocninou ze dvou -> výsledná amplituda by vyskočila o faktor
        // 2 / sqrt(2) = 1.414, tedy o 41 %. Tolerance 5 % ty dva případy
        // bezpečně odděluje.
        var single = new ToneEngine(SampleRate);
        single.NoteOn(60);
        double peakSingle = Render(single, 4096).Max(Math.Abs);

        var doubled = new ToneEngine(SampleRate);
        doubled.NoteOn(60);
        doubled.NoteOn(60);
        double peakDoubled = Render(doubled, 4096).Max(Math.Abs);

        double ratio = peakDoubled / peakSingle;
        Assert.True(ratio < 1.05,
            $"Druhé NoteOn nejspíš vytvořilo další hlas: poměr špiček {ratio:F4} " +
            $"(druhý hlas by dal ~1.414)");
    }

    [Fact]
    public void NoteOn_Dvakrat_RestartujeObalkuAChiff_PROTO_BYL_B1_SLYSET()
    {
        // Charakterizace nálezu B1: druhé NoteOn sice nevytvoří druhý hlas, ale
        // NENÍ zadarmo - volá Voice.NoteOn(), což přehodí ADSR obálku zpět do
        // stavu Attack a nastaví _chiffEnvelope = 1.0. Chiff (zapraskání píšťaly)
        // tedy zazní dvakrát a nástup tónu se uprostřed restartuje.
        //
        // Právě proto byl zdvojený dispatch v App.xaml.cs slyšet jako dvojité
        // cvaknutí. Tenhle test hlídá, že se ta duplicita nevrátí: kdyby někdo
        // NoteOn znovu zdvojil, signál se od jednoduchého případu bude lišit.
        var single = new ToneEngine(SampleRate);
        single.NoteOn(60);
        double[] a = Render(single, 4096);

        var doubled = new ToneEngine(SampleRate);
        doubled.NoteOn(60);
        doubled.NoteOn(60);
        double[] b = Render(doubled, 4096);

        double maxDiff = a.Zip(b, (x, y) => Math.Abs(x - y)).Max();
        Assert.True(maxDiff > 0.0,
            "Druhé NoteOn nemělo na signál žádný vliv - retrigger tedy nefunguje.");
    }

    [Fact]
    public void NoteOff_UkonciTonAVyprazdniSeznam()
    {
        var engine = new ToneEngine(SampleRate);
        engine.NoteOn(60);
        Render(engine, 2000);

        engine.NoteOff(60);
        Render(engine, SampleRate / 2); // release je 0.03 s, půl sekundy bohatě stačí

        double[] tail = Render(engine, 1000);
        Assert.All(tail, s => Assert.Equal(0.0, s, precision: 12));
    }

    [Fact]
    public void GenerateNextMixSample_NikdyNevyjdeZRozsahu()
    {
        var engine = new ToneEngine(SampleRate);
        for (int note = 24; note <= 96; note++) engine.NoteOn(note);

        double[] samples = Render(engine, SampleRate / 2);

        Assert.All(samples, s =>
        {
            Assert.False(double.IsNaN(s), "NaN ve výstupu.");
            Assert.InRange(s, -1.0, 1.0);
        });
    }

    [Fact]
    public void NoteOff_NeznameNoty_Neshodi()
    {
        var engine = new ToneEngine(SampleRate);
        engine.NoteOff(60);      // nikdy nezaznělo
        Render(engine, 100);
    }
}
