using InvisiblePlayer.Core.Filters;
using System;

namespace InvisiblePlayer.Core.Generators
{



    public class OrganVoice : SynthVoice
    {
        private readonly VoicePreset _preset;
        private double[] _phases;
        private double _chiffEnvelope = 1.0;
        private readonly NoiseGenerator _noiseGen = new NoiseGenerator();
        private readonly BandPassFilter _chiffFilter = new BandPassFilter();

        public OrganVoice(VoicePreset preset, double sampleRate) : base(sampleRate)
        {
            _preset = preset;
            _phases = new double[_preset.Harmonics.Length];

            // VARHANNÍ OBÁLKA:
            NoteEnvelope.AttackTime = 0.015f;  // Rychlý náběh
            NoteEnvelope.DecayTime = 0.05f;
            NoteEnvelope.SustainLevel = 1.0f;  // <--- DRŽÍ 100% HLASITOST POKUD DRŽÍŠ KLÁVESU!
            NoteEnvelope.ReleaseTime = 0.03f;  // Rychlé vypnutí po uvolnění

            _chiffFilter.SetParams(_preset.ChiffFilterFreqHz, _preset.ChiffFilterQ, sampleRate);
        }

        public override void NoteOn()
        {
            base.NoteOn();
            _chiffEnvelope = 1.0; // Reset obálky pro startovní zapraskání píšťaly
        }

        protected override double CalculateWaveform(double frequency)
        {
            // 1. Zvuk alikvótních píšťal (Harmonics)
            double organSound = 0;
            for (int i = 0; i < _preset.Harmonics.Length; i++)
            {
                double phase = AdvancePhase(ref _phases[i], _preset.Harmonics[i].FrequencyMultiplier, frequency);
                organSound += Math.Sin(phase * 2.0 * Math.PI) * _preset.Harmonics[i].Amplitude;
            }

            // 2. Chiff (zapraskání vzduchu při otevření ventilu)
            double chiff = 0;
            if (_chiffEnvelope > 0.001)
            {
                double noise = _noiseGen.NextSample((int)SampleRate);
                chiff = _chiffFilter.Process(noise) * _chiffEnvelope * _preset.ChiffNoiseGain;
                _chiffEnvelope *= Math.Exp(-1.0 / (SampleRate * _preset.ChiffDurationSec));
            }

            return organSound + chiff;
        }
    }
}