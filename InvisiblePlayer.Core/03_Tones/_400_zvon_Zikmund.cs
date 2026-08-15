using InvisiblePlayer.Core.Generators;
using InvisiblePlayer.Core.Synthesis;

namespace InvisiblePlayer.Core.Tones
{
    public static class _400_Bell_Zikmund
    {
        // Stejně jako u Cembalo presetu - zvuk je "zadrátovaný" v BellVoice,
        // preset zatím jen vybírá zvukový engine.
        public static VoicePreset Preset => new VoicePreset
        {
            Name = "Zvon Zikmund",
            Instrument = InstrumentType.Bell
        };
    }
}
