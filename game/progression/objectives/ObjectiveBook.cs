using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// What the carrier is trying to do right now, worked out from what has happened (plain C#, no
/// node, so tests can drive it directly; the ObjectiveDirector feeds it game events):
/// - the one main goal: the next station before dark, sleeping at the station once evening comes,
///   then the station after that;
/// - the circle below as the long goal;
/// - on the first runs, a guide: follow the cairns, pick up food or water, read a sign, eat or drink,
///   take a boon, meet the traveller or take a job;
/// - side goals: jobs at the board (named after the traveller who asked, if one sits there), relics
///   to leave at an altar, and something worth a detour close by.
/// Finished side goals stay a few seconds (struck through) and then go to Finished.
/// </summary>
public sealed class ObjectiveBook
{
    public const string MainId = "main";
    public const string CircleId = "circle";
    public const string NearbyId = "nearby";

    /// <summary>Metres of descent that finish the "follow the cairns" step.</summary>
    public float GuideWalkDepth { get; set; } = 25.0f;
    /// <summary>Seconds a finished side goal stays on show before it moves to Finished.</summary>
    public float DoneLinger { get; set; } = 8.0f;

    public bool GuideOn { get; private set; }
    public int Highest { get; private set; }
    public int Shelter { get; private set; }
    public int SleptAt { get; private set; }
    public bool CanSleep { get; set; }
    public bool Night { get; set; }
    /// <summary>The main goal's progress line ("0.6 km · ~4 min · before dark"), formatted by the director.</summary>
    public string MainProgress { get; set; } = "";
    public bool HasProvisions { get; private set; }
    public float Depth { get; private set; }
    public IReadOnlyList<ObjectiveNote> Finished => _finished;

    private readonly HashSet<GuideStep> _steps = new();
    private readonly List<Side> _sides = new();
    private readonly List<ObjectiveNote> _finished = new();
    private readonly List<ObjectiveNote> _announcements = new();
    private ObjectiveNote _circle = new(CircleId, ObjectiveKind.Circle, "", "", "");
    private WaysideNote? _nearby;
    private double _nowHours;
    private float _hoursPerSecond;

    private sealed class Side
    {
        public ObjectiveNote Note;
        public int FromStation;
        public bool Offered;
        public float DoneFor;
    }

    /// <summary>A new run: no stations, no side goals, the guide on or off.</summary>
    public void Reset(bool guide)
    {
        GuideOn = guide;
        Highest = 0;
        Shelter = 0;
        SleptAt = 0;
        Depth = 0.0f;
        HasProvisions = false;
        MainProgress = "";
        _steps.Clear();
        _sides.Clear();
        _finished.Clear();
        _announcements.Clear();
        _nearby = null;
    }

    /// <summary>Goals that are new or just finished since the last call (the director announces them).</summary>
    public List<ObjectiveNote> TakeAnnouncements()
    {
        List<ObjectiveNote> taken = new(_announcements);
        _announcements.Clear();
        return taken;
    }

    public void SetClock(double nowHours, float hoursPerSecond)
    {
        _nowHours = nowHours;
        _hoursPerSecond = hoursPerSecond;
    }

    /// <summary>The circle goal: the next circle's number and name and how far it is, or past the ninth.</summary>
    public void SetCircle(int nextCircle, string nextName, float metresToGo, bool beyond)
    {
        if (beyond)
        {
            _circle = new ObjectiveNote(CircleId, ObjectiveKind.Circle, "Go deeper than anyone",
                "The circles are behind you. Whatever waits below has no name.", Mathf.FloorToInt(Depth) + " m down");
            return;
        }
        string title = "Go beyond the ninth circle";
        if (nextCircle < 10)
        {
            title = "Descend into Circle " + Roman(nextCircle) + " · " + nextName;
        }
        _circle = new ObjectiveNote(CircleId, ObjectiveKind.Circle, title,
            "Somewhere below, they say, a carrier becomes a god.", Mathf.CeilToInt(Mathf.Max(metresToGo, 0.0f)) + " m to go");
    }

    public void StationReached(int station)
    {
        Highest = Mathf.Max(Highest, station);
        // Jobs offered at earlier stations can't be taken any more (nobody walks back uphill).
        _sides.RemoveAll(side => side.Offered && side.FromStation < Highest);
    }

    public void ShelterChanged(bool inside, int station)
    {
        Shelter = 0;
        if (inside)
        {
            Shelter = station;
        }
    }

    public void Slept()
    {
        SleptAt = Shelter;
        if (SleptAt == 0)
        {
            SleptAt = Highest;
        }
    }

    public void DepthChanged(float depth)
    {
        Depth = Mathf.Max(Depth, depth);
        if (Depth >= GuideWalkDepth)
        {
            Complete(GuideStep.Walk);
        }
    }

    public void ItemPicked(string category)
    {
        if (category == "Food" || category == "Water")
        {
            HasProvisions = true;
            Complete(GuideStep.Gather);
        }
    }

    public void Consumed() => Complete(GuideStep.EatDrink);

    public void LoreRead() => Complete(GuideStep.Read);

    public void BoonTaken() => Complete(GuideStep.Boon);

    public void TalkedToTraveller() => Complete(GuideStep.Visit);

    /// <summary>Whether a guide step is done (or skipped by finishing it early).</summary>
    public bool IsDone(GuideStep step) => _steps.Contains(step);

    /// <summary>The guide step shown now, or null when the guide is off or nothing is left to show.</summary>
    public GuideStep? CurrentStep()
    {
        if (!GuideOn)
        {
            return null;
        }
        // At the first station's fire, the station's own steps come before anything left over from the way down.
        if (Shelter == 1)
        {
            foreach (GuideStep step in new[] { GuideStep.Boon, GuideStep.Visit })
            {
                if (!_steps.Contains(step) && Available(step))
                {
                    return step;
                }
            }
        }
        foreach (GuideStep step in System.Enum.GetValues<GuideStep>())
        {
            if (!_steps.Contains(step) && Available(step))
            {
                return step;
            }
        }
        return null;
    }

    private bool Available(GuideStep step)
    {
        switch (step)
        {
            case GuideStep.Walk:
            case GuideStep.Read:
                return Highest < 1;
            case GuideStep.EatDrink:
                return HasProvisions;
            case GuideStep.Boon:
            case GuideStep.Visit:
                return Highest == 1;
            default:
                return true;
        }
    }

    private void Complete(GuideStep step)
    {
        GuideStep? shown = CurrentStep();
        if (!_steps.Add(step))
        {
            return;
        }
        if (shown == step)
        {
            ObjectiveNote note = GuideNote(step);
            _announcements.Add(note with { Done = true });
        }
    }

    /// <summary>A job offered at a station's board; the traveller sitting there (if any) asks for it.</summary>
    public void JobOffered(ContractNote job, string giver)
    {
        string id = ErrandId(job.InstanceId);
        if (FindSide(id) != null)
        {
            return;
        }
        string title = "A job at the board: " + job.Title;
        if (giver.Length > 0)
        {
            title = giver + " asks: " + job.Title;
        }
        ObjectiveNote note = new(id, ObjectiveKind.Errand, title,
            "Carry the " + job.CargoName.ToLowerInvariant() + " (" + job.CargoMassKg.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " kg) to Station " + job.ToStation + ". Take it at the board by the fire (E).",
            RewardLine(job.Score), false, false, giver);
        _sides.Add(new Side { Note = note, FromStation = job.FromStation, Offered = true });
        _announcements.Add(note);
    }

    /// <summary>The job was taken: the errand is now to carry it down.</summary>
    public void JobAccepted(ContractNote job, string giver)
    {
        string id = ErrandId(job.InstanceId);
        Side? side = FindSide(id);
        if (side == null)
        {
            side = new Side { FromStation = job.FromStation };
            _sides.Add(side);
        }
        if (giver.Length == 0)
        {
            giver = side.Note.Giver ?? "";
        }
        string title = "Job: " + job.Title;
        if (giver.Length > 0)
        {
            title = giver + "'s errand: " + job.Title;
        }
        side.Offered = false;
        side.Note = new ObjectiveNote(id, ObjectiveKind.Errand, title,
            "Carry the " + job.CargoName.ToLowerInvariant() + " down to Station " + job.ToStation + " and hand it over at the table there (E, then E).",
            JobProgress(job), false, false, giver);
        _announcements.Add(side.Note);
        Complete(GuideStep.Visit);
    }

    /// <summary>The cargo took a knock or the clock moved: refresh the errand's condition and time left.</summary>
    public void JobUpdated(ContractNote job)
    {
        Side? side = FindSide(ErrandId(job.InstanceId));
        if (side != null && !side.Offered && !side.Note.Done)
        {
            side.Note = side.Note with { Progress = JobProgress(job) };
        }
    }

    /// <summary>The job ended: delivered (with what it paid, <paramref name="reward"/>), failed or given up.</summary>
    public void JobResolved(ContractNote job, string reward)
    {
        Side? side = FindSide(ErrandId(job.InstanceId));
        if (side == null)
        {
            side = new Side { FromStation = job.FromStation, Note = new ObjectiveNote(ErrandId(job.InstanceId), ObjectiveKind.Errand, "Job: " + job.Title, "", "") };
            _sides.Add(side);
        }
        side.Offered = false;
        if (job.Status == ContractStatus.Delivered)
        {
            string paid = "Done: +" + job.Score;
            if (reward.Length > 0)
            {
                paid += ", " + reward;
            }
            side.Note = side.Note with { Progress = paid, Done = true };
        }
        else if (job.Status == ContractStatus.Abandoned)
        {
            side.Note = side.Note with { Progress = "Given up", Done = true, Failed = true };
        }
        else
        {
            string why = "Failed";
            if (!string.IsNullOrEmpty(job.Reason))
            {
                why = "Failed: the " + job.CargoName.ToLowerInvariant() + " " + job.Reason;
            }
            side.Note = side.Note with { Progress = why, Done = true, Failed = true };
        }
        _announcements.Add(side.Note);
    }

    /// <summary>A relic was found: leave it at a station altar.</summary>
    public void RelicFound(RelicNote relic)
    {
        string id = "relic:" + relic.Id;
        if (FindSide(id) != null)
        {
            return;
        }
        ObjectiveNote note = new(id, ObjectiveKind.Relic, "Leave the " + StripThe(relic.Name) + " at an altar",
            "Every station has an altar beside its graves: E, then E again.", "Studied relics add to your score · knocks hurt it");
        _sides.Add(new Side { Note = note });
        _announcements.Add(note);
    }

    public void RelicLeft(RelicNote relic)
    {
        Side? side = FindSide("relic:" + relic.Id);
        if (side == null)
        {
            return;
        }
        side.Note = side.Note with { Progress = "Studied: +" + relic.Score, Done = true };
        _announcements.Add(side.Note);
    }

    /// <summary>
    /// A side quest from someone outside the job boards (the restsite president): added, updated or
    /// finished by its id. New and finished ones are announced; a finished one fades like the others.
    /// </summary>
    public void Quest(ObjectiveNote note)
    {
        Side? side = FindSide(note.Id);
        if (side == null)
        {
            if (note.Done)
            {
                return;
            }
            _sides.Add(new Side { Note = note });
            _announcements.Add(note);
            return;
        }
        bool finished = note.Done && !side.Note.Done;
        side.Note = note;
        if (finished)
        {
            _announcements.Add(note);
        }
    }

    /// <summary>The closest point of interest worth a detour, or null.</summary>
    public void SetNearby(WaysideNote? nearby) => _nearby = nearby;

    /// <summary>Lets finished side goals fade: after DoneLinger seconds they move to Finished.</summary>
    public void Tick(float seconds)
    {
        for (int i = _sides.Count - 1; i >= 0; i--)
        {
            Side side = _sides[i];
            if (!side.Note.Done)
            {
                continue;
            }
            side.DoneFor += seconds;
            if (side.DoneFor >= DoneLinger)
            {
                _finished.Add(side.Note);
                _sides.RemoveAt(i);
            }
        }
    }

    /// <summary>The main goal right now.</summary>
    public ObjectiveNote Main()
    {
        if (Shelter > 0)
        {
            if (CanSleep && SleptAt != Shelter)
            {
                return new ObjectiveNote(MainId, ObjectiveKind.Main, "Sleep by the fire",
                    "E on the bedroll. Sleep heals, and dawn starts the next stage.", "Station " + Shelter + " · sheltered");
            }
            return new ObjectiveNote(MainId, ObjectiveKind.Main, "Go down to Station " + (Shelter + 1),
                "Take a boon at the fire, look at the job board, talk to whoever sits here, repack (Tab). Then go before the light goes.", MainProgress);
        }
        int next = Highest + 1;
        if (Night)
        {
            return new ObjectiveNote(MainId, ObjectiveKind.Main, "Reach Station " + next + ": it's dark",
                "The cold drains your health outside a shelter. Keep going to the fire.", MainProgress);
        }
        return new ObjectiveNote(MainId, ObjectiveKind.Main, "Reach Station " + next + " before dark",
            "Night kills outside a shelter. The fire at the station is safe.", MainProgress);
    }

    /// <summary>Every goal now: the main goal, the guide step, side goals, something nearby, then the circle.</summary>
    public List<ObjectiveNote> Build()
    {
        List<ObjectiveNote> notes = new() { Main() };
        GuideStep? step = CurrentStep();
        if (step != null)
        {
            notes.Add(GuideNote(step.Value));
        }
        foreach (Side side in _sides)
        {
            if (side.Offered && (side.FromStation != Shelter || side.FromStation < Highest))
            {
                continue;
            }
            notes.Add(side.Note);
        }
        if (_nearby != null)
        {
            WaysideNote near = _nearby.Value;
            notes.Add(new ObjectiveNote(NearbyId, ObjectiveKind.Nearby, "Off the path: " + Lower(near.Title),
                near.Text, Mathf.RoundToInt(near.Distance) + " m " + near.Side));
        }
        notes.Add(_circle);
        return notes;
    }

    public static ObjectiveNote GuideNote(GuideStep step)
    {
        string id = "guide:" + step;
        return step switch
        {
            GuideStep.Walk => new ObjectiveNote(id, ObjectiveKind.Guide, "Follow the cairns downhill", "Stacked stones mark the safe path to the fire.", "First steps"),
            GuideStep.Gather => new ObjectiveNote(id, ObjectiveKind.Guide, "Pick up food or water (E)", "Your pack is empty, and hunger and thirst drain as you walk.", "First steps"),
            GuideStep.Read => new ObjectiveNote(id, ObjectiveKind.Guide, "Read a sign or note on the way (E)", "Those who came down before you left words beside the paths.", "First steps"),
            GuideStep.EatDrink => new ObjectiveNote(id, ObjectiveKind.Guide, "Eat (F) or drink (Q) when a dial runs low", "The dials top left: health, hunger, thirst, stamina.", "First steps"),
            GuideStep.Boon => new ObjectiveNote(id, ObjectiveKind.Guide, "Take a boon at the fire (E)", "Choose one of three offerings. It lasts the whole run.", "At the station"),
            _ => new ObjectiveNote(id, ObjectiveKind.Guide, "Talk to the traveller, or take the job at the board", "Jobs pay score and supplies. Travellers know the way down.", "At the station"),
        };
    }

    private string JobProgress(ContractNote job)
    {
        string line = RewardLine(job.Score);
        if (job.Breakable)
        {
            line = Mathf.RoundToInt(job.Condition) + "% intact";
            if (job.MinCondition > 0.0f)
            {
                line += " (needs " + Mathf.RoundToInt(job.MinCondition) + "%)";
            }
            line += " · " + RewardLine(job.Score);
        }
        if (job.DeadlineHours >= 0.0 && _hoursPerSecond > 0.0f)
        {
            float left = (float)System.Math.Max(job.DeadlineHours - _nowHours, 0.0);
            line += " · " + TimeText.FromHours(left, _hoursPerSecond) + " left";
        }
        return line;
    }

    private static string RewardLine(int score) => "Reward: +" + score + " and supplies";

    private static string ErrandId(string instanceId) => "job:" + instanceId;

    private Side? FindSide(string id)
    {
        foreach (Side side in _sides)
        {
            if (side.Note.Id == id)
            {
                return side;
            }
        }
        return null;
    }

    private static string StripThe(string name)
    {
        if (name.StartsWith("The ") || name.StartsWith("the "))
        {
            return name.Substring(4);
        }
        return name;
    }

    private static string Lower(string name)
    {
        if (name.Length > 1 && char.IsUpper(name[0]) && !char.IsUpper(name[1]))
        {
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }
        return name;
    }

    public static string Roman(int n)
    {
        string[] numerals = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
        if (n >= 0 && n < numerals.Length)
        {
            return numerals[n];
        }
        return n.ToString();
    }
}
