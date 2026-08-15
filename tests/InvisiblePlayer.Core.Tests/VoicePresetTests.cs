using InvisiblePlayer.Core.Generators;
using InvisiblePlayer.Core.Tones;

namespace InvisiblePlayer.Core.Tests;

/// <summary>
/// Regresní testy k nálezu S1: VoicePreset.Harmonics bylo non-nullable pole
/// bez inicializátoru (CS8618) a presety Cembalo/Bell ho nenastavovaly.
/// OrganVoice na něm padal v konstruktoru na .Length.
/// </summary>
public class VoicePresetTests
{
    /// <summary>
    /// Všechny presety v repu. Až přibude další, patří sem - test pak hlídá,
    /// že nikdo nezaloží preset s null Harmonics.
    /// </summary>
    public static TheoryData<string, VoicePreset> VsechnyPresety() => new()
    {
        { nameof(_001_Bombard16Preset),      _001_Bombard16Preset.Preset },
        { nameof(_085_Aeolus),               _085_Aeolus.Preset },
        { nameof(_200_Piano_Petrof),         _200_Piano_Petrof.Preset },
        { nameof(_300_Cembalo_RandallHopkirk), _300_Cembalo_RandallHopkirk.Preset },
        { nameof(_400_Bell_Zikmund),         _400_Bell_Zikmund.Preset },
    };

    [Theory]
    [MemberData(nameof(VsechnyPresety))]
    public void KazdyPreset_MaNeprazdneHarmonics(string name, VoicePreset preset)
    {
        Assert.True(preset.Harmonics is { Length: > 0 },
            $"{name}: Harmonics je null nebo prázdné -> OrganVoice by spadl v konstruktoru.");
    }

    [Theory]
    [MemberData(nameof(VsechnyPresety))]
    public void KazdyPreset_LzePouzitVOrganVoiceBezVyjimky(string name, VoicePreset preset)
    {
        // Přesně ta cesta, která dřív hodila NullReferenceException.
        var voice = new OrganVoice(preset, 44100.0);
        voice.NoteOn();

        for (int i = 0; i < 1000; i++)
        {
            double s = voice.GenerateSample(440.0);
            Assert.False(double.IsNaN(s), $"{name}: NaN ve vzorku {i}");
        }
    }

    [Theory]
    [MemberData(nameof(VsechnyPresety))]
    public void KazdyPreset_MaKladneParametryChiffFiltru(string name, VoicePreset preset)
    {
        // q <= 0 by ve BandPassFilter vedlo k dělení nulou (nález S4).
        Assert.True(preset.ChiffFilterQ > 0, $"{name}: ChiffFilterQ = {preset.ChiffFilterQ}");
        Assert.True(preset.ChiffFilterFreqHz > 0, $"{name}: ChiffFilterFreqHz = {preset.ChiffFilterFreqHz}");
        Assert.True(preset.ChiffDurationSec > 0, $"{name}: ChiffDurationSec = {preset.ChiffDurationSec}");
    }
}

public class SynthVoiceTests
{
    [Theory]
    [InlineData(typeof(PianoVoice))]
    [InlineData(typeof(CembaloVoice))]
    [InlineData(typeof(BellVoice))]
    public void VsechnyHlasy_ProduujiKonecneVzorky(Type voiceType)
    {
        // Tyhle tři třídy jsou zatím mrtvý kód (nález S2 - ToneEngine je
        // nevytváří), ale otestované být mají: až se zapojí factory podle
        // InstrumentType, chceme vědět, že fungují.
        var voice = (SynthVoice)Activator.CreateInstance(voiceType, 44100.0)!;
        voice.NoteOn();

        for (int i = 0; i < 44100; i++)
        {
            double s = voice.GenerateSample(220.0);
            Assert.False(double.IsNaN(s), $"{voiceType.Name}: NaN ve vzorku {i}");
            Assert.False(double.IsInfinity(s), $"{voiceType.Name}: Infinity ve vzorku {i}");
        }
    }

    [Fact]
    public void OrganVoice_PoNoteOffDojedeDoIsFinished()
    {
        var voice = new OrganVoice(_001_Bombard16Preset.Preset, 44100.0);
        voice.NoteOn();
        for (int i = 0; i < 4410; i++) voice.GenerateSample(440.0);

        Assert.False(voice.IsFinished);

        voice.NoteOff();
        for (int i = 0; i < 44100; i++) voice.GenerateSample(440.0);

        Assert.True(voice.IsFinished, "Hlas po NoteOff nedojel do IsFinished -> únik v ToneEngine.");
    }
}
