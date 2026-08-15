using System;

namespace InvisiblePlayer.Core.Generators
{
    public abstract class SynthVoice
    {
        // Vlastnosti, ne pole (CA1051): odvozené třídy na ně sahají, takže jde
        // o veřejnou plochu typu - a ta se do budoucna mění snáz u vlastnosti.
        protected double SampleRate { get; }
        protected AdsrEnvelope NoteEnvelope { get; } = new AdsrEnvelope();
        protected bool HasStarted { get; private set; }

        // Konec tónu: Až po stisku (HasStarted) a doznění obálky (Idle)
        public bool IsFinished => HasStarted && !NoteEnvelope.IsActive;

        protected SynthVoice(double sampleRate)
        {
            SampleRate = sampleRate;
        }

        public virtual void NoteOn()
        {
            HasStarted = true;
            NoteEnvelope.TriggerGate(true);
        }

        public virtual void NoteOff()
        {
            NoteEnvelope.TriggerGate(false);
        }

        /// <summary>
        /// Pomocná jednotná metoda pro posun fáze oscilátoru (0.0 až 1.0)
        /// </summary>
        protected double AdvancePhase(ref double phase, double frequencyMult, double baseFreq)
        {
            phase += (baseFreq * frequencyMult) / SampleRate;
            if (phase >= 1.0) phase -= Math.Floor(phase);
            return phase;
        }

        /// <summary>
        /// Jednotné generování vzorku pro libovolný nástroj.
        /// </summary>
        public double GenerateSample(double frequency)
        {
            // Obálka se spočítá automaticky pro VŠECHNY nástroje stejně!
            double envelopeValue = NoteEnvelope.Process((int)SampleRate);

            // Pokud tón nehraje nebo dozněl, šetříme procesor
            if (envelopeValue <= 0.00001 && HasStarted) return 0.0;

            // Zavolá specifický tvar vlny pro daný nástroj
            double rawWave = CalculateWaveform(frequency);

            return rawWave * envelopeValue;
        }

        /// <summary>
        /// Každý nástroj zde definuje POUZE svůj jedinečný tvar vlny/zvuku.
        /// </summary>
        protected abstract double CalculateWaveform(double frequency);
    }
}