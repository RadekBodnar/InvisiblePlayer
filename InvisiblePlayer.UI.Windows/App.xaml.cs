using InvisiblePlayer.Core;            // Pro AudioEngine
using InvisiblePlayer.Core.Generators;
using InvisiblePlayer.Core.Input;      // Pro InputManager
using InvisiblePlayer.Core.Output;
using InvisiblePlayer.Core.Synthesis;
using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace InvisiblePlayer.UI.Windows
{
    public static class MediaLauncher
    {
        // Seznam přípon, které považujeme za video
        private static readonly string[] VideoExtensions = new[]
        {
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v"
        };

        public static void Launch(string filePath, InputManager inputManager)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (VideoExtensions.Contains(ext))
            {
                // VIDEO -> Spustíme WPF okno s LibVLC
                Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;

                var window = new MainWindow();
                Application.Current.MainWindow = window;
                window.Show();
                window.PlayFile(filePath);
            }
            else if (ext == ".mid" || ext == ".midi")
            {
                // MIDI -> Spustíme přehrávání souboru přes náš nový InputManager v Core
                _ = inputManager.PlayMidiFileAsync(filePath);

                // Spustíme VGA konzoli pro vizualizaci
                VgaEngine.Run(filePath);
            }
            else
            {
                // AUDIO / OSTATNÍ -> Spustíme původní VGA konzoli
                VgaEngine.Run(filePath);

                // Po skončení konzole aplikaci ukončíme
                Application.Current.Shutdown();
            }
        }
    }

    public partial class App : Application
    {
        private InputManager? _inputManager;
        private ToneEngine? _toneEngine;
        private AudioEngine? _audioEngine;

        // Statická reference, aby k aktuálnímu AudioEngine (varhany) mohly
        // přistoupit i jiné statické třídy jako VgaEngine (pro VU metr).
        public static AudioEngine? OrganEngine { get; private set; }


        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. INICIALIZACE NOVÉHO TONEENGINE (Rejstříky / Varhany)
            _toneEngine = new ToneEngine();

            // Pokud AudioEngine přijímá generátor zvuku:
            _audioEngine = new AudioEngine(_toneEngine);
            _audioEngine.Start();

            OrganEngine = _audioEngine;

            // 2. INICIALIZACE CORE INPUTU (Živé piano z USB / Casio)
            _inputManager = new InputManager();

            _inputManager.OnInputEvent += evt =>
            {
                // ZVUK! Předáme stisknutou / uvolněnou notu přímo do ToneEngine
                if (evt.Type == InputEventType.NoteOn && evt.Velocity > 0)
                {
                    _toneEngine?.NoteOn(evt.Note.Number);
                }
                else
                {
                    _toneEngine?.NoteOff(evt.Note.Number);
                }

                System.Diagnostics.Debug.WriteLine($"[{evt.Source}] {evt.Type} | Nota: {evt.Note.Number} ({evt.Note.FrequencyHz:F1} Hz)");
            };

            // MIDI soubor se přehrává fire-and-forget (viz MediaLauncher.Launch),
            // takže bez tohoto handleru by chyba čtení souboru zmizela beze stopy.
            _inputManager.OnPlaybackError += (path, ex) =>
                Dispatcher.InvokeAsync(() => MessageBox.Show(
                    $"Nelze přehrát MIDI soubor:\n{path}\n\n{ex.GetBaseException().Message}",
                    "InvisiblePlayer"));

            // Spustíme odchytávání z piana na pozadí
            _inputManager.StartLiveDevice("USB MIDI"); // nebo název tvého Casia




            // 3. KONTROLA ARGUMENTŮ (Spuštění bez souboru = zůstane běžet jako hrací stůl pro piano)
            if (e.Args.Length == 0)
            {
                // Aplikace běží na pozadí a hraje přímo z kláves!
                return;
            }

            // Složíme případně rozsekanou cestu (s mezerami)
            string filePath = string.Join(" ", e.Args).Trim('"');

            try
            {
                filePath = Path.GetFullPath(filePath);
            }
            catch { /* ponecháme původní */ }

            // 4. KONTROLA EXISTENCE SOUBORU
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"CHYBA: Soubor neexistuje!\nNačtená cesta:\n{filePath}", "InvisiblePlayer Error");
                Shutdown();
                return;
            }

            // 5. SPUŠTĚNÍ PŘES MEDIA LAUNCHER
            MediaLauncher.Launch(filePath, _inputManager);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Úklid zdrojů při vypnutí
            _inputManager?.Dispose();
            _audioEngine?.Stop();
            _audioEngine?.Dispose();

            base.OnExit(e);
        }
    }
}
