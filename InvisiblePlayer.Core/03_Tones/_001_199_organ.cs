using InvisiblePlayer.Core.Generators;

namespace InvisiblePlayer.Core.Tones
{
    public static class _001_Bombard16Preset
    {
        // OPRAVENO 2026-08-15 (nález S3): preset měl Name = "Aeolus" a Number = 85,
        // tedy jméno i číslo patřící presetu _085_Aeolus o pár řádků níž.
        //
        // Že jsou špatně DATA (a ne název třídy), je vidět z harmonických:
        // celočíselná řada 1-2-3-4-5 s klesajícími amplitudami je spektrum varhanní
        // píšťaly. Zvon má partiály neceločíselné (0.501, 1.199, 1.502 ... viz
        // BellVoice), takže tenhle preset zvonkohrou být nemůže.
        public static VoicePreset Preset => new VoicePreset
        {
            Name = "Bombard 16'",
            Number = 1,
            Instrument = InstrumentType.Organ,
            Harmonics = new (double Ratio, double Amp)[]
            {
                (1.0, 1.0), (2.0, 0.75), (3.0, 0.60), (4.0, 0.40), (5.0, 0.25)
            },
            ChiffFilterFreqHz = 800.0,
            ChiffFilterQ = 1.2,

            // ModType/ModSpeedHz/ModDepth zde ZÁMĚRNĚ NEJSOU: OrganVoice modulační
            // pole vůbec nečte (jediný generátor, který je respektuje, je BellVoice,
            // a jen pro FM). Původní ModType = AM tedy sliboval tremolo, které nikdy
            // nezaznělo. Až se AM ve OrganVoice implementuje, patří to sem zpátky.
        };
    }
    public static class _085_Aeolus
    {
        // Stejně jako u Cembalo presetu - zvuk je "zadrátovaný" v BellVoice,
        // preset zatím jen vybírá zvukový engine.
        public static VoicePreset Preset => new VoicePreset
        {
            Name = "Aeolus",
            Number = 85,
            Instrument = InstrumentType.Bell,
            Harmonics = new (double Ratio, double Amp)[]
            {
                (0.501, 1.0), (2.0, 0.75), (3.0, 0.60), (4.0, 0.40), (5.0, 0.25)
            },
            ChiffFilterFreqHz = 800.0,
            ChiffFilterQ = 1.2,
            ModType = ModulationType.FM,
            ModSpeedHz = 5.5,
            ModDepth = 0.08
        };
    }

}