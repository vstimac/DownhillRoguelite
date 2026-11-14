using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// An NPC's voice manifest: a CSV with the header id, priority, event, emotion, text_sr, audio_path
/// (narrative/president/data/npc_restsite_president.csv, imported with "keep" so Godot leaves it a
/// plain file). Lines are looked up by id or by event; nothing is ever written in code.
/// </summary>
public sealed class VoiceManifest
{
    private readonly Dictionary<string, VoiceLine> _byId = new();
    private readonly Dictionary<string, List<VoiceLine>> _byEvent = new();

    public int Count => _byId.Count;
    public IEnumerable<VoiceLine> Lines => _byId.Values;

    public static VoiceManifest Load(string path)
    {
        VoiceManifest manifest = new();
        using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"VoiceManifest: can't open {path} ({FileAccess.GetOpenError()}).");
            return manifest;
        }
        string[] header = file.GetCsvLine();
        Dictionary<string, int> column = new();
        for (int i = 0; i < header.Length; i++)
        {
            column[header[i].Trim().TrimStart('﻿')] = i;
        }
        foreach (string needed in new[] { "id", "priority", "event", "emotion", "text_sr", "audio_path" })
        {
            if (!column.ContainsKey(needed))
            {
                GD.PushWarning($"VoiceManifest: {path} has no '{needed}' column.");
                return manifest;
            }
        }
        while (!file.EofReached())
        {
            string[] row = file.GetCsvLine();
            if (row.Length < header.Length || row[column["id"]].Trim().Length == 0)
            {
                continue;
            }
            int.TryParse(row[column["priority"]], out int priority);
            VoiceLine line = new(
                row[column["id"]].Trim(),
                priority,
                row[column["event"]].Trim(),
                row[column["emotion"]].Trim(),
                row[column["text_sr"]].Trim(),
                row[column["audio_path"]].Trim(),
                column.TryGetValue("text_en", out int english) && english < row.Length ? row[english].Trim() : "");
            manifest.Add(line);
        }
        return manifest;
    }

    public void Add(VoiceLine line)
    {
        _byId[line.Id] = line;
        if (!_byEvent.TryGetValue(line.Event, out List<VoiceLine>? list))
        {
            list = new List<VoiceLine>();
            _byEvent[line.Event] = list;
        }
        list.Add(line);
    }

    public bool Has(string id) => _byId.ContainsKey(id);

    public VoiceLine? Find(string id)
    {
        if (_byId.TryGetValue(id, out VoiceLine line))
        {
            return line;
        }
        return null;
    }

    /// <summary>The lines of an event, in manifest order (empty if none).</summary>
    public IReadOnlyList<VoiceLine> ForEvent(string eventName)
    {
        if (_byEvent.TryGetValue(eventName, out List<VoiceLine>? list))
        {
            return list;
        }
        return System.Array.Empty<VoiceLine>();
    }
}
