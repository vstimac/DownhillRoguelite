using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The carrier's journal, written as things happen (presentation only; it changes nothing): each
/// run starts a new chapter on a new mountain, and the day and hour are noted beside each entry:
/// circles entered, stations reached, boons taken, paths chosen, weather turning, the narrator's
/// remarks quoted, and how the run ended. Kept for the session.
/// </summary>
public partial class JournalLog : Node
{
    public readonly record struct Entry(int Run, string Stamp, string Text, bool Quote);

    public const int MaxEntries = 400;

    public IReadOnlyList<Entry> Entries => _entries;

    private readonly List<Entry> _entries = new();
    private RunState? _run;
    private WorldClock? _clock;
    private int _circle = -1;
    private float _lastWeather = -999.0f;
    private float _time;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        GameEvents.RespawnRequested += OnNewRun;
        GameEvents.DepthChanged += OnDepth;
        GameEvents.StationReached += OnStation;
        GameEvents.BoonChosen += OnBoon;
        GameEvents.RouteChosen += OnRoute;
        GameEvents.WeatherChanged += OnWeather;
        GameEvents.NarrationStarted += OnNarration;
        GameEvents.RunEnded += OnRunEnded;
        GameEvents.Slept += OnSlept;
        GameEvents.Mended += OnMended;
        GameEvents.ItemLost += OnLost;
        GameEvents.AskRequested += OnAsked;
        GameEvents.CompanionReplied += OnReplied;
        CallDeferred(MethodName.Begin);
    }

    public override void _ExitTree()
    {
        GameEvents.RespawnRequested -= OnNewRun;
        GameEvents.DepthChanged -= OnDepth;
        GameEvents.StationReached -= OnStation;
        GameEvents.BoonChosen -= OnBoon;
        GameEvents.RouteChosen -= OnRoute;
        GameEvents.WeatherChanged -= OnWeather;
        GameEvents.NarrationStarted -= OnNarration;
        GameEvents.RunEnded -= OnRunEnded;
        GameEvents.Slept -= OnSlept;
        GameEvents.Mended -= OnMended;
        GameEvents.ItemLost -= OnLost;
        GameEvents.AskRequested -= OnAsked;
        GameEvents.CompanionReplied -= OnReplied;
    }

    private void OnAsked(string question)
    {
        SpeakerInfo to = GameEvents.CurrentSpeaker;
        if (to.IsCompanion)
        {
            Write($"I said: \"{question.StripEdges()}\"");
        }
        else
        {
            Write($"I said to {to.Name}: \"{question.StripEdges()}\"");
        }
    }

    private void OnReplied(string text, bool done)
    {
        if (done && text.Length > 0)
        {
            SpeakerInfo from = GameEvents.ReplySpeaker;
            if (from.IsCompanion)
            {
                Write(text, true);
            }
            else
            {
                Write($"{from.Name}: {text}", true);
            }
        }
    }

    private void OnMended(string what, float amount)
    {
        if (amount > 0.0f)
        {
            Write($"By the fire I mended {what}.");
        }
    }

    private void OnLost(string itemId) => Write($"The torn pack let go of my {(ItemText.Find(itemId)?.DisplayName ?? itemId).ToLowerInvariant()}.");

    public override void _Process(double delta)
    {
        _time += (float)delta;
    }

    public void Write(string text, bool quote = false)
    {
        string stamp = "";
        if (_clock != null)
        {
            int hour = Mathf.FloorToInt(_clock.TimeOfDay);
            int minute = Mathf.FloorToInt((_clock.TimeOfDay - hour) * 60.0f);
            stamp = $"Day {_clock.Day}, {hour:00}:{minute:00}";
        }
        int run = 1;
        if (_run != null)
        {
            run = _run.RunNumber;
        }
        _entries.Add(new Entry(run, stamp, text, quote));
        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveAt(0);
        }
    }

    private void Begin()
    {
        ulong seed = 0;
        if (_run != null)
        {
            seed = _run.Seed;
        }
        Write($"I stand on the summit of mountain {seed % 100000}. The only way is down.");
    }

    private void OnNewRun()
    {
        _circle = -1;
        CallDeferred(MethodName.Begin);
    }

    private void OnDepth(float depth)
    {
        if (_run == null)
        {
            return;
        }
        int circle = _run.Circles.CircleAt(depth);
        if (circle != _circle && _circle >= 0)
        {
            Write($"I crossed into {_run.Circles.NameOf(circle)}.");
        }
        _circle = circle;
    }

    private void OnStation(int station) => Write($"Reached station {station}. A fire, a lean-to, graves of those before me.");

    private void OnBoon(string boon) => Write($"At the fire I took a boon: {boon}.");

    private void OnRoute(int stage, RouteKind kind)
    {
        string text = "I kept to the safe path, the one with cairns.";
        if (kind == RouteKind.Hazardous)
        {
            text = "I took the hazardous line: loose stone, but shorter.";
        }
        else if (kind == RouteKind.SupplyRich)
        {
            text = "I took the long way round, where supplies lie thick.";
        }
        Write(text);
    }

    private void OnWeather(WeatherKind kind)
    {
        // Not every turn of the sky: at most one weather note a minute.
        if (_time - _lastWeather < 60.0f)
        {
            return;
        }
        _lastWeather = _time;
        string text = kind switch
        {
            WeatherKind.Fog => "Fog came up the slope.",
            WeatherKind.Rain => "Rain. The ground turned slick.",
            WeatherKind.Snow => "Snow fell, and the path went quiet.",
            WeatherKind.Wind => "The wind rose and pulled at the pack.",
            WeatherKind.Heat => "The air turned hot and dry.",
            _ => "The sky cleared.",
        };
        Write(text);
    }

    private void OnNarration(string id, string text) => Write(text, true);

    private void OnSlept(float hours) => Write($"Slept {hours:0} hours by the fire.");

    private void OnRunEnded(RunSummary summary)
    {
        Write($"Here my descent ended, {summary.Depth} m down, taken by {summary.Cause}.");
    }
}
