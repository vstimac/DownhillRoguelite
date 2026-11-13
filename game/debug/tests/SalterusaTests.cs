using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Narrative;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The šalteruša (narrative/salterusa): every line of her manifest has its recording, every event she
/// uses has lines, and the import's list (audio/original_recordings.csv) and the manifest agree; she
/// spawns in her booth, seated and dressed; coming up to the counter makes her bark (voiced), standing
/// around makes her talk on her own, E opens the counter (her explanation, then the choices), questions
/// get answers, the door and refusals once a topic runs dry, "Can we just talk?" opens the conversation
/// with her, a blow gets "Bože dragi" then anger, and after a death the next run's first greeting is a
/// return line.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/SalterusaTests.tscn
/// </summary>
public partial class SalterusaTests : Node3D
{
    private static readonly string[] Events = { "bark", "idle", "breath", "explain", "quest", "documents", "gear", "refuse", "door", "angry", "return", "rare", "dear" };

    private readonly List<string> _failures = new();
    private readonly List<(string Line, bool Voiced)> _started = new();
    private SpeakerInfo? _talkedTo;
    private Vector3 _carrier = new(0, 0, 40);

    public override async void _Ready()
    {
        StaticBody3D floor = new() { CollisionLayer = 1 };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(200, 1, 200) }, Position = Vector3.Down * 0.5f });
        AddChild(floor);
        GameEvents.VoiceLineStarted += OnStarted;
        GameEvents.TalkRequested += OnTalk;

        Salterusa clerk = new() { PlaceAtStation = false, RareChance = 0.0f };
        AddChild(clerk);
        await Hold(3);

        TestData(clerk);
        await TestSpawn(clerk);
        await TestGreetAndIdle(clerk);
        await TestCounter(clerk);
        TestTopics(clerk);
        await TestStruck(clerk);
        await TestReturn(clerk);

        GameEvents.VoiceLineStarted -= OnStarted;
        GameEvents.TalkRequested -= OnTalk;
        if (_failures.Count == 0)
        {
            GD.Print("SALTERUSA TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print("SALTERUSA TESTS: FAIL (" + string.Join(", ", _failures) + ")");
            GetTree().Quit(1);
        }
    }

    private void OnStarted(string npc, string line, bool voiced)
    {
        if (npc == "salterusa")
        {
            _started.Add((line, voiced));
        }
    }

    private void OnTalk(SpeakerInfo speaker) => _talkedTo = speaker;

    private void TestData(Salterusa clerk)
    {
        int unvoiced = 0;
        int empty = 0;
        foreach (VoiceLine line in clerk.Manifest.Lines)
        {
            AudioStream? stream = NpcVoice.Stream(line.AudioPath);
            if (stream == null || stream.GetLength() <= 0.1)
            {
                unvoiced++;
            }
            if (line.Text.Length == 0)
            {
                empty++;
            }
        }
        List<string> missing = new();
        foreach (string name in Events)
        {
            if (clerk.Manifest.ForEvent(name).Count == 0)
            {
                missing.Add(name);
            }
        }
        // Every clip the import brought in for her is in her manifest.
        int listed = 0;
        int unlisted = 0;
        HashSet<string> paths = new();
        foreach (VoiceLine line in clerk.Manifest.Lines)
        {
            paths.Add(line.AudioPath);
        }
        using (FileAccess? file = FileAccess.Open("res://audio/original_recordings.csv", FileAccess.ModeFlags.Read))
        {
            while (file != null && !file.EofReached())
            {
                string[] row = file.GetCsvLine();
                if (row.Length > 0 && row[0].Contains("npc_salterusa/"))
                {
                    listed++;
                    if (!paths.Contains("res://" + row[0]))
                    {
                        unlisted++;
                    }
                }
            }
        }
        Check("all 97 recorded lines load, each event has lines, the import's list and the manifest agree",
            clerk.Manifest.Count == 97 && unvoiced == 0 && empty == 0 && missing.Count == 0 && listed == 97 && unlisted == 0,
            $"{clerk.Manifest.Count} lines, {unvoiced} without a recording, {empty} without a subtitle, events missing: {string.Join(", ", missing)}; {listed} in the import list, {unlisted} not in the manifest");
    }

    private async Task TestSpawn(Salterusa clerk)
    {
        await Hold(10);
        Node? bun = clerk.FindChild("Bun", true, false);
        Node? glass = clerk.FindChild("Glass", true, false);
        Vector3 window = clerk.InteractionPoint - clerk.GlobalPosition;
        Check("she sits in her booth behind the glass, dressed, as someone to talk to and to strike",
            clerk.Booth != null && glass != null && bun != null && clerk.Animations != null && clerk.Clip.StartsWith("Sitting_Idle")
            && clerk.IsInGroup(IInteractable.Group) && clerk.IsInGroup(IMeleeTarget.Group) && window.Z > 1.0f && window.Y > 1.0f && window.Y < 1.6f,
            $"clip {clerk.Clip}, bun {bun != null}, glass {glass != null}, window at {window}");
    }

    private async Task TestGreetAndIdle(Salterusa clerk)
    {
        _started.Clear();
        _carrier = new Vector3(0, 0, 4);
        await Hold(3);
        bool barked = clerk.Said.Count == 1 && clerk.Said[0].StartsWith("bark_") && _started.Count == 1 && _started[0].Voiced;
        await Hold(20);
        string talkingClip = clerk.Clip;
        Check("coming up to the counter, she barks, voiced, and talks with her hands",
            barked && talkingClip.StartsWith("Sitting_Talking"), $"said {string.Join(",", clerk.Said)}, voiced {_started.Count > 0 && _started[0].Voiced}, clip {talkingClip}");
        clerk.IdleInterval = new Vector2(0.2f, 0.3f);
        clerk.Voice!.Skip();
        await Hold(40);
        string idle = Last(clerk);
        Check("standing around, she talks (or sighs) on her own",
            clerk.Said.Count == 2 && (idle.StartsWith("idle_") || idle.StartsWith("breath_") || idle.StartsWith("angry_")), $"said {string.Join(",", clerk.Said)}");
        clerk.Voice.Skip();
        clerk.IdleInterval = new Vector2(1000.0f, 1000.0f);
        await Hold(2);
    }

    private async Task TestCounter(Salterusa clerk)
    {
        _talkedTo = null;
        clerk.Interact(clerk);
        string explained = Last(clerk);
        bool waits = clerk.Talking && !clerk.ChoicesShown;
        clerk.Interact(clerk);
        await Hold(2);
        Check("E opens the counter: she explains, then the choices come up",
            explained == "explain_01" && waits && clerk.Talking && clerk.ChoicesShown, $"said {explained}, choices {clerk.ChoicesShown}");
        clerk.RefuseChance = 0.0f;
        GameEvents.RequestDialogueChoice(1);
        string answer = Last(clerk);
        clerk.Voice!.Skip();
        await Hold(2);
        Check("asking what it takes, she answers in order, then the choices are back",
            answer == "quest_01" && clerk.ChoicesShown, $"answered {answer}, choices {clerk.ChoicesShown}");
        GameEvents.RequestDialogueChoice(3);
        await Hold(2);
        Check("\"Can we just talk?\" opens the conversation with her (the local model)",
            !clerk.Talking && _talkedTo is SpeakerInfo talk && talk.Id == "salterusa" && talk.Persona.Length > 0 && talk.SilentLines.Length > 0,
            $"counter open {clerk.Talking}, talking to {_talkedTo?.Id}");
        GameEvents.PublishConversation(false);
    }

    private void TestTopics(Salterusa clerk)
    {
        clerk.RefuseChance = 0.0f;
        List<string> answers = new();
        for (int i = 0; i < 9; i++)
        {
            answers.Add(clerk.Answer("gear"));
        }
        int gear = answers.FindAll(a => a.StartsWith("gear_")).Count;
        clerk.RefuseChance = 1.0f;
        string refused = clerk.Answer("documents");
        clerk.RefuseChance = 0.0f;
        Check("a topic runs dry: then it's on the door, then refusals; sometimes she just refuses",
            gear == 6 && answers[6] == "door_01" && answers[7].StartsWith("refuse_") && refused.StartsWith("refuse_"),
            string.Join(",", answers) + "; refused with " + refused);
    }

    private async Task TestStruck(Salterusa clerk)
    {
        clerk.TakeHit(5.0f, Vector3.Zero, "test");
        string first = Last(clerk);
        clerk.Voice!.Skip();
        clerk.TakeHit(5.0f, Vector3.Zero, "test");
        string second = Last(clerk);
        clerk.Voice.Skip();
        await Hold(2);
        Check("struck, she cries \"Bože dragi\", then threatens", first.StartsWith("dear_") && second.StartsWith("angry_"), $"{first}, then {second}");
    }

    private async Task TestReturn(Salterusa clerk)
    {
        _carrier = new Vector3(0, 0, 40);
        await Hold(3);
        GameEvents.PublishCarrierDied("test");
        GameEvents.PublishRespawnRequest();
        await Hold(3);
        bool cleared = clerk.Said.Count == 0;
        _carrier = new Vector3(0, 0, 4);
        await Hold(3);
        Check("after a death, a new run's first greeting is a return line", cleared && clerk.Returning && Last(clerk).StartsWith("return_"),
            $"cleared {cleared}, said {string.Join(",", clerk.Said)}");
        clerk.Voice!.Skip();
        // Let the audio thread let go of the stopped playback (in real time: fixed-fps frames race ahead of it) before the test quits.
        _carrier = new Vector3(0, 0, 40);
        await Hold(2);
        OS.DelayMsec(250);
        await Hold(2);
    }

    private static string Last(Salterusa clerk)
    {
        if (clerk.Said.Count == 0)
        {
            return "";
        }
        return clerk.Said[clerk.Said.Count - 1];
    }

    /// <summary>Waits physics frames, telling everyone where the stand-in carrier stands each frame.</summary>
    private async Task Hold(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            GameEvents.PublishLocomotion(default(LocomotionSnapshot) with { Position = _carrier, Forward = Vector3.Forward });
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
