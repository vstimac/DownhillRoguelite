using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The conversation page (Enter): talk with the companion (M11). The exchange is written down the
/// page in ink, the carrier's words in a softer hand and the companion's reply appearing as it is
/// written; a line at the bottom takes the question (Enter sends, Esc closes). A note under the
/// title says whether the companion is listening, waking, thinking or silent (no model found).
/// Presentation only: it sends questions through GameEvents.RequestAsk and shows CompanionReplied.
/// </summary>
public partial class TalkScreen : ParchmentPanel
{
    /// <summary>How many exchanges the page keeps written down.</summary>
    [Export] public int KeepLines { get; set; } = 12;

    public string Transcript => _text?.GetParsedText() ?? "";
    public LineEdit? Input => _input;

    private readonly List<(bool Companion, string Text)> _lines = new();
    private RichTextLabel? _text;
    private LineEdit? _input;
    private Label? _status;
    private bool _wasOpen;
    private bool _streaming;

    public override void _Ready()
    {
        Title = "Conversation";
        Closable = true;
        Seed = 23.0f;
        Size = new Vector2(660, 480);
        base._Ready();
        _status = UiStyle.MakeLabel("", UiStyle.SmallSize, false, UiStyle.InkSoft);
        _status.Position = new Vector2(28, 56);
        AddChild(_status);
        _text = new RichTextLabel
        {
            Position = new Vector2(28, 86),
            Size = new Vector2(Size.X - 56, Size.Y - 170),
            BbcodeEnabled = true,
            ScrollFollowing = true,
            MouseFilter = MouseFilterEnum.Pass,
        };
        _text.AddThemeFontOverride("normal_font", UiStyle.Hand);
        _text.AddThemeFontOverride("italics_font", UiStyle.Hand);
        _text.AddThemeFontSizeOverride("normal_font_size", UiStyle.TextSize);
        _text.AddThemeFontSizeOverride("italics_font_size", UiStyle.TextSize);
        _text.AddThemeColorOverride("default_color", UiStyle.Ink);
        AddChild(_text);
        _input = new LineEdit
        {
            Position = new Vector2(28, Size.Y - 70),
            Size = new Vector2(Size.X - 56, 40),
            PlaceholderText = "Say something to the companion...",
            MaxLength = 240,
            ContextMenuEnabled = false,
        };
        StyleBoxFlat line = new() { BgColor = new Color(1, 1, 1, 0.12f), BorderColor = UiStyle.InkSoft, BorderWidthBottom = 2 };
        line.ContentMarginLeft = 8;
        _input.AddThemeStyleboxOverride("normal", line);
        _input.AddThemeStyleboxOverride("focus", line);
        _input.AddThemeFontOverride("font", UiStyle.Hand);
        _input.AddThemeFontSizeOverride("font_size", UiStyle.TextSize);
        _input.AddThemeColorOverride("font_color", UiStyle.Ink);
        _input.AddThemeColorOverride("font_placeholder_color", UiStyle.InkSoft);
        _input.AddThemeColorOverride("caret_color", UiStyle.Ink);
        _input.TextSubmitted += Send;
        AddChild(_input);
        GameEvents.CompanionReplied += OnReply;
        GameEvents.CompanionChanged += OnState;
        GameEvents.RespawnRequested += OnNewRun;
        OnState(GameEvents.Companion);
        SetOpenNow(false);
    }

    public override void _ExitTree()
    {
        GameEvents.CompanionReplied -= OnReply;
        GameEvents.CompanionChanged -= OnState;
        GameEvents.RespawnRequested -= OnNewRun;
        if (GameEvents.IsConversing)
        {
            GameEvents.PublishConversation(false);
        }
    }

    public override void _Process(double delta)
    {
        bool open = Visible && IsOpen;
        if (open == _wasOpen)
        {
            return;
        }
        _wasOpen = open;
        GameEvents.PublishConversation(open);
        if (open)
        {
            _input?.CallDeferred(Control.MethodName.GrabFocus);
        }
        else
        {
            _input?.ReleaseFocus();
        }
    }

    /// <summary>Sends what's typed (Enter); also used by tests.</summary>
    public void Send(string text)
    {
        string question = text.StripEdges();
        if (_input != null)
        {
            _input.Text = "";
        }
        if (question.Length == 0)
        {
            return;
        }
        Add(false, question);
        _streaming = false;
        GameEvents.RequestAsk(question);
    }

    private void OnReply(string text, bool done)
    {
        if (!_streaming)
        {
            Add(true, text);
            _streaming = true;
        }
        else if (_lines.Count > 0)
        {
            _lines[^1] = (true, text);
            Redraw();
        }
        if (done)
        {
            _streaming = false;
        }
    }

    private void Add(bool companion, string text)
    {
        _lines.Add((companion, text));
        while (_lines.Count > KeepLines * 2)
        {
            _lines.RemoveAt(0);
        }
        Redraw();
    }

    private void Redraw()
    {
        if (_text == null)
        {
            return;
        }
        List<string> parts = new();
        foreach ((bool companion, string text) in _lines)
        {
            string safe = text.Replace("[", "[lb]");
            if (companion)
            {
                parts.Add(safe);
            }
            else
            {
                parts.Add($"[color=#{UiStyle.InkSoft.ToHtml()}][i]- {safe}[/i][/color]");
            }
        }
        _text.Text = string.Join("\n\n", parts);
    }

    private void OnState(CompanionState state)
    {
        if (_status == null)
        {
            return;
        }
        _status.Text = state switch
        {
            CompanionState.Asleep => "The companion walks beside you, quiet. Speak, and they'll answer.",
            CompanionState.Waking => "The companion gathers their thoughts...",
            CompanionState.Listening => "The companion is listening.",
            CompanionState.Thinking => "The companion is thinking...",
            _ => "The companion is silent (no voice model found: see the README).",
        };
        if (_input != null)
        {
            _input.Editable = state != CompanionState.Thinking && state != CompanionState.Waking;
        }
    }

    private void OnNewRun()
    {
        _lines.Clear();
        _streaming = false;
        Redraw();
    }
}
