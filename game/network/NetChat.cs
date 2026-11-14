using System.Collections.Generic;
using System.Text;
using DownhillRoguelite.Core;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>
/// Co-op chat (a child of NetSession): Y opens a line at the bottom left; Enter sends it to the whole
/// party, Esc (or Y on an empty line) closes it. The last few lines stay on screen for a while, and
/// all of them while the line is open. While typing, the carrier stands still (no walking or leaning
/// on the letters you type).
///
/// Network: a client sends its line to the host, which checks it (a player in the session, not too
/// long, not too often) and passes it to everyone else with the player's name.
/// </summary>
public partial class NetChat : CanvasLayer
{
    [Export] public Key OpenKey { get; set; } = Key.Y;
    [Export] public int MaxLength { get; set; } = 120;
    /// <summary>At most this many lines are kept (and shown while typing).</summary>
    [Export] public int KeepLines { get; set; } = 8;
    /// <summary>A line stays on screen this long, in s (the last FadeSeconds it fades).</summary>
    [Export] public float LineSeconds { get; set; } = 14.0f;
    [Export] public float FadeSeconds { get; set; } = 2.0f;
    /// <summary>A player can send at most one line this often, in s (the host drops anything faster).</summary>
    [Export] public float Cooldown { get; set; } = 0.4f;
    [Export] public Vector2 Corner { get; set; } = new(18.0f, 190.0f);
    [Export] public float Width { get; set; } = 440.0f;
    [Export] public Color OwnColour { get; set; } = new(0.95f, 0.8f, 0.4f);
    [Export] public Color OthersColour { get; set; } = new(0.86f, 0.79f, 0.64f);
    [Export] public string FirstHint { get; set; } = "{0} says something. Press Y to answer the party.";

    public NetSession? Session { get; set; }
    public bool IsOpen => _open;
    /// <summary>Lines this machine sent, and lines from others shown here (tests).</summary>
    public int Sent { get; private set; }
    public int Seen { get; private set; }

    private sealed class Line
    {
        public string Text = "";
        public bool Own;
        public double At;
    }

    private readonly List<Line> _lines = new();
    private readonly Dictionary<long, double> _lastFrom = new();
    private VBoxContainer? _box;
    private RichTextLabel? _log;
    private LineEdit? _input;
    private bool _open;
    private double _lastSent = -100.0;
    private bool _toldFirst;

    private bool Live => Session != null && Session.Active && Session.Joined;

    private static double Now => Time.GetTicksMsec() / 1000.0;

    public override void _Ready()
    {
        Layer = 6;
        ProcessMode = ProcessModeEnum.Always;
        _box = new VBoxContainer { Name = "Chat", MouseFilter = Control.MouseFilterEnum.Ignore };
        _box.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _box.GrowVertical = Control.GrowDirection.Begin;
        _box.OffsetLeft = Corner.X;
        _box.OffsetRight = Corner.X + Width;
        _box.OffsetBottom = -Corner.Y;
        _box.OffsetTop = -Corner.Y;
        AddChild(_box);
        _log = new RichTextLabel
        {
            Name = "Log",
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(Width, 0.0f),
        };
        _log.AddThemeFontSizeOverride("normal_font_size", UiStyle.SmallSize);
        _log.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        _log.AddThemeConstantOverride("shadow_offset_x", 2);
        _log.AddThemeConstantOverride("shadow_offset_y", 2);
        _box.AddChild(_log);
        StyleBoxFlat back = new()
        {
            BgColor = new Color(0.08f, 0.06f, 0.04f, 0.72f),
            BorderColor = new Color(UiStyle.Paper, 0.5f),
            ContentMarginLeft = 8.0f,
            ContentMarginRight = 8.0f,
            ContentMarginTop = 4.0f,
            ContentMarginBottom = 4.0f,
        };
        back.SetBorderWidthAll(1);
        back.SetCornerRadiusAll(3);
        _input = new LineEdit
        {
            Name = "Input",
            MaxLength = MaxLength,
            PlaceholderText = "Say to the party (Enter sends, Esc closes)",
            Visible = false,
            CustomMinimumSize = new Vector2(Width, 0.0f),
            ContextMenuEnabled = false,
        };
        _input.AddThemeStyleboxOverride("normal", back);
        _input.AddThemeStyleboxOverride("focus", back);
        _input.AddThemeColorOverride("font_color", UiStyle.Paper);
        _input.AddThemeFontSizeOverride("font_size", UiStyle.SmallSize);
        _input.TextSubmitted += OnSubmitted;
        _input.GuiInput += OnInputKey;
        _box.AddChild(_input);
        Visible = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_open || @event is not InputEventKey key || !key.Pressed || key.Echo || key.PhysicalKeycode != OpenKey)
        {
            return;
        }
        if (!Live || key.AltPressed || key.CtrlPressed || Input.MouseMode != Input.MouseModeEnum.Captured
            || GameEvents.IsScreenOpen || GameEvents.IsPacking || GameEvents.IsMapOpen || GameEvents.IsConversing || GetTree().Paused)
        {
            return;
        }
        Open();
        GetViewport().SetInputAsHandled();
    }

    private void OnInputKey(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            Close();
            _input?.AcceptEvent();
        }
    }

    public void Open()
    {
        if (_open || _input == null)
        {
            return;
        }
        _open = true;
        _input.Text = "";
        _input.Visible = true;
        _input.CallDeferred(Control.MethodName.GrabFocus);
        GameEvents.PublishScreenOpen(true);
        // The letters typed mustn't walk or lean the carrier.
        Session?.LockControls("chat", true);
        Refresh();
    }

    public void Close()
    {
        if (!_open || _input == null)
        {
            return;
        }
        _open = false;
        _input.ReleaseFocus();
        _input.Visible = false;
        GameEvents.PublishScreenOpen(false);
        Session?.LockControls("chat", false);
        Refresh();
    }

    private void OnSubmitted(string text)
    {
        Say(text);
        Close();
    }

    /// <summary>Sends a line to the party (the chat line, or the co-op probe).</summary>
    public void Say(string text)
    {
        text = Clean(text);
        if (!Live || text.Length == 0 || Now - _lastSent < Cooldown)
        {
            return;
        }
        _lastSent = Now;
        Sent++;
        NetSession session = Session!;
        Add(session.LocalName, text, true);
        if (session.Mode == NetSession.Role.Host)
        {
            foreach (long id in session.Peers.Keys)
            {
                RpcId(id, MethodName.PeerSaid, session.LocalName, text);
            }
        }
        else
        {
            RpcId(1, MethodName.Said, text);
        }
    }

    /// <summary>Host: a client said something; shown here and passed to the others.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Said(string text)
    {
        long sender = Multiplayer.GetRemoteSenderId();
        NetSession? session = Session;
        if (session == null || session.Mode != NetSession.Role.Host || !session.Peers.TryGetValue(sender, out NetPeer? peer))
        {
            return;
        }
        text = Clean(text);
        if (text.Length == 0 || (_lastFrom.TryGetValue(sender, out double last) && Now - last < Cooldown))
        {
            return;
        }
        _lastFrom[sender] = Now;
        Add(peer.Name, text, false);
        foreach (long id in session.Peers.Keys)
        {
            if (id != sender)
            {
                RpcId(id, MethodName.PeerSaid, peer.Name, text);
            }
        }
    }

    /// <summary>Client: someone's line, from the host.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PeerSaid(string name, string text)
    {
        if (Session?.Mode != NetSession.Role.Client)
        {
            return;
        }
        text = Clean(text);
        name = Clean(name);
        if (name.Length > 24)
        {
            name = name.Substring(0, 24);
        }
        if (text.Length > 0)
        {
            Add(name, text, false);
        }
    }

    /// <summary>One line, no control characters, at most MaxLength long.</summary>
    private string Clean(string text)
    {
        StringBuilder clean = new();
        foreach (char c in text)
        {
            if (char.IsControl(c))
            {
                continue;
            }
            clean.Append(c);
            if (clean.Length >= MaxLength)
            {
                break;
            }
        }
        return clean.ToString().Trim();
    }

    private void Add(string name, string text, bool own)
    {
        if (!own)
        {
            Seen++;
            if (!_toldFirst)
            {
                _toldFirst = true;
                GameEvents.PublishHint(string.Format(FirstHint, name));
            }
        }
        // Shown as written: a "[" typed is a bracket, not markup.
        _lines.Add(new Line { Text = $"[b]{name.Replace("[", "[lb]")}:[/b] {text.Replace("[", "[lb]")}", Own = own, At = Now });
        while (_lines.Count > KeepLines)
        {
            _lines.RemoveAt(0);
        }
        GD.Print($"[Net] chat {name}: {text}");
        Refresh();
    }

    /// <summary>The session ended: the chat closes and forgets.</summary>
    public void Clear()
    {
        Close();
        _lines.Clear();
        _lastFrom.Clear();
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_open && (!Live || GetTree().Paused))
        {
            Close();
        }
        if (_lines.Count > 0 && !_open)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        if (_log == null)
        {
            return;
        }
        double now = Now;
        StringBuilder text = new();
        bool any = false;
        foreach (Line line in _lines)
        {
            float alpha = 1.0f;
            if (!_open)
            {
                double left = line.At + LineSeconds - now;
                if (left <= 0.0)
                {
                    continue;
                }
                alpha = Mathf.Clamp((float)left / Mathf.Max(FadeSeconds, 0.01f), 0.0f, 1.0f);
            }
            Color colour = OthersColour;
            if (line.Own)
            {
                colour = OwnColour;
            }
            colour.A = alpha;
            if (any)
            {
                text.Append('\n');
            }
            text.Append($"[color=#{colour.ToHtml(true)}]{line.Text}[/color]");
            any = true;
        }
        string shown = text.ToString();
        if (_log.Text != shown)
        {
            _log.Text = shown;
        }
        Visible = Live && (any || _open);
    }
}
