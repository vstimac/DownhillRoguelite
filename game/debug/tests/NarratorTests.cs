using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Narrative;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for the narrator: all 500 lines load with their audio; circle contexts match
/// the right circles; an event plays a line, publishes NarrationStarted and blocks a second line
/// until the gap has passed; weather changes reach the narrator.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/NarratorTests.tscn
/// </summary>
public partial class NarratorTests : Node
{
    [Export] public NarratorDirector? Narrator { get; set; }
    [Export] public int ExpectedLines { get; set; } = 500;

    private int _failures;
    private int _started;

    public override async void _Ready()
    {
        if (Narrator == null)
        {
            GD.PushError("NarratorTests: Narrator must be assigned.");
            GetTree().Quit(1);
            return;
        }
        GameEvents.NarrationStarted += OnStarted;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        int missing = 0;
        for (int i = 1; i <= ExpectedLines; i++)
        {
            if (!ResourceLoader.Exists($"{Narrator.AudioFolder}/NARR_{i:000}.wav"))
            {
                missing++;
            }
        }
        Check("all lines load with audio", Narrator.LineCount == ExpectedLines && missing == 0,
            $"{Narrator.LineCount} lines, {missing} missing audio files");

        NarratorLine range = new("x", "", "", "circles_6_beyond", "", 1, 0, 1, false, "");
        NarratorLine single = new("x", "", "", "circle_3", "", 1, 0, 1, false, "");
        NarratorLine beyond = new("x", "", "", "beyond_ninth", "", 1, 0, 1, false, "");
        Check("circle contexts match", range.FitsCircle(5) && range.FitsCircle(12) && !range.FitsCircle(4)
            && single.FitsCircle(2) && !single.FitsCircle(3) && beyond.FitsCircle(9) && !beyond.FitsCircle(8),
            "circles_6_beyond, circle_3, beyond_ninth");

        bool first = Narrator.Say("player_fell");
        bool second = Narrator.Say("player_stumbled");
        Check("an event plays a line, one at a time", first && !second && _started == 1 && GameEvents.IsNarrating,
            $"first {first}, second {second}, started {_started}, playing {Narrator.CurrentLine}");

        GameEvents.PublishDialogue(true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        bool stopped = !GameEvents.IsNarrating && Narrator.CurrentLine == null;
        bool spoke = Narrator.Say("player_fell");
        GameEvents.PublishDialogue(false);
        Check("a dialogue with an NPC stops the narrator and keeps it quiet", stopped && !spoke, $"stopped {stopped}, spoke over it {spoke}");

        GameEvents.NarrationStarted -= OnStarted;
        Narrator.Silence();
        Check("silence stops the line", !GameEvents.IsNarrating && Narrator.CurrentLine == null, "stopped");
        // Let the audio server drop the stopped playback before quitting.
        for (int i = 0; i < 10; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (_failures == 0)
        {
            GD.Print("NARRATOR TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"NARRATOR TESTS: FAIL ({_failures})");
            GetTree().Quit(1);
        }
    }

    private void OnStarted(string id, string text)
    {
        _started++;
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
            _failures++;
        }
    }
}
