using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// Player-side words for a feature (choices, names, hints) from a CSV with the header id,text, so
/// none of them live in code; an optional text_en column holds the English, used when Options →
/// Dialogue text is English. {name} placeholders are filled by Format. A missing id shows as [id],
/// so a gap is visible instead of crashing.
/// </summary>
public sealed class TextTable
{
    private readonly Dictionary<string, string> _texts = new();
    private readonly Dictionary<string, string> _english = new();

    public int Count => _texts.Count;

    public static TextTable Load(string path)
    {
        TextTable table = new();
        using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"TextTable: can't open {path} ({FileAccess.GetOpenError()}).");
            return table;
        }
        string[] header = file.GetCsvLine();
        int english = System.Array.FindIndex(header, h => h.Trim().TrimStart('﻿') == "text_en");
        while (!file.EofReached())
        {
            string[] row = file.GetCsvLine();
            if (row.Length >= 2 && row[0].Trim().Length > 0)
            {
                string id = row[0].Trim().TrimStart('﻿');
                table._texts[id] = row[1];
                if (english > 0 && english < row.Length && row[english].Length > 0)
                {
                    table._english[id] = row[english];
                }
            }
        }
        return table;
    }

    public bool Has(string id) => _texts.ContainsKey(id);

    public string Get(string id)
    {
        if (_texts.TryGetValue(id, out string? text))
        {
            return Core.DialogueText.FromCroatian(text, _english.TryGetValue(id, out string? english) ? english : "");
        }
        return "[" + id + "]";
    }

    /// <summary>The text with each {key} replaced by its value (pairs: key, value, key, value...).</summary>
    public string Format(string id, params object[] pairs)
    {
        string text = Get(id);
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            text = text.Replace("{" + pairs[i] + "}", pairs[i + 1].ToString());
        }
        return text;
    }
}
