using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// Subtitles for everything said aloud (GameEvents.SpeechStarted): a strip of parchment with the
/// speaker's name and words, in a band low on the screen (clear of the carrier and above the
/// hotbar), leaning to the side the voice comes from: a voice ahead in the middle, one to the left
/// on the left, and an inked arrow points toward a speaker who is off to the side or
/// behind. The newest line sits at its spot, older ones above it; each stays as long as it is
/// spoken, then fades. Up to MaxLines at once. Each strip is a fixed window fitted to its rows,
/// broken here rather than by the label; long speech turns pages of MaxRows rows.
/// </summary>
public partial class Subtitles : Control
{
    [Export] public int MaxLines { get; set; } = 3;
    [Export] public float MaxWidth { get; set; } = 520.0f;
    /// <summary>How far toward the side a voice from the side is written, as a share of the screen's width.</summary>
    [Export] public float SideReach { get; set; } = 0.33f;
    /// <summary>Captions sit in a band this far above the bottom of the screen (clear of the hotbar), in px.</summary>
    [Export] public float BottomMargin { get; set; } = 112.0f;
    /// <summary>A voice from behind is written this much lower than one ahead, in px (still above BottomMargin's band).</summary>
    [Export] public float BehindDrop { get; set; } = 0.0f;
    /// <summary>Padding inside the strip, in px (sides, top, bottom).</summary>
    [Export] public Vector2 Padding { get; set; } = new(14.0f, 6.0f);
    /// <summary>An arrow points at the speaker when they are more than this far off the view's centre, in degrees.</summary>
    [Export] public float ArrowFrom { get; set; } = 40.0f;
    [Export] public float Linger { get; set; } = 0.6f;
    /// <summary>Rows of words per caption; longer speech turns pages, timed to the voice.</summary>
    [Export] public int MaxRows { get; set; } = 3;
    /// <summary>The strip is never wider than this share of the screen.</summary>
    [Export] public float MaxScreenShare { get; set; } = 0.46f;

    private sealed class Line
    {
        public SpeechLine Speech;
        public float Left;
        public ColorRect? Paper;
        public Label? Words;
        public float Angle;
        /// <summary>The words broken into pages of at most MaxRows rows (explicit line breaks).</summary>
        public List<string> Pages = new();
        public List<float> PageTimes = new();
        public int Page;
        public float PageLeft;
    }

    public int Count => _lines.Count;
    public string NewestText
    {
        get
        {
            if (_lines.Count > 0)
            {
                return _lines[^1].Speech.Text;
            }
            return "";
        }
    }

    private readonly List<Line> _lines = new();
    private readonly RandomNumberGenerator _random = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _random.Seed = 11;
        GameEvents.SpeechStarted += OnSpeech;
        GameEvents.RespawnRequested += Clear;
        GameOptions.Changed += ApplyOptions;
        ApplyOptions();
    }

    /// <summary>Options → Accessibility → Subtitles hides the captions (lines are still kept, so turning them on mid-line shows it).</summary>
    private void ApplyOptions()
    {
        Visible = GameOptions.Subtitles;
    }

    public override void _ExitTree()
    {
        GameEvents.SpeechStarted -= OnSpeech;
        GameEvents.RespawnRequested -= Clear;
        GameOptions.Changed -= ApplyOptions;
    }

    private void OnSpeech(SpeechLine speech)
    {
        if (speech.Text.Length == 0)
        {
            return;
        }
        // A fixed window fitted to the words: the rows are broken here (explicit line breaks, so the
        // label never wraps differently from what was measured); the strip is as wide as its widest
        // row and as tall as its rows; long speech turns pages of MaxRows rows, timed to the voice.
        float maxWidth = Mathf.Min(MaxWidth, Mathf.Max(240.0f, GetViewportRect().Size.X * MaxScreenShare));
        float wrapWidth = maxWidth - Padding.X * 2.0f - 6.0f;
        List<string> rows = Rows(speech.Text, wrapWidth, out float widest);
        Line line = new() { Speech = speech, Left = speech.Duration + Linger };
        int rowsPerPage = Mathf.Max(1, MaxRows);
        for (int i = 0; i < rows.Count; i += rowsPerPage)
        {
            line.Pages.Add(string.Join("\n", rows.GetRange(i, Mathf.Min(rowsPerPage, rows.Count - i))));
        }
        float spoken = Mathf.Max(speech.Duration, 0.5f);
        foreach (string page in line.Pages)
        {
            line.PageTimes.Add(spoken * page.Length / Mathf.Max(1, speech.Text.Length));
        }
        line.PageLeft = line.PageTimes[0];
        int shownRows = Mathf.Min(rows.Count, rowsPerPage);
        float rowHeight = UiStyle.Hand.GetHeight(UiStyle.TextSize) + RowSpacing;
        float nameHeight = UiStyle.Heading.GetHeight(UiStyle.SmallSize);
        float nameWidth = UiStyle.Heading.GetStringSize(speech.Speaker, HorizontalAlignment.Left, -1, UiStyle.SmallSize).X;
        Vector2 size = new(Mathf.Ceil(Mathf.Max(widest, nameWidth)) + Padding.X * 2.0f + 6.0f, Mathf.Ceil(Padding.Y * 2.0f + nameHeight + shownRows * rowHeight + 2.0f));
        ColorRect paper = new() { Size = size, MouseFilter = MouseFilterEnum.Ignore, ClipContents = true };
        paper.Material = UiStyle.Parchment(size, _random.Randf() * 40.0f, 2.5f, 3.0f);
        Label name = UiStyle.MakeLabel(speech.Speaker, UiStyle.SmallSize, true, UiStyle.InkSoft);
        name.Position = new Vector2(Padding.X, Padding.Y - 2.0f);
        paper.AddChild(name);
        Label text = UiStyle.MakeLabel(line.Pages[0], UiStyle.TextSize);
        text.AddThemeConstantOverride("line_spacing", (int)RowSpacing);
        text.Position = new Vector2(Padding.X, Padding.Y + nameHeight - 2.0f);
        text.AutowrapMode = TextServer.AutowrapMode.Off;
        text.Size = new Vector2(size.X - Padding.X * 2.0f, shownRows * rowHeight + 2.0f);
        paper.AddChild(text);
        paper.Modulate = new Color(1, 1, 1, 0);
        AddChild(paper);
        paper.CreateTween().TweenProperty(paper, "modulate:a", 1.0f, 0.2);
        line.Paper = paper;
        line.Words = text;
        _lines.Add(line);
        while (_lines.Count > MaxLines)
        {
            _lines[0].Paper?.QueueFree();
            _lines.RemoveAt(0);
        }
    }

    /// <summary>Space between rows of words, in px.</summary>
    private const float RowSpacing = 2.0f;

    /// <summary>The words broken into rows no wider than <paramref name="width"/>; <paramref name="widest"/> is the widest row.</summary>
    public static List<string> Rows(string text, float width, out float widest)
    {
        List<string> rows = new();
        widest = 0.0f;
        TextParagraph paragraph = new() { Width = width, BreakFlags = TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Adaptive };
        paragraph.AddString(text, UiStyle.Hand, UiStyle.TextSize);
        for (int i = 0; i < paragraph.GetLineCount(); i++)
        {
            Vector2I range = paragraph.GetLineRange(i);
            int start = Mathf.Clamp(range.X, 0, text.Length);
            int end = Mathf.Clamp(range.Y, start, text.Length);
            string row = text.Substring(start, end - start).Trim();
            if (row.Length == 0)
            {
                continue;
            }
            rows.Add(row);
            widest = Mathf.Max(widest, UiStyle.Hand.GetStringSize(row, HorizontalAlignment.Left, -1, UiStyle.TextSize).X);
        }
        if (rows.Count == 0)
        {
            rows.Add(text);
            widest = UiStyle.Hand.GetStringSize(text, HorizontalAlignment.Left, -1, UiStyle.TextSize).X;
        }
        return rows;
    }

    private void Clear()
    {
        foreach (Line line in _lines)
        {
            line.Paper?.QueueFree();
        }
        _lines.Clear();
        QueueRedraw();
    }

    /// <summary>Angle of a world point from the view's forward direction (radians, + to the right, ±π behind).</summary>
    public float AngleTo(Vector3 from)
    {
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera == null)
        {
            return 0.0f;
        }
        Vector3 to = from - camera.GlobalPosition;
        to.Y = 0.0f;
        Vector3 forward = -camera.GlobalBasis.Z;
        forward.Y = 0.0f;
        if (to.IsZeroApprox() || forward.IsZeroApprox())
        {
            return 0.0f;
        }
        forward = forward.Normalized();
        Vector3 right = forward.Cross(Vector3.Up);
        to = to.Normalized();
        return Mathf.Atan2(to.Dot(right), to.Dot(forward));
    }

    /// <summary>Where the centre of a caption for a voice at this angle goes on a screen of this size.</summary>
    public Vector2 AnchorFor(float angle, Vector2 screen)
    {
        // Low on the screen, clear of the carrier in the middle and of the hotbar below; leaning to the voice's side.
        float x = screen.X * 0.5f + Mathf.Sin(angle) * screen.X * SideReach;
        float y = screen.Y - BottomMargin + (1.0f - Mathf.Cos(angle)) * 0.5f * BehindDrop;
        return new Vector2(x, y);
    }

    public override void _Process(double delta)
    {
        if (_lines.Count == 0)
        {
            return;
        }
        // The screen itself, not this control's size (it can be zero inside the HUD's layers).
        Vector2 screen = GetViewportRect().Size;
        float stackY = 0.0f;
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            Line line = _lines[i];
            line.Left -= (float)delta;
            if (line.Left <= 0.0f || line.Paper == null)
            {
                line.Paper?.QueueFree();
                _lines.RemoveAt(i);
                continue;
            }
            ColorRect paper = line.Paper;
            // Long speech: the next page of rows when this one has been said.
            line.PageLeft -= (float)delta;
            if (line.PageLeft <= 0.0f && line.Page + 1 < line.Pages.Count && line.Words != null)
            {
                line.Page++;
                line.Words.Text = line.Pages[line.Page];
                line.PageLeft = line.PageTimes[line.Page];
            }
            if (line.Left < 0.4f)
            {
                paper.Modulate = new Color(1, 1, 1, line.Left / 0.4f);
            }
            // A speaker who walks with the carrier keeps moving: follow them, not where they started.
            Vector3 from = line.Speech.From;
            if (line.Speech.Speaker == "Companion")
            {
                from = GameEvents.CompanionPosition;
            }
            line.Angle = AngleTo(from);
            Vector2 anchor = AnchorFor(line.Angle, screen);
            Vector2 at = anchor - new Vector2(paper.Size.X * 0.5f, paper.Size.Y) - new Vector2(0.0f, stackY);
            at.X = Mathf.Clamp(at.X, 12.0f, Mathf.Max(12.0f, screen.X - paper.Size.X - 12.0f));
            at.Y = Mathf.Clamp(at.Y, 12.0f, Mathf.Max(12.0f, screen.Y - paper.Size.Y - 12.0f));
            paper.Position = at;
            stackY += paper.Size.Y + 8.0f;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_lines.Count == 0)
        {
            return;
        }
        // One arrow, by the newest caption, toward a speaker off to the side or behind.
        Line newest = _lines[^1];
        if (newest.Paper == null || Mathf.Abs(newest.Angle) < Mathf.DegToRad(ArrowFrom))
        {
            return;
        }
        Rect2 box = new(newest.Paper.Position, newest.Paper.Size);
        Vector2 direction = new(Mathf.Sin(newest.Angle), -Mathf.Cos(newest.Angle));
        Vector2 start = box.GetCenter() + new Vector2(Mathf.Sign(direction.X) * (box.Size.X * 0.5f + 10.0f), 0.0f);
        Vector2 tip = start + direction * 34.0f;
        Color ink = new(UiStyle.Ink, newest.Paper.Modulate.A);
        DrawLine(start, tip, ink, 3.5f, true);
        Vector2 back = -direction * 12.0f;
        Vector2 side = new Vector2(-direction.Y, direction.X) * 8.0f;
        DrawColoredPolygon(new[] { tip + direction * 4.0f, tip + back + side, tip + back - side }, ink);
    }
}
