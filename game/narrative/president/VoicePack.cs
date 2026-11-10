using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// The recorded lines of an NPC (narrative/president/data/npc_restsite_president_voice.json, the
/// pack's NPC_Vucic_Dialogues.json): { "lines": [ { id, key, context, text, audio: { forest,
/// outdoor }, duration_seconds: { forest, outdoor } } ] }. A manifest line is voiced when a recorded
/// line has exactly its words, so the dialogue keeps the manifest's ids (intro_001) and the audio
/// keeps the recording's (vucic_001); both appear in the log.
/// </summary>
public sealed class VoicePack
{
    private readonly Dictionary<string, VoicePackLine> _byId = new();
    private readonly Dictionary<string, VoicePackLine> _byText = new();

    public int Count => _byId.Count;
    public IEnumerable<VoicePackLine> Lines => _byId.Values;

    public static VoicePack Load(string path)
    {
        VoicePack pack = new();
        using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"VoicePack: can't open {path} ({FileAccess.GetOpenError()}).");
            return pack;
        }
        Variant parsed = Json.ParseString(file.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            GD.PushWarning($"VoicePack: {path} is not a JSON object.");
            return pack;
        }
        Godot.Collections.Dictionary root = parsed.AsGodotDictionary();
        if (!root.ContainsKey("lines"))
        {
            return pack;
        }
        foreach (Variant entry in root["lines"].AsGodotArray())
        {
            if (entry.VariantType != Variant.Type.Dictionary)
            {
                continue;
            }
            Godot.Collections.Dictionary line = entry.AsGodotDictionary();
            Godot.Collections.Dictionary audio = Section(line, "audio");
            Godot.Collections.Dictionary seconds = Section(line, "duration_seconds");
            pack.Add(new VoicePackLine(
                Text(line, "id"),
                Text(line, "key"),
                Text(line, "context"),
                Text(line, "text"),
                Text(audio, VoicePackLine.Forest),
                Text(audio, VoicePackLine.Outdoor),
                Number(seconds, VoicePackLine.Forest),
                Number(seconds, VoicePackLine.Outdoor)));
        }
        return pack;
    }

    public void Add(VoicePackLine line)
    {
        if (line.Id.Length == 0)
        {
            return;
        }
        _byId[line.Id] = line;
        _byText[Normalize(line.Text)] = line;
    }

    public VoicePackLine? Find(string id)
    {
        if (_byId.TryGetValue(id, out VoicePackLine line))
        {
            return line;
        }
        return null;
    }

    /// <summary>The recording of these exact words, if there is one.</summary>
    public VoicePackLine? ForText(string text)
    {
        if (_byText.TryGetValue(Normalize(text), out VoicePackLine line))
        {
            return line;
        }
        return null;
    }

    private static string Normalize(string text) => text.Trim().Replace("\r", "");

    private static Godot.Collections.Dictionary Section(Godot.Collections.Dictionary from, string key)
    {
        if (from.ContainsKey(key) && from[key].VariantType == Variant.Type.Dictionary)
        {
            return from[key].AsGodotDictionary();
        }
        return new Godot.Collections.Dictionary();
    }

    private static string Text(Godot.Collections.Dictionary from, string key)
    {
        if (from.ContainsKey(key))
        {
            return from[key].AsString();
        }
        return "";
    }

    private static float Number(Godot.Collections.Dictionary from, string key)
    {
        if (from.ContainsKey(key))
        {
            return (float)from[key].AsDouble();
        }
        return 0.0f;
    }
}
