namespace DownhillRoguelite.Narrative;

/// <summary>One row of narrative/data/metadata.csv: a voiced narrator line and when it may play.</summary>
public sealed record NarratorLine(
    string Id,
    string Category,
    string Trigger,
    string CircleContext,
    string Phase,
    int Priority,
    float CooldownSeconds,
    int MaxPlaysPerRun,
    bool OncePerSave,
    string Text)
{
    /// <summary>Circle index beyond the ninth (0-based circles: Limbo is 0, Treachery 8).</summary>
    public const int Beyond = 9;

    /// <summary>
    /// Whether the line suits a 0-based circle: "any", "circle_3", "beyond_ninth" or a range like
    /// "circles_2_5" / "circles_6_beyond" (1-based in the file).
    /// </summary>
    public bool FitsCircle(int circle)
    {
        if (CircleContext == "any" || CircleContext.Length == 0)
        {
            return true;
        }
        if (CircleContext == "beyond_ninth")
        {
            return circle >= Beyond;
        }
        string[] parts = CircleContext.Split('_');
        if (parts.Length == 2 && parts[0] == "circle" && int.TryParse(parts[1], out int single))
        {
            return circle == single - 1;
        }
        if (parts.Length == 3 && parts[0] == "circles" && int.TryParse(parts[1], out int from))
        {
            int to = int.MaxValue;
            if (parts[2] != "beyond" && int.TryParse(parts[2], out int last))
            {
                to = last - 1;
            }
            return circle >= from - 1 && circle <= to;
        }
        return false;
    }
}
