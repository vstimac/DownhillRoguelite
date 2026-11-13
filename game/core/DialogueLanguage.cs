using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// The language of dialogue text (Options → Accessibility → "Dialogue text"): what NPCs say in
/// subtitles, and the player's answers and notes in their conversations. Only dialogue: the rest of
/// the interface stays as it is, and voices are never changed (a Croatian voice keeps speaking
/// Croatian; with English chosen, its subtitle is the English translation).
/// </summary>
public enum DialogueLanguage
{
    English,
    Croatian,
}

/// <summary>Picks the dialogue text for the chosen language.</summary>
public static class DialogueText
{
    /// <summary>Languages counted as "Croatian" for the default: Croatian, Bosnian, Serbian, Montenegrin, Serbo-Croatian.</summary>
    private static readonly string[] LocalLanguages = { "hr", "bs", "sr", "cnr", "sh" };

    /// <summary>The first-launch default: Croatian on a Croatian/Bosnian/Serbian system, English elsewhere.</summary>
    public static DialogueLanguage SystemDefault()
    {
        string language = OS.GetLocaleLanguage().ToLowerInvariant();
        return System.Array.IndexOf(LocalLanguages, language) >= 0 ? DialogueLanguage.Croatian : DialogueLanguage.English;
    }

    /// <summary>
    /// For a line written in Croatian (<paramref name="croatian"/>) with an English translation: the
    /// translation when English is chosen and there is one, otherwise the Croatian.
    /// </summary>
    public static string FromCroatian(string croatian, string english)
    {
        return GameOptions.DialogueLanguage == DialogueLanguage.English && english.Length > 0 ? english : croatian;
    }

    /// <summary>For a line written in English with a Croatian translation: the other way round.</summary>
    public static string FromEnglish(string english, string croatian)
    {
        return GameOptions.DialogueLanguage == DialogueLanguage.Croatian && croatian.Length > 0 ? croatian : english;
    }
}
