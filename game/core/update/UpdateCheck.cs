using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace DownhillRoguelite.Core.Update;

/// <summary>
/// Keeps play-testers on the newest release. The title screen starts a check in the background
/// (exported Windows builds only, and only while Options → "Check for new versions" is on); when
/// GitHub has a newer release for this folder (UpdatePlanner), the menu offers "Update". Picking it
/// writes the updater (export/playtest/update.ps1, built into the game's code) to the temp folder,
/// runs it in its own window and closes the game; the updater waits, installs and starts the game
/// again. Without a connection nothing is shown and nothing waits.
///
/// Test switches (after "--"): --update-feed=&lt;url or .json file&gt; reads that release list instead of
/// GitHub's, --update-prerelease also takes pre-releases, --update-dir=&lt;folder&gt; treats that folder
/// as the game's (so the check also runs from the editor), --no-update-check turns it off.
/// </summary>
public static class UpdateCheck
{
    public const string ScriptResource = "DownhillRoguelite.update.ps1";
    private const string FeedArg = "--update-feed=";
    private const string DirArg = "--update-dir=";

    private static int _started;
    private static volatile UpdateOffer? _offer;
    private static volatile string _status = "";

    /// <summary>The update found, or null (none yet, up to date, offline or not checked).</summary>
    public static UpdateOffer? Offer => _offer;
    /// <summary>What the last check did, for logs and tests.</summary>
    public static string Status => _status;

    /// <summary>The game folder the check reads base.txt and version.txt from (next to the exe).</summary>
    public static string GameDir
    {
        get
        {
            string? dir = Arg(DirArg);
            return dir ?? OS.GetExecutablePath().GetBaseDir();
        }
    }

    /// <summary>Checking applies here: an exported Windows build (or a test folder), with the option on.</summary>
    public static bool Applies
    {
        get
        {
            if (!GameOptions.CheckForUpdates || Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-update-check") >= 0)
            {
                return false;
            }
            if (Arg(DirArg) != null)
            {
                return true;
            }
            return OS.HasFeature("template") && OS.GetName() == "Windows";
        }
    }

    /// <summary>Starts the background check once per session (later calls do nothing).</summary>
    public static void Start()
    {
        if (!Applies || Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }
        string dir = GameDir;
        string? feed = Arg(FeedArg);
        bool prerelease = Array.IndexOf(OS.GetCmdlineUserArgs(), "--update-prerelease") >= 0;
        Task.Run(async () =>
        {
            try
            {
                _offer = await FindAsync(dir, feed, prerelease);
                _status = _offer == null ? "up to date" : $"found {_offer.Kind} {_offer.Version}";
            }
            catch (Exception error)
            {
                // Offline, GitHub's hourly limit, a folder without markers: just play.
                _status = "check failed: " + error.Message;
            }
            GD.Print("UpdateCheck: " + _status);
        });
    }

    /// <summary>Reads the folder's markers and the release list, and plans (tests call it directly).</summary>
    public static async Task<UpdateOffer?> FindAsync(string gameDir, string? feed, bool includePrerelease)
    {
        string baseTag = ReadLine(Path.Combine(gameDir, "base.txt"));
        string installed = ReadLine(Path.Combine(gameDir, "version.txt"));
        if (baseTag.Length == 0 && installed.Length == 0)
        {
            throw new InvalidOperationException("this folder has no base.txt or version.txt");
        }
        string json;
        if (feed != null && File.Exists(feed))
        {
            json = await File.ReadAllTextAsync(feed);
        }
        else
        {
            using System.Net.Http.HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DownhillRoguelite-game");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            json = await client.GetStringAsync(feed ?? UpdatePlanner.ReleasesUrl);
        }
        return UpdatePlanner.Plan(json, baseTag, installed, includePrerelease);
    }

    /// <summary>
    /// Writes the updater out and starts it on <paramref name="offer"/>, to install after this game
    /// closes and then start it again. True when it started (the caller then quits the game).
    /// </summary>
    public static bool Launch(UpdateOffer offer) => Launch(offer, GameDir, true);

    /// <summary>Tests: install into <paramref name="gameDir"/>; <paramref name="restart"/> false neither waits for this process nor starts the game.</summary>
    public static bool Launch(UpdateOffer offer, string gameDir, bool restart)
    {
        string script = WriteScript();
        System.Collections.Generic.List<string> args = new()
        {
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
            "-GameDir", gameDir,
        };
        if (restart)
        {
            args.Add("-WaitPid");
            args.Add(OS.GetProcessId().ToString());
            args.Add("-Relaunch");
        }
        args.AddRange(new[]
        {
            "-Kind", offer.Kind.ToString(),
            "-Url", offer.Url,
            "-Version", offer.Version,
            "-Tag", offer.Tag,
            "-Name", offer.ReleaseName,
        });
        if (offer.Sha256.Length > 0)
        {
            args.Add("-Sha256");
            args.Add(offer.Sha256);
        }
        int pid = OS.CreateProcess("powershell.exe", args.ToArray(), restart);
        GD.Print($"UpdateCheck: started the updater for {offer.Version} (pid {pid})");
        return pid > 0;
    }

    /// <summary>The updater script from the game's code, written to the temp folder; returns its path.</summary>
    public static string WriteScript()
    {
        string target = Path.Combine(Path.GetTempPath(), "DownhillRoguelite-update.ps1");
        using Stream? source = Assembly.GetExecutingAssembly().GetManifestResourceStream(ScriptResource);
        if (source == null)
        {
            throw new InvalidOperationException("the updater is missing from the game's code");
        }
        using (FileStream file = File.Create(target))
        {
            source.CopyTo(file);
        }
        return target;
    }

    /// <summary>Tests: forget the last check so another can run.</summary>
    public static void ResetForTests()
    {
        _offer = null;
        _status = "";
        Interlocked.Exchange(ref _started, 0);
    }

    private static string ReadLine(string path)
    {
        if (!File.Exists(path))
        {
            return "";
        }
        foreach (string line in File.ReadLines(path))
        {
            return line.Trim().TrimStart('﻿');
        }
        return "";
    }

    private static string? Arg(string prefix)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                return arg.Substring(prefix.Length);
            }
        }
        return null;
    }
}
