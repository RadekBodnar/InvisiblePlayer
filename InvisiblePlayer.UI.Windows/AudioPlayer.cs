using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace InvisiblePlayer.UI.Windows
{
    public class AudioPlayer : IDisposable
    {
        private WasapiOut? _wasapiOut;
        private AudioFileReader? _audioFile;
        private MeteringSampleProvider? _meteringProvider;

        public event Action? PlaybackEnded;

        public float MaxLeftPeak { get; private set; }
        public float MaxRightPeak { get; private set; }

        public bool IsPlaying => _wasapiOut?.PlaybackState == PlaybackState.Playing;
        public TimeSpan CurrentTime => _audioFile?.CurrentTime ?? TimeSpan.Zero;
        public TimeSpan TotalTime => _audioFile?.TotalTime ?? TimeSpan.Zero;

        public float Volume
        {
            get => _audioFile?.Volume ?? 1.0f;
            set { if (_audioFile != null) _audioFile.Volume = Math.Clamp(value, 0f, 1f); }
        }

        public void Load(string filePath)
        {
            Stop();

            _audioFile = new AudioFileReader(filePath);

            // 1. Passively inspect raw audio peaks (Pre-Fader / WinAmp style)
            _meteringProvider = new MeteringSampleProvider(_audioFile);
            _meteringProvider.StreamVolume += OnStreamVolume;

            // 2. Initialize WASAPI with the sample provider
            _wasapiOut = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100);
            _wasapiOut.Init(_meteringProvider);
            _wasapiOut.PlaybackStopped += OnPlaybackStopped; // <-- Add this!
        }

        private void OnStreamVolume(object? sender, StreamVolumeEventArgs e)
        {
            // Pre-fader peak capture (unaffected by master output volume)
            if (e.MaxSampleValues.Length > 0) MaxLeftPeak = e.MaxSampleValues[0];
            if (e.MaxSampleValues.Length > 1) MaxRightPeak = e.MaxSampleValues[1];
            else MaxRightPeak = MaxLeftPeak;
        }

        
        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            // Check if we reached the end of the file naturally
            if (_audioFile != null && _audioFile.CurrentTime >= _audioFile.TotalTime - TimeSpan.FromMilliseconds(500))
            {
                PlaybackEnded?.Invoke();
            }
        }


        public void Play() => _wasapiOut?.Play();
        public void Pause() => _wasapiOut?.Pause();

        public void Stop()
        {
            _wasapiOut?.Stop();
            _wasapiOut?.Dispose();
            _wasapiOut = null;

            _audioFile?.Dispose();
            _audioFile = null;

            MaxLeftPeak = 0f;
            MaxRightPeak = 0f;
        }

        public void Seek(double offsetSeconds)
        {
            if (_audioFile == null) return;

            // Vypočítáme nový čas
            var newTime = CurrentTime.Add(TimeSpan.FromSeconds(offsetSeconds));

            // Ošetření hranic (nesmíme jít pod 0 ani nad celkovou délku)
            if (newTime < TimeSpan.Zero)
            {
                newTime = TimeSpan.Zero;
            }
            else if (newTime > TotalTime)
            {
                newTime = TotalTime;
            }

            _audioFile.CurrentTime = newTime;
        }

        public (float left, float right) ReadPeakLevels()
        {
            if (!IsPlaying) return (0f, 0f);
            return (MaxLeftPeak, MaxRightPeak);
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}