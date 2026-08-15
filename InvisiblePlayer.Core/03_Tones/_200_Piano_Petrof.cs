using InvisiblePlayer.Core.Generators;
using InvisiblePlayer.Core.Input;

namespace InvisiblePlayer.Core.Tones
{
    public static class _200_Piano_Petrof
    {
        // OPRAVA: preset neměl Name, Number ani Instrument, takže spadl na výchozí
        // InstrumentType.Organ - klavírní preset tedy vyrobil OrganVoice, ne PianoVoice.
        // Do zavedení factory (S2) to nikdo nepoznal, protože se preset nepoužíval.
        // Nález externího review.
        public static VoicePreset Preset => new VoicePreset
        {
            Name = "Piano (Petrof)",
            Number = RegisterNumbers.Piano,
            Instrument = InstrumentType.Piano,
            Harmonics = new (double Ratio, double Amp)[]
            {
                (1.0, 1.0), (2.0, 0.75), (3.0, 0.60), (4.0, 0.40), (5.0, 0.25)
            },
            ChiffFilterFreqHz = 800.0,
            ChiffFilterQ = 1.2,
            ModType = ModulationType.None,
            ModSpeedHz = 5.5,
            ModDepth = 0.08
        };
    }
}