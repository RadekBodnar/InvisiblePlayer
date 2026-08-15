using System;

namespace InvisiblePlayer.Core.Filters
{
    public class BandPassFilter
    {
        private double _b0, _b1, _b2, _a1, _a2;
        private double _x1, _x2, _y1, _y2;

        /// <summary>
        /// Nastaví koeficienty pásmové propusti (biquad, RBJ Audio EQ Cookbook).
        /// Vstupy se klampují stejně jako u LowPassFilter - bez toho stačilo q = 0
        /// k dělení nulou, alpha = Infinity a NaN koeficientům. NaN pak přežije
        /// i Math.Clamp ve výstupním mixu, takže by filtr otrávil celý zvuk natrvalo.
        /// </summary>
        public void SetParams(double centerFreqHz, double q, double sampleRate = 44100.0)
        {
            if (sampleRate <= 0.0) sampleRate = 44100.0;

            // Nyquist: nad ~45 % vzorkovací frekvence je odezva nestabilní.
            centerFreqHz = Math.Clamp(centerFreqHz, 20.0, sampleRate * 0.45);

            // 0.1 = velmi široké pásmo, 20.0 = velmi úzké. Nula je zakázaná.
            q = Math.Clamp(q, 0.1, 20.0);

            double w0 = 2.0 * Math.PI * centerFreqHz / sampleRate;
            double alpha = Math.Sin(w0) / (2.0 * q);

            double a0 = 1.0 + alpha;
            _b0 = alpha / a0;
            _b1 = 0.0;
            _b2 = -alpha / a0;
            _a1 = (-2.0 * Math.Cos(w0)) / a0;
            _a2 = (1.0 - alpha) / a0;
        }

        public double Process(double input)
        {
            double y = _b0 * input + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = input;
            _y2 = _y1; _y1 = y;
            return y;
        }

        /// <summary>
        /// Vynuluje vnitřní stav filtru (obdoba LowPassFilter.Reset).
        /// Nutné při znovupoužití instance pro nový tón, jinak do něj prosákne
        /// dozvuk předchozího.
        /// </summary>
        public void Reset()
        {
            _x1 = _x2 = _y1 = _y2 = 0.0;
        }
    }
}
