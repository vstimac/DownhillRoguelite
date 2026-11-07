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

    public SpeakerInfo Speaker => _speaker;

    private List<(bool Companion, string Text)> _lines = new();
    private readonly Dictionary<string, List<(bool Companion, string Text)>> _transcripts = new();
    private SpeakerInfo _speaker = SpeakerInfo.Companion;
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
        _transcripts[SpeakerInfo.CompanionId] = _lines;
        GameEvents.CompanionReplied += OnReply;
        GameEvents.CompanionChanged += OnState;
        GameEvents.RespawnRequested += OnNewRun;
        GameEvents.TalkRequested += SetSpeaker;
        OnState(GameEvents.Companion);
        SetOpenNow(false);
    }

    public override void _ExitTree()
    {
        GameEvents.CompanionReplied -= OnReply;
        GameEvents.CompanionChanged -= OnState;
        GameEvents.RespawnRequested -= OnNewRun;
        GameEvents.TalkRequested -= SetSpeaker;
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

    /// <summary>Turns the page to the conversation with someone (each keeps its own transcript).</summary>
    public void SetSpeaker(SpeakerInfo speaker)
    {
        _speaker = speaker;
        if (!_transcripts.TryGetValue(speaker.Id, out List<(bool Companion, string Text)>? lines))
        {
            lines = new List<(bool Companion, string Text)>();
            _transcripts[speaker.Id] = lines;
        }
        _lines = lines;
        _streaming = false;
        if (_input != null)
        {
            _input.PlaceholderText = $"Say something to {SpeakerWord(false)}...";
        }
        Redraw();
        OnState(GameEvents.Companion);
    }

    /// <summary>"the companion" or the traveller's name; capitalised for the start of a sentence.</summary>
    private string SpeakerWord(bool capital)
    {
        if (!_speaker.IsCompanion)
        {
            return _speaker.Name;
        }
        if (capital)
        {
            return "The companion";
        }
        return "the companion";
    }

    private void OnReply(string text, bool done)
    {
        SpeakerInfo from = GameEvents.ReplySpeaker;
        if (from.Id != _speaker.Id)
        {
            // A reply from someone else (the page was turned meanwhile): keep it in their transcript.
            if (done && _transcripts.TryGetValue(from.Id, out List<(bool Companion, string Text)>? other))
            {
                other.Add((true, text));
            }
            return;
        }
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
        string who = SpeakerWord(true);
        string silent = "The companion is silent (no voice model found: see the README).";
        string asleep = "The companion walks beside you, quiet. Speak, and they'll answer.";
        if (!_speaker.IsCompanion)
        {
            silent = $"{who} answers in few words.";
            asleep = $"{who} looks up from the fire.";
        }
        _status.Text = state switch
        {
            CompanionState.Asleep => asleep,
            CompanionState.Waking => $"{who} gathers their thoughts...",
            CompanionState.Listening => $"{who} is listening.",
            CompanionState.Thinking => $"{who} is thinking...",
            _ => silent,
        };
        if (_input != null)
        {
            _input.Editable = state != CompanionState.Thinking && state != CompanionState.Waking;
        }
    }

    private void OnNewRun()
    {
        _transcripts.Clear();
        _lines = new List<(bool Companion, string Text)>();
        _transcripts[SpeakerInfo.CompanionId] = _lines;
        _speaker = SpeakerInfo.Companion;
        _streaming = false;
        Redraw();
    }
}
