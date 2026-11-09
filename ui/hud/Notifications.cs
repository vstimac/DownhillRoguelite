using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// Small paper notes that slide in under the descent panel for things worth knowing: an item picked
/// up, eaten or drunk, nothing left to eat, a station reached, a boon taken, a path chosen, a new
/// circle, the weather turning, the starting kit, and warnings when hunger, thirst or stamina run
/// low. Each note is a scrap of parchment pinned at a slight tilt; it stays a few seconds, then
/// lifts and fades. Up to four at once.
/// </summary>
public partial class Notifications : Control
{
    public const int MaxNotes = 4;
    [Export] public float Life { get; set; } = 4.0f;

    public IReadOnlyList<string> Recent => _recent;

    private readonly List<string> _recent = new();
    private readonly RandomNumberGenerator _random = new();
    private RunState? _run;
    private int _circle = -1;
    private bool _hungry;
    private bool _thirsty;
    private bool _exhausted;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _random.Seed = 7;
        _run = GetNodeOrNull<RunState>("/root/RunState");
        GameEvents.ItemPickedUp += OnPicked;
        GameEvents.ItemConsumed += OnConsumed;
        GameEvents.ConsumeFailed += OnFailed;
        GameEvents.StationReached += OnStation;
        GameEvents.BoonChosen += OnBoon;
        GameEvents.RouteChosen += OnRoute;
        GameEvents.DepthChanged += OnDepth;
        GameEvents.WeatherChanged += OnWeather;
        GameEvents.ItemsGranted += OnGranted;
        GameEvents.VitalsUpdated += OnVitals;
        GameEvents.RespawnRequested += OnNewRun;
        GameEvents.Mended += OnMended;
        GameEvents.ItemLost += OnLost;
        GameEvents.GearChanged += OnGear;
        GameEvents.VisitorMet += OnVisitor;
        GameEvents.ItemDropped += OnDropped;
        GameEvents.HintShown += OnHint;
        CallDeferred(MethodName.Hint);
    }

    [Export] public string StartHint { get; set; } = "Esc: pause and controls";

    private void Hint() => Note(StartHint);

    public override void _ExitTree()
    {
        GameEvents.ItemPickedUp -= OnPicked;
        GameEvents.ItemConsumed -= OnConsumed;
        GameEvents.ConsumeFailed -= OnFailed;
        GameEvents.StationReached -= OnStation;
        GameEvents.BoonChosen -= OnBoon;
        GameEvents.RouteChosen -= OnRoute;
        GameEvents.DepthChanged -= OnDepth;
        GameEvents.WeatherChanged -= OnWeather;
        GameEvents.ItemsGranted -= OnGranted;
        GameEvents.VitalsUpdated -= OnVitals;
        GameEvents.RespawnRequested -= OnNewRun;
        GameEvents.Mended -= OnMended;
        GameEvents.ItemLost -= OnLost;
        GameEvents.GearChanged -= OnGear;
        GameEvents.VisitorMet -= OnVisitor;
        GameEvents.ItemDropped -= OnDropped;
        GameEvents.HintShown -= OnHint;
    }

    private void OnHint(string text) => Note(text, true);

    private void OnDropped(string itemId) => Note($"Put down {NameOf(itemId)}");

    private void OnVisitor(string name, string title) => Note($"Someone sits by the fire: {name}, {title}");

    private readonly HashSet<string> _broken = new();

    private void OnMended(string what, float amount)
    {
        if (amount > 0.0f)
        {
            Note($"Mended {what} (+{amount:0})");
        }
        else
        {
            Note(what, true);
        }
    }

    private void OnLost(string itemId) => Note($"{NameOf(itemId)} fell out of the torn pack", true);

    /// <summary>Warns once when a piece of gear (or the pack) wears through.</summary>
    private void OnGear(GearSnapshot gear)
    {
        List<WornPiece> pieces = new();
        if (gear.Slots != null)
        {
            pieces.AddRange(gear.Slots);
        }
        pieces.Add(gear.Backpack);
        foreach (WornPiece piece in pieces)
        {
            if (piece.Empty || piece.MaxDurability <= 0.0f)
            {
                continue;
            }
            string key = piece.ItemId;
            if (piece.Broken && _broken.Add(key))
            {
                string name = piece.Name;
                if (key == "backpack")
                {
                    name = "pack";
                    Note("Your pack is torn: things may fall out", true);
                    continue;
                }
                Note($"Your {name.ToLowerInvariant()} is worn through", true);
            }
            else if (!piece.Broken)
            {
                _broken.Remove(key);
            }
        }
    }

    /// <summary>Pins a note; the oldest goes when there are too many.</summary>
    public void Note(string text, bool warning = false)
    {
        _recent.Add(text);
        if (_recent.Count > 20)
        {
            _recent.RemoveAt(0);
        }
        List<Node> notes = new(GetChildren());
        if (notes.Count >= MaxNotes && notes[0] is Control oldest)
        {
            oldest.QueueFree();
        }
        // As wide as its words (up to 320 px), as many lines tall as it needs: notes never cut off.
        Vector2 words = UiStyle.Wrapped(text, UiStyle.TextSize, 280.0f);
        Vector2 size = new(Mathf.Min(320.0f, words.X + 40.0f), words.Y + 14.0f);
        ColorRect paper = new() { Size = size, MouseFilter = MouseFilterEnum.Ignore };
        paper.Material = UiStyle.Parchment(size, _random.Randf() * 50.0f, 2.5f, 3.0f);
        Color ink = UiStyle.Ink;
        if (warning)
        {
            ink = UiStyle.Warning.Darkened(0.25f);
        }
        Label label = UiStyle.MakeLabel(text, UiStyle.TextSize, false, ink);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.Position = new Vector2(18, 6);
        label.Size = new Vector2(size.X - 30.0f, words.Y + 4.0f);
        paper.AddChild(label);
        AddChild(paper);
        Restack();
        paper.PivotOffset = new Vector2(size.X, 0);
        paper.Rotation = Mathf.DegToRad(_random.RandfRange(-3.0f, 3.0f));
        paper.Modulate = new Color(1, 1, 1, 0);
        float x = paper.Position.X;
        paper.Position += new Vector2(60, 0);
        Tween tween = paper.CreateTween();
        tween.SetParallel();
        tween.TweenProperty(paper, "position:x", x, 0.3).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(paper, "modulate:a", 1.0f, 0.2);
        tween.Chain().TweenInterval(Life);
        tween.Chain().TweenProperty(paper, "modulate:a", 0.0f, 0.5);
        tween.Parallel().TweenProperty(paper, "position:y", paper.Position.Y - 16.0f, 0.5);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            paper.QueueFree();
            CallDeferred(MethodName.Restack);
        }));
    }

    private void Restack()
    {
        float y = 0.0f;
        foreach (Node node in GetChildren())
        {
            if (node is Control note && !note.IsQueuedForDeletion())
            {
                note.Position = new Vector2(Size.X - note.Size.X, y);
                y += note.Size.Y + 6.0f;
            }
        }
    }

    private static string NameOf(string itemId)
    {
        return ItemText.Find(itemId)?.DisplayName ?? itemId;
    }

    private void OnPicked(string itemId, string category) => Note($"Picked up {NameOf(itemId)}");

    private void OnConsumed(SupplyNeed need, string itemId, float restore)
    {
        if (need == SupplyNeed.Hunger)
        {
            Note($"Ate {NameOf(itemId)} (+{restore:0})");
        }
        else
        {
            Note($"Drank {NameOf(itemId)} (+{restore:0})");
        }
    }

    private void OnFailed(SupplyNeed need)
    {
        if (need == SupplyNeed.Hunger)
        {
            Note("Nothing left to eat", true);
        }
        else
        {
            Note("Nothing left to drink", true);
        }
    }

    private void OnStation(int station) => Note($"Station {station} reached. Rest by the fire.");

    private void OnBoon(string boon) => Note($"Boon taken: {boon}");

    private void OnRoute(int stage, RouteKind kind)
    {
        string name = "safe";
        if (kind == RouteKind.Hazardous)
        {
            name = "hazardous";
        }
        else if (kind == RouteKind.SupplyRich)
        {
            name = "supply-rich";
        }
        Note($"You took the {name} path");
    }

    private void OnDepth(float depth)
    {
        if (_run == null)
        {
            return;
        }
        int circle = _run.Circles.CircleAt(depth);
        if (_circle >= 0 && circle != _circle)
        {
            Note($"You descend into {_run.Circles.NameOf(circle)}");
        }
        _circle = circle;
    }

    private void OnWeather(WeatherKind kind)
    {
        string text = kind switch
        {
            WeatherKind.Fog => "Fog rolls in",
            WeatherKind.Rain => "It starts to rain",
            WeatherKind.Snow => "Snow begins to fall",
            WeatherKind.Wind => "The wind rises",
            WeatherKind.Heat => "The air turns hot",
            _ => "The sky clears",
        };
        Note(text);
    }

    private void OnGranted(string[] items)
    {
        List<string> names = new();
        foreach (string path in items)
        {
            names.Add(ItemText.Find(path.GetFile().GetBaseName())?.DisplayName ?? path.GetFile().GetBaseName());
        }
        Note($"You set out with {string.Join(", ", names)}");
    }

    private void OnVitals(VitalsSnapshot vitals)
    {
        bool hungry = vitals.Hunger < 25.0f;
        bool thirsty = vitals.Thirst < 25.0f;
        if (hungry && !_hungry)
        {
            Note("You are hungry", true);
        }
        if (thirsty && !_thirsty)
        {
            Note("You are thirsty", true);
        }
        if (vitals.Exhausted && !_exhausted)
        {
            Note("Out of breath: no sprinting or jumping for a moment", true);
        }
        _hungry = hungry;
        _thirsty = thirsty;
        _exhausted = vitals.Exhausted;
    }

    private void OnNewRun()
    {
        _circle = -1;
        foreach (Node node in GetChildren())
        {
            node.QueueFree();
        }
        CallDeferred(MethodName.Hint);
    }
}
