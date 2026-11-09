using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// Gives the carrier a goal at every moment (the purpose patch). Feeds an ObjectiveBook from game
/// events (stations, shelter, sleep, depth, pickups, eating, reading, boons, talking, jobs, relics,
/// travellers seated, points of interest nearby) and the clock and route estimate, and publishes the
/// goals through GameEvents.PublishObjectives whenever they change. New and finished goals are
/// announced (ObjectiveAnnounced) for the HUD's notes and banner and the journal.
///
/// Jobs become named errands: a job offered at a station where a traveller sits is "their" errand
/// (ErrandPosted lets the traveller say so), and a delivered job also pays supplies (RewardItemPaths,
/// packed through ItemsGranted). The first-run guide shows while the records hold fewer than
/// GuideRuns finished runs. Talks to no system directly: GameEvents, RunState and WorldClock only.
/// </summary>
public partial class ObjectiveDirector : Node
{
    /// <summary>The guide shows while fewer runs than this have ended (records; headless runs always see it).</summary>
    [Export] public int GuideRuns { get; set; } = 2;
    /// <summary>Metres of descent that finish the guide's first step.</summary>
    [Export] public float GuideWalkDepth { get; set; } = 25.0f;
    /// <summary>Seconds a finished side goal stays on the HUD, struck through.</summary>
    [Export] public float DoneLinger { get; set; } = 8.0f;
    /// <summary>Seconds between refreshes of the progress lines (estimate, time left).</summary>
    [Export] public float RefreshInterval { get; set; } = 0.25f;
    /// <summary>Supplies a delivered job pays on top of its score (item resource paths).</summary>
    [Export] public string[] RewardItemPaths { get; set; } = { "res://inventory/items/ration.tres", "res://inventory/items/water_flask.tres" };

    public ObjectiveBook Book { get; } = new();
    public IReadOnlyDictionary<int, string> Givers => _givers;

    private readonly Dictionary<int, string> _givers = new();
    private RunState? _run;
    private WorldClock? _clock;
    private float _timer;
    private string _signature = "";
    private string _mainTitle = "";

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        Book.GuideWalkDepth = GuideWalkDepth;
        Book.DoneLinger = DoneLinger;
        GameEvents.RespawnRequested += OnNewRun;
        GameEvents.StationReached += OnStation;
        GameEvents.ShelterChanged += OnShelter;
        GameEvents.Slept += OnSlept;
        GameEvents.DepthChanged += OnDepth;
        GameEvents.ItemPickedUp += OnPicked;
        GameEvents.ItemConsumed += OnConsumed;
        GameEvents.LoreRead += OnLore;
        GameEvents.BoonChosen += OnBoon;
        GameEvents.TalkRequested += OnTalk;
        GameEvents.VisitorSeated += OnSeated;
        GameEvents.ContractOffered += OnOffered;
        GameEvents.ContractAccepted += OnAccepted;
        GameEvents.ContractUpdated += OnUpdated;
        GameEvents.ContractResolved += OnResolved;
        GameEvents.RelicDiscovered += OnRelicFound;
        GameEvents.RelicDelivered += OnRelicLeft;
        GameEvents.WaysideNear += OnNearby;
        Begin();
    }

    public override void _ExitTree()
    {
        GameEvents.RespawnRequested -= OnNewRun;
        GameEvents.StationReached -= OnStation;
        GameEvents.ShelterChanged -= OnShelter;
        GameEvents.Slept -= OnSlept;
        GameEvents.DepthChanged -= OnDepth;
        GameEvents.ItemPickedUp -= OnPicked;
        GameEvents.ItemConsumed -= OnConsumed;
        GameEvents.LoreRead -= OnLore;
        GameEvents.BoonChosen -= OnBoon;
        GameEvents.TalkRequested -= OnTalk;
        GameEvents.VisitorSeated -= OnSeated;
        GameEvents.ContractOffered -= OnOffered;
        GameEvents.ContractAccepted -= OnAccepted;
        GameEvents.ContractUpdated -= OnUpdated;
        GameEvents.ContractResolved -= OnResolved;
        GameEvents.RelicDiscovered -= OnRelicFound;
        GameEvents.RelicDelivered -= OnRelicLeft;
        GameEvents.WaysideNear -= OnNearby;
        GameEvents.PublishObjectives(System.Array.Empty<ObjectiveNote>(), System.Array.Empty<ObjectiveNote>());
    }

    /// <summary>Whether this run shows the first-run guide.</summary>
    public bool GuideWanted()
    {
        int runs = 0;
        if (_run != null)
        {
            runs = _run.Records.Runs;
        }
        return runs < GuideRuns;
    }

    /// <summary>Starts the goals over for a new run (also called by tests after changing GuideRuns).</summary>
    public void Begin()
    {
        Book.Reset(GuideWanted());
        _mainTitle = "";
        _signature = "";
        Refresh();
    }

    public override void _Process(double delta)
    {
        Book.Tick((float)delta);
        _timer -= (float)delta;
        if (_timer > 0.0f)
        {
            return;
        }
        _timer = RefreshInterval;
        Refresh();
    }

    /// <summary>Reads the clock, the estimate and the depth into the book, and publishes if anything changed.</summary>
    public void Refresh()
    {
        if (_clock != null)
        {
            Book.CanSleep = _clock.CanSleepNow;
            Book.Night = _clock.IsNight;
            Book.SetClock(_clock.TotalHours, _clock.HoursPerSecond);
        }
        Book.MainProgress = EstimateLine(GameEvents.LatestEstimate);
        UpdateCircle();

        List<ObjectiveNote> notes = Book.Build();
        foreach (ObjectiveNote note in Book.TakeAnnouncements())
        {
            GameEvents.PublishObjectiveAnnounced(note);
        }
        ObjectiveNote main = notes[0];
        if (main.Title != _mainTitle)
        {
            _mainTitle = main.Title;
            GameEvents.PublishObjectiveAnnounced(main);
        }
        string signature = Signature(notes);
        if (signature == _signature)
        {
            return;
        }
        _signature = signature;
        GameEvents.PublishObjectives(notes, new List<ObjectiveNote>(Book.Finished));
    }

    private void UpdateCircle()
    {
        if (_run == null)
        {
            return;
        }
        CircleTable circles = _run.Circles;
        int depth = _run.Depth;
        int circle = circles.CircleAt(depth);
        int next = circle + 1;
        bool beyond = next > circles.Names.Length;
        float toGo = next * circles.DepthPerCircle - depth;
        Book.SetCircle(next + 1, circles.NameOf(next), toGo, beyond);
    }

    /// <summary>"0.6 km · ~4 min · before dark" toward the station the main goal names, or "".</summary>
    private string EstimateLine(RouteEstimate estimate)
    {
        if (!estimate.Valid || _clock == null)
        {
            return "";
        }
        int target = Book.Highest + 1;
        if (Book.Shelter > 0)
        {
            target = Book.Shelter + 1;
        }
        if (estimate.Station != target)
        {
            return "";
        }
        string dark = "before dark";
        if (!estimate.BeforeNightfall)
        {
            dark = "after dark: don't linger";
        }
        return (estimate.DistanceMeters / 1000.0f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km · ~" + TimeText.Real(estimate.TotalSeconds) + " · " + dark;
    }

    private static string Signature(List<ObjectiveNote> notes)
    {
        System.Text.StringBuilder text = new();
        foreach (ObjectiveNote note in notes)
        {
            text.Append(note.Id).Append('|').Append(note.Title).Append('|').Append(note.Why).Append('|').Append(note.Progress).Append('|').Append(note.Done).Append('\n');
        }
        return text.ToString();
    }

    private string GiverAt(int station)
    {
        if (_givers.TryGetValue(station, out string? name))
        {
            return name;
        }
        return "";
    }

    /// <summary>"a ration and a water flask": what a delivered job pays besides score, packed now.</summary>
    private string PayReward()
    {
        List<string> names = new();
        foreach (string path in RewardItemPaths)
        {
            if (ResourceLoader.Exists(path) && ResourceLoader.Load(path) is ItemDefinition item)
            {
                names.Add("a " + item.DisplayName.ToLowerInvariant());
            }
        }
        if (RewardItemPaths.Length > 0)
        {
            GameEvents.PublishItemsGranted(RewardItemPaths);
        }
        if (names.Count == 0)
        {
            return "";
        }
        if (names.Count == 1)
        {
            return names[0];
        }
        return string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[^1];
    }

    private void OnNewRun()
    {
        // Travellers are seated again for the new mountain (VisitorSeated) after this.
        _givers.Clear();
        Begin();
    }

    private void OnStation(int station)
    {
        Book.StationReached(station);
        Refresh();
    }

    private void OnShelter(bool inside, int station)
    {
        Book.ShelterChanged(inside, station);
        Refresh();
    }

    private void OnSlept(float hours)
    {
        Book.Slept();
        Refresh();
    }

    private void OnDepth(float depth) => Book.DepthChanged(depth);

    private void OnPicked(string itemId, string category)
    {
        Book.ItemPicked(category);
        Refresh();
    }

    private void OnConsumed(SupplyNeed need, string itemId, float restore)
    {
        Book.Consumed();
        Refresh();
    }

    private void OnLore(LoreNote note)
    {
        Book.LoreRead();
        Refresh();
    }

    private void OnBoon(string name)
    {
        Book.BoonTaken();
        Refresh();
    }

    private void OnTalk(SpeakerInfo speaker)
    {
        if (!speaker.IsCompanion)
        {
            Book.TalkedToTraveller();
            Refresh();
        }
    }

    private void OnSeated(int station, string name, string title)
    {
        _givers[station] = name;
    }

    private void OnOffered(ContractNote job)
    {
        string giver = GiverAt(job.FromStation);
        Book.JobOffered(job, giver);
        if (giver.Length > 0)
        {
            GameEvents.PublishErrandPosted(job.FromStation, giver, job.Title);
        }
        Refresh();
    }

    private void OnAccepted(ContractNote job)
    {
        Book.JobAccepted(job, GiverAt(job.FromStation));
        Refresh();
    }

    private void OnUpdated(ContractNote job)
    {
        Book.JobUpdated(job);
        Refresh();
    }

    private void OnResolved(ContractNote job)
    {
        string reward = "";
        if (job.Status == ContractStatus.Delivered)
        {
            reward = PayReward();
        }
        Book.JobResolved(job, reward);
        Refresh();
    }

    private void OnRelicFound(RelicNote relic)
    {
        Book.RelicFound(relic);
        Refresh();
    }

    private void OnRelicLeft(RelicNote relic)
    {
        Book.RelicLeft(relic);
        Refresh();
    }

    private void OnNearby(WaysideNote? nearby)
    {
        Book.SetNearby(nearby);
        Refresh();
    }
}
