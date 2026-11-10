using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

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
    private const string Repo = "vstimac/DownhillRoguelite";
    private const string WindowsAssetSuffix = "-windows.zip";
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

    /// <summary>Installs the latest release's Windows build if it isn't the one installed.</summary>
    private static async Task UpdateAsync(string install)
    {
        Say("Checking for the newest version...");
        using JsonDocument release = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
        JsonElement root = release.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        string name = tag;
        if (root.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String)
        {
            name = n.GetString() ?? tag;
        }
        string? url = null;
        long size = 0;
        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            string assetName = asset.GetProperty("name").GetString() ?? "";
            if (assetName.EndsWith(WindowsAssetSuffix, StringComparison.OrdinalIgnoreCase))
            {
                url = asset.GetProperty("browser_download_url").GetString();
                size = asset.GetProperty("size").GetInt64();
            }
        }
        string versionPath = Path.Combine(install, VersionFile);
        string installed = "";
        if (File.Exists(versionPath))
        {
            installed = File.ReadAllText(versionPath).Trim();
        }
        if (installed == tag && File.Exists(Path.Combine(install, GameExe)))
        {
            Say($"You have the newest version: {name}.");
            return;
        }
        if (url == null)
        {
            throw new InvalidOperationException("the newest release has no Windows build");
        }
        if (installed.Length == 0)
        {
            Say($"Installing {name} ({size / 1_000_000} MB) into:");
        }
        else
        {
            Say($"New version: {name} ({size / 1_000_000} MB). Updating:");
        }
        Say(install);
        string zip = Path.Combine(Path.GetTempPath(), $"downhill-{tag}.zip");
        await DownloadAsync(url, zip, size);
        Say("Unpacking...");
        Unpack(zip, install);
        File.Delete(zip);
        File.WriteAllText(versionPath, tag);
        Say("Done.");
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
