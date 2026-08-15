namespace InvisiblePlayer.Core.Tests;

using InvisiblePlayer.Core.Synthesis;

/// <summary>
/// S8: mutování MIDI kanálů. Dřív se `ConsoleInputEvent.Channel` cestou do
/// `ToneEngine` zahazoval, takže všechny stopy MIDI souboru hrály jedním
/// rejstříkem a umlčet jednu z nich nešlo — přestože to legenda ve `VgaEngine`
/// slibovala.
/// </summary>
public class ChannelMuteTests
{
    private const int SampleRate = 44100;

    private static ToneEngine NewEngine() => new(SampleRate, null, noiseSeed: 99);

    private static double PeakOf(ToneEngine engine, int samples = 4096)
    {
        double peak = 0;
        for (int i = 0; i < samples; i++) peak = Math.Max(peak, Math.Abs(engine.GenerateNextMixSample()));
        return peak;
    }

    [Fact]
    public void VychoziStav_ZadnyKanalNeniUmlceny()
    {
        var engine = NewEngine();
        for (int ch = 0; ch < 16; ch++) Assert.False(engine.IsChannelMuted(ch));
    }

    [Fact]
    public void UmlcenyKanal_NotuVubecNerozezni()
    {
        var engine = NewEngine();
        engine.SetChannelMuted(3, true);

        engine.NoteOn(60, channel: 3);

        Assert.Equal(0.0, PeakOf(engine), precision: 12);
    }

    [Fact]
    public void UmlceniJednohoKanalu_NeovlivniOstatni()
    {
        var reference = NewEngine();
        reference.NoteOn(60, channel: 5);
        double expected = PeakOf(reference);

        var engine = NewEngine();
        engine.SetChannelMuted(3, true);
        engine.NoteOn(60, channel: 5);   // jiný kanál — musí znít stejně jako bez mute

        Assert.Equal(expected, PeakOf(engine), precision: 9);
    }

    [Fact]
    public void UmlceniZaBehu_UkonciJizZnejiciTony()
    {
        // Kdyby mute zabral až u další noty, u drženého akordu by se tvářil,
        // že nefunguje — uživatel zmáčkne klávesu a nic se nestane.
        var engine = NewEngine();
        engine.NoteOn(60, channel: 2);
        Assert.True(PeakOf(engine, 2000) > 0.01, "Tón vůbec nezazněl — test by nic neměřil.");

        engine.SetChannelMuted(2, true);

        // Release varhanní obálky je 0,03 s; půl sekundy bohatě stačí na doznění.
        PeakOf(engine, SampleRate / 2);
        Assert.Equal(0.0, PeakOf(engine, 2000), precision: 12);
    }

    [Fact]
    public void ZruseniUmlceni_KanalZnovuHraje()
    {
        var engine = NewEngine();
        engine.SetChannelMuted(7, true);
        engine.NoteOn(60, channel: 7);
        Assert.Equal(0.0, PeakOf(engine), precision: 12);

        engine.SetChannelMuted(7, false);
        engine.NoteOn(60, channel: 7);

        Assert.True(PeakOf(engine) > 0.01, "Po zrušení mute musí kanál zase hrát.");
    }

    [Fact]
    public void TatazNota_NaDvouKanalech_JsouDvaNezavisleHlasy()
    {
        // MIDI soubor běžně hraje tentýž tón na dvou stopách. Kdyby se hlasy
        // hledaly jen podle čísla noty, druhá stopa by první jen retriggerovala
        // a umlčení jednoho kanálu by ztišilo obě.
        var engine = NewEngine();
        engine.NoteOn(60, channel: 0);
        engine.NoteOn(60, channel: 1);

        engine.SetChannelMuted(0, true);
        PeakOf(engine, SampleRate / 2);   // necháme doznít umlčený hlas

        Assert.True(PeakOf(engine, 4096) > 0.01,
            "Kanál 1 musí hrát dál, i když je kanál 0 umlčený.");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    [InlineData(999)]
    public void NeplatneCisloKanalu_Neshodi(int channel)
    {
        var engine = NewEngine();
        engine.SetChannelMuted(channel, true);
        Assert.False(engine.IsChannelMuted(channel));

        engine.NoteOn(60, channel);   // nesmí vyhodit výjimku
        PeakOf(engine, 100);
    }

    [Fact]
    public void NoteOff_RozlisujeKanaly()
    {
        var engine = NewEngine();
        engine.NoteOn(60, channel: 0);
        engine.NoteOn(60, channel: 1);
        PeakOf(engine, 2000);

        engine.NoteOff(60, channel: 0);   // vypnout se má JEN kanál 0
        PeakOf(engine, SampleRate / 2);

        Assert.True(PeakOf(engine, 4096) > 0.01,
            "NoteOff na kanálu 0 nesmí ukončit tón na kanálu 1.");
    }

    [Fact]
    public void VychoziKanal_JeNula_ZpetnaKompatibilita()
    {
        // Přetížení bez kanálu musí dál fungovat (živý MIDI vstup i staré volání).
        var engine = NewEngine();
        engine.NoteOn(60);
        Assert.True(PeakOf(engine) > 0.01);

        engine.SetChannelMuted(0, true);
        var muted = NewEngine();
        muted.SetChannelMuted(0, true);
        muted.NoteOn(60);
        Assert.Equal(0.0, PeakOf(muted), precision: 12);
    }
}
