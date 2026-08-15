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

    /// <summary>
    /// Regresní test k S3: dva presety se hlásily ke stejnému číslu rejstříku 85
    /// (_001_Bombard16Preset měl zděděné Name = "Aeolus" a Number = 85).
    /// Číslo rejstříku odpovídá fyzické klapce na hracím stole, takže duplicita
    /// znamená, že jedna z nich je nedosažitelná.
    /// </summary>
    [Fact]
    public void CislaRejstriku_JsouJedinecna()
    {
        var duplicates = VsechnyPresety()
            .Select(row => ((string)row[0], (VoicePreset)row[1]))
            .Where(t => t.Item2.Number >= 0)
            .GroupBy(t => t.Item2.Number)
            .Where(g => g.Count() > 1)
            .Select(g => $"číslo {g.Key}: {string.Join(", ", g.Select(t => t.Item1))}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Kolize čísel rejstříků: " + string.Join(" | ", duplicates));
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
        // Oba argumenty musí být uvedené explicitně: Activator.CreateInstance
        // volitelné parametry sám nedoplňuje (chtělo by to BindingFlags
        // .OptionalParamBinding), takže po přidání 'int? noiseSeed' přestal
        // konstruktor odpovídat jednoargumentovému volání.
        var voice = (SynthVoice)Activator.CreateInstance(voiceType, 44100.0, (int?)null)!;
        voice.NoteOn();

        for (int i = 0; i < 44100; i++)
        {
            double s = voice.GenerateSample(220.0);
            Assert.False(double.IsNaN(s), $"{voiceType.Name}: NaN ve vzorku {i}");
            Assert.False(double.IsInfinity(s), $"{voiceType.Name}: Infinity ve vzorku {i}");
        }
    }

    /// <summary>
    /// Regresní testy k S2: factory v ToneEngine musí VoicePreset.Instrument
    /// skutečně číst. Dřív se pole nastavovalo, ale nikde nevyhodnocovalo -
    /// NoteOn vždy vytvořil OrganVoice a Piano/Cembalo/Bell byly nedosažitelné.
    ///
    /// Testujeme přes pozorovatelné chování, ne přes typ hlasu (ten je privátní):
    /// jednotlivé nástroje mají výrazně odlišné obálky, takže se liší i doznění.
    /// </summary>
    [Theory]
    [InlineData(InstrumentType.Organ)]
    [InlineData(InstrumentType.Piano)]
    [InlineData(InstrumentType.Cembalo)]
    [InlineData(InstrumentType.Bell)]
    public void ToneEngine_RespektujePresetInstrument(InstrumentType instrument)
    {
        var engine = new InvisiblePlayer.Core.ToneEngine.ToneEngine(44100.0, null, noiseSeed: 1)
        {
            CurrentPreset = new VoicePreset { Instrument = instrument },
        };

        engine.NoteOn(60);
        var samples = new double[8192];
        for (int i = 0; i < samples.Length; i++) samples[i] = engine.GenerateNextMixSample();

        Assert.All(samples, s => Assert.False(double.IsNaN(s) || double.IsInfinity(s)));
        Assert.True(samples.Any(s => Math.Abs(s) > 0.001),
            $"{instrument}: hlas nevydal žádný slyšitelný signál.");
    }

    [Fact]
    public void ToneEngine_RuzneNastroje_ZniRuzne()
    {
        // Kdyby factory Instrument ignorovala (stav před S2), byly by všechny
        // průběhy identické.
        static double[] RenderWith(InstrumentType instrument)
        {
            var engine = new InvisiblePlayer.Core.ToneEngine.ToneEngine(44100.0, null, noiseSeed: 1)
            {
                CurrentPreset = new VoicePreset { Instrument = instrument },
            };
            engine.NoteOn(60);
            var s = new double[8192];
            for (int i = 0; i < s.Length; i++) s[i] = engine.GenerateNextMixSample();
            return s;
        }

        double[] organ = RenderWith(InstrumentType.Organ);

        Assert.NotEqual(organ, RenderWith(InstrumentType.Piano));
        Assert.NotEqual(organ, RenderWith(InstrumentType.Cembalo));
        Assert.NotEqual(organ, RenderWith(InstrumentType.Bell));
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
