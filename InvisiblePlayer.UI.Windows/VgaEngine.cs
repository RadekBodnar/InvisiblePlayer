using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using InvisiblePlayer.Core;

namespace InvisiblePlayer.UI.Windows
{
    internal static class VgaEngine
    {
        private static DirectoryNavigator _navigator = new();
        private static AudioPlayer _audioPlayer = new();

        private static double _leftDb = -120.0;
        private static double _rightDb = -120.0;
        private static int _volume = 80;
        private static bool _isPaused = false;

        private static bool[] _channelMuted = new bool[16];
        private static bool _isMidiMode = false;

        #region Windows API pro konzoli a myš
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const int STD_INPUT_HANDLE = -10;
        private const uint ENABLE_PROCESSED_INPUT = 0x0001;
        private const uint ENABLE_LINE_INPUT = 0x0002;
        private const uint ENABLE_ECHO_INPUT = 0x0004;
        private const uint ENABLE_MOUSE_INPUT = 0x0010;
        private const uint ENABLE_EXTENDED_FLAGS = 0x0080;
        private const ushort MOUSE_EVENT = 0x0002;
        private const uint MOUSE_WHEELED = 0x0004;
        private const uint FROM_LEFT_1ST_BUTTON_PRESSED = 0x0001;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleInput, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleInput, uint dwMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PeekConsoleInput(IntPtr hConsoleInput, [Out] INPUT_RECORD[] lpBuffer, uint nLength, out uint lpNumberOfEventsRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadConsoleInput(IntPtr hConsoleInput, [Out] INPUT_RECORD[] lpBuffer, uint nLength, out uint lpNumberOfEventsRead);

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT_RECORD
        {
            [FieldOffset(0)] public ushort EventType;
            [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
            [FieldOffset(4)] public MOUSE_EVENT_RECORD MouseEvent;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEY_EVENT_RECORD
        {
            public bool bKeyDown;
            public ushort wRepeatCount;
            public ushort wVirtualKeyCode;
            public ushort wVirtualScanCode;
            public char uChar;
            public uint dwControlKeyState;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSE_EVENT_RECORD
        {
            public COORD dwMousePosition;
            public uint dwButtonState;
            public uint dwControlKeyState;
            public uint dwEventFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct COORD
        {
            public short X;
            public short Y;
        }
        #endregion

        public static void Run(string filePath)
        {
            // 1. Otevření konzole a vynucení fokusu
            AllocConsole();
            IntPtr hwnd = GetConsoleWindow();
            if (hwnd != IntPtr.Zero)
            {
                SetForegroundWindow(hwnd);
            }

            Console.OutputEncoding = Encoding.UTF8;
            Console.CursorVisible = false;
            Console.Title = "Invisible Player - VGA Console Engine";

            try
            {
                Console.SetBufferSize(Console.WindowWidth, Console.WindowHeight);
            }
            catch { }

            // 2. Nastavení režimu vstupu konzole
            IntPtr hInput = GetStdHandle(STD_INPUT_HANDLE);
            if (GetConsoleMode(hInput, out uint mode))
            {
                mode |= ENABLE_MOUSE_INPUT | ENABLE_EXTENDED_FLAGS;
                // Vypneme řádkový vstup a echo, aby klávesnice reagovala okamžitě
                mode &= ~(ENABLE_LINE_INPUT | ENABLE_ECHO_INPUT);
                SetConsoleMode(hInput, mode);
            }

            if (File.Exists(filePath))
            {
                _navigator.LoadDirectory(filePath);
                UpdateFileTypeState();
            }

            RenderDashboard();

            bool running = true;
            INPUT_RECORD[] recordBuffer = new INPUT_RECORD[1];

            while (running)
            {
                // A) Čtení klávesnice přes standardní .NET Console API (100% spolehlivé)
                while (Console.KeyAvailable)
                {
                    var keyInfo = Console.ReadKey(true);
                    running = HandleInput(keyInfo.Key);
                    RenderDashboard();
                    if (!running) break;
                }

                if (!running) break;

                // B) Zpracování myši (Kolečko + Kliky)
                PeekConsoleInput(hInput, recordBuffer, 1, out uint eventsRead);
                if (eventsRead > 0)
                {
                    ReadConsoleInput(hInput, recordBuffer, 1, out _);
                    var record = recordBuffer[0];

                    // Kolečko myši
                    if (record.EventType == MOUSE_EVENT && record.MouseEvent.dwEventFlags == MOUSE_WHEELED)
                    {
                        int scrollDelta = (int)record.MouseEvent.dwButtonState >> 16;
                        ChangeVolume(scrollDelta > 0 ? 5 : -5);
                        RenderDashboard();
                    }
                    // Kliknutí myší (Levé tlačítko)
                    else if (record.EventType == MOUSE_EVENT && record.MouseEvent.dwEventFlags == 0)
                    {
                        if ((record.MouseEvent.dwButtonState & FROM_LEFT_1ST_BUTTON_PRESSED) != 0)
                        {
                            HandleMouseClick(record.MouseEvent.dwMousePosition.X, record.MouseEvent.dwMousePosition.Y);
                            RenderDashboard();
                        }
                    }
                }

                // C) Kontrola konce skladby -> AUTOMATICKÝ POSUN NA DALŠÍ SOUBOR
                if (!_isMidiMode && !_isPaused && _audioPlayer.TotalTime > TimeSpan.Zero)
                {
                    if (_audioPlayer.CurrentTime >= _audioPlayer.TotalTime - TimeSpan.FromMilliseconds(300))
                    {
                        // ZDE SE ROZHODUJE POLITIKA KONCE PLAYLISTU (nález S6).
                        // GetNextFile() nově vrací null, když další soubor není -
                        // dřív vracel tentýž a poslední skladba se opakovala
                        // donekonečna. Zvolené chování: přehrávání skončí.
                        // Pro zacyklení celého playlistu by tu stačilo místo
                        // pauzy zavolat _navigator.LoadDirectory(prvníSoubor).
                        if (_navigator.GetNextFile() != null)
                        {
                            UpdateFileTypeState();
                            Console.Clear();
                            RenderDashboard();
                        }
                        else
                        {
                            _audioPlayer.Pause();
                            _isPaused = true;
                            RenderDashboard();
                        }
                    }
                }

                // D) Vykreslení VU metrů / MIDI osnovy
                if (!_isMidiMode)
                {
                    var (peakL, peakR) = _audioPlayer.ReadPeakLevels();

                    // Přidáno: úroveň živě hraných varhan (mono) - promítne se do obou
                    // kanálů, pokud je v tu chvíli hlasitější než přehrávaný soubor.
                    // Když je přehrávání pozastavené (peakL/peakR = 0), ukáže VU metr
                    // čistě hru na varhany.
                    float organPeak = App.OrganEngine?.ReadPeak() ?? 0f;
                    float combinedL = Math.Max(peakL, organPeak);
                    float combinedR = Math.Max(peakR, organPeak);

                    _leftDb = AudioMeter.LinearToDecibels(combinedL);
                    _rightDb = AudioMeter.LinearToDecibels(combinedR);
                    RenderMetersOnly();
                }
                else
                {
                    RenderMidiStaffOnly();
                }

                Thread.Sleep(30);
            }

            _audioPlayer.Dispose();
            Console.CursorVisible = true;
        }

        private static void HandleMouseClick(short x, short y)
        {
            if (y == 1)
            {
                if (x >= 40 && x <= 43)      // [<<]
                {
                    _audioPlayer.Seek(-5.0);
                }
                else if (x >= 45 && x <= 47) // [►]
                {
                    if (_isPaused) TogglePlayPause();
                }
                else if (x >= 49 && x <= 51) // [▄]
                {
                    if (!_isPaused) TogglePlayPause();
                }
                else if (x >= 53 && x <= 56) // [>>]
                {
                    _audioPlayer.Seek(5.0);
                }
            }
        }

        private static void TogglePlayPause()
        {
            _isPaused = !_isPaused;
            if (_isPaused)
            {
                _audioPlayer.Pause();
            }
            else
            {
                _audioPlayer.Play();
            }
        }

        private static void ChangeVolume(int delta)
        {
            _volume = Math.Clamp(_volume + delta, 0, 100);
            _audioPlayer.Volume = _volume / 100.0f;
        }

        private static void UpdateFileTypeState()
        {
            string currentFile = _navigator.CurrentFile ?? "";
            string ext = Path.GetExtension(currentFile).ToLowerInvariant();

            _isMidiMode = (ext == ".mid" || ext == ".midi" || ext == ".kar");

            if (!_isMidiMode && File.Exists(currentFile))
            {
                _audioPlayer.Load(currentFile);
                _audioPlayer.Volume = _volume / 100.0f;
                _audioPlayer.Play();
                _isPaused = false;
            }
        }

        private static bool HandleInput(ConsoleKey key)
        {
            switch (key)
            {
                case ConsoleKey.Escape:
                    return false;

                case ConsoleKey.Spacebar:
                    TogglePlayPause();
                    break;

                // PgDown -> Další soubor
                case ConsoleKey.PageDown:
                    {
                        string? nextFile = _navigator.GetNextFile();
                        if (nextFile != null)
                        {
                            UpdateFileTypeState();
                            Console.Clear();
                        }
                    }
                    break;

                // PgUp -> Předchozí soubor
                case ConsoleKey.PageUp:
                    {
                        string? prevFile = _navigator.GetPreviousFile();
                        if (prevFile != null)
                        {
                            UpdateFileTypeState();
                            Console.Clear();
                        }
                    }
                    break;

                // Šipka doprava -> +5s
                case ConsoleKey.RightArrow:
                    _audioPlayer.Seek(5.0);
                    break;

                // Šipka doleva -> -5s
                case ConsoleKey.LeftArrow:
                    _audioPlayer.Seek(-5.0);
                    break;

                // Hlasitost
                case ConsoleKey.UpArrow:
                    ChangeVolume(5);
                    break;

                case ConsoleKey.DownArrow:
                    ChangeVolume(-5);
                    break;
            }
            return true;
        }

        private static void RenderDashboard()
        {
            Console.SetCursorPosition(0, 0);

            string currentFile = _navigator.CurrentFile ?? "No file loaded";
            string fileName = Path.GetFileName(currentFile);
            string folderPath = Path.GetDirectoryName(currentFile) ?? "";
            string modeLabel = _isMidiMode ? "MIDI PASS-THROUGH" : "AUDIO STREAM (WASAPI)";

            TimeSpan current = _audioPlayer.CurrentTime;
            TimeSpan total = _audioPlayer.TotalTime;
            string timeStr = $"{current:mm\\:ss} / {total:mm\\:ss}";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=========================================================================================");

            Console.Write($" INVISIBLE PLAYER | Vol: {_volume,3}% | [{timeStr}] ");

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("[<<] ");

            if (!_isPaused && _audioPlayer.IsPlaying)
            {
                Console.BackgroundColor = ConsoleColor.Green;
                Console.ForegroundColor = ConsoleColor.Black;
                Console.Write("[►]");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("[►]");
            }
            Console.Write(" ");

            if (_isPaused || !_audioPlayer.IsPlaying)
            {
                Console.BackgroundColor = ConsoleColor.DarkRed;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("[▄]");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("[▄]");
            }
            Console.Write(" ");

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("[>>] ");

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write("♪♫");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine();

            Console.WriteLine($" Mode: {modeLabel,-22}");
            Console.WriteLine("=========================================================================================");
            Console.ResetColor();

            Console.WriteLine($" {fileName,-35} | {folderPath,-45}");
            Console.WriteLine("-----------------------------------------------------------------------------------------");
        }

        private static void RenderMetersOnly()
        {
            Console.SetCursorPosition(0, 7);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Legend: -120dB                                             -60dB                       0dB");
            Console.ResetColor();

            string barL = AudioMeter.RenderBar(_leftDb, 80);
            string barR = AudioMeter.RenderBar(_rightDb, 80);

            Console.Write(" L: [");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(barL);
            Console.ResetColor();
            Console.WriteLine($"] {_leftDb,6:F1} dB");

            Console.Write(" R: [");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(barR);
            Console.ResetColor();
            Console.WriteLine($"] {_rightDb,6:F1} dB");
        }

        private static void RenderMidiStaffOnly()
        {
            Console.SetCursorPosition(0, 7);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(" Staff Attenuation Keys [1-0]: ");
            for (int i = 0; i < 10; i++)
            {
                string label = (i == 9) ? "Ch10[DRUM]" : $"Ch{i + 1:D2}";
                if (_channelMuted[i])
                {
                    Console.BackgroundColor = ConsoleColor.DarkRed;
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"[{label}:MUTED] ");
                    Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                }
                else
                {
                    Console.Write($"[{label}:ON] ");
                }
            }
            Console.WriteLine();
            Console.ResetColor();
            Console.WriteLine("-----------------------------------------------------------------------------------------");
            Console.WriteLine(" [MIDI Active Notes & Lyrics Staff View Placeholder]                                    ");
        }
    }
}
