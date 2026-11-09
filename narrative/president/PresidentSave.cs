using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// What the restsite president remembers between runs, saved to user://restsite_president.cfg:
/// whether you've met, your favour, how often you've come back, the three quests' states, the
/// unlocks they gave (temporary mandate, megaphone, the bridge's second phase), the secret scene and
/// its badge, the "Historic victories" board, whether the banner tore, and whether the last run ended in a death. What
/// belongs to one run only (the podium speech, found documents) is kept elsewhere and never saved.
/// Not persistent (headless tests) keeps it in memory, so tests never touch the player's save.
/// </summary>
public sealed class PresidentSave
{
    public const string DefaultPath = "user://restsite_president.cfg";
    public const string QuestResults = "q_final_results";
    public const string QuestContractors = "q_contractors";
    public const string QuestVictory = "q_historic_victory";
    public const string Badge = "the_last_announcement";
    private const string Section = "president";

    public bool Persistent { get; }
    public bool Met { get; set; }
    public int Favor { get; set; }
    public int Returns { get; set; }
    public bool SecretSeen { get; set; }
    public bool BadgeEarned { get; set; }
    public bool BridgeUnlocked { get; set; }
    public bool MandateUnlocked { get; set; }
    public bool MegaphoneUnlocked { get; set; }
    public int Victories { get; set; }
    public bool LastRunDied { get; set; }
    /// <summary>The banner of quest 3 was torn on the way (a partial hand-in).</summary>
    public bool VictoryTorn { get; set; }
    /// <summary>How quest 1 ended: truth, agree or silent (empty until it ends).</summary>
    public string ResultsEnding { get; set; } = "";

    private readonly Dictionary<string, QuestStatus> _quests = new();
    private readonly string _path;

    public PresidentSave(bool persistent, string path = DefaultPath)
    {
        Persistent = persistent;
        _path = path;
        if (persistent)
        {
            Load();
        }
    }

    public QuestStatus Quest(string id)
    {
        if (_quests.TryGetValue(id, out QuestStatus status))
        {
            return status;
        }
        return QuestStatus.NotStarted;
    }

    public void SetQuest(string id, QuestStatus status) => _quests[id] = status;

    public bool AllQuestsDone => Quest(QuestResults) == QuestStatus.Completed
        && Quest(QuestContractors) == QuestStatus.Completed
        && Quest(QuestVictory) == QuestStatus.Completed;

    public static string StatusText(QuestStatus status)
    {
        switch (status)
        {
            case QuestStatus.Active:
                return "active";
            case QuestStatus.Ready:
                return "ready";
            case QuestStatus.Completed:
                return "completed";
            default:
                return "not_started";
        }
    }

    public static QuestStatus ParseStatus(string text)
    {
        switch (text)
        {
            case "active":
                return QuestStatus.Active;
            case "ready":
                return QuestStatus.Ready;
            case "completed":
                return QuestStatus.Completed;
            default:
                return QuestStatus.NotStarted;
        }
    }

    /// <summary>Forgets everything (tests, a fresh save).</summary>
    public void Reset()
    {
        Met = false;
        Favor = 0;
        Returns = 0;
        SecretSeen = false;
        BadgeEarned = false;
        BridgeUnlocked = false;
        MandateUnlocked = false;
        MegaphoneUnlocked = false;
        Victories = 0;
        LastRunDied = false;
        VictoryTorn = false;
        ResultsEnding = "";
        _quests.Clear();
        Save();
    }

    public void Load()
    {
        ConfigFile file = new();
        if (file.Load(_path) != Error.Ok)
        {
            return;
        }
        Met = (bool)file.GetValue(Section, "met", false);
        Favor = (int)file.GetValue(Section, "favor", 0);
        Returns = (int)file.GetValue(Section, "player_returns_to_restsite", 0);
        SecretSeen = (bool)file.GetValue(Section, "secret_seen", false);
        BadgeEarned = (bool)file.GetValue(Section, Badge, false);
        BridgeUnlocked = (bool)file.GetValue(Section, "bridge_unlocked", false);
        MandateUnlocked = (bool)file.GetValue(Section, "mandate_unlocked", false);
        MegaphoneUnlocked = (bool)file.GetValue(Section, "megaphone_unlocked", false);
        Victories = (int)file.GetValue(Section, "historic_victories", 0);
        LastRunDied = (bool)file.GetValue(Section, "last_run_died", false);
        VictoryTorn = (bool)file.GetValue(Section, "victory_torn", false);
        ResultsEnding = (string)file.GetValue(Section, "results_ending", "");
        foreach (string quest in new[] { QuestResults, QuestContractors, QuestVictory })
        {
            _quests[quest] = ParseStatus((string)file.GetValue(Section, quest, "not_started"));
        }
    }

    public void Save()
    {
        if (!Persistent)
        {
            return;
        }
        ConfigFile file = new();
        file.SetValue(Section, "met", Met);
        file.SetValue(Section, "favor", Favor);
        file.SetValue(Section, "player_returns_to_restsite", Returns);
        file.SetValue(Section, "secret_seen", SecretSeen);
        file.SetValue(Section, Badge, BadgeEarned);
        file.SetValue(Section, "bridge_unlocked", BridgeUnlocked);
        file.SetValue(Section, "mandate_unlocked", MandateUnlocked);
        file.SetValue(Section, "megaphone_unlocked", MegaphoneUnlocked);
        file.SetValue(Section, "historic_victories", Victories);
        file.SetValue(Section, "last_run_died", LastRunDied);
        file.SetValue(Section, "victory_torn", VictoryTorn);
        file.SetValue(Section, "results_ending", ResultsEnding);
        foreach (string quest in new[] { QuestResults, QuestContractors, QuestVictory })
        {
            file.SetValue(Section, quest, StatusText(Quest(quest)));
        }
        Error error = file.Save(_path);
        if (error != Error.Ok)
        {
            GD.PushWarning($"PresidentSave: can't save {_path} ({error}).");
        }
    }
}
