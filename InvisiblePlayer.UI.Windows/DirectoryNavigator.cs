using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace InvisiblePlayer.Core
{
    public class DirectoryNavigator
    {
        private List<string> _playlist = new();
        private int _currentIndex = -1;

        public string? CurrentFile => (_currentIndex >= 0 && _currentIndex < _playlist.Count)
            ? _playlist[_currentIndex]
            : null;

        public void LoadDirectory(string initialFilePath)
        {
            if (string.IsNullOrEmpty(initialFilePath) || !File.Exists(initialFilePath))
                return;

            string? folder = Path.GetDirectoryName(initialFilePath);
            if (folder == null) return;

            // Načteme všechny podporované soubory v aktuální složce.
            // StringComparer.Ordinal, ne výchozí OrderBy - to řadí podle AKTUÁLNÍ
            // KULTURY, takže v češtině by "ch" skončilo až za "h" a pořadí playlistu
            // by záviselo na nastavení systému.
            _playlist = Directory.GetFiles(folder)
                .Where(f => IsSupportedExtension(f))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            // IndexOf porovnává case-sensitive, ale Windows FS je case-insensitive:
            // cesta z příkazové řádky ("C:\hudba\SONG.MP3") se nemusí trefit do toho,
            // co vrátil Directory.GetFiles ("...\Song.mp3") -> -1 -> CurrentFile == null
            // -> přehrávání tiše nezačne.
            _currentIndex = _playlist.FindIndex(
                f => string.Equals(f, initialFilePath, StringComparison.OrdinalIgnoreCase));
        }

        public string? GetNextFile()
        {
            if (_playlist.Count == 0) return null;

            string? lastValidFile = CurrentFile;
            _currentIndex++;

            // Pokud jsme dojeli na konec složky, zkusíme najít sousední složku
            if (_currentIndex >= _playlist.Count)
            {
                if (TryToNavigateToNeighborFolder(lastValidFile, next: true))
                {
                    _currentIndex = 0; // První soubor v nové složce
                }
                else
                {
                    _currentIndex = _playlist.Count - 1; // Zůstaneme na posledním
                }
            }

            return CurrentFile;
        }

        public string? GetPreviousFile()
        {
            if (_playlist.Count == 0) return null;

            string? lastValidFile = CurrentFile;
            _currentIndex--;

            // Pokud jsme vyskočili před začátek složky, zkusíme přejít do předchozí složky
            if (_currentIndex < 0)
            {
                if (TryToNavigateToNeighborFolder(lastValidFile, next: false))
                {
                    _currentIndex = _playlist.Count - 1; // Poslední soubor v předchozí složce
                }
                else
                {
                    _currentIndex = 0; // Zůstaneme na prvním
                }
            }

            return CurrentFile;
        }

        private bool TryToNavigateToNeighborFolder(string? referenceFile, bool next)
        {
            if (string.IsNullOrEmpty(referenceFile)) return false;

            string? currentFolder = Path.GetDirectoryName(referenceFile);
            if (currentFolder == null) return false;

            DirectoryInfo? parentDir = Directory.GetParent(currentFolder);
            if (parentDir == null) return false;

            // Seznam všech podsložek v nadřazeném adresáři
            var subFolders = parentDir.GetDirectories()
                .OrderBy(d => d.FullName, StringComparer.Ordinal)
                .ToList();

            int currentFolderIndex = subFolders.FindIndex(d => d.FullName.Equals(currentFolder, StringComparison.OrdinalIgnoreCase));
            if (currentFolderIndex == -1) return false;

            int targetFolderIndex = next ? currentFolderIndex + 1 : currentFolderIndex - 1;

            // Hledáme nejbližší složku, která obsahuje nějaké hratelné soubory
            while (targetFolderIndex >= 0 && targetFolderIndex < subFolders.Count)
            {
                var targetFolder = subFolders[targetFolderIndex];
                var filesInTarget = Directory.GetFiles(targetFolder.FullName)
                    .Where(f => IsSupportedExtension(f))
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToList();

                if (filesInTarget.Count > 0)
                {
                    _playlist = filesInTarget;
                    return true;
                }

                targetFolderIndex += next ? 1 : -1;
            }

            return false;
        }


        private static bool IsSupportedExtension(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".mp3" || ext == ".wav" || ext == ".flac" || ext == ".mid" || ext == ".midi" || ext == ".kar"
                || ext == ".avi" || ext == ".mp4" || ext == ".mkv" || ext == ".wmv"; // <--- Přidány video formáty
        }
    }
}