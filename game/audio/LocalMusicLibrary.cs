using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace DownhillRoguelite.Audio;

/// <summary>
/// Songs the player keeps on their own computer for the summit car's radio. The game ships no
/// songs: it looks for .mp3, .ogg and .wav files in user://music/ (created on first look) and in a
/// music/ folder next to the game, and loads them from their bytes while it runs. Nothing here is
/// part of res:// or an export.
/// </summary>
public static class LocalMusicLibrary
{
    public const string UserFolder = "user://music";
    private static readonly string[] Extensions = { ".mp3", ".ogg", ".wav" };
    private static readonly Regex Clutter = new(@"\s*[\(\[][^\)\]]*[\)\]]\s*", RegexOptions.Compiled);

    /// <summary>The folders searched, in order: user://music, then music/ next to the game (exported builds).</summary>
    public static List<string> Folders()
    {
        List<string> folders = new() { UserFolder };
        if (!OS.HasFeature("editor"))
        {
            string beside = OS.GetExecutablePath().GetBaseDir().PathJoin("music");
            folders.Add(beside);
        }
        return folders;
    }

    /// <summary>Where the player is told to put songs (the real path on disk).</summary>
    public static string ShownFolder()
    {
        return ProjectSettings.GlobalizePath(UserFolder);
    }

    /// <summary>Makes user://music if it isn't there yet, so the player can find it.</summary>
    public static void EnsureFolder()
    {
        string path = ProjectSettings.GlobalizePath(UserFolder);
        if (!DirAccess.DirExistsAbsolute(path))
        {
            DirAccess.MakeDirRecursiveAbsolute(path);
        }
    }

    /// <summary>Every song in the folders, sorted by name (a song in more than one folder counts once).</summary>
    public static List<string> Scan(IEnumerable<string> folders)
    {
        List<string> found = new();
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders)
        {
            string absolute = folder;
            if (folder.StartsWith("user://") || folder.StartsWith("res://"))
            {
                absolute = ProjectSettings.GlobalizePath(folder);
            }
            if (!DirAccess.DirExistsAbsolute(absolute))
            {
                continue;
            }
            string[] files = DirAccess.GetFilesAt(absolute);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                if (!IsSong(file) || !names.Add(file))
                {
                    continue;
                }
                found.Add(absolute.PathJoin(file));
            }
        }
        found.Sort((a, b) => string.Compare(a.GetFile(), b.GetFile(), StringComparison.OrdinalIgnoreCase));
        return found;
    }

    public static bool IsSong(string file)
    {
        foreach (string extension in Extensions)
        {
            if (file.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Loads a song from its bytes, or null if it can't be read.</summary>
    public static AudioStream? Load(string path)
    {
        if (!Godot.FileAccess.FileExists(path))
        {
            return null;
        }
        byte[] bytes = Godot.FileAccess.GetFileAsBytes(path);
        if (bytes.Length == 0)
        {
            return null;
        }
        if (path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            return AudioStreamOggVorbis.LoadFromBuffer(bytes);
        }
        if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return AudioStreamWav.LoadFromBuffer(bytes);
        }
        AudioStreamMP3 mp3 = new() { Data = bytes };
        return mp3;
    }

    /// <summary>A song's name for the page: its file name without the extension or bracketed extras ("(Official Video)").</summary>
    public static string TrackName(string path)
    {
        string name = path.GetFile().GetBaseName();
        string cleaned = Clutter.Replace(name, " ").Trim();
        if (cleaned.Length == 0)
        {
            return name;
        }
        return cleaned;
    }
}
