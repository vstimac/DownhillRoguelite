using System.Diagnostics;
using System.IO.Compression;
using DownhillRoguelite.Core.Update;

namespace LetsPlay;

/// <summary>
/// "Let's Play": installs Downhill Roguelite next to itself (a "Downhill Roguelite" folder), keeps it
/// on the newest GitHub release and starts it. Checks the latest release; when it's newer than what's
/// installed, downloads the Windows build with a progress bar and unpacks it over the old one (the
/// talking model in Models\ is kept). Offers the optional 2.1 GB talking model once. Offline, it just
/// starts what's installed. "--race" starts the same-mountain race (seed 1); other arguments go to
/// the game.
/// </summary>
public static class Program
{
    private const string GameExe = "DownhillRoguelite.exe";
    private const string VersionFile = "installed-version.txt";
    private const string ModelFile = "qwen2.5-3b-instruct-q4_k_m.gguf";
    private const string ModelUrl = "https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf";
    private const string ModelAskedFile = "model-offered.txt";
    private const int RaceSeed = 1;

    private static readonly HttpClient Http = CreateClient();

    public static async Task<int> Main(string[] args)
    {
        Console.Title = "Let's Play: Downhill Roguelite";
        Banner();
        string install = InstallFolder();
        Directory.CreateDirectory(install);
        string exe = Path.Combine(install, GameExe);

        try
        {
            await UpdateAsync(install);
        }
        catch (Exception error)
        {
            Say($"Couldn't check for a new version ({error.Message}).");
            if (!File.Exists(exe))
            {
                Say("The game isn't installed yet and GitHub can't be reached. Check your connection and try again.");
                Wait();
                return 1;
            }
            Say("Starting the version you have.");
        }

        if (!File.Exists(exe))
        {
            Say("The download didn't contain the game. Please tell the developer.");
            Wait();
            return 1;
        }

        await OfferModelAsync(install);
        return Start(exe, install, args);
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromMinutes(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DownhillRoguelite-LetsPlay/1.0");
        return client;
    }

    private static void Banner()
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine();
        Console.WriteLine("        /\\");
        Console.WriteLine("       /  \\      D O W N H I L L   R O G U E L I T E");
        Console.WriteLine("      /    \\     Let's Play");
        Console.WriteLine("     ~~~~~~~~");
        Console.WriteLine();
        Console.ResetColor();
    }

    private static void Say(string text)
    {
        Console.WriteLine("  " + text);
    }

    private static void Wait()
    {
        Say("Press any key to close.");
        try
        {
            Console.ReadKey(true);
        }
        catch (InvalidOperationException)
        {
            // No console to read from (started without one): just close.
        }
    }

    /// <summary>A "Downhill Roguelite" folder next to the launcher, or in local app data if that can't be written.</summary>
    private static string InstallFolder()
    {
        string here = Path.Combine(AppContext.BaseDirectory, "Downhill Roguelite");
        try
        {
            Directory.CreateDirectory(here);
            string probe = Path.Combine(here, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return here;
        }
        catch (Exception)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Downhill Roguelite");
        }
    }

    /// <summary>
    /// Brings the install up to the newest release (UpdatePlanner's rules, shared with the game): a
    /// small update when one is made for the installed full release, else the whole new build. The
    /// download is checked against GitHub's SHA-256 before anything in the install changes.
    /// </summary>
    private static async Task UpdateAsync(string install)
    {
        Say("Checking for the newest version...");
        string json = await Http.GetStringAsync(UpdatePlanner.ReleasesUrl);
        string baseTag = ReadLine(Path.Combine(install, "base.txt"));
        string installed = ReadLine(Path.Combine(install, "version.txt"));
        if (installed.Length == 0)
        {
            // Launchers before 2.8 kept the tag here.
            installed = ReadLine(Path.Combine(install, VersionFile));
        }
        bool present = File.Exists(Path.Combine(install, GameExe));
        UpdateOffer? offer = UpdatePlanner.Plan(json, present ? baseTag : "", present ? installed : "");
        if (offer == null)
        {
            if (!present)
            {
                throw new InvalidOperationException("no release has a Windows build yet");
            }
            Say($"You have the newest version: {(installed.Length > 0 ? installed : baseTag)}.");
            return;
        }
        string what = offer.Kind == UpdateKind.Patch ? "update" : "game";
        if (!present)
        {
            Say($"Installing {offer.ReleaseName} ({offer.Size / 1_000_000} MB) into:");
        }
        else
        {
            Say($"New version: {offer.ReleaseName} ({what}, {offer.Size / 1_000_000} MB). Updating:");
        }
        Say(install);
        string zip = Path.Combine(Path.GetTempPath(), $"downhill-{offer.Version}.zip");
        await DownloadAsync(offer.Url, zip, offer.Size);
        if (offer.Sha256.Length > 0)
        {
            string actual;
            using (FileStream file = File.OpenRead(zip))
            {
                actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)).ToLowerInvariant();
            }
            if (actual != offer.Sha256)
            {
                File.Delete(zip);
                throw new InvalidOperationException("the download is damaged (checksum mismatch); nothing was changed");
            }
        }
        Say("Unpacking...");
        if (offer.Kind == UpdateKind.Full)
        {
            // A new full release replaces every earlier small update.
            string patches = Path.Combine(install, "patches");
            if (Directory.Exists(patches))
            {
                Directory.Delete(patches, true);
            }
            File.Delete(Path.Combine(install, "base.txt"));
            File.Delete(Path.Combine(install, "version.txt"));
        }
        Unpack(zip, install);
        File.Delete(zip);
        if (offer.Kind == UpdateKind.Full && !File.Exists(Path.Combine(install, "base.txt")))
        {
            File.WriteAllText(Path.Combine(install, "base.txt"), offer.Tag + Environment.NewLine);
        }
        File.WriteAllText(Path.Combine(install, "version.txt"), offer.Version + Environment.NewLine);
        File.WriteAllText(Path.Combine(install, VersionFile), offer.Version);
        Say("Done.");
    }

    private static string ReadLine(string path)
    {
        return File.Exists(path) ? (File.ReadLines(path).FirstOrDefault() ?? "").Trim() : "";
    }

    /// <summary>Downloads a file with a progress bar.</summary>
    private static async Task DownloadAsync(string url, string target, long expected)
    {
        using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? expected;
        await using Stream source = await response.Content.ReadAsStreamAsync();
        await using FileStream file = File.Create(target);
        byte[] buffer = new byte[1 << 16];
        long done = 0;
        int lastPercent = -1;
        Stopwatch clock = Stopwatch.StartNew();
        while (true)
        {
            int read = await source.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }
            await file.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            int percent = 0;
            if (total > 0)
            {
                percent = (int)(done * 100 / total);
            }
            if (percent != lastPercent)
            {
                lastPercent = percent;
                double speed = done / 1_000_000.0 / Math.Max(clock.Elapsed.TotalSeconds, 0.1);
                int bars = percent / 4;
                Console.Write($"\r  [{new string('#', bars)}{new string('.', 25 - bars)}] {percent,3}%  {done / 1_000_000} / {total / 1_000_000} MB  {speed:0.0} MB/s   ");
            }
        }
        Console.WriteLine();
    }

    /// <summary>Unpacks the build's folder over the install (keeping Models and settings), dropping its top folder.</summary>
    private static void Unpack(string zip, string install)
    {
        using ZipArchive archive = ZipFile.OpenRead(zip);
        string prefix = "";
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            int slash = entry.FullName.IndexOf('/');
            if (slash > 0 && entry.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && entry.FullName.IndexOf('/', slash + 1) < 0)
            {
                prefix = entry.FullName.Substring(0, slash + 1);
                break;
            }
        }
        string root = Path.GetFullPath(install) + Path.DirectorySeparatorChar;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string relative = entry.FullName;
            if (prefix.Length > 0 && relative.StartsWith(prefix, StringComparison.Ordinal))
            {
                relative = relative.Substring(prefix.Length);
            }
            if (relative.Length == 0)
            {
                continue;
            }
            string target = Path.GetFullPath(Path.Combine(install, relative));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (relative.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    /// <summary>Asks once whether to fetch the talking model (2.1 GB); without it the companion stays quiet.</summary>
    private static async Task OfferModelAsync(string install)
    {
        string models = Path.Combine(install, "Models");
        string model = Path.Combine(models, ModelFile);
        string asked = Path.Combine(install, ModelAskedFile);
        if (File.Exists(model) || File.Exists(asked))
        {
            return;
        }
        File.WriteAllText(asked, "asked");
        Console.WriteLine();
        Say("Optional: talk with your companion and the travellers you meet.");
        Say("That needs a 2.1 GB model, run on your own PC (nothing goes online).");
        Console.Write("  Download it now? (y/N, waits 15 s): ");
        bool yes = false;
        Stopwatch clock = Stopwatch.StartNew();
        try
        {
            while (clock.Elapsed.TotalSeconds < 15)
            {
                if (Console.KeyAvailable)
                {
                    yes = char.ToLowerInvariant(Console.ReadKey(true).KeyChar) == 'y';
                    break;
                }
                await Task.Delay(100);
            }
        }
        catch (InvalidOperationException)
        {
            yes = false;
        }
        if (yes)
        {
            Console.WriteLine("yes");
        }
        else
        {
            Console.WriteLine("no");
        }
        if (!yes)
        {
            Say("Skipped. Delete 'model-offered.txt' in the game folder to be asked again.");
            return;
        }
        Directory.CreateDirectory(models);
        string partial = model + ".part";
        try
        {
            await DownloadAsync(ModelUrl, partial, 2_104_932_768);
            File.Move(partial, model, true);
            Say("Model installed.");
        }
        catch (Exception error)
        {
            Say($"The model download failed ({error.Message}); the game works without it.");
        }
    }

    /// <summary>Starts the game ("--race": the same-mountain race) and closes the launcher.</summary>
    private static int Start(string exe, string install, string[] args)
    {
        List<string> gameArgs = new();
        List<string> userArgs = new();
        foreach (string arg in args)
        {
            if (arg == "--race")
            {
                userArgs.Add($"--seed={RaceSeed}");
            }
            else
            {
                userArgs.Add(arg);
            }
        }
        if (userArgs.Count > 0)
        {
            gameArgs.Add("--");
            gameArgs.AddRange(userArgs);
        }
        Say("Starting the game. Good luck on the way down.");
        ProcessStartInfo start = new(exe) { WorkingDirectory = install, UseShellExecute = false };
        foreach (string arg in gameArgs)
        {
            start.ArgumentList.Add(arg);
        }
        Process.Start(start);
        Thread.Sleep(1500);
        return 0;
    }
}
