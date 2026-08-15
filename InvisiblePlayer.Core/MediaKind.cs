using System;
using System.Collections.Generic;
using System.IO;

namespace InvisiblePlayer.Core
{
    /// <summary>Jak se s daným souborem má naložit.</summary>
    public enum MediaKind
    {
        /// <summary>Nepodporovaná přípona.</summary>
        Unsupported,
        Audio,
        Midi,
        Video,
    }

    /// <summary>
    /// JEDINÝ zdroj pravdy o tom, které přípony projekt zná a co která znamená.
    /// </summary>
    /// <remarks>
    /// Nález externího review: seznamy byly DVA a rozcházely se.
    ///   - `MediaLauncher.VideoExtensions` znal `.mov`, `.flv`, `.webm`, `.m4v`,
    ///     ale `DirectoryNavigator.IsSupportedExtension` je nezahrnoval. Takový
    ///     soubor se sice rozpoznal jako video, ale navigátor ho z playlistu vyřadil
    ///     -> `CurrentFile` zůstal null a video se nespustilo.
    ///   - Naopak `.kar` navigátor bral jako MIDI, ale launcher ho neposlal do
    ///     `PlayMidiFileAsync` (kontroloval jen `.mid`/`.midi`), takže nehrál.
    ///
    /// Dva seznamy se dřív nebo později rozejdou vždy. Proto jeden.
    /// </remarks>
    public static class MediaTypes
    {
        private static readonly Dictionary<string, MediaKind> ByExtension =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [".mp3"] = MediaKind.Audio,
                [".wav"] = MediaKind.Audio,
                [".flac"] = MediaKind.Audio,

                [".mid"] = MediaKind.Midi,
                [".midi"] = MediaKind.Midi,
                [".kar"] = MediaKind.Midi,   // karaoke MIDI

                [".mp4"] = MediaKind.Video,
                [".avi"] = MediaKind.Video,
                [".mkv"] = MediaKind.Video,
                [".mov"] = MediaKind.Video,
                [".wmv"] = MediaKind.Video,
                [".flv"] = MediaKind.Video,
                [".webm"] = MediaKind.Video,
                [".m4v"] = MediaKind.Video,
            };

        /// <summary>Určí typ média podle přípony souboru.</summary>
        public static MediaKind Classify(string? path)
        {
            if (string.IsNullOrEmpty(path)) return MediaKind.Unsupported;

            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return MediaKind.Unsupported;

            return ByExtension.TryGetValue(ext, out var kind) ? kind : MediaKind.Unsupported;
        }

        /// <summary>Umí projekt tenhle soubor vůbec přehrát?</summary>
        public static bool IsSupported(string? path) => Classify(path) != MediaKind.Unsupported;

        /// <summary>Všechny známé přípony (pro diagnostiku a testy).</summary>
        public static IReadOnlyCollection<string> AllExtensions => ByExtension.Keys;
    }
}
