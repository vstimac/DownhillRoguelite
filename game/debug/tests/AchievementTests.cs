using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Achievements: the tracker counts what happens (stations, depth, kills, torches...) into
/// RunState.Achievements, unlocks each one once when its goal is reached and announces it; the page
/// ticks the done ones and shows progress on the rest. Headless runs keep the book in memory, so the
/// player's own achievements are never touched.
/// </summary>
public partial class AchievementTests : Node
{
    private readonly List<string> _failures = new();
    private readonly List<string> _announced = new();

    public override async void _Ready()
    {
        RunState run = GetNode<RunState>("/root/RunState");
        AchievementBook book = run.Achievements;
        book.Reset();
        GameEvents.AchievementUnlocked += OnUnlocked;
        Check("the book stays in memory in tests", !book.Persistent, $"persistent {book.Persistent}");
        Check("nothing is done on a fresh book", book.UnlockedCount == 0 && book.Total >= 15, $"{book.UnlockedCount} of {book.Total}");

        GameEvents.PublishStationReached(1);
        Check("reaching the first station unlocks First Fire", book.IsUnlocked("first_fire") && _announced.Contains("first_fire")
            && !book.IsUnlocked("five_fires"), $"announced [{string.Join(", ", _announced)}]");
        GameEvents.PublishStationReached(1);
        int firstFires = _announced.FindAll(id => id == "first_fire").Count;
        Check("an achievement is announced only once", firstFires == 1, $"First Fire announced {firstFires} times");
        GameEvents.PublishStationReached(5);
        Check("the fifth station unlocks Five Fires", book.IsUnlocked("five_fires") && !book.IsUnlocked("ten_fires"), $"best station {book.Tally(AchievementBook.Tallies.BestStation)}");

        GameEvents.PublishEnemyKilled("bog ogre");
        Check("a kill unlocks First Blood and counts towards Hunter", book.IsUnlocked("first_blood") && !book.IsUnlocked("hunter")
            && book.Tally(AchievementBook.Tallies.Kills) == 1, $"kills {book.Tally(AchievementBook.Tallies.Kills)}");
        for (int i = 0; i < 24; i++)
        {
            GameEvents.PublishEnemyKilled("night beast");
        }
        Check("25 kills make a Hunter", book.IsUnlocked("hunter"), $"kills {book.Tally(AchievementBook.Tallies.Kills)}");

        GameEvents.PublishTorch(true);
        GameEvents.PublishTorch(false);
        Check("lighting a torch unlocks Torchbearer", book.IsUnlocked("torchbearer"), $"torches {book.Tally(AchievementBook.Tallies.Torches)}");

        float circle = run.Circles.DepthPerCircle;
        GameEvents.PublishDepth(circle + 10.0f);
        Check("going below the first circle unlocks Past Limbo and A Kilometre Down", book.IsUnlocked("past_limbo") && book.IsUnlocked("kilometre")
            && !book.IsUnlocked("greed"), $"depth {book.Tally(AchievementBook.Tallies.BestDepth)} m, circle {book.Tally(AchievementBook.Tallies.BestCircle)}");

        GameEvents.PublishCarrierDied("test");
        Check("a fall unlocks Not the End", book.IsUnlocked("not_the_end"), $"deaths {book.Tally(AchievementBook.Tallies.Deaths)}");

        AchievementsPage page = new() { Size = new Vector2(760.0f, 640.0f) };
        AddChild(page);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        page.OpenSheet();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        int ticked = 0;
        foreach (Ui.TickBox tick in page.Ticks)
        {
            if (tick.Ticked)
            {
                ticked++;
            }
        }
        Check("the page lists every achievement and ticks the done ones", page.Ticks.Count == book.Total && ticked == book.UnlockedCount
            && page.Summary != null && page.Summary.Text.StartsWith($"{book.UnlockedCount} of"), $"{ticked} ticked of {page.Ticks.Count}: \"{page.Summary?.Text}\"");

        GameEvents.AchievementUnlocked -= OnUnlocked;
        book.Reset();
        if (_failures.Count == 0)
        {
            GD.Print("ACHIEVEMENT TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"ACHIEVEMENT TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void OnUnlocked(string id, string title)
    {
        _announced.Add(id);
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
