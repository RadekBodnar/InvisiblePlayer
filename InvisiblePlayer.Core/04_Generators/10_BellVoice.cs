using System;

namespace InvisiblePlayer.Core.Generators
{
    /// <summary>
    /// Model velkého bronzového zvonu (inspirováno svatovítským Zikmundem, 1549) -
    /// jde o obecný fyzikální model zvonu, ne o změřený vzorek konkrétního zvonu.
    ///
    /// Reálný zvon nemá harmonické spektrum jako struna nebo píšťala - má tzv.
    /// PARTIÁLY s vlastními tradičními názvy a poměry vůči základní frekvenci:
    ///   Hum     (~0.5x)  - hluboký "podtón", nejdéle doznívající
    ///   Prime   (1.0x)   - základní tón
    ///   Tierce  (~1.2x)  - malá tercie (dává zvonu ten charakteristický "mollový" nádech)
    ///   Kvinta  (~1.5x)
    ///   Nominál (2.0x)   - nejsilnější partiál, ten, který ucho vnímá jako "výšku" zvonu
    ///
    /// Navíc: typické "vlnění/třepotání" (beating) velkých zvonů vzniká, když zvon
    /// není dokonale osově symetrický - dvě velmi blízké frekvence (např. dva mírně
    /// rozladěné Nominály) spolu interferují a hlasitost pravidelně "pulzuje".
    /// </summary>
    public class BellVoice : SynthVoice
    {
        private readonly double[] _phases = new double[7];
        private double _elapsedSeconds = 0.0;

        // Poměry partiálů vůči základní frekvenci (Hum, Prime, Tierce, Kvinta, Nominál, +2 vyšší)
        private static readonly double[] PartialRatios =
        {
            0.501, 1.000, 1.199, 1.502, 2.000, 2.514, 3.011
        };

        // Druhý, mírně rozladěný Nominál pro efekt "vlnění" (beating)
        private const double DetunedNominalRatio = 2.006;
        private double _detunedNominalPhase = 0.0;

        // Počáteční amplitudy jednotlivých partiálů
        private static readonly double[] PartialAmplitudes =
        {
            0.35, 0.55, 0.40, 0.30, 0.50, 0.20, 0.12
        };

        // Rychlost doznívání jednotlivých partiálů (za sekundu) - vyšší partiály
        // odeznívají mnohem rychleji, Hum doznívá nejdéle (typické pro velké zvony)
        private static readonly double[] PartialDecayRates =
        {
            0.12, 0.35, 0.55, 0.70, 0.45, 1.10, 1.60
        };

        // --- FM "wobble" (nakřáplost) - volitelné, řízené z VoicePreset ---
        // Výchozí stav (bez presetu / ModType != FM) = beze změny oproti původnímu chování.
        private double _modPhase = 0.0;
        private readonly double _modSpeedHz;
        private readonly double _modDepth;
        private readonly bool _modEnabled;

        public BellVoice(double sampleRate, int? noiseSeed = null) : base(sampleRate)
        {
            NoteEnvelope.AttackTime = 0.002f;   // Okamžitý úder srdce o plášť
            NoteEnvelope.DecayTime = 3.0f;
            NoteEnvelope.SustainLevel = 0.0f;   // Zvon nemá sustain, jen dozvuk
            NoteEnvelope.ReleaseTime = 2.5f;    // Dlouhý dojezd i po "puštění" (u zvonu spíš teoretické)

            _modEnabled = false;
        }

        // Nový konstruktor - volitelný, s presetem. Umožňuje řídit FM wobble
        // (ModType == FM) z VoicePreset, např. pro "nakřáplý" rejstřík č. 85.
        public BellVoice(VoicePreset preset, double sampleRate, int? noiseSeed = null) : this(sampleRate, noiseSeed)
        {
            if (preset != null && preset.ModType == ModulationType.FM)
            {
                _modEnabled = true;
                _modSpeedHz = preset.ModSpeedHz;
                _modDepth = preset.ModDepth;
            }
        }

        public override void NoteOn()
        {
            base.NoteOn();
            _elapsedSeconds = 0.0;
        }

        protected override double CalculateWaveform(double frequency)
        {
            double effectiveFrequency = frequency;

            if (_modEnabled)
            {
                // Pomalé kolísání základní frekvence - aplikuje se na všechny partiály
                // společně (celý zvon "plave"), ne na každý partiál zvlášť.
                double modPhase = AdvancePhase(ref _modPhase, 1.0, _modSpeedHz);
                double modulator = Math.Sin(modPhase * 2.0 * Math.PI);
                effectiveFrequency = frequency * (1.0 + modulator * _modDepth);
            }

            double sample = 0.0;

            for (int i = 0; i < PartialRatios.Length; i++)
            {
                double phase = AdvancePhase(ref _phases[i], PartialRatios[i], effectiveFrequency);
                double decay = Math.Exp(-_elapsedSeconds * PartialDecayRates[i]);
                sample += Math.Sin(phase * 2.0 * Math.PI) * PartialAmplitudes[i] * decay;
            }

            // Druhý, mírně rozladěný Nominál - stejná amplituda/dozvuk jako hlavní Nominál (index 4),
            // ale jiná frekvence -> vznikne slyšitelné "vlnění" hlasitosti (beating)
            double detunedPhase = AdvancePhase(ref _detunedNominalPhase, DetunedNominalRatio, effectiveFrequency);
            double nominalDecay = Math.Exp(-_elapsedSeconds * PartialDecayRates[4]);
            sample += Math.Sin(detunedPhase * 2.0 * Math.PI) * PartialAmplitudes[4] * nominalDecay;

            _elapsedSeconds += 1.0 / SampleRate;

            // Normalizace - partiálů je dohromady 8, ať zvon nepřebuzuje výstup
            return sample * 0.3;
        }
    }
}
