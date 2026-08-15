using System;

namespace InvisiblePlayer.Core.Generators
{
    public enum EnvelopeState
    {
        Idle,
        Attack,
        Decay,
        Sustain,
        Release
    }

    public class AdsrEnvelope
    {
        // Časy v sekundách a úroveň sustainu (0.0 až 1.0)
        public float AttackTime { get; set; } = 0.01f;   // 10 ms rychlý náběh
        public float DecayTime { get; set; } = 0.1f;     // 100 ms pokles
        public float SustainLevel { get; set; } = 0.8f;  // Udržení na 80% hlasitosti
        public float ReleaseTime { get; set; } = 0.3f;   // 300 ms plynulé doznění

        public EnvelopeState State { get; private set; } = EnvelopeState.Idle;
        public float CurrentLevel { get; private set; } = 0.0f;

        public bool IsActive => State != EnvelopeState.Idle;

        public void TriggerGate(bool gateOn)
        {
            if (gateOn)
            {
                State = EnvelopeState.Attack;
            }
            else if (State != EnvelopeState.Idle)
            {
                State = EnvelopeState.Release;
            }
        }

        public float Process(int sampleRate)
        {
            if (sampleRate <= 0 || State == EnvelopeState.Idle)
                return 0.0f;

            float sampleTime = 1.0f / sampleRate;

            switch (State)
            {
                case EnvelopeState.Attack:
                    if (AttackTime <= 0.0f)
                    {
                        CurrentLevel = 1.0f;
                        State = EnvelopeState.Decay;
                    }
                    else
                    {
                        CurrentLevel += sampleTime / AttackTime;
                        if (CurrentLevel >= 1.0f)
                        {
                            CurrentLevel = 1.0f;
                            State = EnvelopeState.Decay;
                        }
                    }
                    break;

                case EnvelopeState.Decay:
                    if (DecayTime <= 0.0f)
                    {
                        CurrentLevel = SustainLevel;
                        State = StateAfterDecay();
                    }
                    else
                    {
                        CurrentLevel -= (1.0f - SustainLevel) * (sampleTime / DecayTime);
                        if (CurrentLevel <= SustainLevel)
                        {
                            CurrentLevel = SustainLevel;
                            State = StateAfterDecay();
                        }
                    }
                    break;

                case EnvelopeState.Sustain:
                    CurrentLevel = SustainLevel;

                    // Pojistka pro případ, že se SustainLevel sníží na nulu až
                    // za běhu tónu - i pak musí obálka dojet do Idle.
                    if (SustainLevel <= 0.0f) State = EnvelopeState.Idle;
                    break;

                case EnvelopeState.Release:
                    if (ReleaseTime <= 0.0f)
                    {
                        CurrentLevel = 0.0f;
                        State = EnvelopeState.Idle;
                    }
                    else
                    {
                        CurrentLevel -= sampleTime / ReleaseTime;
                        if (CurrentLevel <= 0.0f)
                        {
                            CurrentLevel = 0.0f;
                            State = EnvelopeState.Idle;
                        }
                    }
                    break;
            }

            return Math.Clamp(CurrentLevel, 0.0f, 1.0f);
        }

        /// <summary>
        /// Kam obálka přejde po dokončení decay.
        /// </summary>
        /// <remarks>
        /// OPRAVA S5: dřív se vždy šlo do Sustain. U nástrojů se SustainLevel = 0
        /// (CembaloVoice, BellVoice - brnknutá struna a zvon sustain nemají, jen
        /// doznívají) tam obálka uvázla trvale na úrovni 0: byla němá, ale
        /// IsActive hlásilo true, takže SynthVoice.IsFinished zůstalo false
        /// a ToneEngine hlas nikdy neodstranil ze seznamu.
        ///
        /// Následek: němé hlasy se hromadily a přes voiceCount snižovaly
        /// kompenzací 1/sqrt(N) hlasitost tónů, které skutečně zněly - deset
        /// dohraných cembalových not utlumilo jedenáctou o ~10 dB.
        ///
        /// Sustain na nulové úrovni nedává smysl: tón, který doznívá do ticha,
        /// je dohraný.
        /// </remarks>
        private EnvelopeState StateAfterDecay()
            => SustainLevel <= 0.0f ? EnvelopeState.Idle : EnvelopeState.Sustain;

        public void Reset()
        {
            State = EnvelopeState.Idle;
            CurrentLevel = 0.0f;
        }

      
    }
}