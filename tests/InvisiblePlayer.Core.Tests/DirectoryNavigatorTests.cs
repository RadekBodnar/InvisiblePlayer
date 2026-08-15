using InvisiblePlayer.Core;

namespace InvisiblePlayer.Core.Tests;

/// <summary>
/// Testy k nálezům M8 (case-sensitive IndexOf), L5 (kulturně závislé řazení)
/// a S6 (chování na konci playlistu).
///
/// Vyžadují skutečný souborový systém - DirectoryNavigator volá Directory.GetFiles
/// přímo. Každý test si vytvoří vlastní dočasný strom a po sobě uklidí.
/// </summary>
public sealed class DirectoryNavigatorTests : IDisposable
{
    private readonly string _sandbox;   // izolovaný obal - nikdy se do něj nekouká
    private readonly string _root;      // "nadřazený adresář" z pohledu testů

    public DirectoryNavigatorTests()
    {
        // Dvě úrovně jsou nutné: DirectoryNavigator prochází SOUSEDNÍ složky
        // v nadřazeném adresáři. Kdyby _root ležel přímo v /tmp, navigace by
        // procházela cizí složky (včetně dočasných adresářů souběžně běžících
        // testů) a testy by byly nedeterministické.
        _sandbox = Path.Combine(Path.GetTempPath(), "ip-navtest-" + Guid.NewGuid().ToString("N")[..8]);
        _root = Path.Combine(_sandbox, "root");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, recursive: true);
    }

    private string MakeFile(string relativePath)
    {
        string full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
        return full;
    }

    /// <summary>
    /// Zjistí, jestli je souborový systém case-insensitive (Windows/macOS ano,
    /// typický Linux ne). Bez toho by test M8 na Linuxu tiše neověřoval nic.
    /// </summary>
    private bool FileSystemIsCaseInsensitive()
    {
        string probe = Path.Combine(_root, "CaseProbe.tmp");
        File.WriteAllText(probe, "x");
        try { return File.Exists(Path.Combine(_root, "caseprobe.tmp")); }
        finally { File.Delete(probe); }
    }

    // ---------------------------------------------------------------- M8 ----

    [Fact]
    public void LoadDirectory_PresnaCesta_NastaviCurrentFile()
    {
        MakeFile("alfa.mp3");
        string target = MakeFile("beta.mp3");
        MakeFile("gama.mp3");

        var nav = new DirectoryNavigator();
        nav.LoadDirectory(target);

        Assert.Equal(target, nav.CurrentFile);
    }

    [Fact]
    public void LoadDirectory_JinaVelikostPismen_NajdeSoubor()
    {
        // NÁLEZ M8: dřív se hledalo přes List.IndexOf, což porovnává ordinálně
        // a case-sensitive. Na Windows (case-insensitive FS) tak cesta předaná
        // z příkazové řádky jako "C:\hudba\SONG.MP3" nenašla "...\Song.mp3",
        // CurrentFile zůstal null a přehrávání tiše nezačalo.
        string target = MakeFile("Song.mp3");

        if (!FileSystemIsCaseInsensitive())
        {
            // Na case-sensitive FS (Linux) scénář nelze reprodukovat - File.Exists
            // by selhal dřív, než se k porovnání vůbec dojde. Ověříme aspoň, že
            // se nezměnilo chování u přesné shody.
            var linuxNav = new DirectoryNavigator();
            linuxNav.LoadDirectory(target);
            Assert.Equal(target, linuxNav.CurrentFile);
            return;
        }

        string upper = Path.Combine(_root, "SONG.MP3");
        var nav = new DirectoryNavigator();
        nav.LoadDirectory(upper);

        Assert.NotNull(nav.CurrentFile);
        Assert.Equal("Song.mp3", Path.GetFileName(nav.CurrentFile!));
    }

    // ---------------------------------------------------------------- L5 ----

    [Fact]
    public void LoadDirectory_RadiOrdinalne_NezavisleNaKulture()
    {
        // "chalupa" vs "hudba": v české kultuře se "ch" řadí AŽ ZA "h", ordinálně
        // je "c" < "h". Test tedy odhalí, kdyby se vrátilo kulturně závislé OrderBy.
        MakeFile("chalupa.mp3");
        MakeFile("hudba.mp3");
        string first = MakeFile("aaa.mp3");

        var nav = new DirectoryNavigator();
        nav.LoadDirectory(first);

        Assert.Equal("aaa.mp3", Path.GetFileName(nav.CurrentFile!));
        Assert.Equal("chalupa.mp3", Path.GetFileName(nav.GetNextFile()!));
        Assert.Equal("hudba.mp3", Path.GetFileName(nav.GetNextFile()!));
    }

    // ----------------------------------------------------------- navigace ---

    [Fact]
    public void GetNextFile_APrevious_ProchazejiSlozku()
    {
        MakeFile("01.mp3");
        string second = MakeFile("02.mp3");
        MakeFile("03.mp3");

        var nav = new DirectoryNavigator();
        nav.LoadDirectory(second);

        Assert.Equal("03.mp3", Path.GetFileName(nav.GetNextFile()!));
        Assert.Equal("02.mp3", Path.GetFileName(nav.GetPreviousFile()!));
        Assert.Equal("01.mp3", Path.GetFileName(nav.GetPreviousFile()!));
    }

    [Fact]
    public void LoadDirectory_IgnorujeNepodporovanePripony()
    {
        string target = MakeFile("hudba.mp3");
        MakeFile("poznamky.txt");
        MakeFile("obal.jpg");

        var nav = new DirectoryNavigator();
        nav.LoadDirectory(target);

        // Jediný podporovaný soubor: zůstaneme na něm, ale "další" už není (S6).
        Assert.Equal(target, nav.CurrentFile);
        Assert.Null(nav.GetNextFile());
        Assert.Equal(target, nav.CurrentFile);
    }

    /// <summary>
    /// OPRAVA S6: na konci playlistu musí GetNextFile() vrátit null, ne tentýž
    /// soubor. Dřív ho vracel, VgaEngine to bral jako novou skladbu, znovu ji
    /// načetl a přehrál — poslední skladba se opakovala donekonečna.
    ///
    /// Pozice přitom zůstává na posledním souboru, aby PageUp fungoval dál.
    /// (Tento test vznikl jako charakterizační pro tehdejší vadné chování;
    /// po rozhodnutí a opravě byl přepsán — přesně proto tam byl.)
    /// </summary>
    [Fact]
    public void GetNextFile_NaKonciPlaylistu_VraciNull_APoziciNechava()
    {
        MakeFile("01.mp3");
        string last = MakeFile("02.mp3");

        var nav = new DirectoryNavigator();
        nav.LoadDirectory(last);

        Assert.Null(nav.GetNextFile());
        Assert.Null(nav.GetNextFile());

        // Pozice se nesmí posunout ani "zabalit" — jsme pořád na posledním.
        Assert.Equal(last, nav.CurrentFile);
        Assert.Equal("01.mp3", Path.GetFileName(nav.GetPreviousFile()!));
    }

    [Fact]
    public void GetPreviousFile_NaZacatkuPlaylistu_VraciNull_APoziciNechava()
    {
        string first = MakeFile("01.mp3");
        MakeFile("02.mp3");

        var nav = new DirectoryNavigator();
        nav.LoadDirectory(first);

        Assert.Null(nav.GetPreviousFile());
        Assert.Equal(first, nav.CurrentFile);
        Assert.Equal("02.mp3", Path.GetFileName(nav.GetNextFile()!));
    }

    [Fact]
    public void LoadDirectory_NeexistujiciSoubor_Neshodi()
    {
        var nav = new DirectoryNavigator();
        nav.LoadDirectory(Path.Combine(_root, "neexistuje.mp3"));

        Assert.Null(nav.CurrentFile);
        Assert.Null(nav.GetNextFile());
        Assert.Null(nav.GetPreviousFile());
    }

    [Fact]
    public void LoadDirectory_PrazdnyRetezec_Neshodi()
    {
        var nav = new DirectoryNavigator();
        nav.LoadDirectory("");
        Assert.Null(nav.CurrentFile);
    }

    // ------------------------------------------------- sousední složky ------

    [Fact]
    public void GetNextFile_NaKonciSlozky_PrejdeDoSousedni()
    {
        MakeFile(Path.Combine("a-prvni", "01.mp3"));
        string last = MakeFile(Path.Combine("a-prvni", "02.mp3"));
        MakeFile(Path.Combine("b-druha", "03.mp3"));

        var nav = new DirectoryNavigator();
        nav.LoadDirectory(last);

        string? next = nav.GetNextFile();
        Assert.Equal("03.mp3", Path.GetFileName(next!));
        Assert.Contains("b-druha", next!);
    }

    /// <summary>
    /// NÁLEZ M9 (odhalen tímto testovacím souborem, ne review ani překladačem):
    /// nečitelná sousední složka shodila celou navigaci neodchycenou
    /// UnauthorizedAccessException. Na Windows stačilo přehrávat soubor z kořene
    /// disku - každý NTFS svazek tam má "System Volume Information" se zamítnutým
    /// ACL - a stisknout PageDown na konci složky.
    /// </summary>
    [Fact]
    public void GetNextFile_NecitelnaSousedniSlozka_NeshodiNavigaci()
    {
        string last = MakeFile(Path.Combine("a-prvni", "01.mp3"));

        string denied = Path.Combine(_root, "b-nepristupna");
        Directory.CreateDirectory(denied);
        File.WriteAllText(Path.Combine(denied, "skryta.mp3"), "x");

        MakeFile(Path.Combine("c-treti", "02.mp3"));

        // Odebrání práv na čtení/průchod. Pod rootem to nefunguje (root obejde
        // vše), takže tam se test degraduje na kontrolu běžného přeskočení.
        bool denialWorks = false;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(denied, UnixFileMode.None);
            try { Directory.GetFiles(denied); }
            catch (UnauthorizedAccessException) { denialWorks = true; }
        }

        try
        {
            var nav = new DirectoryNavigator();
            nav.LoadDirectory(last);

            string? next = nav.GetNextFile();   // dřív zde letěla výjimka

            Assert.NotNull(next);
            Assert.Contains("c-treti", next!);
            if (!denialWorks)
            {
                // Poznámka do výstupu: scénář nebyl plně reprodukován (běžíme jako
                // root nebo na Windows). Test pak ověřuje jen běžné přeskočení.
                Assert.True(true);
            }
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(denied, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void GetNextFile_PreskociPrazdnouSousedniSlozku()
    {
        string last = MakeFile(Path.Combine("a-prvni", "01.mp3"));
        Directory.CreateDirectory(Path.Combine(_root, "b-prazdna"));
        MakeFile(Path.Combine("c-treti", "02.mp3"));

        var nav = new DirectoryNavigator();
        nav.LoadDirectory(last);

        string? next = nav.GetNextFile();
        Assert.Contains("c-treti", next!);
    }
}
