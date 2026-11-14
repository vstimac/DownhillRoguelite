using System;
using System.Globalization;

namespace DownhillRoguelite.Core;

/// <summary>
/// The version this game is, and the day it came out. Players who don't read the patch notes go by
/// the date ("Beta 3.0" alone tells them nothing), so the title screen and the About page show both.
/// Bump both with every release, small updates included (their code replaces this one).
/// </summary>
public static class GameVersion
{
    public const string Name = "Beta 3.0";
    public static readonly DateTime Released = new(2026, 11, 14);

    /// <summary>"Beta 3.0 · 14 Nov 2026".</summary>
    public static string Label => Name + "  ·  " + Date(Released);

    /// <summary>A date as players read it everywhere in the game: "14 Nov 2026" (English month, whatever the PC's language).</summary>
    public static string Date(DateTime day)
    {
        return day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
