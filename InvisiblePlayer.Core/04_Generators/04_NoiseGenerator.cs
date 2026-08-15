using System;

namespace InvisiblePlayer.Core.Generators
{
    public class NoiseGenerator : IOscillator
    {
        private readonly int? _seed;
        private Random _random;

        /// <summary>
        /// Generátor bílého šumu.
        /// </summary>
        /// <param name="seed">
        /// Semínko generátoru. <c>null</c> (výchozí) = nepředvídatelný šum, tedy
        /// chování pro běžné přehrávání.
        ///
        /// Konkrétní hodnota dělá výstup REPRODUKOVATELNÝM, což je nutná podmínka
        /// pro charakterizační testy zvuku: bez semínka dá dvojí spuštění téhož
        /// kódu jiné vzorky (chiff/úder kladívka jsou šum) a nelze pak prokázat,
        /// že refaktor zvuk nezměnil.
        /// </param>
        public NoiseGenerator(int? seed = null)
        {
            _seed = seed;
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public void SetFrequency(float frequencyHz)
        {
            // Šum nemá konkrétní frekvenci (pitch), ale rozhraní to vyžaduje
        }

        /// <summary>
        /// U seedovaného generátoru vrátí posloupnost na začátek (opakovatelné
        /// spuštění tónu dá stejný šum). U neseedovaného nedělá nic - tam by
        /// "reset" neměl smysl.
        /// </summary>
        public void Reset()
        {
            if (_seed.HasValue) _random = new Random(_seed.Value);
        }

        public float NextSample(int sampleRate)
        {
            // Náhodný vzorek od -1.0 do +1.0
            return (float)(_random.NextDouble() * 2.0 - 1.0);
        }
    }
}
