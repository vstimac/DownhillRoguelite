using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Narrative;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for the restsite president (docs/restsite-president.md): the manifest, voice pack
/// and texts load and agree (every recorded line is a manifest line, every id the dialogue uses
/// exists); a recorded line plays its preferred take and falls back to the other one, an unrecorded
/// line shows subtitles without failing; the first meeting runs intro_001, intro_002, the choices,
/// and isn't repeated; quest 1 places three records, picking them up makes it ready, the hand-in
/// runs the return lines and the ending choice, takes the records and pays once; the temporary
/// mandate brings the carrier back from a fatal fall once; the podium gives one speech per run;
/// the secret question opens only after five returns with every quest done; the save round-trips.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/PresidentTests.tscn
/// </summary>
public partial class PresidentTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public PresidentDirector? Director { get; set; }

    /// <summary>Every manifest id the dialogue can say (PresidentDialogue).</summary>
    private static readonly string[] UsedLines =
    {
        "intro_001", "intro_002", "intro_004", "intro_a2", "intro_a3", "intro_b2", "intro_c2", "intro_d2",
        "q1_offer_01", "q1_offer_03", "q1_accept", "q1_decline", "q1_return_01", "q1_return_02", "q1_return_04",
        "q1_end_truth", "q1_end_agree", "q1_end_silent", "rest_001", "shop_001", "shop_002", "leave_01",
        "return_after_death_01", "return_after_death_02", "return_after_death_03",
        "q2_offer_01", "q2_offer_03", "q2_offer_05", "q2_accept", "q2_complete", "q2_fail",
        "q3_offer_01", "q3_offer_03", "q3_accept", "q3_complete", "q3_broken",
        "heavy_inventory_01", "low_health_01", "no_money_01", "podium_buff_01", "podium_skip_01",
        "secret_02", "secret_04", "secret_06", "secret_07", "secret_aftermath_01", "farewell_final_01",
    };

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        if (PlayerBody == null || Streamer == null || Director == null)
        {
            GD.PushError("PresidentTests: PlayerBody, Streamer and Director are needed.");
            GetTree().Quit(1);
            return;
        }
        Director.UseSave(new PresidentSave(false));
        await WaitSeconds(1.0f);
        TestData(Director);
        TestVoice(Director);
        PresidentNpc? npc = await Arrive(PlayerBody, Director);
        if (npc != null)
        {
            await TestFirstMeeting(npc, Director);
            await TestResults(PlayerBody, npc, Director);
            TestMandate(PlayerBody, Director);
            await TestPodium(npc, Director);
            TestSecret(Director);
        }
        TestSaveFile();

        if (_failures.Count == 0)
        {
            GD.Print("PRESIDENT TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"PRESIDENT TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void TestData(PresidentDirector director)
    {
        int unmatched = 0;
        foreach (VoicePackLine recorded in director.Pack.Lines)
        {
            bool found = false;
            foreach (VoiceLine line in director.Manifest.Lines)
            {
                if (line.Text.Trim() == recorded.Text.Trim())
                {
                    found = true;
                }
            }
            if (!found)
            {
                unmatched++;
                GD.Print($"      recorded line without a manifest line: {recorded.Id}");
            }
        }
        List<string> missing = new();
        foreach (string id in UsedLines)
        {
            if (!director.Manifest.Has(id))
            {
                missing.Add(id);
            }
        }
        Check("manifest, voice pack and texts load and agree",
            director.Manifest.Count == 57 && director.Pack.Count == 24 && unmatched == 0 && missing.Count == 0 && director.Text.Has("c_descending"),
            $"{director.Manifest.Count} lines, {director.Pack.Count} recorded ({unmatched} unmatched), missing ids: {string.Join(", ", missing)}");
    }

    private void TestVoice(PresidentDirector director)
    {
        NpcVoice voice = new() { Manifest = director.Manifest, Pack = director.Pack, NpcId = "test", LogLines = false };
        AddChild(voice);
        VoiceLine intro = director.Manifest.Find("intro_001")!.Value;
        voice.Variant = VoicePackLine.Forest;
        AudioStream? forest = voice.Resolve(intro, out string forestSource);
        voice.Variant = VoicePackLine.Outdoor;
        AudioStream? outdoor = voice.Resolve(intro, out string outdoorSource);
        // A variant that doesn't exist falls back to a take that does.
        voice.Variant = "cave";
        AudioStream? fallback = voice.Resolve(intro, out string fallbackSource);
        AudioStream? unrecorded = voice.Resolve(director.Manifest.Find("idle_01")!.Value, out string _);
        int completed = 0;
        void OnCompleted(string npc, string line, bool skipped)
        {
            if (npc == "test")
            {
                completed++;
            }
        }
        GameEvents.VoiceLineCompleted += OnCompleted;
        bool said = voice.Say("idle_01");
        voice.Skip();
        bool unknown = voice.Say("no_such_line");
        GameEvents.VoiceLineCompleted -= OnCompleted;
        voice.QueueFree();
        Check("a recorded line plays its take (forest/outdoor, falling back), an unrecorded one shows subtitles",
            forest != null && outdoor != null && fallback != null && forestSource.Contains("forest") && outdoorSource.Contains("outdoor")
            && unrecorded == null && said && completed == 1 && !unknown,
            $"forest {forestSource}, outdoor {outdoorSource}, fallback {fallbackSource}, idle_01 voiced {unrecorded != null}, subtitles-only line ran {said} and ended {completed}, unknown id refused {!unknown}");
    }

    private async Task<PresidentNpc?> Arrive(PlayerController player, PresidentDirector director)
    {
        for (int i = 0; i < 600 && !director.Npcs.ContainsKey(director.FirstStation); i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        if (!director.Sets.TryGetValue(director.FirstStation, out RestsiteSet? set))
        {
            Check("the restsite is set up at the first station", false, "no restsite");
            return null;
        }
        await Place(player, set.GlobalPosition + set.PresidentOffset + new Vector3(0.0f, 0.0f, 2.0f), 1.5f);
        PresidentNpc npc = director.Npcs[director.FirstStation];
        Check("the restsite is set up at the first station, the president on the ground beside his podium",
            npc.Grounded && set.Podium != null && director.Visiting == director.FirstStation,
            $"grounded {npc.Grounded}, visiting {director.Visiting}");
        return npc;
    }

    private async Task TestFirstMeeting(PresidentNpc npc, PresidentDirector director)
    {
        List<string> said = new();
        void OnStarted(string id, string line, bool voiced)
        {
            if (id == PresidentNpc.NpcId)
            {
                said.Add(line);
            }
        }
        GameEvents.VoiceLineStarted += OnStarted;
        npc.Interact(PlayerBody!);
        await Talk(npc);
        int choices = npc.Options.Count;
        bool met = director.Save.Met;
        npc.Choose(0);
        await Talk(npc);
        int after = npc.Options.Count;
        npc.Choose(npc.Options.Count - 1);
        await Talk(npc);
        bool closed = !npc.Talking;
        string first = string.Join(" ", said);
        said.Clear();
        npc.Interact(PlayerBody!);
        await Talk(npc);
        string second = string.Join(" ", said);
        npc.Close();
        GameEvents.VoiceLineStarted -= OnStarted;
        Check("the first meeting: intro_001, intro_002, five choices; 'Ali ja se spuštam' answers intro_004; 'Idem dalje' closes",
            first == "intro_001 intro_002 intro_004 intro_d2" && choices == 5 && after == 4 && met && closed,
            $"said '{first}', {choices} then {after} choices, met {met}, closed {closed}");
        Check("the first meeting isn't repeated", second.Length > 0 && !second.Contains("intro_001"), $"second talk said '{second}'");
    }

    private async Task TestResults(PlayerController player, PresidentNpc npc, PresidentDirector director)
    {
        DialogueBeat offer = director.Dialogue!.ResultsOffer();
        npc.Run(offer);
        await Talk(npc);
        npc.Choose(0);
        await Talk(npc);
        int placed = director.Records.Count;
        QuestStatus accepted = director.Save.Quest(PresidentSave.QuestResults);
        foreach (WorldItem record in new List<WorldItem>(director.Records))
        {
            record.Interact(player);
        }
        await WaitSeconds(0.2f);
        QuestStatus gathered = director.Save.Quest(PresidentSave.QuestResults);
        int favorBefore = director.Save.Favor;
        List<string> lines = new();
        void OnStarted(string id, string line, bool voiced)
        {
            lines.Add(line);
        }
        GameEvents.VoiceLineStarted += OnStarted;
        npc.Interact(player);
        await Talk(npc);
        int carriedAfterHandIn = director.RecordsCarried;
        npc.Choose(1);
        await Talk(npc);
        GameEvents.VoiceLineStarted -= OnStarted;
        int favor = director.Save.Favor;
        director.FinishResults("agree");
        bool once = director.Save.Favor == favor;
        bool mandate = false;
        foreach (string id in GameEvents.CarriedItems)
        {
            mandate = mandate || id == "item_temporary_mandate";
        }
        Check("quest 1: three records placed, carrying them makes it ready",
            placed == 3 && accepted == QuestStatus.Active && gathered == QuestStatus.Ready,
            $"{placed} records, accepted {accepted}, after pickup {gathered}");
        Check("the hand-in: q1_return_01/02/04, the records taken, the ending chosen, paid once, the mandate given",
            string.Join(" ", lines) == "q1_return_01 q1_return_02 q1_return_04 q1_end_agree" && carriedAfterHandIn == 0
            && director.Save.Quest(PresidentSave.QuestResults) == QuestStatus.Completed && favor == favorBefore + director.ResultsFavor && once
            && director.Save.MandateUnlocked && mandate,
            $"said '{string.Join(" ", lines)}', records left {carriedAfterHandIn}, favour {favorBefore} -> {favor} (again: {director.Save.Favor}), mandate carried {mandate}");
    }

    private void TestMandate(PlayerController player, PresidentDirector director)
    {
        CarrierHealth? health = player.Health;
        if (health == null)
        {
            Check("the mandate brings the carrier back once", false, "no health");
            return;
        }
        health.Refill();
        health.TakeDamage(health.EffectiveMax * 3.0f, "rock");
        bool alive = !health.Dead;
        float share = health.Current / health.EffectiveMax;
        bool kept = false;
        foreach (string id in GameEvents.CarriedItems)
        {
            kept = kept || id == "item_temporary_mandate";
        }
        health.Refill();
        Check("the temporary mandate brings the carrier back from a fatal fall at 25 %, and is used up",
            alive && Mathf.IsEqualApprox(share, director.MandateShare) && !kept,
            $"alive {alive}, health {share * 100.0f:0} %, mandate still carried {kept}");
    }

    private async Task TestPodium(PresidentNpc npc, PresidentDirector director)
    {
        RunState? run = GetNodeOrNull<RunState>("/root/RunState");
        float before = run?.Modifiers.StaminaRecovery ?? 1.0f;
        npc.Run(director.Dialogue!.Podium());
        await Talk(npc);
        npc.Choose(0);
        await Talk(npc);
        float during = run?.Modifiers.StaminaRecovery ?? 1.0f;
        float detect = GameEvents.Detectability;
        await WaitSeconds(0.1f);
        detect = GameEvents.Detectability;
        bool again = director.Dialogue.Podium() == null;
        Check("the podium: one speech per run, more stamina and more noticed",
            again && during > before && detect > 1.0f,
            $"stamina recovery {before:0.00} -> {during:0.00}, detectability {detect:0.00}, second speech refused {again}");
    }

    private void TestSecret(PresidentDirector director)
    {
        PresidentDialogue dialogue = director.Dialogue!;
        bool closedBefore = !dialogue.SecretOpen;
        director.Save.Returns = 5;
        director.Save.SetQuest(PresidentSave.QuestContractors, QuestStatus.Completed);
        director.Save.SetQuest(PresidentSave.QuestVictory, QuestStatus.Completed);
        bool open = dialogue.SecretOpen;
        DialogueBeat hub = dialogue.Hub();
        bool offered = hub.Options.Exists(option => option.Id == "c_secret");
        director.FinishSecret();
        bool closedAfter = !dialogue.SecretOpen && director.Save.BadgeEarned;
        Check("the secret question opens after five returns with every quest done, once",
            closedBefore && open && offered && closedAfter,
            $"closed before {closedBefore}, open {open}, offered {offered}, closed after with badge {closedAfter}");
    }

    private void TestSaveFile()
    {
        const string path = "user://president_test.cfg";
        PresidentSave save = new(true, path);
        save.Reset();
        save.Met = true;
        save.Favor = 4;
        save.Returns = 6;
        save.SecretSeen = true;
        save.BridgeUnlocked = true;
        save.Victories = 12;
        save.SetQuest(PresidentSave.QuestResults, QuestStatus.Completed);
        save.SetQuest(PresidentSave.QuestContractors, QuestStatus.Active);
        save.Save();
        PresidentSave loaded = new(true, path);
        bool same = loaded.Met && loaded.Favor == 4 && loaded.Returns == 6 && loaded.SecretSeen && loaded.BridgeUnlocked
            && loaded.Victories == 12 && loaded.Quest(PresidentSave.QuestResults) == QuestStatus.Completed
            && loaded.Quest(PresidentSave.QuestContractors) == QuestStatus.Active
            && loaded.Quest(PresidentSave.QuestVictory) == QuestStatus.NotStarted;
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        Check("the save round-trips (quests, favour, returns, secret, bridge, board)", same, $"loaded met {loaded.Met}, favour {loaded.Favor}, returns {loaded.Returns}");
    }

    /// <summary>Skips through the lines being spoken until choices show or the talk ends.</summary>
    private async Task Talk(PresidentNpc npc)
    {
        for (int i = 0; i < 40 && npc.Voice != null && npc.Voice.Speaking; i++)
        {
            npc.Voice.Skip();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Place(PlayerController player, Vector3 position, float settle)
    {
        player.Teleport(position + Vector3.Up * 0.2f, 0.0f);
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
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
