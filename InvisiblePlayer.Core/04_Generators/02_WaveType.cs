namespace InvisiblePlayer.Core.Generators
{
    public enum WaveType
    {
        Sine,       // Čistý sinus (základní tón)
        Sawtooth,   // Pila (bohatá na sudé i liché harmonické - smyčce, žestě)
        Square,     // Čtverec (liché harmonické - klarinet, 8-bit zvuky)
        Triangle,   // Trojúhelník (jemný tón, flétna)
        WhiteNoise  // Bílý šum (perkuse, fuk varhan)
    }

    // Který "zvukový engine" má preset použít. Výchozí je Organ, takže všechny
    // dosavadní presety (Bombard, Piano...) zůstávají beze změny funkční.
    public enum InstrumentType
    {
        Organ,
        Piano,
        Cembalo,
        Bell
    }

    public class VoicePreset
    {
        public string Name { get; set; } = "Default";
        public int Number { get; set; } = -1; // -1 = nepřiřazeno, jinak číslo rejstříku

        // Který nástroj/engine preset používá. U Piano/Cembalo/Bell presetů
        // se pole Harmonics atd. zatím nevyužívají - ty mají svůj zvuk
        // "zadrátovaný" přímo ve třídě (PianoVoice, CembaloVoice, BellVoice).
        public InstrumentType Instrument { get; set; } = InstrumentType.Organ;

        // Tabulka alikvót: (poměr frekvence, hlasitost).
        // Výchozí hodnota (čistý základní tón) je nutná: pole je non-nullable, ale
        // presety Piano/Cembalo/Bell ho nenastavují (zvuk mají zadrátovaný ve své
        // třídě). Bez inicializátoru zůstalo null a OrganVoice na něm padal
        // v konstruktoru na _preset.Harmonics.Length -> NullReferenceException.
        public (double FrequencyMultiplier, double Amplitude)[] Harmonics { get; set; }
            = new (double FrequencyMultiplier, double Amplitude)[] { (1.0, 1.0) };

        // Parametry Šumu / Chiffu / Úderu
        public double ChiffNoiseGain { get; set; } = 0.2;
        public double ChiffFilterFreqHz { get; set; } = 800.0;
        public double ChiffFilterQ { get; set; } = 1.0;
        public double ChiffDurationSec { get; set; } = 0.030; // 30ms

        // Modulace (Vibrato / Tremolo)
        public ModulationType ModType { get; set; } = ModulationType.None;
        public double ModSpeedHz { get; set; } = 5.5;
        public double ModDepth { get; set; } = 0.08;
    }

    public enum ModulationType
    {
        None,
        AM, // Amplitudová modulace (Tremolo)
        FM  // Frekvenční modulace (Vibrato)
    }

}
