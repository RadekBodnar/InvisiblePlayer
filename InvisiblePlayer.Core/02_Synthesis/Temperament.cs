using System;
using System.Collections.Generic;
using InvisiblePlayer.Core.Generators;
using InvisiblePlayer.Core.Tones;
using InvisiblePlayer.Core.Input;

namespace InvisiblePlayer.Core.Synthesis;

/// <summary>
/// Historická temperatura - odchylky ladění jednotlivých tónů od rovnoměrné.
/// </summary>
/// <remarks>
/// OPRAVA L3 (CA1050): třída byla jako jediná v repu v GLOBÁLNÍM namespace,
/// takže se vnucovala každému konzumentovi assembly.
/// </remarks>
public class Temperament
{
    public string Name { get; set; } = "Rovnoměrná (Equal)";

    // Odchylky v centech od rovnoměrné temperatury, indexováno podle pitch class:
    // [0]=C, [1]=C#, [2]=D, [3]=D#, [4]=E, [5]=F, [6]=F#, [7]=G, [8]=G#, [9]=A, [10]=B(b), [11]=H
    public double[] CentOffsets { get; set; } = new double[12]; // default = samé nuly = rovnoměrná

    public double CentOffset(int midiNoteNumber)
    {
        int pitchClass = ((midiNoteNumber % 12) + 12) % 12;
        return CentOffsets[pitchClass];
    }

    // Melzerovy varhany, 1932, temperatura "Georg Kratky I'" (a' = 440 Hz při 16 °C)
    // Zdroj: kniha o svatovítských varhanách, str. 51.
    public static Temperament MelzerGeorgKratkyI => new Temperament
    {
        Name = "Georg Kratky I' (Melzer 1932)",
        CentOffsets = new double[]
        {
                /* C  */  2,
                /* C# */ -1,
                /* D  */  0,
                /* D# */  1,
                /* E  */ -1,
                /* F  */  3,
                /* F# */ -2,
                /* G  */  1,
                /* G# */  0,
                /* A  */  0,
                /* B  */  2,
                /* H  */ -2,
        }
    };
}