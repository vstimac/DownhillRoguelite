using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Narrative;
using DownhillRoguelite.Player;
using DownhillRoguelite.Progression;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for the purpose patch's goals:
/// - The book: at the start the main goal is Station 1 before dark, with the guide's first step
///   and the next circle. Descending, picking up food, reading and eating tick the guide off in
///   order. A station switches the main goal to going on, then to sleeping once evening comes, and
///   sleeping points at the next station. Night says so. Jobs offered at a station become a
///   traveller's request, hidden away from that station; taken, they become "their errand" with
///   condition; delivered, they show what they paid and then move to the finished list. Relics
///   found become a goal until left. Past the ninth circle the long goal is "go deeper than anyone".
/// - The director in a real scene: it publishes at once (guide on in headless runs). Reaching
///   Station 1 changes the main goal. The traveller seated there asks for the station's job: the
///   goal is named after them and they say so on their tooltip. Taking it makes an errand, and
///   delivering it packs the reward supplies once. A new run starts the goals over, and
///   GuideRuns 0 hides the guide.
/// - The HUD: the goal note shows the main goal, the card queues it, the journal's "Now" page lists
///   it, notes say "New goal", and the note fits beside the dials and descent note from 1024 px to
///   2560 px wide without covering them.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/ObjectiveTests.tscn
/// </summary>
public partial class ObjectiveTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public ObjectiveDirector? Director { get; set; }
    [Export] public ContractDirector? Contracts { get; set; }
    [Export] public StationVisitors? Visitors { get; set; }
    [Export] public Hud? Ui { get; set; }

    private readonly List<string> _failures = new();
    private readonly List<ObjectiveNote> _announced = new();
    private RunState? _run;

    public override async void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        CarrierInventory? inventory = PlayerBody?.GetNodeOrNull<CarrierInventory>("Inventory");
        if (PlayerBody == null || Streamer?.Generator == null || Director == null || Contracts == null || Visitors == null || Ui == null || inventory == null || _run?.Route == null)
        {
            GD.PushError("ObjectiveTests: PlayerBody (with Inventory), Streamer, Director, Contracts, Visitors, Ui and RunState.Route are needed.");
            GetTree().Quit(1);
            return;
        }
        GameEvents.ObjectiveAnnounced += OnAnnounced;
        await WaitSeconds(0.5f);

        TestStartAndGuide();
        TestStationChain();
        TestErrands();
        TestRelicsAndCircles();
        await TestDirectorStart();
        await TestHud();
        await TestStationAndErrand(PlayerBody, inventory, _run.Route);
        await TestNewRun();

        GameEvents.ObjectiveAnnounced -= OnAnnounced;
        if (_failures.Count == 0)
        {
            GD.Print("OBJECTIVE TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print("OBJECTIVE TESTS: FAIL (" + _failures.Count + ")");
            GetTree().Quit(1);
        }
    }

    private void OnAnnounced(ObjectiveNote note) => _announced.Add(note);

    private static ObjectiveNote? Find(List<ObjectiveNote> notes, ObjectiveKind kind)
    {
        foreach (ObjectiveNote note in notes)
        {
            if (note.Kind == kind)
            {
                return note;
            }
        }
        return null;
    }

    private static string Titles(IEnumerable<ObjectiveNote> notes) => string.Join(" | ", notes.Select(n => n.Title));

    private static ContractNote Job(string id, ContractStatus status = ContractStatus.Accepted, float condition = 100.0f)
    {
        return new ContractNote(id, "A Parcel for the Next Fire", "Sealed parcel", 3.5f, 1, 2, status, 30, "", condition, 70.0f, -1.0, true);
    }

    private void TestStartAndGuide()
    {
        ObjectiveBook book = new();
        book.Reset(true);
        book.SetCircle(2, "Lust", 1800.0f, false);
        List<ObjectiveNote> start = book.Build();
        ObjectiveNote? circle = Find(start, ObjectiveKind.Circle);
        bool main = start[0].Kind == ObjectiveKind.Main && start[0].Title == "Reach Station 1 before dark" && start[0].Why.Contains("Night");
        bool walk = book.CurrentStep() == GuideStep.Walk && Find(start, ObjectiveKind.Guide)?.Title == "Follow the cairns downhill";
        bool circled = circle != null && circle.Value.Title == "Descend into Circle II · Lust" && circle.Value.Progress == "1800 m to go";
        Check("a run starts with Station 1 before dark, the first guide step and the next circle", main && walk && circled, Titles(start));

        List<GuideStep?> steps = new();
        book.ItemPicked("Food");
        steps.Add(book.CurrentStep());
        book.DepthChanged(30.0f);
        steps.Add(book.CurrentStep());
        book.LoreRead();
        steps.Add(book.CurrentStep());
        book.Consumed();
        steps.Add(book.CurrentStep());
        List<ObjectiveNote> done = book.TakeAnnouncements();
        bool order = steps[0] == GuideStep.Walk && steps[1] == GuideStep.Read && steps[2] == GuideStep.EatDrink && steps[3] == null;
        // Food picked before the walking step was done counts, but is only announced when it was the step shown.
        bool announced = done.Count == 3 && done.All(n => n.Done && n.Kind == ObjectiveKind.Guide);
        Check("descending, reading and eating tick the guide off in order (food picked early still counts)", order && announced && book.IsDone(GuideStep.Gather),
            "steps " + string.Join(", ", steps.Select(s => s?.ToString() ?? "none")) + ", announced: " + Titles(done));
    }

    private void TestStationChain()
    {
        ObjectiveBook book = new();
        book.Reset(true);
        book.ShelterChanged(true, 1);
        book.StationReached(1);
        string arrived = book.Main().Title;
        GuideStep? boonStep = book.CurrentStep();
        book.BoonTaken();
        GuideStep? visitStep = book.CurrentStep();
        book.CanSleep = true;
        string evening = book.Main().Title;
        book.Slept();
        book.CanSleep = false;
        string morning = book.Main().Title;
        book.ShelterChanged(false, 0);
        string away = book.Main().Title;
        book.Night = true;
        string night = book.Main().Title;
        bool passed = arrived == "Go down to Station 2" && boonStep == GuideStep.Boon && visitStep == GuideStep.Visit
            && evening == "Sleep by the fire" && morning == "Go down to Station 2" && away == "Reach Station 2 before dark" && night == "Reach Station 2: it's dark";
        Check("a station turns the goal to going on, then to sleeping at evening, then to the next station", passed,
            arrived + " → " + evening + " → " + morning + " → " + away + " → " + night + "; guide " + boonStep + ", " + visitStep);
    }

    private void TestErrands()
    {
        ObjectiveBook book = new();
        book.Reset(false);
        book.ShelterChanged(true, 1);
        book.StationReached(1);
        book.JobOffered(Job("J1"), "Maren");
        ObjectiveNote? offered = Find(book.Build(), ObjectiveKind.Errand);
        book.ShelterChanged(false, 0);
        bool hiddenAway = Find(book.Build(), ObjectiveKind.Errand) == null;
        book.ShelterChanged(true, 1);
        book.JobAccepted(Job("J1"), "Maren");
        book.JobUpdated(Job("J1", ContractStatus.Accepted, 82.0f));
        ObjectiveNote? carrying = Find(book.Build(), ObjectiveKind.Errand);
        book.JobResolved(Job("J1", ContractStatus.Delivered, 82.0f), "a ration and a water flask");
        ObjectiveNote? paid = Find(book.Build(), ObjectiveKind.Errand);
        book.Tick(book.DoneLinger + 0.1f);
        bool moved = Find(book.Build(), ObjectiveKind.Errand) == null && book.Finished.Count == 1;

        bool asked = offered != null && offered.Value.Title == "Maren asks: A Parcel for the Next Fire" && offered.Value.Why.Contains("Station 2") && offered.Value.Progress.Contains("+30");
        bool errand = carrying != null && carrying.Value.Title == "Maren's errand: A Parcel for the Next Fire" && carrying.Value.Progress.Contains("82%") && carrying.Value.Progress.Contains("needs 70%");
        bool done = paid != null && paid.Value.Done && paid.Value.Progress == "Done: +30, a ration and a water flask";
        Check("a job becomes the traveller's request, then their errand, then shows what it paid", asked && hiddenAway && errand && done && moved,
            $"offered '{offered?.Title}' ({offered?.Progress}), hidden away {hiddenAway}, carrying '{carrying?.Title}' ({carrying?.Progress}), paid '{paid?.Progress}', moved to finished {moved}");

        book.JobOffered(Job("J2"), "");
        ObjectiveNote? board = Find(book.Build(), ObjectiveKind.Errand);
        book.JobAccepted(Job("J2"), "");
        book.JobResolved(Job("J2", ContractStatus.Failed) with { Reason = "broke" }, "");
        ObjectiveNote? failed = Find(book.Build(), ObjectiveKind.Errand);
        book.StationReached(2);
        book.JobOffered(Job("J3") with { FromStation = 1 }, "Ilse");
        bool stale = book.Build().All(n => n.Id != "job:J3");
        Check("a job without a traveller is the board's; a broken job fails; old stations' jobs don't show",
            board?.Title == "A job at the board: A Parcel for the Next Fire" && failed != null && failed.Value.Failed && failed.Value.Progress.Contains("broke") && stale,
            $"'{board?.Title}', failed '{failed?.Progress}', stale offer hidden {stale}");
    }

    private void TestRelicsAndCircles()
    {
        ObjectiveBook book = new();
        book.Reset(false);
        RelicNote relic = new("RL_001", "The Unsent Letter", "A letter.", true);
        book.RelicFound(relic);
        ObjectiveNote? found = Find(book.Build(), ObjectiveKind.Relic);
        book.RelicLeft(relic with { Score = 35 });
        ObjectiveNote? left = Find(book.Build(), ObjectiveKind.Relic);
        book.SetCircle(10, "Beyond the circles", 0.0f, false);
        string ninth = Find(book.Build(), ObjectiveKind.Circle)?.Title ?? "";
        book.DepthChanged(17000.0f);
        book.SetCircle(11, "Beyond the circles", 0.0f, true);
        ObjectiveNote? beyond = Find(book.Build(), ObjectiveKind.Circle);
        book.SetNearby(new WaysideNote("0:0", "A cold spring", "Water.", Vector3.Zero, 35.0f, "to your left"));
        ObjectiveNote? near = Find(book.Build(), ObjectiveKind.Nearby);
        bool passed = found?.Title == "Leave the Unsent Letter at an altar" && left != null && left.Value.Done && left.Value.Progress == "Studied: +35"
            && ninth == "Go beyond the ninth circle" && beyond?.Title == "Go deeper than anyone" && beyond.Value.Progress == "17000 m down"
            && near?.Title == "Off the path: a cold spring" && near.Value.Progress == "35 m to your left";
        Check("relics are goals until left; the circles end in going deeper than anyone; a nearby spring is named", passed,
            $"'{found?.Title}' → '{left?.Progress}', '{ninth}', '{beyond?.Title}' ({beyond?.Progress}), '{near?.Title}' ({near?.Progress})");
    }

    private async Task TestDirectorStart()
    {
        Director!.GuideRuns = 2;
        Director.Begin();
        await WaitSeconds(0.3f);
        IReadOnlyList<ObjectiveNote> current = GameEvents.CurrentObjectives;
        bool main = current.Count > 0 && current[0].Title == "Reach Station 1 before dark";
        bool guide = current.Any(n => n.Kind == ObjectiveKind.Guide);
        bool circle = current.Count > 0 && current[^1].Kind == ObjectiveKind.Circle && current[^1].Title.Contains("Lust");
        Check("the director publishes the goals at once, with the guide on a first run", main && guide && circle, Titles(current));
    }

    private async Task TestHud()
    {
        await WaitSeconds(0.3f);
        ObjectivePanel? panel = Ui!.Objectives;
        JournalScreen? journal = Ui.Journal;
        ObjectiveBanner? banner = Ui.Banner;
        if (panel == null || journal == null || banner == null || Ui.Notes == null)
        {
            Check("the HUD has a goal note, a card, notes and the journal", false, "missing");
            return;
        }
        bool note = panel.Visible && panel.Text.Contains("Reach Station 1 before dark") && panel.Text.Contains("Next: Follow the cairns downhill") && panel.Text.Contains("Lust");
        bool card = banner.Showing == "Reach Station 1 before dark" || banner.Queued > 0;
        bool newGoal = Ui.Notes.Recent.Any(t => t.StartsWith("New goal: Reach Station 1"));
        Ui.Toggle(journal);
        await WaitSeconds(0.4f);
        bool now = journal.NowText.Contains("Now") && journal.NowText.Contains("Reach Station 1 before dark") && journal.NowText.Contains("Night kills");
        journal.Close();
        await WaitSeconds(0.4f);
        Check("the goal note, the card, a note and the journal's Now page all say Station 1 before dark", note && card && newGoal && now,
            $"note {note} ('{panel.Text.Replace("\n", " / ")}'), card '{banner.Showing}' queued {banner.Queued}, note {newGoal}, journal {now}");

        List<string> problems = new();
        float tallest = panel.MaxHeight;
        ObjectiveNote cardGoal = new("main", ObjectiveKind.Main, "Reach Station 1 before dark", "Night kills outside a shelter. The fire at the station is safe.", "");
        foreach (Vector2 size in new[] { new Vector2(1024, 768), new Vector2(1047, 589), new Vector2(1152, 648), new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(2560, 1440) })
        {
            Ui.LayoutFor(size);
            Rect2 goal = new(panel.Position, new Vector2(ObjectivePanel.PanelWidth, tallest));
            Rect2 shown = banner.Fit(cardGoal, size);
            if (!new Rect2(Vector2.Zero, size).Encloses(shown))
            {
                problems.Add("card outside at " + size);
            }
            Control?[] underCard = { Ui.Hearts, Ui.HungerIcon, Ui.ThirstIcon, Ui.WarmthIcon, Ui.Compass, Ui.Equipment, Ui.Hotbar, Ui.Descent };
            foreach (Control? other in underCard)
            {
                if (other != null && shown.Intersects(new Rect2(other.Position, other.Size)))
                {
                    problems.Add("card over " + other.Name + " at " + size);
                }
            }
            if (shown.Intersects(goal))
            {
                problems.Add("card over the goal note at " + size);
            }
            if (Ui.ObjectivesStacked)
            {
                // Narrow windows: the notes follow the goal note's real height (checked below), not its tallest.
                goal = new Rect2(panel.Position, panel.Size);
            }
            if (!new Rect2(Vector2.Zero, size).Encloses(goal))
            {
                problems.Add("outside at " + size);
            }
            Control?[] others = { Ui.Hearts, Ui.HungerIcon, Ui.ThirstIcon, Ui.WarmthIcon, Ui.Descent, Ui.Compass };
            foreach (Control? other in others)
            {
                if (other != null && goal.Intersects(new Rect2(other.Position, other.Size)))
                {
                    problems.Add(other.Name + " at " + size);
                }
            }
            if (Ui.Notes != null && goal.Intersects(new Rect2(Ui.Notes.Position, Ui.Notes.Size)))
            {
                problems.Add("notes at " + size);
            }
        }
        Ui.Layout();
        Check("the goal note fits from 1024 to 2560 px wide without covering the hearts, need icons, descent note, compass or notes", problems.Count == 0,
            "[" + string.Join(", ", problems) + "]");
    }

    private async Task TestStationAndErrand(PlayerController player, CarrierInventory inventory, IRouteQuery route)
    {
        inventory.Clear();
        _announced.Clear();
        await Place(player, route.Station(1) + new Vector3(0.0f, 0.0f, 1.5f), 1.0f);
        IReadOnlyList<ObjectiveNote> atStation = GameEvents.CurrentObjectives;
        string main = "";
        if (atStation.Count > 0)
        {
            main = atStation[0].Title;
        }
        bool turned = main == "Go down to Station 2" || main == "Sleep by the fire";
        NpcFigure? traveller = Visitors!.Figures.FirstOrDefault(f => f.Station == 1);
        ObjectiveNote? request = atStation.FirstOrDefault(n => n.Kind == ObjectiveKind.Errand);
        string giver = traveller?.Title ?? "";
        bool named = traveller != null && request != null && request.Value.Title.StartsWith(giver + " asks: ");
        bool tooltip = traveller != null && traveller.Errand.Length > 0 && traveller.Details.Contains("Has a job for you");
        Check("at Station 1 the goal moves on, and the traveller there asks for the station's job", turned && named && tooltip,
            $"main '{main}', traveller '{giver}', goal '{request?.Title}', tooltip '{traveller?.Details}'");

        Contracts!.Offers.TryGetValue(1, out ContractInstance? offer);
        if (offer == null)
        {
            Check("taking the job makes an errand and delivering it pays supplies", false, "no offer at Station 1");
            return;
        }
        Contracts.Accept(offer);
        await WaitSeconds(0.3f);
        ObjectiveNote? errand = GameEvents.CurrentObjectives.FirstOrDefault(n => n.Kind == ObjectiveKind.Errand);
        bool carrying = errand != null && errand.Value.Title.StartsWith(giver + "'s errand: ");
        int rations = GameEvents.CarriedItems.Count(id => id == "ration");
        int flasks = GameEvents.CarriedItems.Count(id => id == "water_flask");
        ContractNote delivered = (Contracts.Active?.Note() ?? Job("missing")) with { Status = ContractStatus.Delivered, Score = 30 };
        GameEvents.PublishContractResolved(delivered);
        await WaitSeconds(0.3f);
        int rationsAfter = GameEvents.CarriedItems.Count(id => id == "ration");
        int flasksAfter = GameEvents.CarriedItems.Count(id => id == "water_flask");
        ObjectiveNote? paid = GameEvents.CurrentObjectives.FirstOrDefault(n => n.Kind == ObjectiveKind.Errand);
        bool rewarded = rationsAfter == rations + 1 && flasksAfter == flasks + 1 && paid != null && paid.Value.Done && paid.Value.Progress.Contains("ration");
        Check("taking the job makes the traveller's errand, and delivering it packs a ration and a flask once", carrying && rewarded,
            $"errand '{errand?.Title}', rations {rations}→{rationsAfter}, flasks {flasks}→{flasksAfter}, '{paid?.Progress}'");
    }

    private async Task TestNewRun()
    {
        GameEvents.PublishRespawnRequest();
        await WaitSeconds(0.5f);
        IReadOnlyList<ObjectiveNote> fresh = GameEvents.CurrentObjectives;
        bool reset = fresh.Count > 0 && fresh[0].Title == "Reach Station 1 before dark" && fresh.All(n => n.Kind != ObjectiveKind.Errand);
        Director!.GuideRuns = 0;
        Director.Begin();
        await WaitSeconds(0.2f);
        bool noGuide = GameEvents.CurrentObjectives.All(n => n.Kind != ObjectiveKind.Guide) && !Director.Book.GuideOn;
        Director.GuideRuns = 2;
        Check("a new run starts the goals over, and GuideRuns 0 hides the guide", reset && noGuide, Titles(fresh) + "; without guide: " + Titles(GameEvents.CurrentObjectives));
    }

    private async Task Place(PlayerController player, Vector3 position, float settle)
    {
        player.Teleport(position + Vector3.Up * 0.1f, 0.0f);
        player.Health?.Refill();
        await WaitSeconds(settle);
    }

    private async Task WaitSeconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print("PASS  " + name + ": " + detail);
        }
        else
        {
            GD.Print("FAIL  " + name + ": " + detail);
            _failures.Add(name);
        }
    }
}
