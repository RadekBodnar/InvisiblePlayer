using InvisiblePlayer.Core;

namespace InvisiblePlayer.Core.Tests;

/// <summary>
/// Nález externího review: existovaly DVA seznamy přípon (MediaLauncher
/// a DirectoryNavigator), které se rozešly. Následek byl tichý — soubor se
/// rozpoznal jako video, ale navigátor ho z playlistu vyhodil, takže se
/// nespustilo nic a nikde nebyla chyba.
/// </summary>
public class MediaTypesTests
{
    [Theory]
    [InlineData("song.mp3", MediaKind.Audio)]
    [InlineData("song.wav", MediaKind.Audio)]
    [InlineData("song.flac", MediaKind.Audio)]
    [InlineData("song.mid", MediaKind.Midi)]
    [InlineData("song.midi", MediaKind.Midi)]
    [InlineData("song.kar", MediaKind.Midi)]      // karaoke MIDI
    [InlineData("clip.mp4", MediaKind.Video)]
    [InlineData("clip.avi", MediaKind.Video)]
    [InlineData("clip.mkv", MediaKind.Video)]
    [InlineData("clip.wmv", MediaKind.Video)]
    [InlineData("clip.mov", MediaKind.Video)]
    [InlineData("clip.flv", MediaKind.Video)]
    [InlineData("clip.webm", MediaKind.Video)]
    [InlineData("clip.m4v", MediaKind.Video)]
    [InlineData("readme.txt", MediaKind.Unsupported)]
    [InlineData("cover.jpg", MediaKind.Unsupported)]
    public void Classify_UrciSpravnyTyp(string path, MediaKind expected)
    {
        Assert.Equal(expected, MediaTypes.Classify(path));
    }

    [Theory]
    [InlineData("SONG.MP3")]
    [InlineData("Clip.MoV")]
    [InlineData("song.MIDI")]
    public void Classify_NezalezinaVelikostiPismen(string path)
    {
        Assert.NotEqual(MediaKind.Unsupported, MediaTypes.Classify(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bez_pripony")]
    public void Classify_NeplatnyVstup_JeUnsupported(string? path)
    {
        Assert.Equal(MediaKind.Unsupported, MediaTypes.Classify(path));
    }

    /// <summary>
    /// JÁDRO NÁLEZU: co launcher umí spustit, musí navigátor umět najít.
    /// Kdyby se seznamy zase rozešly, tenhle test to zachytí.
    /// </summary>
    [Fact]
    public void KazdaZnamaPripona_JePodporovana()
    {
        foreach (string ext in MediaTypes.AllExtensions)
        {
            string path = "soubor" + ext;
            Assert.True(MediaTypes.IsSupported(path), $"{ext} není podporovaná");
            Assert.NotEqual(MediaKind.Unsupported, MediaTypes.Classify(path));
        }
    }

    [Fact]
    public void Video_A_Midi_Pripony_JsouVPlaylistu()
    {
        // Konkrétně ty čtyři přípony, které dřív v navigátoru chyběly,
        // a .kar, které naopak chybělo v launcheru.
        foreach (string ext in new[] { ".mov", ".flv", ".webm", ".m4v", ".kar" })
        {
            Assert.True(MediaTypes.IsSupported("soubor" + ext),
                $"{ext} musí být v playlistu, jinak se soubor rozpozná, ale nespustí");
        }
    }
}
