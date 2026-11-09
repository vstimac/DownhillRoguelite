using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// Player-side words for a feature (choices, names, hints) from a CSV with the header id,text, so
/// none of them live in code. {name} placeholders are filled by Format. A missing id shows as [id],
/// so a gap is visible instead of crashing.
/// </summary>
public sealed class TextTable
{
    private readonly Dictionary<string, string> _texts = new();

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
        file.GetCsvLine();
        while (!file.EofReached())
        {
            string[] row = file.GetCsvLine();
            if (row.Length >= 2 && row[0].Trim().Length > 0)
            {
                table._texts[row[0].Trim().TrimStart('﻿')] = row[1];
            }
        }
        return table;
    }

    public bool Has(string id) => _texts.ContainsKey(id);

    public string Get(string id)
    {
        if (_texts.TryGetValue(id, out string? text))
        {
            return text;
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
