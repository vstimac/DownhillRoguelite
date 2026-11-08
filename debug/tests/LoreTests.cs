using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Map;
using DownhillRoguelite.Narrative;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for found texts and path markers: every text resource is valid and unique; the
/// choice of text is the same for the same mountain, never repeats in a run and never puts both
/// accounts of an exclusive group in one run; circle texts only appear in their circle; the lore
/// book tells first readings from re-readings, across runs; the first stage always has a text and
/// later ones often do, many on side paths, all on the ground beside their path; reading one in the
/// world tells everyone once; every path of a stage with a choice gets a marker with the right kind
/// and a length that matches its kind.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/LoreTests.tscn
/// </summary>
public partial class LoreTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public StationManager? Stations { get; set; }
    [Export] public LorePlacer? Lore { get; set; }
    [Export] public int StagesChecked { get; set; } = 24;

    private readonly List<string> _failures = new();
    private readonly List<LoreNote> _read = new();
    private RunState? _run;

    public override async void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        if (PlayerBody == null || Streamer?.Generator == null || Stations == null || Lore == null || _run?.Route == null)
        {
            GD.PushError("LoreTests: PlayerBody, Streamer, Stations, Lore and RunState.Route are needed.");
            GetTree().Quit(1);
            return;
        }
        GameEvents.LoreRead += OnLoreRead;
        IRouteQuery route = _run.Route;

        TestLibrary(Lore.Library);
        TestChoice(Lore.Library);
        TestBook();
        TestPlan(route);
        await TestReading(PlayerBody, route);
        await TestMarkers(PlayerBody, route);

        GameEvents.LoreRead -= OnLoreRead;
        if (_failures.Count == 0)
        {
            GD.Print("LORE TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print("LORE TESTS: FAIL (" + _failures.Count + ")");
            GetTree().Quit(1);
        }
    }

    private void OnLoreRead(LoreNote note)
    {
        _read.Add(note);
    }

    private void TestLibrary(LoreLibrary library)
    {
        HashSet<string> ids = new();
        bool unique = true;
        int firstStage = 0;
        foreach (LoreEntry entry in library.Entries)
        {
            if (!ids.Add(entry.Id))
            {
                unique = false;
            }
            if (entry.EarliestStage == 0)
            {
                firstStage++;
            }
        }
        int files = 0;
        foreach (string file in DirAccess.GetFilesAt(LoreLibrary.Folder))
        {
            if (file.Replace(".remap", "").EndsWith(".tres"))
            {
                files++;
            }
        }
        Check("every text resource loads and is valid", library.Entries.Count == files && files >= 20 && unique,
            library.Entries.Count + " valid of " + files + " files, ids unique " + unique);
        LoreEntry? first = library.Find("FRAG_001");
        Check("the first stage's text is the broken sign", first != null && first.EarliestStage == 0 && firstStage == 1,
            firstStage + " texts allowed in stage 0");
    }

    private void TestChoice(LoreLibrary library)
    {
        bool same = true;
        bool repeats = false;
        bool bothAccounts = false;
        bool wrongCircle = false;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            HashSet<string> used = new();
            HashSet<string> again = new();
            HashSet<string> seen = new();
            for (int stage = 0; stage < 16; stage++)
            {
                int circle = stage / 2;
                LoreEntry? entry = library.Choose(seed, stage, circle, used, null, 0.35f);
                LoreEntry? twin = library.Choose(seed, stage, circle, again, null, 0.35f);
                if (entry?.Id != twin?.Id)
                {
                    same = false;
                }
                if (entry == null)
                {
                    continue;
                }
                if (!seen.Add(entry.Id))
                {
                    repeats = true;
                }
                if (entry.Circle >= 0 && entry.Circle != circle)
                {
                    wrongCircle = true;
                }
                LoreLibrary.MarkUsed(entry, used);
                LoreLibrary.MarkUsed(entry, again);
            }
            if (seen.Contains("FRAG_006") && seen.Contains("FRAG_007"))
            {
                bothAccounts = true;
            }
        }
        Check("the same mountain chooses the same texts", same, "40 seeds, 16 stages each");
        Check("no text twice in a run, one account of the First Lie", !repeats && !bothAccounts, "repeats " + repeats + ", both accounts " + bothAccounts);
        Check("circle texts appear only in their circle", !wrongCircle, "checked 640 choices");
    }

    private void TestBook()
    {
        LoreBook book = new(false);
        book.Read("A", out bool firstRun, out bool firstEver);
        book.Read("A", out bool againRun, out bool againEver);
        book.BeginRun();
        book.Read("A", out bool nextRun, out bool nextEver);
        Check("the lore book tells first readings from re-readings", firstRun && firstEver && !againRun && !againEver && nextRun && !nextEver
            && book.EverCount == 1 && !book.Persistent,
            "first " + firstRun + "/" + firstEver + ", again " + againRun + "/" + againEver + ", next run " + nextRun + "/" + nextEver);
    }

    private void TestPlan(IRouteQuery route)
    {
        Lore!.Forget();
        int texts = 0;
        int onBranches = 0;
        int branchStages = 0;
        float worstGround = 0.0f;
        float worstSide = 0.0f;
        List<string> order = new();
        for (int stage = 0; stage < StagesChecked; stage++)
        {
            if (route.PathCount(stage) > 1)
            {
                branchStages++;
            }
            LorePlacer.Placement? placement = Lore.PlanFor(route, stage);
            if (placement == null)
            {
                order.Add("-");
                continue;
            }
            LorePlacer.Placement p = placement.Value;
            order.Add(p.Entry.Id);
            texts++;
            if (p.Path > 0)
            {
                onBranches++;
            }
            worstGround = Mathf.Max(worstGround, Mathf.Abs(route.GroundHeight(p.Position.X, p.Position.Z) - p.Position.Y));
            worstSide = Mathf.Max(worstSide, Mathf.Abs(route.PathPoint(stage, p.Path, p.Position.Z).X - p.Position.X));
        }
        LorePlacer.Placement? firstStage = Lore.PlanFor(route, 0);
        Check("the first stage has the broken sign", firstStage != null && firstStage.Value.Entry.Id == "FRAG_001", order[0]);
        Check("texts stand along much of the descent, many on side paths",
            texts >= StagesChecked / 3 && (branchStages == 0 || onBranches >= 2),
            texts + " texts in " + StagesChecked + " stages, " + onBranches + " on side paths (" + branchStages + " stages have them)");
        Check("each text stands on the ground beside its path", worstGround < 0.2f && worstSide < 5.0f,
            "worst height off the ground " + worstGround.ToString("0.00") + " m, worst distance from the path " + worstSide.ToString("0.0") + " m");
        Lore.Forget();
        List<string> second = new();
        for (int stage = 0; stage < StagesChecked; stage++)
        {
            LorePlacer.Placement? placement = Lore.PlanFor(route, stage);
            if (placement == null)
            {
                second.Add("-");
            }
            else
            {
                second.Add(placement.Value.Entry.Id);
            }
        }
        Check("planning again gives the same texts in the same places", string.Join(",", order) == string.Join(",", second), string.Join(",", order));
    }

    private async Task TestReading(PlayerController player, IRouteQuery route)
    {
        LorePlacer.Placement? placement = Lore!.PlanFor(route, 0);
        if (placement == null)
        {
            Check("a text can be read", false, "no text in stage 0");
            return;
        }
        await Place(player, route.PathPoint(0, placement.Value.Path, placement.Value.Position.Z), 1.0f);
        Lore.Marks.TryGetValue(0, out LoreMark? mark);
        if (mark == null)
        {
            Check("a text can be read", false, "no mark loaded near the carrier");
            return;
        }
        string promptBefore = mark.Prompt;
        _read.Clear();
        bool first = mark.Interact(player);
        bool second = mark.Interact(player);
        bool told = _read.Count == 1 && _read[0].Id == placement.Value.Entry.Id && _read[0].FirstThisRun;
        Check("reading a text tells everyone once", first && !second && told && mark.HasBeenRead && mark.Prompt.Length == 0 && promptBefore.Length > 0
            && _run!.Lore.ReadThisRun(placement.Value.Entry.Id),
            "first " + first + ", second " + second + ", told " + _read.Count + " time(s), prompt before '" + promptBefore + "'");
    }

    private async Task TestMarkers(PlayerController player, IRouteQuery route)
    {
        int stage = -1;
        for (int s = 0; s < StagesChecked; s++)
        {
            if (route.PathCount(s) >= 3)
            {
                stage = s;
                break;
            }
        }
        if (stage < 0)
        {
            Check("every path of a stage gets a marker", false, "no stage with 3+ paths");
            return;
        }
        Vector2 zone = route.BranchZone(stage);
        await Place(player, route.RoutePoint(zone.X - Stations!.MarkerDistance), 1.0f);
        int paths = route.PathCount(stage);
        int found = 0;
        bool kinds = true;
        bool lengths = true;
        float safe = Stations.PathLength(route, stage, 0, zone.X, zone.Y);
        string text = "";
        for (int path = 0; path < paths; path++)
        {
            if (!Stations.Markers.TryGetValue(stage * 8 + path, out PathMarker? marker))
            {
                continue;
            }
            found++;
            RouteKind kind = route.PathKind(stage, path);
            if (marker.Kind != kind || !marker.Text.StartsWith(PathMarker.Heading(kind)))
            {
                kinds = false;
            }
            float length = Stations.PathLength(route, stage, path, zone.X, zone.Y);
            if (kind == RouteKind.Hazardous && length >= safe)
            {
                lengths = false;
            }
            if (kind == RouteKind.SupplyRich && length <= safe)
            {
                lengths = false;
            }
            text += "\n      " + marker.Text.Replace("\n", " | ");
        }
        Check("every path of a stage gets a marker", found == paths && kinds && lengths,
            found + " markers for " + paths + " paths, kinds right " + kinds + ", lengths in order " + lengths + text);
    }

    private async Task Place(PlayerController player, Vector3 position, float settle)
    {
        player.Teleport(position + Vector3.Up * 0.1f, 0.0f);
        player.Health?.Refill();
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < settle; elapsed += dt)
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
