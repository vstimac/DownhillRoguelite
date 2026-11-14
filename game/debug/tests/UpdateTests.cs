using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DownhillRoguelite.Core.Update;
using DownhillRoguelite.Ui;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The game keeps itself up to date: UpdatePlanner picks the right download from GitHub's release
/// list (a small update for this folder's full release, else a whole new build; drafts, pre-releases
/// and releases with only the other system's files passed over; Linux folders get their own downloads
/// and install them in-game); the updater script built into the game's code installs it
/// (checked by SHA-256, a damaged download changes nothing, a new full release clears old updates
/// and keeps the player's Models and logs); the title screen puts "Update" first. Everything runs
/// on local files: no network.
/// </summary>
public partial class UpdateTests : Node
{
    private const string Base = "v0.2.5-beta";
    private readonly List<string> _failures = new();
    private string _scratch = "";

    public override async void _Ready()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "dr-update-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_scratch);
        try
        {
            TestPlanner();
            TestEmbeddedScript();
            TestFindFromFolder();
            if (OS.GetName() == "Windows")
            {
                await TestScriptInstalls();
            }
            else
            {
                GD.Print("SKIP  the updater script runs only on Windows");
            }
            TestLinuxPlanner();
            TestLinuxInstaller();
            await TestTitleScreen();
        }
        catch (Exception error)
        {
            Check("no exception", false, error.ToString());
        }
        finally
        {
            try
            {
                Directory.Delete(_scratch, true);
            }
            catch (IOException)
            {
            }
        }
        GD.Print(_failures.Count == 0 ? "UPDATE TESTS: PASS" : "UPDATE TESTS: FAIL (" + string.Join(", ", _failures) + ")");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    // ---------------------------------------------------------------- the rules

    private void TestPlanner()
    {
        // Today's shape of the list: the newest release (v0.2.8) has nothing attached.
        string today = Feed(
            Release("v0.2.8", "2026-11-12T14:02:07Z"),
            Release("v0.2.7-beta", "2026-11-11T08:50:37Z", Asset("DownhillRoguelite-update-2.7-for-v0.2.5-beta.zip", 47686314, "cf0f"), Asset("DownhillRoguelite-linux-beta-2.7.tar.gz", 1, "aa")),
            Release("v0.2.5-beta", "2026-11-10T16:02:01Z", Asset("DownhillRoguelite-beta-2.5-windows.zip", 407060750, "bb")));
        UpdateOffer? offer = UpdatePlanner.Plan(today, Base, Base);
        Check("a 2.5 folder is offered the small 2.7 update, past the empty v0.2.8", offer != null && offer.Kind == UpdateKind.Patch && offer.Version == "2.7"
            && offer.Sha256 == "cf0f" && offer.Size == 47686314, Describe(offer));
        Check("a folder already on 2.7 is up to date", UpdatePlanner.Plan(today, Base, "2.7") == null, Describe(UpdatePlanner.Plan(today, Base, "2.7")));
        UpdateOffer? fresh = UpdatePlanner.Plan(today, "", "");
        Check("a folder without base.txt gets the whole newest Windows build", fresh != null && fresh.Kind == UpdateKind.Full && fresh.Tag == "v0.2.5-beta", Describe(fresh));

        string newFull = Feed(
            Release("v0.3.0-beta", "2026-12-01T10:00:00Z", Asset("DownhillRoguelite-windows-full.zip", 10, "c1"), Asset("DownhillRoguelite-beta-3.0-windows.zip", 20, "c2")),
            Release("v0.2.7-beta", "2026-11-11T08:50:37Z", Asset("DownhillRoguelite-update-2.7-for-v0.2.5-beta.zip", 5, "p1")));
        offer = UpdatePlanner.Plan(newFull, Base, "2.7");
        Check("a new full release beats an older small update, and the team's build beats the CI's", offer != null && offer.Kind == UpdateKind.Full
            && offer.AssetName == "DownhillRoguelite-beta-3.0-windows.zip" && offer.Version == "v0.3.0-beta", Describe(offer));
        Check("a folder on the new full release is up to date", UpdatePlanner.Plan(newFull, "v0.3.0-beta", "v0.3.0-beta") == null, "");

        string hidden = Feed(
            Release("v0.2.9-beta", "2026-11-20T10:00:00Z", true, false, Asset("DownhillRoguelite-update-2.9-for-v0.2.5-beta.zip", 5, "d1")),
            Release("v0.2.10-test", "2026-11-21T10:00:00Z", false, true, Asset("DownhillRoguelite-update-2.10-for-v0.2.5-beta.zip", 5, "d2")),
            Release("v0.2.8-beta", "2026-11-15T10:00:00Z", Asset("DownhillRoguelite-update-2.8-for-v0.2.5-beta.zip", 5, "d3"), Asset("DownhillRoguelite-update-2.8-for-v0.1-playtest1.zip", 5, "x")));
        offer = UpdatePlanner.Plan(hidden, Base, "2.7");
        Check("drafts and pre-releases are passed over; the update for this base is picked", offer != null && offer.Version == "2.8" && offer.Sha256 == "d3", Describe(offer));
        offer = UpdatePlanner.Plan(hidden, Base, "2.7", includePrerelease: true);
        Check("pre-releases are offered when asked (testing the path)", offer != null && offer.Version == "2.10", Describe(offer));

        // Out of order in the list: the most recently published wins.
        string shuffled = Feed(
            Release("v0.2.8-beta", "2026-11-15T10:00:00Z", Asset("DownhillRoguelite-update-2.8-for-v0.2.5-beta.zip", 5, "e1")),
            Release("v0.2.9-beta", "2026-11-20T10:00:00Z", Asset("DownhillRoguelite-update-2.9-for-v0.2.5-beta.zip", 5, "e2")));
        offer = UpdatePlanner.Plan(shuffled, Base, "2.8");
        Check("the most recently published release decides", offer != null && offer.Version == "2.9", Describe(offer));

        // A folder ahead of everything published (a test build of 2.9 while 2.9 is still uploading)
        // is never offered an older full game or an older small update.
        Check("a folder ahead of the published releases is offered nothing older",
            UpdatePlanner.Plan(today, "v0.2.9-beta", "2.9") == null && UpdatePlanner.Plan(shuffled, Base, "3.0") == null,
            Describe(UpdatePlanner.Plan(today, "v0.2.9-beta", "2.9")));
        Check("versions compare as numbers, tags and versions alike",
            UpdatePlanner.CompareVersions("v0.2.9-beta", "2.9") == 0 && UpdatePlanner.CompareVersions("v0.3.0-beta", "3.0") == 0
            && UpdatePlanner.CompareVersions("2.10", "2.9") > 0 && UpdatePlanner.CompareVersions("2.9.1", "2.9") > 0
            && UpdatePlanner.CompareVersions("(unknown)", "2.9") == null, "");
        Check("asset names are read strictly", UpdatePlanner.PatchVersion("DownhillRoguelite-update--for-v0.2.5-beta.zip", Base) == null
            && !UpdatePlanner.IsFullWindowsBuild("DownhillRoguelite-linux-beta.tar.gz") && !UpdatePlanner.IsFullWindowsBuild("DownhillRoguelite-update-2.7-for-x-windows.zip")
            && UpdatePlanner.IsFullWindowsBuild("DownhillRoguelite-windows-full.zip"), "");
    }

    private void TestLinuxPlanner()
    {
        const string LinuxBase = "v0.2.9-beta";
        string both = Feed(
            Release("v0.3.0-beta", "2026-12-01T10:00:00Z",
                Asset("DownhillRoguelite-update-3.0-for-v0.2.5-beta.zip", 5, "w1"),
                Asset("DownhillRoguelite-update-3.0-linux-for-v0.2.9-beta.tar.gz", 7, "l1")),
            Release("v0.2.9-beta", "2026-11-20T10:00:00Z",
                Asset("DownhillRoguelite-update-2.9-for-v0.2.5-beta.zip", 5, "w0"),
                Asset("DownhillRoguelite-linux-x86_64.tar.gz", 400, "l0")));
        UpdateOffer? offer = UpdatePlanner.Plan(both, LinuxBase, LinuxBase, false, UpdatePlatform.Linux);
        Check("a Linux folder is offered the Linux small update, not the Windows one", offer != null && offer.Kind == UpdateKind.Patch && offer.Version == "3.0"
            && offer.Sha256 == "l1" && offer.AssetName.EndsWith(".tar.gz"), Describe(offer));
        Check("a Linux folder on that version is up to date", UpdatePlanner.Plan(both, LinuxBase, "3.0", false, UpdatePlatform.Linux) == null, "");
        offer = UpdatePlanner.Plan(both, Base, Base);
        Check("a Windows folder still gets its own update from the same release", offer != null && offer.Version == "3.0" && offer.Sha256 == "w1", Describe(offer));
        offer = UpdatePlanner.Plan(both, "", "old", false, UpdatePlatform.Linux);
        Check("a Linux folder with no base gets the whole newest Linux build", offer != null && offer.Kind == UpdateKind.Full && offer.Tag == "v0.2.9-beta"
            && offer.Sha256 == "l0", Describe(offer));

        // A release with only Windows files is passed over for Linux, and the other way round.
        string windowsOnly = Feed(
            Release("v0.3.1-beta", "2026-12-05T10:00:00Z", Asset("DownhillRoguelite-update-3.1-for-v0.2.5-beta.zip", 5, "w2"), Asset("DownhillRoguelite-beta-3.1-windows.zip", 9, "w3")),
            Release("v0.2.9-beta", "2026-11-20T10:00:00Z", Asset("DownhillRoguelite-linux-x86_64.tar.gz", 400, "l0")));
        Check("a Windows-only release offers Linux nothing new", UpdatePlanner.Plan(windowsOnly, LinuxBase, LinuxBase, false, UpdatePlatform.Linux) == null, "");
        Check("Linux and Windows names are never mixed up",
            UpdatePlanner.PatchVersion("DownhillRoguelite-update-3.0-linux-for-v0.2.9-beta.tar.gz", LinuxBase) == null
            && UpdatePlanner.PatchVersion("DownhillRoguelite-update-3.0-for-v0.2.9-beta.zip", LinuxBase, UpdatePlatform.Linux) == null
            && UpdatePlanner.PatchVersion("DownhillRoguelite-update-3.0-linux-for-v0.2.9-beta.tar.gz", LinuxBase, UpdatePlatform.Linux) == "3.0"
            && UpdatePlanner.PatchName("3.0", LinuxBase, UpdatePlatform.Linux) == "DownhillRoguelite-update-3.0-linux-for-v0.2.9-beta.tar.gz"
            && UpdatePlanner.PatchName("3.0", Base, UpdatePlatform.Windows) == "DownhillRoguelite-update-3.0-for-v0.2.5-beta.zip"
            && UpdatePlanner.IsFullLinuxBuild("DownhillRoguelite-linux-beta-2.7.tar.gz") && !UpdatePlanner.IsFullLinuxBuild("DownhillRoguelite-update-3.0-linux-for-x.tar.gz")
            && !UpdatePlanner.IsFullLinuxBuild("DownhillRoguelite-beta-3.0-windows.zip") && !UpdatePlanner.IsFullWindowsBuild("DownhillRoguelite-linux-x86_64.tar.gz"), "");
    }

    /// <summary>The in-game installer Linux uses (it runs anywhere: here on local .tar.gz files).</summary>
    private void TestLinuxInstaller()
    {
        const string LinuxBase = "v0.2.9-beta";
        string data = "data_DownhillRoguelite_linuxbsd_x86_64";
        string game = Directory.CreateDirectory(Path.Combine(_scratch, "linux game")).FullName;
        File.WriteAllText(Path.Combine(game, "base.txt"), LinuxBase + "\n");
        File.WriteAllText(Path.Combine(game, "version.txt"), LinuxBase + "\n");
        File.WriteAllText(Path.Combine(game, UpdateInstaller.LinuxExe), "old binary");
        File.WriteAllText(Path.Combine(game, UpdateInstaller.Pck), "old pck");
        Directory.CreateDirectory(Path.Combine(game, data));
        File.WriteAllText(Path.Combine(game, data, "DownhillRoguelite.dll"), "old code");
        Directory.CreateDirectory(Path.Combine(game, "Models"));
        File.WriteAllText(Path.Combine(game, "Models", "model.gguf"), "the player's model");

        string patch = TarGz("DownhillRoguelite-update-3.0-linux-for-v0.2.9-beta.tar.gz", new()
        {
            ["patches/update.pck"] = "patch 3.0",
            [data + "/DownhillRoguelite.dll"] = "new code 3.0",
            ["version.txt"] = "3.0\n",
        });
        string feed = Path.Combine(_scratch, "linux-feed.json");
        File.WriteAllText(feed, Feed(Release("v0.3.0-beta", "2026-12-01T10:00:00Z", Asset(Path.GetFileName(patch), new FileInfo(patch).Length, Sha(patch), patch))));
        UpdateOffer? offer = Task.Run(() => UpdateCheck.FindAsync(game, feed, false, UpdatePlatform.Linux)).GetAwaiter().GetResult();
        string error = Install(offer, game);
        Check("Linux: the small update installs from inside the game", error.Length == 0 && Read(game, "patches/update.pck") == "patch 3.0"
            && Read(game, data + "/DownhillRoguelite.dll") == "new code 3.0" && Read(game, "version.txt").Trim() == "3.0"
            && Read(game, "base.txt").Trim() == LinuxBase && Read(game, UpdateInstaller.LinuxExe) == "old binary", error);
        offer = Task.Run(() => UpdateCheck.FindAsync(game, feed, false, UpdatePlatform.Linux)).GetAwaiter().GetResult();
        Check("Linux: after it the folder is up to date", offer == null, Describe(offer));

        string damaged = TarGz("DownhillRoguelite-update-3.1-linux-for-v0.2.9-beta.tar.gz", new() { ["patches/update.pck"] = "patch 3.1", ["version.txt"] = "3.1\n" });
        UpdateOffer bad = new(UpdateKind.Patch, "3.1", "v0.3.1-beta", "Beta 3.1", Path.GetFileName(damaged), damaged, new FileInfo(damaged).Length, new string((char)48, 64), "");
        error = Install(bad, game);
        Check("Linux: a damaged download is refused and the game is left as it was", error.Contains("damaged") && Read(game, "version.txt").Trim() == "3.0"
            && Read(game, "patches/update.pck") == "patch 3.0", error);

        string full = TarGz("DownhillRoguelite-linux-x86_64.tar.gz", new()
        {
            ["./" + UpdateInstaller.LinuxExe] = "binary 3.2",
            ["./" + UpdateInstaller.Pck] = "pck 3.2",
            ["./" + data + "/DownhillRoguelite.dll"] = "code 3.2",
            ["./base.txt"] = "v0.3.2-beta\n",
            ["./version.txt"] = "v0.3.2-beta\n",
        });
        UpdateOffer whole = new(UpdateKind.Full, "v0.3.2-beta", "v0.3.2-beta", "Beta 3.2", Path.GetFileName(full), full, new FileInfo(full).Length, Sha(full), "");
        error = Install(whole, game);
        Check("Linux: a new full build installs, drops the old small update and becomes the base", error.Length == 0 && Read(game, UpdateInstaller.LinuxExe) == "binary 3.2"
            && Read(game, UpdateInstaller.Pck) == "pck 3.2" && !Directory.Exists(Path.Combine(game, "patches")) && Read(game, "base.txt").Trim() == "v0.3.2-beta"
            && Read(game, "version.txt").Trim() == "v0.3.2-beta" && Read(game, "Models/model.gguf") == "the player's model", error);
        Check("Linux: nothing is left behind next to the game", !File.Exists(Path.Combine(game, ".update-write-test"))
            && Directory.GetFiles(game, "*.new", SearchOption.AllDirectories).Length == 0, "");
    }

    /// <summary>Runs the in-game installer; returns "" or what went wrong.</summary>
    private static string Install(UpdateOffer? offer, string game)
    {
        if (offer == null)
        {
            return "no update was offered";
        }
        try
        {
            Task.Run(() => UpdateInstaller.InstallAsync(offer, game, UpdatePlatform.Linux)).GetAwaiter().GetResult();
            return "";
        }
        catch (Exception error)
        {
            return error.GetBaseException().Message;
        }
    }

    private string TarGz(string name, Dictionary<string, string> files)
    {
        string path = Path.Combine(_scratch, name);
        using FileStream file = File.Create(path);
        using GZipStream zipped = new(file, CompressionLevel.Fastest);
        using System.Formats.Tar.TarWriter writer = new(zipped, System.Formats.Tar.TarEntryFormat.Pax);
        foreach (KeyValuePair<string, string> entry in files)
        {
            System.Formats.Tar.PaxTarEntry item = new(System.Formats.Tar.TarEntryType.RegularFile, entry.Key)
            {
                DataStream = new MemoryStream(new UTF8Encoding(false).GetBytes(entry.Value)),
            };
            writer.WriteEntry(item);
        }
        return path;
    }

    private void TestEmbeddedScript()
    {
        string script = UpdateCheck.WriteScript();
        string source = ProjectSettings.GlobalizePath("res://export/playtest/update.ps1");
        bool same = File.Exists(source) && File.ReadAllText(script) == File.ReadAllText(source);
        Check("the updater is built into the game's code, the same as the folder's update.ps1", same, script);
    }

    private void TestFindFromFolder()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_scratch, "markers")).FullName;
        File.WriteAllText(Path.Combine(folder, "base.txt"), "﻿" + Base + "\r\n");
        File.WriteAllText(Path.Combine(folder, "version.txt"), "2.7\n");
        string feed = Path.Combine(_scratch, "markers.json");
        File.WriteAllText(feed, Feed(Release("v0.2.8-beta", "2026-11-15T10:00:00Z", Asset("DownhillRoguelite-update-2.8-for-v0.2.5-beta.zip", 5, "f1"))));
        UpdateOffer? offer = Task.Run(() => UpdateCheck.FindAsync(folder, feed, false)).GetAwaiter().GetResult();
        Check("the game reads its folder's base.txt and version.txt", offer != null && offer.Version == "2.8", Describe(offer));
    }

    // ---------------------------------------------------------------- the updater itself

    private async Task TestScriptInstalls()
    {
        string script = UpdateCheck.WriteScript();
        string game = Directory.CreateDirectory(Path.Combine(_scratch, "Downhill Roguelite (game)")).FullName;
        File.WriteAllText(Path.Combine(game, "base.txt"), Base + "\n");
        File.WriteAllText(Path.Combine(game, "version.txt"), Base + "\n");
        File.WriteAllText(Path.Combine(game, "DownhillRoguelite.exe"), "old exe");
        File.WriteAllText(Path.Combine(game, "DownhillRoguelite.pck"), "old pck");
        Directory.CreateDirectory(Path.Combine(game, "data_DownhillRoguelite_windows_x86_64"));
        File.WriteAllText(Path.Combine(game, "data_DownhillRoguelite_windows_x86_64", "DownhillRoguelite.dll"), "old code");
        Directory.CreateDirectory(Path.Combine(game, "Models"));
        File.WriteAllText(Path.Combine(game, "Models", "model.gguf"), "the player's model");

        // 1. A small update.
        string patch = Zip("DownhillRoguelite-update-2.8-for-v0.2.5-beta.zip", new()
        {
            ["patches/update.pck"] = "patch 2.8",
            ["data_DownhillRoguelite_windows_x86_64/DownhillRoguelite.dll"] = "new code 2.8",
            ["version.txt"] = "2.8\n",
        });
        string feed = Path.Combine(_scratch, "feed.json");
        File.WriteAllText(feed, Feed(Release("v0.2.8-beta", "2026-11-15T10:00:00Z", Asset(Path.GetFileName(patch), new FileInfo(patch).Length, Sha(patch), patch))));
        (int code, string output) = await RunScript(script, "-GameDir", game, "-Feed", feed, "-CheckOnly");
        Check("-CheckOnly reports the update (exit 10)", code == 10 && output.Contains("2.8"), $"exit {code}: {Last(output)}");
        (code, output) = await RunScript(script, "-GameDir", game, "-Feed", feed);
        Check("the small update installs", code == 0 && Read(game, "patches/update.pck") == "patch 2.8"
            && Read(game, "data_DownhillRoguelite_windows_x86_64/DownhillRoguelite.dll") == "new code 2.8" && Read(game, "version.txt").Trim() == "2.8"
            && Read(game, "base.txt").Trim() == Base && Read(game, "DownhillRoguelite.exe") == "old exe", $"exit {code}: {Last(output)}");
        (code, output) = await RunScript(script, "-GameDir", game, "-Feed", feed);
        Check("run again, it is up to date and changes nothing", code == 0 && output.Contains("newest version") && Read(game, "version.txt").Trim() == "2.8", $"exit {code}: {Last(output)}");

        // 2. A damaged download changes nothing.
        string full = Zip("DownhillRoguelite-beta-3.0-windows.zip", new()
        {
            ["DownhillRoguelite-playtest1/DownhillRoguelite.exe"] = "exe 3.0",
            ["DownhillRoguelite-playtest1/DownhillRoguelite.pck"] = "pck 3.0",
            ["DownhillRoguelite-playtest1/data_DownhillRoguelite_windows_x86_64/DownhillRoguelite.dll"] = "code 3.0",
            ["DownhillRoguelite-playtest1/base.txt"] = "v0.3.0-beta\n",
            ["DownhillRoguelite-playtest1/version.txt"] = "v0.3.0-beta\n",
        });
        string bad = Path.Combine(_scratch, "bad.json");
        File.WriteAllText(bad, Feed(Release("v0.3.0-beta", "2026-12-01T10:00:00Z", Asset(Path.GetFileName(full), new FileInfo(full).Length, new string('0', 64), full))));
        (code, output) = await RunScript(script, "-GameDir", game, "-Feed", bad);
        Check("a damaged download is refused and the game is left as it was", code == 1 && output.Contains("damaged") && Read(game, "version.txt").Trim() == "2.8"
            && File.Exists(Path.Combine(game, "patches", "update.pck")) && Read(game, "DownhillRoguelite.exe") == "old exe", $"exit {code}: {Last(output)}");

        // 3. A new full release replaces the game and clears the old updates, keeping the player's files.
        File.WriteAllText(feed, Feed(
            Release("v0.3.0-beta", "2026-12-01T10:00:00Z", Asset(Path.GetFileName(full), new FileInfo(full).Length, Sha(full), full)),
            Release("v0.2.8-beta", "2026-11-15T10:00:00Z", Asset(Path.GetFileName(patch), new FileInfo(patch).Length, Sha(patch), patch))));
        (code, output) = await RunScript(script, "-GameDir", game, "-Feed", feed);
        Check("a new full release installs over the folder, dropping its top folder", code == 0 && Read(game, "DownhillRoguelite.exe") == "exe 3.0"
            && Read(game, "data_DownhillRoguelite_windows_x86_64/DownhillRoguelite.dll") == "code 3.0", $"exit {code}: {Last(output)}");
        Check("the old small updates are gone and the folder is a 3.0 base", !Directory.Exists(Path.Combine(game, "patches"))
            && Read(game, "base.txt").Trim() == "v0.3.0-beta" && Read(game, "version.txt").Trim() == "v0.3.0-beta", $"base {Read(game, "base.txt").Trim()}, version {Read(game, "version.txt").Trim()}");
        Check("the player's Models folder is kept", Read(game, "Models/model.gguf") == "the player's model", "");
        Check("nothing is left behind next to the game", !File.Exists(Path.Combine(game, ".update-write-test")) && !Directory.Exists(Path.Combine(game, "DownhillRoguelite-playtest1")), "");

        // 4. The way the game starts it: the chosen download handed over directly.
        File.WriteAllText(Path.Combine(game, "version.txt"), "v0.3.0-beta\n");
        string next = Zip("DownhillRoguelite-update-3.1-for-v0.3.0-beta.zip", new() { ["patches/update.pck"] = "patch 3.1", ["version.txt"] = "3.1\n" });
        (code, output) = await RunScript(script, "-GameDir", game, "-Kind", "Patch", "-Url", "file:///" + next.Replace('\\', '/'), "-Sha256", Sha(next), "-Version", "3.1", "-Tag", "v0.3.1-beta", "-Name", "Beta 3.1");
        Check("the game's hand-over (-Url, -Sha256, -Kind) installs without asking GitHub", code == 0 && Read(game, "patches/update.pck") == "patch 3.1" && Read(game, "version.txt").Trim() == "3.1", $"exit {code}: {Last(output)}");

        // 5. Started the way the title screen starts it (Godot's OS.CreateProcess quoting: spaces in
        //    the folder, a colon and spaces in the release's name).
        string last = Zip("DownhillRoguelite-update-3.2-for-v0.3.0-beta.zip", new() { ["patches/update.pck"] = "patch 3.2", ["version.txt"] = "3.2\n" });
        UpdateOffer offer = new(UpdateKind.Patch, "3.2", "v0.3.2-beta", "Beta 3.2: the counter clerk", Path.GetFileName(last), last, new FileInfo(last).Length, Sha(last), "");
        bool started = UpdateCheck.Launch(offer, game, false);
        Stopwatch clock = Stopwatch.StartNew();
        while (started && Read(game, "version.txt").Trim() != "3.2" && clock.Elapsed.TotalSeconds < 60.0)
        {
            System.Threading.Thread.Sleep(100);
        }
        double waited = clock.Elapsed.TotalSeconds;
        Check("the title screen's way of starting the updater installs it", started && Read(game, "patches/update.pck") == "patch 3.2" && Read(game, "version.txt").Trim() == "3.2",
            $"started {started}, {waited:0.0} s, version {Read(game, "version.txt").Trim()}");
    }

    // ---------------------------------------------------------------- the title screen

    private async Task TestTitleScreen()
    {
        CanvasLayer layer = new();
        AddChild(layer);
        StartScreen start = new() { ForceShow = true, WarmupSeconds = 1000.0f };
        layer.AddChild(start);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        int before = start.Items.Count;
        UpdateOffer offer = new(UpdateKind.Patch, "2.8", "v0.2.8-beta", "Beta 2.8: the counter clerk", "x.zip", "https://example.invalid/x.zip", 48L * 1048576, "", "");
        start.OfferUpdate(offer);
        start.OfferUpdate(offer);
        MenuItem? item = start.UpdateItem;
        Check("a found update puts one \"Update\" entry at the top of the menu", item != null && start.Items.Count == before + 1 && start.Items[0] == item
            && item.GetIndex() == 0, $"{before} -> {start.Items.Count} entries");
        Check("its hint says what, how big, and that the game restarts", item != null && item.Hint.Contains("Beta 2.8") && item.Hint.Contains("48 MB") && item.Hint.Contains("starts again"), item?.Hint ?? "");
        Check("the corner says a new version is out", start.VersionLabel != null && start.VersionLabel.Text.Contains("new version"), start.VersionLabel?.Text ?? "");
        Check("the check never runs from the editor or tests", !UpdateCheck.Applies, UpdateCheck.Status);
        layer.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // ---------------------------------------------------------------- helpers

    private static Task<(int, string)> RunScript(string script, params string[] args)
    {
        ProcessStartInfo info = new("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script })
        {
            info.ArgumentList.Add(arg);
        }
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }
        // Waits on the main thread: the smoke test stops each scene after a number of frames, and
        // frames must not run on (and end the test early) while PowerShell works.
        using Process process = Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return Task.FromResult((process.ExitCode, output.Result + errors.Result));
    }

    private string Zip(string name, Dictionary<string, string> files)
    {
        string path = Path.Combine(_scratch, name);
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (KeyValuePair<string, string> file in files)
        {
            ZipArchiveEntry entry = archive.CreateEntry(file.Key);
            using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
            writer.Write(file.Value);
        }
        return path;
    }

    private static string Sha(string path)
    {
        using FileStream file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }

    private static string Read(string folder, string relative)
    {
        string path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.ReadAllText(path) : "(missing)";
    }

    private static string Feed(params object[] releases) => JsonSerializer.Serialize(releases);

    private static object Release(string tag, string published, params object[] assets) => Release(tag, published, false, false, assets);

    private static object Release(string tag, string published, bool draft, bool prerelease, params object[] assets)
    {
        return new Dictionary<string, object>
        {
            ["tag_name"] = tag,
            ["name"] = tag,
            ["draft"] = draft,
            ["prerelease"] = prerelease,
            ["published_at"] = published,
            ["html_url"] = "https://github.com/" + UpdatePlanner.Repo + "/releases/tag/" + tag,
            ["assets"] = assets,
        };
    }

    private static object Asset(string name, long size, string sha, string? url = null)
    {
        return new Dictionary<string, object>
        {
            ["name"] = name,
            ["size"] = size,
            ["digest"] = "sha256:" + sha,
            ["browser_download_url"] = url ?? "https://github.com/" + UpdatePlanner.Repo + "/releases/download/x/" + name,
        };
    }

    private static string Describe(UpdateOffer? offer) => offer == null ? "none" : $"{offer.Kind} {offer.Version} ({offer.AssetName}, {offer.Size} bytes, sha {offer.Sha256})";

    private static string Last(string output)
    {
        string[] lines = output.Trim().Split('\n');
        return string.Join(" | ", lines, Math.Max(0, lines.Length - 3), Math.Min(3, lines.Length)).Replace("\r", "").Trim();
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
