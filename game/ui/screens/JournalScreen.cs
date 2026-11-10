using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The journal (J): a two-page spread. The left page, "Now", lists the carrier's goals (main goal,
/// guide step, jobs, relics, the circle below) over a faint ink drawing of the endless mountain,
/// with this run's finished goals struck through (GameEvents.CurrentObjectives and
/// FinishedObjectives). The right page carries the entries the JournalLog wrote, a page at a time,
/// newest last.
/// The arrows (or A and D) turn pages: the right page folds over its spine and the next one
/// unfolds, with a rustle of paper.
/// </summary>
public partial class JournalScreen : ParchmentPanel
{
    public const int EntriesPerPage = 5;

    public JournalLog? Log { get; set; }
    public int Page { get; private set; }
    public int Pages => Mathf.Max(1, Mathf.CeilToInt((Log?.Entries.Count ?? 0) / (float)EntriesPerPage));
    public string PageText => _text?.Text ?? "";
    /// <summary>The "Now" page's goals as plain text (tests read it).</summary>
    public string NowText => _now?.GetParsedText() ?? "";
    /// <summary>Finished goals listed at most on the "Now" page (the newest).</summary>
    [Export] public int FinishedShown { get; set; } = 4;

    private Control? _right;
    private Label? _text;
    private Label? _pageNumber;
    private RichTextLabel? _now;
    private bool _turning;
    private AudioStreamPlayer? _rustle;

    public override void _Ready()
    {
        Title = "Journal";
        Closable = true;
        Seed = 67.0f;
        Size = new Vector2(820, 540);
        base._Ready();
        Illustration art = new() { Position = new Vector2(24, 60), Size = new Vector2(360, 420), Modulate = new Color(1, 1, 1, 0.22f) };
        AddChild(art);
        _now = new RichTextLabel
        {
            Position = new Vector2(40, 66),
            Size = new Vector2(330, 400),
            BbcodeEnabled = true,
            ScrollActive = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _now.AddThemeFontOverride("normal_font", UiStyle.Hand);
        _now.AddThemeFontOverride("bold_font", UiStyle.Heading);
        _now.AddThemeFontSizeOverride("normal_font_size", UiStyle.SmallSize);
        _now.AddThemeFontSizeOverride("bold_font_size", UiStyle.TextSize);
        _now.AddThemeColorOverride("default_color", UiStyle.Ink);
        AddChild(_now);
        GameEvents.ObjectivesChanged += OnObjectives;
        _right = new Control { Position = new Vector2(404, 60), Size = new Vector2(392, 420), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_right);
        _text = UiStyle.MakeLabel("", UiStyle.TextSize);
        _text.Size = _right.Size;
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _right.AddChild(_text);
        InkButton back = new() { Glyph = InkButton.GlyphKind.Left, Position = new Vector2(500, Size.Y - 52), Size = new Vector2(36, 36) };
        back.Pressed += () => Turn(-1);
        AddChild(back);
        InkButton forward = new() { Glyph = InkButton.GlyphKind.Right, Position = new Vector2(660, Size.Y - 52), Size = new Vector2(36, 36) };
        forward.Pressed += () => Turn(1);
        AddChild(forward);
        _pageNumber = UiStyle.MakeLabel("1 / 1", UiStyle.TextSize);
        _pageNumber.Position = new Vector2(560, Size.Y - 50);
        AddChild(_pageNumber);
        if (DisplayServer.GetName() != "headless" && ResourceLoader.Exists("res://audio/sfx/kenney_rpg_audio/bookFlip1.ogg"))
        {
            _rustle = new AudioStreamPlayer { Stream = ResourceLoader.Load<AudioStream>("res://audio/sfx/kenney_rpg_audio/bookFlip1.ogg"), VolumeDb = -6.0f };
            AddChild(_rustle);
        }
        SetOpenNow(false);
    }

    public override void _ExitTree()
    {
        GameEvents.ObjectivesChanged -= OnObjectives;
        if (_rustle != null)
        {
            _rustle.Stop();
            _rustle.Stream = null;
        }
    }

    private void OnObjectives(IReadOnlyList<ObjectiveNote> notes)
    {
        if (Visible)
        {
            FillNow();
        }
    }

    /// <summary>Writes the "Now" page: the goals in order with why each matters, then the finished ones struck through.</summary>
    public void FillNow()
    {
        if (_now == null)
        {
            return;
        }
        System.Text.StringBuilder text = new();
        text.Append("[b]Now[/b]\n");
        foreach (ObjectiveNote note in GameEvents.CurrentObjectives)
        {
            string title = Escape(note.Title);
            if (note.Kind == ObjectiveKind.Guide)
            {
                title = "Next: " + title;
            }
            if (note.Done)
            {
                title = "[s]" + title + "[/s]";
            }
            text.Append("\n[b]").Append(title).Append("[/b]\n");
            if (note.Why.Length > 0)
            {
                text.Append(Escape(note.Why)).Append('\n');
            }
            if (note.Progress.Length > 0)
            {
                text.Append("[color=#5a4630]").Append(Escape(note.Progress)).Append("[/color]\n");
            }
        }
        IReadOnlyList<ObjectiveNote> finished = GameEvents.FinishedObjectives;
        if (finished.Count > 0)
        {
            text.Append("\n[color=#7a6850]Behind me:[/color]\n");
            for (int i = Mathf.Max(0, finished.Count - FinishedShown); i < finished.Count; i++)
            {
                text.Append("[color=#7a6850][s]").Append(Escape(finished[i].Title)).Append("[/s] ").Append(Escape(finished[i].Progress)).Append("[/color]\n");
            }
        }
        _now.Text = text.ToString();
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");

    /// <summary>Opens on the newest page.</summary>
    public void OpenJournal()
    {
        Page = Pages - 1;
        FillNow();
        Fill();
        Open();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || @event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }
        if (key.PhysicalKeycode == Key.A || key.PhysicalKeycode == Key.Left)
        {
            Turn(-1);
            GetViewport().SetInputAsHandled();
        }
        else if (key.PhysicalKeycode == Key.D || key.PhysicalKeycode == Key.Right)
        {
            Turn(1);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Turns a page with the fold animation; returns whether there was a page to turn to.</summary>
    public bool Turn(int direction)
    {
        int target = Mathf.Clamp(Page + direction, 0, Pages - 1);
        if (target == Page || _turning || _right == null)
        {
            return false;
        }
        Page = target;
        _rustle?.Play();
        _turning = true;
        _right.PivotOffset = new Vector2(0, _right.Size.Y * 0.5f);
        Tween tween = CreateTween();
        tween.TweenProperty(_right, "scale:x", 0.0f, 0.16).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(Fill));
        tween.TweenProperty(_right, "scale:x", 1.0f, 0.22).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() => _turning = false));
        return true;
    }

    private void Fill()
    {
        if (_text == null || _pageNumber == null)
        {
            return;
        }
        List<string> lines = new();
        if (Log != null)
        {
            int start = Page * EntriesPerPage;
            for (int i = start; i < Mathf.Min(start + EntriesPerPage, Log.Entries.Count); i++)
            {
                JournalLog.Entry entry = Log.Entries[i];
                string text = entry.Text;
                if (entry.Quote)
                {
                    text = $"“{text}”";
                }
                lines.Add($"{entry.Stamp}\n{text}");
            }
        }
        if (lines.Count == 0)
        {
            lines.Add("The pages are empty. The descent has not begun.");
        }
        _text.Text = string.Join("\n\n", lines);
        _pageNumber.Text = $"{Page + 1} / {Pages}";
    }

    /// <summary>An ink drawing for the left page: the mountain falling away in rings toward a distant spire.</summary>
    private partial class Illustration : Control
    {
        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            Vector2 s = Size;
            InkDraw.Shape(this, new[] { new Vector2(6, 6), new Vector2(s.X - 6, 6), new Vector2(s.X - 6, s.Y - 6), new Vector2(6, s.Y - 6) }, new Color(0.8f, 0.72f, 0.56f), 2.5f, 81);
            // Rings of the descent, nested ellipses getting darker toward the bottom.
            for (int i = 0; i < 7; i++)
            {
                float t = i / 6.0f;
                Vector2 c = new(s.X * 0.5f, s.Y * (0.42f + 0.06f * t));
                float rx = s.X * (0.42f - 0.05f * i);
                float ry = rx * 0.32f;
                List<Vector2> ring = new();
                for (int k = 0; k <= 40; k++)
                {
                    float a = k * Mathf.Tau / 40.0f;
                    ring.Add(c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry));
                }
                DrawPolyline(InkDraw.Wobble(ring, 1.2f, 90 + i), new Color(UiStyle.Ink, 0.35f + 0.5f * t), 2.0f, true);
            }
            // The spire rising from the deepest ring.
            Vector2 b = new(s.X * 0.5f, s.Y * 0.8f);
            InkDraw.Shape(this, new[] { b + new Vector2(-24, 0), b + new Vector2(-6, -s.Y * 0.62f), b + new Vector2(0, -s.Y * 0.7f), b + new Vector2(6, -s.Y * 0.62f), b + new Vector2(24, 0) }, new Color(0.3f, 0.24f, 0.18f), 2.5f, 97);
            for (int i = 1; i < 6; i++)
            {
                float y = b.Y - i * s.Y * 0.1f;
                DrawLine(new Vector2(b.X - 20 + i * 3, y), new Vector2(b.X + 20 - i * 3, y), new Color(UiStyle.Paper, 0.6f), 1.5f);
            }
            DrawString(UiStyle.Heading, new Vector2(20, s.Y - 18), "the descent", HorizontalAlignment.Left, -1, 22, UiStyle.InkSoft);
        }
    }
}
