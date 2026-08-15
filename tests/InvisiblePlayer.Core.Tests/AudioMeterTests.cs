using InvisiblePlayer.Core;

namespace InvisiblePlayer.Core.Tests;

/// <summary>
/// AudioMeter je čistá funkce bez závislostí - první kandidát na test.
/// </summary>
public class AudioMeterTests
{
    [Theory]
    [InlineData(1.0f, 0.0)]        // plný rozkmit = 0 dBFS
    [InlineData(0.5f, -6.02)]      // polovina amplitudy = -6 dB
    [InlineData(0.1f, -20.0)]      // desetina = -20 dB
    public void LinearToDecibels_PrevadiZnameHodnoty(float peak, double expectedDb)
    {
        Assert.Equal(expectedDb, AudioMeter.LinearToDecibels(peak), precision: 1);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.0000001f)]
    [InlineData(-0.5f)]            // záporný vstup nesmí skončit NaN
    public void LinearToDecibels_PodPrahemVraciPodlahu(float peak)
    {
        Assert.Equal(-120.0, AudioMeter.LinearToDecibels(peak));
    }

    [Fact]
    public void LinearToDecibels_NikdyNepresahneNulu()
    {
        // Vstup > 1.0 (přebuzení) se musí ořezat na 0 dBFS, ne vrátit kladné dB.
        Assert.Equal(0.0, AudioMeter.LinearToDecibels(5.0f));
    }

    [Theory]
    [InlineData(-120.0, 40, 0)]    // ticho = prázdný pruh
    [InlineData(0.0, 40, 40)]      // plný signál = plný pruh
    [InlineData(-60.0, 40, 20)]    // půlka rozsahu = půlka pruhu
    public void RenderBar_MaSpravnyPocetVyplnenychZnaku(double db, int width, int expectedFilled)
    {
        string bar = AudioMeter.RenderBar(db, width);

        Assert.Equal(width, bar.Length);
        Assert.Equal(expectedFilled, bar.Count(c => c == '█'));
    }

    [Theory]
    [InlineData(-500.0)]           // hodnoty mimo rozsah nesmí shodit vykreslení
    [InlineData(500.0)]
    public void RenderBar_ZvladneHodnotyMimoRozsah(double db)
    {
        string bar = AudioMeter.RenderBar(db, 40);
        Assert.Equal(40, bar.Length);
    }
}
