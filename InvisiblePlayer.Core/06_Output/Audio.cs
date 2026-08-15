using System;
using InvisiblePlayer.Core.Generators;
using InvisiblePlayer.Core.ToneEngine;
using NAudio.Wave;

namespace InvisiblePlayer.Core.Output

{
    public class AudioEngine : IWaveProvider, IDisposable
    {
        private WaveOutEvent? _waveOut;
        private readonly InvisiblePlayer.Core.ToneEngine.ToneEngine _synth;

        // Nejvyšší absolutní hodnota vzorku od posledního čtení (ReadPeak) - obdoba
        // MaxLeftPeak/MaxRightPeak v AudioPlayer.cs, jen pro mono výstup varhan.
        private float _peakSinceLastRead;

        public WaveFormat WaveFormat { get; }

        public AudioEngine(InvisiblePlayer.Core.ToneEngine.ToneEngine synth, int sampleRate = 44100)
        {
            _synth = synth;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1); // 44.1 kHz, Mono, 32-bit Float
        }

        /// <summary>
        /// Spustí audio výstup na zvukovou kartu.
        /// </summary>
        public void Start()
        {
            if (_waveOut != null) return;

            _waveOut = new WaveOutEvent
            {
                DesiredLatency = 50, // Nízká latence (50 ms) pro živé hraní bez zpoždění
                NumberOfBuffers = 2
            };

            _waveOut.Init(this);
            _waveOut.Play();
        }

        /// <summary>
        /// Zastaví audio výstup.
        /// </summary>
        public void Stop()
        {
            if (_waveOut != null)
            {
                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }
        }

        /// <summary>
        /// Callback od NAudio – plníme zvukový buffer vzorky z našeho PolySynthu.
        /// </summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            // Přepočet počtu bytů na float vzorky (1 Float = 4 Byty)
            int sampleCount = count / 4;
            var waveBuffer = new WaveBuffer(buffer);

            for (int i = 0; i < sampleCount; i++)
            {
                // Získáme namixovaný vzorek z našeho syntetizéru (-1.0f až +1.0f)
                float sample = (float)_synth.GenerateNextMixSample();

                float abs = Math.Abs(sample);
                if (abs > _peakSinceLastRead) _peakSinceLastRead = abs;

                // Zapíšeme do NAudio bufferu
                waveBuffer.FloatBuffer[offset / 4 + i] = sample;
            }

            return count;
        }

        /// <summary>
        /// Vrátí nejvyšší absolutní úroveň vzorku od posledního volání a vynuluje počítadlo.
        /// Určeno pro VU metr (stejný princip jako AudioPlayer.ReadPeakLevels() u MP3 přehrávače).
        /// </summary>
        public float ReadPeak()
        {
            float peak = _peakSinceLastRead;
            _peakSinceLastRead = 0f;
            return peak;
        }

        /// <summary>
        /// True, pokud od posledního volání došlo k oříznutí alespoň jednoho vzorku
        /// (rozsah -1.0..1.0). Čtení příznak nuluje - stejný princip jako ReadPeak().
        /// Průchozí hodnota ze ToneEngine.
        /// </summary>
        public bool ReadClipDetected() => _synth.ReadClipDetected();

        public void Dispose()
        {
            Stop();
        }
    }
}
