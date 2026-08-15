using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NAudio.Wave;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using ScottPlot;
using InvisiblePlayer.Core.Analysis;

using Window = System.Windows.Window;
using MediaColor = System.Windows.Media.Color; // ALIAS PRO VYŘEŠENÍ CHYBY CS0104

namespace InvisiblePlayer.Analyzer
{
    public partial class MainWindow : Window
    {
        private WaveInEvent? _waveIn;
        private const int SampleRate = 44100;

        // _fftSize a _sampleBuffer vlastní VÝHRADNĚ audio vlákno (OnAudioDataAvailable).
        // UI vlákno velikost nemění přímo - jen zapíše požadavek do _pendingFftSize
        // a audio vlákno ho vyzvedne na hranici okna. Přímá výměna reference z UI
        // vlákna dřív mohla trefit okamžik mezi kontrolou délky a zápisem
        // (IndexOutOfRangeException při zmenšení 262144 -> 8192).
        private int _fftSize = 8192;
        private float[] _sampleBuffer = new float[8192];
        private int _bufferIndex = 0;
        private float _maxPeak = 0;

        // Požadavek na změnu velikosti FFT z UI vlákna. 0 = nic nečeká.
        private volatile int _pendingFftSize;

        // Běží už jedno překreslení na UI vlákně? Přechod z blokujícího
        // Dispatcher.Invoke na neblokující InvokeAsync odstraní zablokování audio
        // vlákna, ale zároveň odstraní i zpětný tlak - fronta InvokeAsync by rostla
        // donekonečna, kdyby UI nestíhalo. Tenhle příznak frontu drží na max 1 položce.
        private volatile bool _renderPending;

        // Zapisuje UI vlákno, čte audio vlákno -> volatile kvůli viditelnosti.
        // _waitForSnap se navíc čte v těsné per-sample smyčce, kde by JIT jinak
        // směl hodnotu nacachovat do registru a zápis nikdy neuvidět.
        private volatile bool _isFrozen = false;
        private volatile bool _waitForSnap = false;
        private volatile bool _isMeasuringSnap = false;

        public MainWindow()
        {
            InitializeComponent();
            LoadMicrophones();
            InitFftOptions();
            SetupPlot();
            StartAudioCapture();
        }





        private void ApplyMagicAnalysis(double[] freqsLog, double[] magnitudesDb, double[] freqsHz)
        {
            int selectedMode = ComboMagicMode.SelectedIndex;
            string resultText = "";

            switch (selectedMode)
            {
                // Vlastní analytika žije v InvisiblePlayer.Core.Analysis.SpectrumAnalysis
                // (nález P11) - tady zůstala jen vazba na UI.
                case 0: // 🎹 VARHANY
                    resultText = SpectrumAnalysis.DescribeOrganPartials(freqsHz, magnitudesDb);
                    break;

                case 1: // 🔔 ZVONY
                    resultText = SpectrumAnalysis.DescribeBellPartials(freqsHz, magnitudesDb);
                    break;

                case 2: // 🥁 ŠUMY
                    resultText = SpectrumAnalysis.DescribeNoiseShape(freqsHz, magnitudesDb);
                    break;
            }

            TxtMagicOutput.Text = resultText;
        }

        private void BtnSnap_Click(object sender, RoutedEventArgs e)
        {
            TriggerSnap();
        }








        private void LoadMicrophones()
        {
            ComboMicrophones.Items.Clear();
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var capabilities = WaveIn.GetCapabilities(i);
                ComboMicrophones.Items.Add(capabilities.ProductName);
            }
            if (ComboMicrophones.Items.Count > 0)
                ComboMicrophones.SelectedIndex = 0;
        }

        private void InitFftOptions()
        {
            ComboFftSize.Items.Clear();
            ComboFftSize.Items.Add("8 192 (0,18 s - Fuk / Náběh, step 5,38 Hz)");
            ComboFftSize.Items.Add("16 384 (0,37 s - Rychlý náhled, step 2,69 Hz)");
            ComboFftSize.Items.Add("65 536 (1,48 s - Standard Břitva, step 0,67 Hz)");
            ComboFftSize.Items.Add("262 144 (5,93 s - Sub-Bass 16'/32', step 0,17 Hz)");

            ComboFftSize.SelectedIndex = 0;
        }

        private void ComboFftSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int requested = ComboFftSize.SelectedIndex switch
            {
                0 => 8192,
                1 => 16384,
                2 => 65536,
                3 => 262144,
                _ => 16384,
            };

            // Buffer NEPŘEALOKOVÁVÁME zde - to je UI vlákno a audio vlákno do pole
            // právě zapisuje. Jen předáme požadavek; vyzvedne si ho ApplyPendingFftSize()
            // na hranici okna, tedy v okamžiku, kdy je _bufferIndex stejně nulován.
            _pendingFftSize = requested;

            // Startovní volání (z InitFftOptions v konstruktoru) proběhne dřív, než
            // se rozjede capture - tam je bezpečné aplikovat rovnou.
            if (_waveIn == null) ApplyPendingFftSize();
        }

        /// <summary>
        /// Vyzvedne čekající změnu velikosti FFT. Volá se VÝHRADNĚ z audio vlákna
        /// (nebo před jeho startem), takže výměna reference nemůže kolidovat se zápisem.
        /// </summary>
        private void ApplyPendingFftSize()
        {
            int pending = _pendingFftSize;
            if (pending == 0 || pending == _fftSize)
            {
                _pendingFftSize = 0;
                return;
            }

            _fftSize = pending;
            _sampleBuffer = new float[pending];
            _bufferIndex = 0;
            _maxPeak = 0;
            _pendingFftSize = 0;
        }

        private void SetupPlot()
        {
            WpfPlot1.Plot.Title("Výpomocný frekvenční analyzátor");
            WpfPlot1.Plot.XLabel("Frekvence (Hz)");
            WpfPlot1.Plot.YLabel("Amplituda (dBFS)");

            double[] tickPositions = new double[] {
                Math.Log10(20), Math.Log10(50), Math.Log10(100), Math.Log10(200),
                Math.Log10(500), Math.Log10(1000), Math.Log10(2000), Math.Log10(5000), Math.Log10(10000)
            };

            string[] tickLabels = new string[] {
                "20 Hz", "50 Hz", "100 Hz", "200 Hz",
                "500 Hz", "1 kHz", "2 kHz", "5 kHz", "10 kHz"
            };

            WpfPlot1.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(tickPositions, tickLabels);
            WpfPlot1.Plot.Axes.SetLimits(Math.Log10(20), Math.Log10(10000), -90, 0);
            WpfPlot1.Refresh();
        }

        private void StartAudioCapture()
        {
            if (_waveIn != null) return;

            int selectedDevice = ComboMicrophones.SelectedIndex >= 0 ? ComboMicrophones.SelectedIndex : 0;
            _waveIn = new WaveInEvent
            {
                DeviceNumber = selectedDevice,
                WaveFormat = new WaveFormat(SampleRate, 16, 1)
            };

            _waveIn.DataAvailable += OnAudioDataAvailable;
            _waveIn.StartRecording();
        }

        private void StopAudioCapture()
        {
            if (_waveIn == null) return;

            _waveIn.DataAvailable -= OnAudioDataAvailable;
            _waveIn.StopRecording();
            _waveIn.Dispose();
            _waveIn = null;
        }

        /// <summary>
        /// Přepnutí vstupního zařízení za běhu. Bez tohoto handleru se index mikrofonu
        /// četl jen jednou při startu a výběr v UI neměl žádný efekt - u měřicího
        /// nástroje to znamenalo měřit z jiného vstupu, než uživatel vidí zvolený.
        /// </summary>
        private void ComboMicrophones_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Startovní naplnění comboboxu (LoadMicrophones v konstruktoru) proběhne
            // dřív než StartAudioCapture - tehdy není co restartovat.
            if (_waveIn == null) return;

            StopAudioCapture();

            // Nové zařízení = nesouvisející signál, staré okno by bylo slepencem dvou vstupů.
            _bufferIndex = 0;
            _maxPeak = 0;

            StartAudioCapture();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Bez tohoto zůstalo nahrávací zařízení obsazené a callback běžel dál
            // nad zavřeným oknem.
            StopAudioCapture();
            base.OnClosed(e);
        }




        private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
        {
            // Čekající změnu velikosti FFT vyzvedneme na začátku bloku, tedy mimo
            // zápisovou smyčku - jsme na audio vlákně, které pole vlastní.
            if (_pendingFftSize != 0) ApplyPendingFftSize();

            // Lichý počet bytů by na posledním kroku sáhl za platná data.
            int usableBytes = e.BytesRecorded - (e.BytesRecorded % 2);

            for (int i = 0; i < usableBytes; i += 2)
            {
                short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                float floatSample = sample / 32768.0f;

                float absSample = Math.Abs(floatSample);
                if (absSample > _maxPeak) _maxPeak = absSample;

                // Pokud jsme zmáčkli SNAP, okamžitě zahodíme stará data
                if (_waitForSnap)
                {
                    _bufferIndex = 0;
                    _waitForSnap = false;
                }

                if (_bufferIndex < _sampleBuffer.Length)
                {
                    _sampleBuffer[_bufferIndex] = floatSample;
                    _bufferIndex++;
                }

                // Máme naplněné celé nové okno!
                if (_bufferIndex >= _fftSize)
                {
                    _bufferIndex = 0;

                    // Bezpečně zjišťujeme stav z naší C# proměnné (žádné WPF UI!)
                    bool wasSnapCapture = _isMeasuringSnap;
                    if (wasSnapCapture) _isMeasuringSnap = false;

                    // ZÁVOD, KTERÝ TU BYL (odhaleno externím review):
                    // Dřív se hned po ProcessFFT nastavilo _isFrozen = true. Dokud byl
                    // Dispatcher.Invoke BLOKUJÍCÍ, lambda stihla vykreslit dřív, než se
                    // příznak nastavil. Po přechodu na InvokeAsync (oprava M2) se pořadí
                    // rozvázalo: lambda doběhla až POTOM, narazila na 'if (_isFrozen) return'
                    // a snímek SNAP vůbec nevykreslila - na grafu zůstal ten předchozí.
                    //
                    // Zmrazení proto NEDĚLÁME odsud. Předáváme ho do ProcessFFT, která ho
                    // provede UVNITŘ té samé UI operace, co kreslí - tedy atomicky vůči ní.
                    ProcessFFT(_sampleBuffer, _maxPeak, freezeAfterRender: wasSnapCapture);
                    _maxPeak = 0;
                }
            }
        }



        private void ProcessFFT(float[] samples, float peak, bool freezeAfterRender = false)
        {
            int n = samples.Length;
            double[] window = MathNet.Numerics.Window.Hann(n);

            Complex32[] buffer = new Complex32[n];
            for (int i = 0; i < n; i++)
            {
                float windowedSample = samples[i] * (float)window[i];
                buffer[i] = new Complex32(windowedSample, 0);
            }

            Fourier.Forward(buffer, FourierOptions.Matlab);

            int halfSize = n / 2;
            double[] freqsLog = new double[halfSize];
            double[] freqsHz = new double[halfSize];
            double[] magnitudesDb = new double[halfSize];

            for (int i = 0; i < halfSize; i++)
            {
                double freqHz = (i * (double)SampleRate) / n;
                freqsHz[i] = freqHz;
                freqsLog[i] = freqHz > 0 ? Math.Log10(freqHz) : 0;

                // OPRAVA S9: kompenzace koherentního zisku Hannova okna (0,5).
                // Bez ní vycházela amplituda o ~6 dB nižší, než ve skutečnosti byla.
                magnitudesDb[i] = SpectrumAnalysis.MagnitudeToDbFs(
                    buffer[i].Magnitude, n, SpectrumAnalysis.HannCoherentGain);
            }

            // Špička je časový vzorek, ne FFT přihrádka -> žádné okno ani /n.
            double peakDb = 20 * Math.Log10(Math.Max(peak, 1e-5));
            double vuPercent = Math.Min(100, Math.Max(0, (peakDb + 60) * (100.0 / 60.0)));

            // Předchozí překreslení ještě běží -> tenhle snímek zahodíme. Bez toho by
            // fronta InvokeAsync rostla donekonečna, kdyby UI nestíhalo tempo capture.
            // Výjimka: SNAP odchyt musí projít vždy, jinak by uživateli utekl.
            // SNAP odchyt musí projít VŽDY - jinak by ho zahodil zpětný tlak
            // a uživateli by "zmrazení" ukázalo cizí snímek.
            if (_renderPending && !freezeAfterRender) return;
            _renderPending = true;

            Dispatcher.InvokeAsync(() =>
            {
              try
              {
                // 1. VU METR SE AKTUALIZUJE VŽDY
                VuMeter.Value = vuPercent;
                TxtVuDb.Text = $"{peakDb:F1} dB";

                if (peakDb >= -1.0)
                    VuMeter.Foreground = new SolidColorBrush(MediaColor.FromRgb(231, 76, 60));
                else if (peakDb >= -6.0)
                    VuMeter.Foreground = new SolidColorBrush(MediaColor.FromRgb(241, 196, 15));
                else
                    VuMeter.Foreground = new SolidColorBrush(MediaColor.FromRgb(46, 204, 113));

                // 2. GRAF A KOUZLA JEN KDYŽ NEJSME ZMRAZENI.
                // Výjimka: tohle JE ten snímek, kvůli kterému se mrazí - ten se
                // vykreslit musí, teprve pak se zamkne (viz finally níže).
                if (_isFrozen && !freezeAfterRender) return;

                // Vykreslení grafu
                WpfPlot1.Plot.Clear();
                var scatter = WpfPlot1.Plot.Add.Scatter(freqsLog, magnitudesDb);
                scatter.LineWidth = 1.5f;
                scatter.MarkerSize = 0;
                WpfPlot1.Refresh();

                // Výpočet a výpis Kouzla
                ApplyMagicAnalysis(freqsLog, magnitudesDb, freqsHz);
              }
              finally
              {
                // Zmrazení AŽ TEĎ, ve stejné UI operaci, která snímek vykreslila.
                // Kdyby se nastavovalo z audio vlákna hned po ProcessFFT, lambda by
                // dorazila později a snímek by zahodila.
                if (freezeAfterRender)
                {
                    _isFrozen = true;
                    BtnSnap.Content = "📸 SNAP [Enter]";
                }

                // MUSÍ být ve finally - i větev "return" u _isFrozen musí frontu uvolnit,
                // jinak by se po prvním zmrazení grafu překreslování zaseklo natrvalo.
                _renderPending = false;
              }
            });
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // ENTER nebo MEZERNÍK = SNAP (Zachytit)
            if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Space)
            {
                TriggerSnap();
                e.Handled = true; // Zamezí nechtěnému klikání na jiné prvky
            }
            // ESC = Zpět do Živého náhledu (Odmrazit)
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                TriggerLive();
                e.Handled = true;
            }
        }


        private void TriggerSnap()
        {
            _isFrozen = false;
            _maxPeak = 0;          // jinak by VU metr ukázal špičku z předchozího okna
            _waitForSnap = true;
            _isMeasuringSnap = true;
            BtnSnap.Content = "⏳ MĚŘÍM...";
        }

        private void TriggerLive()
        {
            _isFrozen = false;
            _isMeasuringSnap = false;
            BtnSnap.Content = "📸 SNAP [Enter]";
            TxtMagicOutput.Text = "Živý náhled spuštěn... Stiskni [Enter] pro zachycení okamžiku.";
        }


        private void BtnLive_Click(object sender, RoutedEventArgs e)
        {
            TriggerLive();
        }



    }
}