using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// Subtitles for everything said aloud (GameEvents.SpeechStarted): a strip of parchment with the
/// speaker's name and words, written on the side of the screen the voice comes from. A voice
/// straight ahead is captioned low in the middle; one to the left, on the left; one behind, low
/// at the side it's nearer, and an inked arrow points toward a speaker who is off to the side or
/// behind. The newest line sits at its spot, older ones above it; each stays as long as it is
/// spoken, then fades. Up to MaxLines at once.
/// </summary>
public partial class Subtitles : Control
{
    [Export] public int MaxLines { get; set; } = 3;
    [Export] public float MaxWidth { get; set; } = 520.0f;
    /// <summary>How far toward the side a voice from the side is written, as a share of the screen's width.</summary>
    [Export] public float SideReach { get; set; } = 0.33f;
    /// <summary>Height of a caption for a voice straight ahead, as a share of the screen's height.</summary>
    [Export] public float BaseHeight { get; set; } = 0.72f;
    /// <summary>An arrow points at the speaker when they are more than this far off the view's centre, in degrees.</summary>
    [Export] public float ArrowFrom { get; set; } = 40.0f;
    [Export] public float Linger { get; set; } = 0.6f;

    private sealed class Line
    {
        public SpeechLine Speech;
        public float Left;
        public ColorRect? Paper;
        public float Angle;
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
    }

    public override void _ExitTree()
    {
        GameEvents.SpeechStarted -= OnSpeech;
        GameEvents.RespawnRequested -= Clear;
    }

    private void OnSpeech(SpeechLine speech)
    {
        if (speech.Text.Length == 0)
        {
            return;
        }
        Vector2 textSize = UiStyle.Hand.GetMultilineStringSize(speech.Text, HorizontalAlignment.Left, MaxWidth - 36.0f, UiStyle.TextSize);
        Vector2 size = new(Mathf.Min(MaxWidth, textSize.X + 36.0f), textSize.Y + 44.0f);
        ColorRect paper = new() { Size = size, MouseFilter = MouseFilterEnum.Ignore };
        paper.Material = UiStyle.Parchment(size, _random.Randf() * 40.0f, 2.5f, 3.0f);
        Label name = UiStyle.MakeLabel(speech.Speaker, UiStyle.SmallSize, true, UiStyle.InkSoft);
        name.Position = new Vector2(16, 4);
        paper.AddChild(name);
        Label text = UiStyle.MakeLabel(speech.Text, UiStyle.TextSize);
        text.Position = new Vector2(16, 26);
        text.Size = new Vector2(size.X - 32.0f, textSize.Y + 8.0f);
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        paper.AddChild(text);
        paper.Modulate = new Color(1, 1, 1, 0);
        AddChild(paper);
        paper.CreateTween().TweenProperty(paper, "modulate:a", 1.0f, 0.2);
        _lines.Add(new Line { Speech = speech, Left = speech.Duration + Linger, Paper = paper });
        while (_lines.Count > MaxLines)
        {
            _lines[0].Paper?.QueueFree();
            _lines.RemoveAt(0);
        }
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
        float x = screen.X * 0.5f + Mathf.Sin(angle) * screen.X * SideReach;
        float y = screen.Y * BaseHeight - Mathf.Cos(angle) * screen.Y * 0.06f;
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
