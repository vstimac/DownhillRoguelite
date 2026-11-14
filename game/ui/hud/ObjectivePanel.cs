using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The goal note at the top of the screen (the purpose patch): the main goal in ink capitals with
/// why it matters and how far it is, then the guide step and side goals (jobs, relics, something
/// worth a detour), and the circle below as the long goal; the run's score in the corner. Reads
/// GameEvents.CurrentObjectives and RunState; presentation only. The sheet grows with its lines up
/// to MaxHeight. A changed main goal gives a brief ink smudge.
/// </summary>
public partial class ObjectivePanel : ParchmentPanel
{
    public const float PanelWidth = 320.0f;
    [Export] public float MaxHeight { get; set; } = 236.0f;
    [Export] public int MaxSideLines { get; set; } = 4;
    [Export] public float Padding { get; set; } = 16.0f;
    [Export] public float ScoreLine { get; set; } = 12.0f;

    /// <summary>Everything the note says, line by line (tests read it).</summary>
    public string Text { get; private set; } = "";

    private Label? _score;
    private Label? _title;
    private Label? _why;
    private Label? _progress;
    private readonly List<Label> _sides = new();
    private Label? _circle;
    private RunState? _run;
    private string _lastTitle = "";
    private float _scoreTimer;

    public override void _Ready()
    {
        Seed = 31.0f;
        Size = new Vector2(PanelWidth, 120.0f);
        base._Ready();
        MouseFilter = MouseFilterEnum.Ignore;
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _score = UiStyle.MakeLabel("", UiStyle.SmallSize, false, UiStyle.InkSoft);
        _score.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(_score);
        _title = Wrapping(UiStyle.MakeLabel("", 22, true));
        _why = Wrapping(UiStyle.MakeLabel("", UiStyle.SmallSize, false, UiStyle.InkSoft));
        _progress = Wrapping(UiStyle.MakeLabel("", UiStyle.SmallSize));
        for (int i = 0; i < MaxSideLines; i++)
        {
            _sides.Add(Wrapping(UiStyle.MakeLabel("", UiStyle.SmallSize)));
        }
        _circle = Wrapping(UiStyle.MakeLabel("", UiStyle.SmallSize, false, UiStyle.InkSoft));
        GameEvents.ObjectivesChanged += OnObjectives;
        Show(GameEvents.CurrentObjectives);
    }

    public override void _ExitTree()
    {
        GameEvents.ObjectivesChanged -= OnObjectives;
    }

    private Label Wrapping(Label label)
    {
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(label);
        return label;
    }

    public override void _Process(double delta)
    {
        _scoreTimer -= (float)delta;
        if (_scoreTimer > 0.0f || _score == null || _run == null)
        {
            return;
        }
        _scoreTimer = 0.5f;
        _score.Text = "score " + _run.ScoreOf(_run.Depth, _run.Stage, _run.Telemetry.CargoScore);
    }

    private void OnObjectives(IReadOnlyList<ObjectiveNote> notes) => Show(notes);

    /// <summary>Fills the note from a list of goals (main first, circle last) and fits the sheet to it.</summary>
    public void Show(IReadOnlyList<ObjectiveNote> notes)
    {
        if (_title == null || _why == null || _progress == null || _circle == null || _score == null)
        {
            return;
        }
        ObjectiveNote? main = null;
        ObjectiveNote? circle = null;
        List<ObjectiveNote> sides = new();
        foreach (ObjectiveNote note in notes)
        {
            if (note.Kind == ObjectiveKind.Main)
            {
                main = note;
            }
            else if (note.Kind == ObjectiveKind.Circle)
            {
                circle = note;
            }
            else
            {
                sides.Add(note);
            }
        }
        Visible = main != null;
        if (main == null)
        {
            Text = "";
            return;
        }
        float width = PanelWidth - Padding * 2.0f;
        List<string> lines = new();
        // The score has the top line to itself; the goal starts under it.
        float y = Padding - 2.0f + ScoreLine;
        _score.Position = new Vector2(PanelWidth - Padding - 120.0f, 6.0f);
        _score.Size = new Vector2(120.0f, 20.0f);

        y = Place(_title, main.Value.Title, width, y, 22, true, lines);
        y = Place(_why, main.Value.Why, width, y, UiStyle.SmallSize, false, lines);
        y = Place(_progress, main.Value.Progress, width, y, UiStyle.SmallSize, false, lines);
        y += 4.0f;
        for (int i = 0; i < _sides.Count; i++)
        {
            Label label = _sides[i];
            if (i >= sides.Count)
            {
                label.Visible = false;
                continue;
            }
            ObjectiveNote side = sides[i];
            label.AddThemeColorOverride("font_color", SideColour(side));
            y = Place(label, SideLine(side), width, y, UiStyle.SmallSize, false, lines);
        }
        if (circle != null)
        {
            y += 2.0f;
            y = Place(_circle, circle.Value.Title + " · " + circle.Value.Progress, width, y, UiStyle.SmallSize, false, lines);
        }
        else
        {
            _circle.Visible = false;
        }
        Size = new Vector2(PanelWidth, Mathf.Min(y + Padding - 4.0f, MaxHeight));
        Text = string.Join("\n", lines);
        if (main.Value.Title != _lastTitle)
        {
            _lastTitle = main.Value.Title;
            _title.Modulate = new Color(0.6f, 0.4f, 0.3f);
            CreateTween().TweenProperty(_title, "modulate", Colors.White, 0.8);
        }
    }

    /// <summary>"Next: Pick up food or water (E)" for the guide, "→ Maren's errand: … · Reward …" for the rest.</summary>
    public static string SideLine(ObjectiveNote note)
    {
        if (note.Kind == ObjectiveKind.Guide)
        {
            return "Next: " + note.Title;
        }
        string line = "→ " + note.Title;
        if (note.Progress.Length > 0)
        {
            line += " · " + note.Progress;
        }
        return line;
    }

    private static Color SideColour(ObjectiveNote note)
    {
        if (note.Failed)
        {
            return new Color(UiStyle.Warning, 0.6f);
        }
        if (note.Done)
        {
            return UiStyle.InkSoft;
        }
        if (note.Kind == ObjectiveKind.Guide)
        {
            return UiStyle.Health;
        }
        return UiStyle.Ink;
    }

    private float Place(Label label, string text, float width, float y, int size, bool heading, List<string> lines)
    {
        if (text.Length == 0 || y > MaxHeight - Padding - 12.0f)
        {
            label.Visible = false;
            return y;
        }
        label.Visible = true;
        label.Text = text;
        Vector2 wrapped = UiStyle.Wrapped(text, size, width, heading);
        label.Position = new Vector2(Padding, y);
        label.Size = new Vector2(width, wrapped.Y);
        lines.Add(text);
        return y + wrapped.Y;
    }
}
