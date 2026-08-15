using System;
using System.Collections.Generic;
using InvisiblePlayer.Core.Generators;
using InvisiblePlayer.Core.Tones;
using InvisiblePlayer.Core.Input;

namespace InvisiblePlayer.Core.Synthesis
{
    public class ActiveNote
    {
        public int NoteNumber { get; set; }   // MIDI číslo noty (např. 60 = C4)
        public double Frequency { get; set; } // Kmitočet v Hz (např. 261.63 Hz)

        // 'required': hlas musí být vždy přiřazen. Bez toho šlo o non-nullable
        // vlastnost bez inicializátoru (CS8618) - anotace slibovala "nikdy null",
        // ale nic to nevynucovalo. Volající (NoteOn) ho stejně vždy nastavuje.
        //
        // Typ je SynthVoice, ne OrganVoice: hlas vybírá factory podle
        // VoicePreset.Instrument, takže tu může být i Piano/Cembalo/Bell.
        public required SynthVoice Voice { get; set; }
    }



    public class ToneEngine
    {

        private readonly double _sampleRate;
        private readonly Temperament _temperament;

        // Seznam právě znějících hlasů pro rejstřík Bombard
        private readonly List<ActiveNote> _activeBombardNotes = new List<ActiveNote>();

        // Zámek chránící seznam před konfliktem MIDI vlákna a Audio vlákna
        private readonly object _lock = new object();

        // Základní zesílení výstupu. Žádné tvarování signálu (tanh apod.) záměrně
        // nepoužíváme - pro spektrální analýzu potřebujeme signál beze zkreslení.
        // Hlasitost jednotlivých rejstříků se řeší přímo amplitudami v presetu.
        private const double MasterGain = 0.3;

        // Exponent kompenzace podle počtu znějících hlasů (viz GenerateNextMixSample).
        // 0.5 = kompenzace podle druhé odmocniny (fyzikálně odpovídá součtu N nezávislých
        // /nekorelovaných/ zdrojů zvuku - stejně jako reálné píšťaly).
        // 0.0 = žádná kompenzace (chování jako dřív). 1.0 = plná lineární rezerva (1/N),
        // nejbezpečnější, ale poroste hlasitost s přidávanými hlasy nejméně.
        private const double PolyphonyCompensationExponent = 0.5;

        // Nastaví se na true, jakmile JAKÝKOLI vygenerovaný vzorek přesáhne rozsah
        // -1.0..1.0 (tedy Math.Clamp ho musel oříznout), a drží se, dokud ho někdo
        // nepřečte přes ReadClipDetected(). Ořez trvá typicky jediný vzorek (23 us),
        // zatímco VU metr čte jednou za desítky ms - bez tohoto "zámku" by se
        // transient ořez prakticky nikdy netrefil do okamžiku čtení.
        // Zapisuje audio vlákno, čte UI vlákno -> volatile kvůli viditelnosti.
        private volatile bool _clipSinceLastRead;

        // Semínko šumu pro všechny vytvářené hlasy. null = běžný provoz.
        // Konkrétní hodnota dělá výstup enginu reprodukovatelným (charakterizační testy).
        private readonly int? _noiseSeed;

        /// <summary>
        /// Preset, ze kterého se vytvářejí nové hlasy. Určuje i to, KTERÝ generátor
        /// se použije (viz VoicePreset.Instrument a CreateVoice).
        ///
        /// Dřív byl preset zadrátovaný přímo v NoteOn, takže se vždy vytvořil
        /// OrganVoice s Bombardem - a PianoVoice / CembaloVoice / BellVoice byly
        /// nedosažitelný kód.
        /// </summary>
        public VoicePreset CurrentPreset { get; set; } = _001_Bombard16Preset.Preset;

        public ToneEngine(double sampleRate = 44100.0, Temperament? temperament = null, int? noiseSeed = null)
        {
            _sampleRate = sampleRate;
            _temperament = temperament ?? new Temperament(); // default = rovnoměrná (samé nuly = beze změny chování)
            _noiseSeed = noiseSeed;
        }

        /// <summary>
        /// Vybere zvukový generátor podle presetu. Tohle je ten chybějící článek,
        /// kvůli kterému se pole VoicePreset.Instrument nastavovalo, ale nikde nečetlo.
        /// </summary>
        private SynthVoice CreateVoice(VoicePreset preset) => preset.Instrument switch
        {
            InstrumentType.Piano => new PianoVoice(_sampleRate, _noiseSeed),
            InstrumentType.Cembalo => new CembaloVoice(_sampleRate, _noiseSeed),
            InstrumentType.Bell => new BellVoice(preset, _sampleRate, _noiseSeed),
            _ => new OrganVoice(preset, _sampleRate, _noiseSeed),
        };

        // =========================================================================
        // 1. REAKCE NA MIDI / KLÁVESNICI (Volá se při stisku a pustití klávesy)
        // =========================================================================

        public void NoteOn(int noteNumber)
        {
            // Přepočet MIDI noty na frekvenci (A440 ladění + volitelná historická temperatura)
            double freq = 440.0 * Math.Pow(2.0, (noteNumber - 69) / 12.0)
                        * Math.Pow(2.0, _temperament.CentOffset(noteNumber) / 1200.0);

            lock (_lock)
            {
                // Pokud tenhle tón už hraje (rychlé opakování / opětovný NoteOn dřív, než
                // doznělo předchozí spuštění), NEVYTVÁŘÍME druhou překrývající se instanci
                // (ta způsobovala náhodné "přeladění" zvuku fázovým rušením). Místo toho
                // jen znovu spustíme (retrigger) tu existující.
                var existing = _activeBombardNotes.Find(n => n.NoteNumber == noteNumber);
                if (existing != null)
                {
                    existing.Voice.NoteOn();
                    existing.Frequency = freq;
                    return;
                }

                // Vytvoříme nový hlas podle aktuálního presetu. Typ generátoru
                // (varhany / klavír / cembalo / zvon) vybírá CreateVoice.
                var voice = CreateVoice(CurrentPreset);
                voice.NoteOn();

                _activeBombardNotes.Add(new ActiveNote
                {
                    NoteNumber = noteNumber,
                    Frequency = freq,
                    Voice = voice
                });
            }
        }

        public void NoteOff(int noteNumber)
        {
            lock (_lock)
            {
                for (int i = 0; i < _activeBombardNotes.Count; i++)
                {
                    if (_activeBombardNotes[i].NoteNumber == noteNumber)
                    {
                        _activeBombardNotes[i].Voice?.NoteOff();
                    }
                }
            }
        }


        // =========================================================================
        // 2. GENEROVÁNÍ ZVUKU PRO ZVUKOVKU (Volá audio karta v reálném čase)
        // =========================================================================

        public double GenerateNextMixSample()
        {
            double mixedSample = 0.0;
            int voiceCount;

            lock (_lock)
            {
                voiceCount = _activeBombardNotes.Count;

                for (int i = _activeBombardNotes.Count - 1; i >= 0; i--)
                {
                    var activeNote = _activeBombardNotes[i];

                    if (activeNote?.Voice != null)
                    {
                        // Vygenerujeme vzorek
                        double sample = activeNote.Voice.GenerateSample(activeNote.Frequency);
                        mixedSample += sample;

                        // ČIŠTĚNÍ PAMĚTI: Pokud nota dozněla (ADSR obálka dojela na konec), smažeme ji ze seznamu!
                        if (activeNote.Voice.IsFinished)
                        {
                            _activeBombardNotes.RemoveAt(i);
                        }
                    }
                }
            }

            // PREVENTIVNÍ KOMPENZACE PODLE POČTU ZNĚJÍCÍCH HLASŮ:
            // N vzájemně fázově nezávislých zdrojů zvuku (různé píšťaly/rejstříky, žádná
            // pevná fázová vazba mezi nimi) se energeticky sčítá přibližně jako √N, ne
            // lineárně jako N. Kompenzací 1/√N proto vyrovnáme očekávaný nárůst hlasitosti
            // PŘEDEM, dřív než dojde k ořezu - místo aby limiter/clamp zasahoval až samotné
            // zkreslení bylo slyšet. Tvrdý Math.Clamp níže zůstává jako poslední pojistka
            // pro výjimečné fázové shody (např. útok/chiff), ne jako běžný způsob řešení.
            double compensation = voiceCount > 1
                ? 1.0 / Math.Pow(voiceCount, PolyphonyCompensationExponent)
                : 1.0;

            double gained = mixedSample * MasterGain * compensation;

            // Pouze nastavujeme (nikdy nenulujeme) - nulování patří výhradně
            // do ReadClipDetected(), jinak by se ořez ztratil mezi dvěma čteními.
            if (gained > 1.0 || gained < -1.0) _clipSinceLastRead = true;

            return Math.Clamp(gained, -1.0, 1.0);
        }

        /// <summary>
        /// Došlo od posledního volání k ořezu alespoň jednoho vzorku? Čtení příznak nuluje
        /// (stejná sémantika jako AudioEngine.ReadPeak()).
        /// </summary>
        public bool ReadClipDetected()
        {
            bool clipped = _clipSinceLastRead;
            _clipSinceLastRead = false;
            return clipped;
        }
    }
}