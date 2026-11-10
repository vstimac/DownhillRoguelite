using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// Keeps the achievements' tallies (RunState.Achievements) from what happens in a run: stations and
/// depth reached, kills, torches lit, sleeps, contracts and relics delivered, texts read, boons,
/// mending, falls and runs begun. Each achievement unlocked for the first time is announced through
/// GameEvents.AchievementUnlocked (the HUD's toast) and the log. An autoload, so it counts
/// everywhere a run is played; it only listens and never changes the run.
/// </summary>
public partial class AchievementTracker : Node
{
    public override void _Ready()
    {
        GameEvents.StationReached += OnStation;
        GameEvents.DepthChanged += OnDepth;
        GameEvents.EnemyKilled += OnKill;
        GameEvents.TorchChanged += OnTorch;
        GameEvents.Slept += OnSlept;
        GameEvents.ContractResolved += OnContract;
        GameEvents.RelicDelivered += OnRelic;
        GameEvents.LoreRead += OnLore;
        GameEvents.BoonChosen += OnBoon;
        GameEvents.Mended += OnMended;
        GameEvents.CarrierDied += OnDied;
        GameEvents.RunBegun += OnRunBegun;
    }

    public override void _ExitTree()
    {
        GameEvents.StationReached -= OnStation;
        GameEvents.DepthChanged -= OnDepth;
        GameEvents.EnemyKilled -= OnKill;
        GameEvents.TorchChanged -= OnTorch;
        GameEvents.Slept -= OnSlept;
        GameEvents.ContractResolved -= OnContract;
        GameEvents.RelicDelivered -= OnRelic;
        GameEvents.LoreRead -= OnLore;
        GameEvents.BoonChosen -= OnBoon;
        GameEvents.Mended -= OnMended;
        GameEvents.CarrierDied -= OnDied;
        GameEvents.RunBegun -= OnRunBegun;
    }

    private RunState? Run()
    {
        return GetNodeOrNull<RunState>("/root/RunState");
    }

    private void Count(string tally, int amount = 1)
    {
        RunState? run = Run();
        if (run != null)
        {
            Announce(run.Achievements.Count(tally, amount));
        }
    }

    private void Best(string tally, int value)
    {
        RunState? run = Run();
        if (run != null)
        {
            Announce(run.Achievements.Best(tally, value));
        }
    }

    private static void Announce(List<AchievementBook.Entry> unlocked)
    {
        foreach (AchievementBook.Entry entry in unlocked)
        {
            GD.Print($"[Achievement] {entry.Title}: {entry.Description}");
            GameEvents.PublishAchievementUnlocked(entry.Id, entry.Title);
        }
    }

    private void OnStation(int station)
    {
        Best(AchievementBook.Tallies.BestStation, station);
    }

    private void OnDepth(float depth)
    {
        Best(AchievementBook.Tallies.BestDepth, Mathf.FloorToInt(depth));
        RunState? run = Run();
        if (run != null)
        {
            // Circles count from one here: the first circle, Limbo, is 1.
            Best(AchievementBook.Tallies.BestCircle, run.Circles.CircleAt(depth) + 1);
        }
    }

    private void OnKill(string name)
    {
        Count(AchievementBook.Tallies.Kills);
    }

    private void OnTorch(bool lit)
    {
        if (lit)
        {
            Count(AchievementBook.Tallies.Torches);
        }
    }

    private void OnSlept(float hours)
    {
        Count(AchievementBook.Tallies.Sleeps);
    }

    private void OnContract(ContractNote note)
    {
        if (note.Status == ContractStatus.Delivered)
        {
            Count(AchievementBook.Tallies.Contracts);
        }
    }

    private void OnRelic(RelicNote note)
    {
        Count(AchievementBook.Tallies.Relics);
    }

    private void OnLore(LoreNote note)
    {
        RunState? run = Run();
        if (run != null)
        {
            // Different texts, ever: re-reading one doesn't count again.
            Best(AchievementBook.Tallies.LoreRead, run.Lore.EverCount);
        }
    }

    private void OnBoon(string name)
    {
        Count(AchievementBook.Tallies.Boons);
    }

    private void OnMended(string what, float amount)
    {
        Count(AchievementBook.Tallies.Mends);
    }

    private void OnDied(string cause)
    {
        Count(AchievementBook.Tallies.Deaths);
    }

    private void OnRunBegun(bool continuing)
    {
        if (!continuing)
        {
            Count(AchievementBook.Tallies.Runs);
        }
    }
}
