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
        // POZOR na filtr `Number >= 0`: dřív tady byl a byl to SLEPÝ BOD.
        // Presety s Number = -1 ("nepřiřazeno") se tím z kontroly vyřadily,
        // takže test mlčel o tom, že Cembalo a Zvon číslo vůbec nemají.
        // Teď kontrolujeme VŠECHNY a nepřiřazené číslo je samo o sobě chyba.
        var all = VsechnyPresety()
            .Select(row => ((string)row[0], (VoicePreset)row[1]))
            .ToList();

        var unnumbered = all.Where(t => t.Item2.Number < 0).Select(t => t.Item1).ToList();
        Assert.True(unnumbered.Count == 0,
            "Preset bez přiřazeného čísla rejstříku: " + string.Join(", ", unnumbered));

        var duplicates = all
            .GroupBy(t => t.Item2.Number)
            .Where(g => g.Count() > 1)
            .Select(g => $"číslo {g.Key}: {string.Join(", ", g.Select(t => t.Item1))}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Kolize čísel rejstříků: " + string.Join(" | ", duplicates));
    }

    /// <summary>
    /// Konkrétní očekávaná mapa číslo → jméno → generátor.
    ///
    /// Kontrola „je to jedinečné a nenulové" je slabá: `_200_Piano_Petrof` neměl
    /// `Instrument` vůbec, spadl na výchozí `InstrumentType.Organ` a klavírní preset
    /// tak vyráběl `OrganVoice`. Žádný test na jedinečnost to odhalit nemohl —
    /// odhalilo to až externí review. Proto se tady tvrdí konkrétní hodnoty.
    /// </summary>
    [Theory]
    [InlineData(nameof(_001_Bombard16Preset), 1, "Bombard 16'", InstrumentType.Organ)]
    [InlineData(nameof(_085_Aeolus), 85, "Aeolus", InstrumentType.Bell)]
    [InlineData(nameof(_200_Piano_Petrof), 200, "Piano (Petrof)", InstrumentType.Piano)]
    [InlineData(nameof(_300_Cembalo_RandallHopkirk), 201, "Cembalo (Randall & Hopkirk)", InstrumentType.Cembalo)]
    [InlineData(nameof(_400_Bell_Zikmund), 202, "Zvon Zikmund", InstrumentType.Bell)]
    public void Preset_MaOcekavanaMetadata(string presetName, int number, string name, InstrumentType instrument)
    {
        var preset = VsechnyPresety()
            .Select(row => ((string)row[0], (VoicePreset)row[1]))
            .Single(t => t.Item1 == presetName).Item2;

        Assert.Equal(number, preset.Number);
        Assert.Equal(name, preset.Name);
        Assert.Equal(instrument, preset.Instrument);
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
        var engine = new InvisiblePlayer.Core.Synthesis.ToneEngine(44100.0, null, noiseSeed: 1)
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
            var engine = new InvisiblePlayer.Core.Synthesis.ToneEngine(44100.0, null, noiseSeed: 1)
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

    /// <summary>
    /// Regresní test k S5, ověřený přes DŮSLEDEK, ne přes vnitřní stav.
    ///
    /// Němé cembalové hlasy se dřív hromadily v seznamu ToneEngine a přes
    /// voiceCount snižovaly kompenzací 1/sqrt(N) hlasitost tónů, které skutečně
    /// zněly. Test tedy nechá doznít deset not a pak zahraje jedenáctou:
    /// její amplituda musí odpovídat sólovému tónu, ne tónu utlumenému
    /// o sqrt(11).
    /// </summary>
    [Fact]
    public void ToneEngine_DoznelaCembalovaNota_NetlumiDalsi()
    {
        static double PeakOfFreshNote(int warmupNotes)
        {
            var engine = new InvisiblePlayer.Core.Synthesis.ToneEngine(44100.0, null, noiseSeed: 7)
            {
                CurrentPreset = _300_Cembalo_RandallHopkirk.Preset,
            };

            // Necháme doznít několik not (cembalo má SustainLevel = 0, decay 0,9 s).
            for (int n = 0; n < warmupNotes; n++)
            {
                engine.NoteOn(48 + n);
                for (int i = 0; i < 44100 * 2; i++) engine.GenerateNextMixSample();
            }

            // Teprve teď zahrajeme sledovanou notu.
            engine.NoteOn(60);
            double peak = 0;
            for (int i = 0; i < 4096; i++) peak = Math.Max(peak, Math.Abs(engine.GenerateNextMixSample()));
            return peak;
        }

        double solo = PeakOfFreshNote(warmupNotes: 0);
        double afterTen = PeakOfFreshNote(warmupNotes: 10);

        Assert.True(solo > 0.01, $"Sólový tón je příliš tichý ({solo:F4}) — test by nic neměřil.");
        Assert.True(Math.Abs(afterTen - solo) < solo * 0.01,
            $"Doznělé hlasy tlumí nové tóny: sólo {solo:F4} vs po deseti {afterTen:F4}");
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
