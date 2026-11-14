using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace DownhillRoguelite.Core.Update;

/// <summary>
/// Installs an update from inside the running game (Linux: there a running program's files can be
/// replaced, so no separate updater window is needed; Windows keeps export/playtest/update.ps1).
/// Downloads to a temporary folder, checks the SHA-256, unpacks, checks that the download really
/// holds an update, and only then puts each file in place (written beside its target and renamed
/// over it, so a file is never half-written). If anything fails before that, the game folder is
/// untouched. Models/, logs and the player's saves are never in an update.
///
/// The same steps as update.ps1: a full build drops the old patches/ folder and becomes the new
/// base (base.txt); every update writes version.txt.
/// </summary>
public static class UpdateInstaller
{
    public const string WindowsExe = "DownhillRoguelite.exe";
    public const string LinuxExe = "DownhillRoguelite.x86_64";
    public const string Pck = "DownhillRoguelite.pck";
    public const string PatchFile = "patches/update.pck";

    private static volatile string _stage = "";
    private static volatile float _fraction;

    /// <summary>What the installer is doing now, for the title screen ("Downloading 42%").</summary>
    public static string Stage => _stage;
    /// <summary>How far the download is, 0..1.</summary>
    public static float Fraction => _fraction;

    public static string ExeName(UpdatePlatform platform)
    {
        if (platform == UpdatePlatform.Linux)
        {
            return LinuxExe;
        }
        return WindowsExe;
    }

    /// <summary>Downloads and installs <paramref name="offer"/> into <paramref name="gameDir"/>; throws with a player-readable message when it can't.</summary>
    public static async Task InstallAsync(UpdateOffer offer, string gameDir, UpdatePlatform platform)
    {
        _fraction = 0.0f;
        _stage = "Preparing";
        gameDir = Path.GetFullPath(gameDir);
        string probe = Path.Combine(gameDir, ".update-write-test");
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception)
        {
            throw new InvalidOperationException("can't write to the game folder. Move it somewhere you own and try again");
        }

        string work = Path.Combine(Path.GetTempPath(), "downhill-update-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(work);
        try
        {
            string archive = Path.Combine(work, "download");
            _stage = "Downloading";
            await DownloadAsync(offer.Url, archive, offer.Size);

            if (offer.Sha256.Length > 0)
            {
                _stage = "Checking the download";
                string actual = await Task.Run(() => Sha256Of(archive));
                if (!string.Equals(actual, offer.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("the download is damaged (its checksum does not match). Nothing was changed; try again");
                }
            }

            _stage = "Unpacking";
            string staging = Path.Combine(work, "files");
            Directory.CreateDirectory(staging);
            await Task.Run(() => Unpack(archive, staging));
            string exe = ExeName(platform);
            string root = staging;
            if (!File.Exists(Path.Combine(root, exe)) && !File.Exists(Path.Combine(root, PatchFile)))
            {
                // A whole build may sit in one top folder.
                List<string> inner = new();
                foreach (string folder in Directory.GetDirectories(staging))
                {
                    if (File.Exists(Path.Combine(folder, exe)))
                    {
                        inner.Add(folder);
                    }
                }
                if (inner.Count == 1)
                {
                    root = inner[0];
                }
            }
            if (offer.Kind == UpdateKind.Full)
            {
                if (!File.Exists(Path.Combine(root, exe)) || !File.Exists(Path.Combine(root, Pck)))
                {
                    throw new InvalidOperationException("the download has no game in it. Nothing was changed");
                }
            }
            else if (!File.Exists(Path.Combine(root, PatchFile)))
            {
                throw new InvalidOperationException("the update has no patch in it. Nothing was changed");
            }

            _stage = "Installing";
            await Task.Run(() => Install(offer, root, gameDir, exe));
            _stage = "Done";
        }
        finally
        {
            try
            {
                Directory.Delete(work, true);
            }
            catch (Exception)
            {
                // A leftover temporary folder is harmless.
            }
        }
    }

    private static void Install(UpdateOffer offer, string root, string gameDir, string exe)
    {
        if (offer.Kind == UpdateKind.Full)
        {
            // A new full release replaces every earlier small update.
            string patches = Path.Combine(gameDir, "patches");
            if (Directory.Exists(patches))
            {
                Directory.Delete(patches, true);
            }
            foreach (string marker in new[] { "version.txt", "base.txt" })
            {
                File.Delete(Path.Combine(gameDir, marker));
            }
        }
        foreach (string source in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, source);
            string target = Path.Combine(gameDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            // Beside the target, then renamed over it: the running game keeps the file it has open.
            string fresh = target + ".new";
            File.Copy(source, fresh, true);
            File.Move(fresh, target, true);
        }
        if (!OperatingSystem.IsWindows())
        {
            string binary = Path.Combine(gameDir, exe);
            if (File.Exists(binary))
            {
                File.SetUnixFileMode(binary, File.GetUnixFileMode(binary) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
        }
        if (offer.Kind == UpdateKind.Full && !File.Exists(Path.Combine(root, "base.txt")))
        {
            File.WriteAllText(Path.Combine(gameDir, "base.txt"), offer.Tag + "\n");
        }
        if (offer.Version.Length > 0)
        {
            File.WriteAllText(Path.Combine(gameDir, "version.txt"), offer.Version + "\n");
        }
    }

    /// <summary>Unpacks a .zip or a .tar.gz (told apart by their first bytes, not their name).</summary>
    public static void Unpack(string archive, string into)
    {
        byte[] head = new byte[2];
        using (FileStream peek = File.OpenRead(archive))
        {
            peek.ReadExactly(head, 0, 2);
        }
        if (head[0] == 0x1f && head[1] == 0x8b)
        {
            using FileStream file = File.OpenRead(archive);
            using GZipStream unzipped = new(file, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(unzipped, into, true);
            return;
        }
        ZipFile.ExtractToDirectory(archive, into, true);
    }

    private static async Task DownloadAsync(string from, string to, long expected)
    {
        if (from.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            from = new Uri(from).LocalPath;
        }
        if (File.Exists(from))
        {
            // A local path: tests.
            File.Copy(from, to, true);
            _fraction = 1.0f;
            return;
        }
        using HttpClient client = new() { Timeout = TimeSpan.FromMinutes(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DownhillRoguelite-game");
        using HttpResponseMessage response = await client.GetAsync(from, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? expected;
        await using Stream source = await response.Content.ReadAsStreamAsync();
        await using FileStream file = File.Create(to);
        byte[] buffer = new byte[1 << 20];
        long done = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer);
            if (read <= 0)
            {
                break;
            }
            await file.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            if (total > 0)
            {
                _fraction = Math.Clamp((float)done / total, 0.0f, 1.0f);
            }
        }
    }

    private static string Sha256Of(string path)
    {
        using FileStream file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }
}
