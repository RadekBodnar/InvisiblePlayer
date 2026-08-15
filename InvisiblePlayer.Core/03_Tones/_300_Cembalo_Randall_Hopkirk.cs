using InvisiblePlayer.Core.Generators;
using InvisiblePlayer.Core.Synthesis;

namespace InvisiblePlayer.Core.Tones
{
    public static class _300_Cembalo_RandallHopkirk
    {
        // Harmonics/Chiff/Mod pole se u Cembalo presetů zatím nevyužívají -
        // zvuk je "zadrátovaný" přímo v CembaloVoice. Preset zatím jen říká
        // ToneEngine, který zvukový engine se má pro tento rejstřík použít.
        public static VoicePreset Preset => new VoicePreset
        {
            Name = "Cembalo (Randall & Hopkirk)",
            Instrument = InstrumentType.Cembalo
        };
    }
}
