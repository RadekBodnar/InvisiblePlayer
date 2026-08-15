using InvisiblePlayer.Core.Filters;
using System;

namespace InvisiblePlayer.Core.Generators
{
    /// <summary>
    /// Model cembala (pracovní název "Randall & Hopkirk" - žádný konkrétní historický
    /// nástroj, jen interní jméno presetu).
    /// Dva hlavní jevy, které dělají cembalo cembalem (na rozdíl od klavíru):
    ///  1) BRNKNUTÍ BRČKEM (plectrum) - ostrý, velmi krátký šumový "click" na začátku,
    ///     mnohem kratší a "sušší" než úder klavírního kladívka.
    ///  2) NEZÁVISLÉ DOZNÍVÁNÍ HARMONICKÝCH - u brnkané struny odeznívají vyšší
    ///     harmonické mnohem rychleji než základní tón (na rozdíl od klavíru, kde
    ///     odeznívají víc "společně").
    /// </summary>
    public class CembaloVoice : SynthVoice
    {
        private readonly double[] _phases = new double[6];

        // Kolik sekund uplynulo od NoteOn - používá se pro nezávislé dozvuky harmonických
        private double _elapsedSeconds = 0.0;

        // Pluck (brnknutí) - krátký ostrý šumový impuls
        private double _pluckEnvelope = 1.0;
        private readonly NoiseGenerator _pluckNoise;
        private readonly BandPassFilter _pluckFilter = new BandPassFilter();
        private const double PluckDurationSec = 0.004; // 4 ms - ostřejší než klavírní kladívko

        public CembaloVoice(double sampleRate, int? noiseSeed = null) : base(sampleRate)
        {
            _pluckNoise = new NoiseGenerator(noiseSeed);

            NoteEnvelope.AttackTime = 0.001f;  // Prakticky okamžitý náběh
            NoteEnvelope.DecayTime = 0.9f;
            NoteEnvelope.SustainLevel = 0.0f;  // Cembalo nemá sustain - struna jen doznívá
            NoteEnvelope.ReleaseTime = 0.12f;

            _pluckFilter.SetParams(3800.0, 2.0, sampleRate); // vyšší a užší než u klavíru
        }

        public override void NoteOn()
        {
            base.NoteOn();
            _elapsedSeconds = 0.0;
            _pluckEnvelope = 1.0;
        }

        protected override double CalculateWaveform(double frequency)
        {
            double sample = 0.0;

            // Základní amplitudy harmonických - cembalo je bohatší na vyšší harmonické než klavír
            Span<double> amplitudes = stackalloc double[6] { 1.0, 0.7, 0.55, 0.4, 0.3, 0.2 };

            // Čím vyšší harmonická, tím rychleji sama odezní (nezávisle na hlavní ADSR obálce)
            Span<double> decayRatesPerSec = stackalloc double[6] { 1.0, 2.2, 3.5, 5.0, 7.0, 9.5 };

            for (int i = 0; i < _phases.Length; i++)
            {
                int harmonicNumber = i + 1;
                double phase = AdvancePhase(ref _phases[i], harmonicNumber, frequency);

                double individualDecay = Math.Exp(-_elapsedSeconds * decayRatesPerSec[i]);
                sample += Math.Sin(phase * 2.0 * Math.PI) * amplitudes[i] * individualDecay;
            }

            sample *= 0.35;

            // Pluck - ostrý krátký "click" na začátku
            if (_pluckEnvelope > 0.001)
            {
                double noise = _pluckNoise.NextSample((int)SampleRate);
                sample += _pluckFilter.Process(noise) * _pluckEnvelope * 0.25;
                _pluckEnvelope *= Math.Exp(-1.0 / (SampleRate * PluckDurationSec));
            }

            _elapsedSeconds += 1.0 / SampleRate;

            return sample;
        }
    }
}
