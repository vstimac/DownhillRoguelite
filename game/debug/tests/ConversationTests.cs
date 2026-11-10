using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Narrative;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for M11, the companion's conversation: the situation note describes the
/// carrier in words (place, time, weather, body, gear, load); without a model the companion is
/// silent and says so instead of hanging; the conversation page opens, takes a question, shows the
/// exchange, holds the narrator and the movement keys, and closes. With `-- --llm-live` (and the
/// model on disk) it also asks the real model and waits for a streamed reply.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/ConversationTests.tscn [-- --llm-live]
/// </summary>
public partial class ConversationTests : Node
{
    [Export] public CompanionChat? Companion { get; set; }
    [Export] public Hud? Ui { get; set; }
    [Export] public StationVisitors? Visitors { get; set; }
    /// <summary>How long the live test waits for the model to load and answer, in seconds.</summary>
    [Export] public float LiveTimeout { get; set; } = 120.0f;

    private readonly List<string> _failures = new();
    private readonly List<string> _partials = new();
    private string _reply = "";
    private bool _done;

    public override async void _Ready()
    {
        if (Companion == null || Ui == null)
        {
            GD.PushError("ConversationTests: Companion and Ui must be assigned.");
            GetTree().Quit(1);
            return;
        }
        GameEvents.CompanionReplied += OnReply;
        // Companion chat is an optional developer feature; the core game's HUD leaves it off.
        Ui.EnableCompanionChat = true;
        await Frames(3);

        TestSituation();
        bool live = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--llm-live") >= 0;
        if (live)
        {
            await TestLive(Companion);
        }
        else
        {
            await TestSilent(Companion);
        }
        await TestPage(Ui, live);
        await TestTravellers(Ui, live);

        GameEvents.CompanionReplied -= OnReply;
        if (_failures.Count == 0)
        {
            GD.Print("CONVERSATION TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            foreach (string failure in _failures)
            {
                GD.PrintErr("FAIL: " + failure);
            }
            GD.Print("CONVERSATION TESTS: FAIL");
            GetTree().Quit(1);
        }
    }

    private void OnReply(string text, bool done)
    {
        _reply = text;
        if (!done)
        {
            _partials.Add(text);
        }
        _done = done;
    }

    private void Check(bool ok, string pass, string fail)
    {
        if (ok)
        {
            GD.Print("PASS: " + pass);
        }
        else
        {
            _failures.Add(fail);
        }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private void TestSituation()
    {
        WornPiece[] slots = new WornPiece[7];
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = new WornPiece("", "", 0.0f, 0.0f);
        }
        slots[(int)EquipSlot.Head] = new WornPiece("wool_hat", "Wool hat", 0.0f, 60.0f);
        slots[(int)EquipSlot.Feet] = new WornPiece("hobnail_boots", "Hobnail boots", 90.0f, 100.0f);
        GearSnapshot gear = new(slots, new WornPiece("backpack", "Pack", 0.0f, 120.0f), 0.0f, 0.0f, 0.15f, 0.0f, 0, 0);
        VitalsSnapshot vitals = new(40.0f, 100.0f, 20.0f, 100.0f, false, false, false, 0.0f, "", 30.0f, 80.0f);
        LoadSnapshot load = GameEvents.CurrentLoad with { Mass = 14.2f };
        string note = CompanionChat.Situation(1, "Lust", 420, 22.5f, WeatherKind.Wind, false, vitals, gear, load,
            new[] { "Water flask", "Bread", "Water flask" });
        GD.Print("Situation note: " + note);
        bool place = note.Contains("circle 2, Lust") && note.Contains("420 m");
        bool scene = note.Contains("night") && note.Contains("strong wind") && note.Contains("out on the slope");
        bool body = note.Contains("hurt") && note.Contains("hungry") && note.Contains("tired") && !note.Contains("thirsty");
        bool kit = note.Contains("wool hat (worn through)") && note.Contains("hobnail boots") && note.Contains("torn")
            && note.Contains("14 kg") && note.Contains("2 water flask, bread");
        Check(place && scene && body && kit, "the situation note describes place, time, weather, body, gear and load in words",
            $"situation note is missing something (place {place}, scene {scene}, body {body}, kit {kit}): {note}");
        Check(CompanionChat.Clean("  Hello there.<|im_end|> ") == "Hello there.", "replies are trimmed of chat-format leftovers",
            "Clean() left leftovers");
    }

    private async Task TestSilent(CompanionChat companion)
    {
        Check(GameEvents.Companion == CompanionState.Silent && companion.ResolvedModelPath.Length == 0,
            "headless without --llm-live, the companion is silent (no model loaded)",
            $"companion state {GameEvents.Companion}, model '{companion.ResolvedModelPath}'");
        _done = false;
        GameEvents.RequestAsk("Is there anything down there?");
        await Frames(2);
        Check(_done && _reply == companion.SilentReply, "a silent companion answers at once that it only listens",
            $"silent ask gave done={_done}, '{_reply}'");
    }

    private async Task TestLive(CompanionChat companion)
    {
        Check(companion.ResolvedModelPath.Length > 0 && GameEvents.Companion == CompanionState.Asleep,
            $"the model is found ({companion.ResolvedModelPath.GetFile()}) and waits asleep until spoken to",
            $"live: model '{companion.ResolvedModelPath}', state {GameEvents.Companion}");
        _done = false;
        _partials.Clear();
        ulong start = Time.GetTicksMsec();
        GameEvents.RequestAsk("I'm tired and the wind is pulling at my pack. What should I do?");
        while (!_done && (Time.GetTicksMsec() - start) / 1000.0f < LiveTimeout)
        {
            await Frames(1);
        }
        float seconds = (Time.GetTicksMsec() - start) / 1000.0f;
        GD.Print($"Companion replied in {seconds:0.0} s: {_reply}");
        Check(_done && _reply.Length > 10 && _partials.Count > 2 && !_reply.Contains("[circle"),
            $"the companion wakes, and the reply streams in ({_partials.Count} steps, {seconds:0} s)",
            $"live reply failed: done={_done}, {_partials.Count} partials, '{_reply}'");
        Check(GameEvents.Companion == CompanionState.Listening, "the companion listens again after answering",
            $"state after reply {GameEvents.Companion}");
    }

    /// <summary>Travellers: picked by seed and circle, spoken to with E, answered in their own words, kept apart from the companion.</summary>
    private async Task TestTravellers(Hud ui, bool live)
    {
        if (Visitors == null || Visitors.Personas.Length == 0 || ui.Talk == null)
        {
            _failures.Add("no travellers to test");
            return;
        }
        bool same = true;
        bool noPenitentInLimbo = true;
        int seated = 0;
        for (int station = 1; station <= 40; station++)
        {
            NpcPersona? first = Visitors.Pick(station, 0);
            NpcPersona? again = Visitors.Pick(station, 0);
            same &= first == again;
            if (first != null)
            {
                seated++;
                noPenitentInLimbo &= first.MinCircle <= 0;
            }
        }
        Check(same && noPenitentInLimbo && seated > 10 && seated < 40,
            $"travellers are picked by the mountain's seed, fit the circle, and sit at most stations ({seated}/40)",
            $"traveller picks: same {same}, circle ok {noPenitentInLimbo}, seated {seated}/40");

        NpcPersona persona = Visitors.Personas[0];
        NpcFigure figure = new();
        figure.Setup(persona, 1, Vector3.Zero);
        AddChild(figure);
        figure.Position = new Vector3(2, 0, 0);
        await Frames(2);
        figure.Interact(figure);
        await Frames(30);
        TalkScreen page = ui.Talk;
        Check(page.Visible && page.IsOpen && GameEvents.CurrentSpeaker.Id == persona.Id && page.Speaker.Name == persona.DisplayName,
            $"E on {persona.DisplayName} opens the conversation with them",
            $"talking to the traveller: open {page.IsOpen}, speaker {GameEvents.CurrentSpeaker.Id}");
        _done = false;
        page.Send("Who are you?");
        ulong start = Time.GetTicksMsec();
        while (!_done && (Time.GetTicksMsec() - start) / 1000.0f < LiveTimeout)
        {
            await Frames(1);
        }
        GD.Print($"{persona.DisplayName} replied: {_reply}");
        bool own = GameEvents.ReplySpeaker.Id == persona.Id && _reply.Length > 0;
        if (!live)
        {
            own &= System.Array.IndexOf(persona.SilentLines, _reply) >= 0;
        }
        bool apart = page.Transcript.Contains("Who are you?") && !page.Transcript.Contains("Hello, companion.");
        int entries = ui.Log?.Entries.Count ?? 0;
        bool journaled = entries > 0 && ui.Log!.Entries[entries - 1].Text.StartsWith(persona.DisplayName + ":");
        Check(own && apart && journaled, $"{persona.DisplayName} answers in their own words, on their own page, and the journal names them",
            $"traveller reply: own {own}, apart {apart}, journaled {journaled}, '{_reply}'");
        page.Close();
        await Frames(40);
        GameEvents.RequestTalkTo(SpeakerInfo.Companion);
        await Frames(30);
        Check(page.Speaker.IsCompanion && page.Transcript.Contains("Hello, companion."),
            "back with the companion, their conversation is still there", $"companion transcript lost: '{page.Transcript}'");
        page.Close();
        await Frames(40);
        figure.QueueFree();
    }

    private async Task TestPage(Hud ui, bool live)
    {
        TalkScreen? page = ui.Talk;
        if (page == null)
        {
            _failures.Add("the Hud has no conversation page");
            return;
        }
        ui.Toggle(page);
        await Frames(30);
        Check(page.Visible && page.IsOpen && GameEvents.IsConversing && GameEvents.IsScreenOpen,
            "the conversation page opens and holds the narrator and the movement keys",
            $"page open {page.IsOpen}, conversing {GameEvents.IsConversing}, screen {GameEvents.IsScreenOpen}");
        _done = false;
        page.Send("Hello, companion.");
        ulong start = Time.GetTicksMsec();
        while (!_done && (Time.GetTicksMsec() - start) / 1000.0f < LiveTimeout)
        {
            await Frames(1);
        }
        string transcript = page.Transcript;
        Check(transcript.Contains("Hello, companion.") && transcript.Contains(_reply) && _reply.Length > 0 && page.Input?.Text == "",
            "the page writes the question and the reply down, and clears the line",
            $"transcript wrong: '{transcript}'");
        int entries = ui.Log?.Entries.Count ?? 0;
        bool journaled = entries > 0 && ui.Log!.Entries[entries - 1].Quote;
        Check(journaled, "the journal records the exchange", "the reply isn't in the journal");
        page.Close();
        await Frames(40);
        Check(!page.Visible && !GameEvents.IsConversing && !GameEvents.IsScreenOpen,
            "the page closes and hands the keys back", $"after close: visible {page.Visible}, conversing {GameEvents.IsConversing}");
    }
}
