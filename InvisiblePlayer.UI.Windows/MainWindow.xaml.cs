using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using InvisiblePlayer.Core;
using LibVLCSharp.Shared;

namespace InvisiblePlayer.UI.Windows
{
    public partial class MainWindow : Window
    {
        private DirectoryNavigator _navigator = new();
        private bool _isFullscreen = true;

        // VLC objekty
        private LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;

        public MainWindow()
        {
            InitializeComponent();

            // Inicializace samotného jádra LibVLC
            LibVLCSharp.Shared.Core.Initialize();
            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);

            // Propojení VLC s WPF prvkem v XAML
            VideoViewer.MediaPlayer = _mediaPlayer;

            this.MouseLeftButtonDown += MainWindow_MouseLeftButtonDown;
            
            this.WindowStyle = WindowStyle.None;
            this.WindowState = WindowState.Maximized;
            this.Topmost = true; // Zabezpečí překrytí hlavního panelu
        }

        public void PlayFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;

                _navigator.LoadDirectory(filePath);
                StartPlayingCurrentFile();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Chyba načítání videa: {ex.Message}");
            }
        }

        private void StartPlayingCurrentFile()
        {
            string? currentFile = _navigator.CurrentFile;
            if (currentFile != null && File.Exists(currentFile) && _libVLC != null && _mediaPlayer != null)
            {
                using var media = new Media(_libVLC, new Uri(currentFile));
                _mediaPlayer.Play(media);
            }
        }

        private void ToggleFullscreen()
        {
            if (_isFullscreen)
            {
                this.WindowStyle = WindowStyle.SingleBorderWindow;
                this.WindowState = WindowState.Normal;
                this.Topmost = false;
                _isFullscreen = false;
            }
            else
            {
                this.WindowStyle = WindowStyle.None;
                this.WindowState = WindowState.Maximized;
                this.Topmost = true;
                _isFullscreen = true;
            }
        }

        private void MainWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleFullscreen();
            }
        }

        private void MainWindow_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_mediaPlayer == null) return;

            // Hlasitost ve VLC je 0 až 100
            if (e.Delta > 0)
            {
                _mediaPlayer.Volume = Math.Min(100, _mediaPlayer.Volume + 5);
            }
            else
            {
                _mediaPlayer.Volume = Math.Max(0, _mediaPlayer.Volume - 5);
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (_mediaPlayer == null) return;

            bool isAltPressed = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
            bool isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

            // Alt + F4
            if (e.Key == Key.System && e.SystemKey == Key.F4)
            {
                Close();
                return;
            }

            // Přepínání Fullscreen / Okno
            if ((e.Key == Key.Return && isAltPressed) ||
                (e.Key == Key.Return) ||
                (e.Key == Key.F) ||
                (e.Key == Key.F && isCtrlPressed) ||
                (e.Key == Key.L && isCtrlPressed))
            {
                ToggleFullscreen();
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;

                case Key.Space:
                    if (_mediaPlayer.IsPlaying)
                    {
                        _mediaPlayer.Pause();
                    }
                    else
                    {
                        _mediaPlayer.Play();
                    }
                    e.Handled = true;
                    break;

                // Hlasitost šipkami nahoru/dolů
                case Key.Up:
                    _mediaPlayer.Volume = Math.Min(100, _mediaPlayer.Volume + 5);
                    e.Handled = true;
                    break;

                case Key.Down:
                    _mediaPlayer.Volume = Math.Max(0, _mediaPlayer.Volume - 5);
                    e.Handled = true;
                    break;

                // BLESKOVÉ PŘEVÍJENÍ ŠIPKAMI (+/- 5000 ms)
                case Key.Right:
                    _mediaPlayer.SeekTo(TimeSpan.FromMilliseconds(_mediaPlayer.Time + 5000));
                    e.Handled = true;
                    break;

                case Key.Left:
                    _mediaPlayer.SeekTo(TimeSpan.FromMilliseconds(Math.Max(0, _mediaPlayer.Time - 5000)));
                    e.Handled = true;
                    break;

                // Přeskakování souborů. Návratovou hodnotu je nutné kontrolovat:
                // null znamená "další/předchozí soubor není" (nález S6). Bez toho
                // by se na konci složky znovu spustil tentýž soubor od začátku.
                case Key.PageDown:
                    if (_navigator.GetNextFile() != null) StartPlayingCurrentFile();
                    e.Handled = true;
                    break;

                case Key.PageUp:
                    if (_navigator.GetPreviousFile() != null) StartPlayingCurrentFile();
                    e.Handled = true;
                    break;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Úklid paměti při zavření okna
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();

            base.OnClosed(e);
        }
    }
}