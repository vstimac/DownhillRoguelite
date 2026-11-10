using System;

namespace DownhillRoguelite.Core;

/// <summary>
/// Hooks the restsite president's rewards need from other systems (docs/restsite-president.md):
/// a last chance before death, side quests on the objectives panel, how noticeable the carrier is,
/// and the megaphone. Each is a plain event or value; whoever owns the system decides what it does.
/// </summary>
public static partial class GameEvents
{
    /// <summary>
    /// Asked by the carrier's health just before a death (cause = the damage source). A handler that
    /// spares the carrier returns the share of health to come back with (0 = no). The first non-zero answer wins.
    /// </summary>
    public static event Func<string, float>? LastChanceRequested;

    public static float RequestLastChance(string cause)
    {
        if (LastChanceRequested == null)
        {
            return 0.0f;
        }
        foreach (Delegate handler in LastChanceRequested.GetInvocationList())
        {
            float share = ((Func<string, float>)handler)(cause);
            if (share > 0.0f)
            {
                return share;
            }
        }
        return 0.0f;
    }

    /// <summary>A side quest's line on the objectives panel changed (added, progressed, done); the objective director shows it.</summary>
    public static event Action<ObjectiveNote>? QuestNoteChanged;

    public static void PublishQuestNote(ObjectiveNote note) => QuestNoteChanged?.Invoke(note);

    /// <summary>
    /// How noticeable the carrier is to things that hunt (1 = normal). Raised for a while after a
    /// speech at the podium or a megaphone blast. Nothing reads it yet: hunters may scale their
    /// detection by it (see docs/restsite-president.md).
    /// </summary>
    public static float Detectability { get; private set; } = 1.0f;

    public static void PublishDetectability(float value) => Detectability = value;

    /// <summary>
    /// The megaphone was used: where, how far it carries (m), how long hunters within reach should be
    /// confused, then how long they notice the carrier more (s). No system reacts yet; it's the interface.
    /// </summary>
    public static event Action<Godot.Vector3, float, float, float>? MegaphoneBlasted;

    public static void PublishMegaphone(Godot.Vector3 at, float radius, float confuseSeconds, float noticeSeconds)
        => MegaphoneBlasted?.Invoke(at, radius, confuseSeconds, noticeSeconds);
}
