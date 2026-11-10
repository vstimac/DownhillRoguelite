using System;
using DownhillRoguelite.Core;
using DownhillRoguelite.Network;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// The main menu's Co-op page (beta): your nickname (remembered; others see it over your head) and
/// your carrier (Character… opens the character creator: preset and outfit colours, which the others
/// see too), then either Host (others join this computer on the port) or Join (the host's address
/// and port). Hosting starts the run at once; joining waits for
/// the host to let you in, then starts on the host's mountain. The status line follows the session
/// (connecting, refused, no answer). Back, Esc or B returns.
/// </summary>
public partial class CoopPage : ParchmentPanel
{
    [Export] public Vector2 MaxSize { get; set; } = new(640.0f, 520.0f);
    [Export] public float Margin { get; set; } = 30.0f;
    [Export] public string DefaultAddress { get; set; } = "127.0.0.1";
    [Export(PropertyHint.MultilineText)] public string Explainer { get; set; } =
        "Co-op beta: up to four carriers on the same mountain. One hosts; the others join with the host's address " +
        "(on a home network, or over the internet through a forwarded port). Fall, and you come back at the last station.";

    public event Action? BackPressed;
    /// <summary>The session started (hosting) or the host let this player in: the run can begin.</summary>
    public event Action? RunReady;
    /// <summary>Character… was pressed: the start screen opens the character creator (and comes back here).</summary>
    public event Action? CharacterRequested;

    public LineEdit? NameField => _name;
    public LineEdit? AddressField => _address;
    public LineEdit? PortField => _port;
    public Label? StatusLabel => _status;

    private LineEdit? _name;
    private LineEdit? _address;
    private LineEdit? _port;
    private Label? _status;
    private Label? _explainer;
    private VBoxContainer? _column;
    private InkButton? _host;
    private InkButton? _join;
    private InkButton? _back;
    private InkButton? _character;
    private Label? _playingAs;
    private ColorRect? _shirtSwatch;
    private ColorRect? _packSwatch;
    private NetSession? _session;

    public CoopPage()
    {
        Title = "Co-op (beta)";
        Seed = 47.0f;
    }

    public override void _Ready()
    {
        base._Ready();
        _column = new VBoxContainer();
        _column.AddThemeConstantOverride("separation", 8);
        AddChild(_column);
        _explainer = UiStyle.MakeLabel(Explainer, UiStyle.TextSize - 2, false, new Color(UiStyle.Ink, 0.85f));
        _explainer.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _column.AddChild(_explainer);
        _name = Field("Nickname (the others see it over your head)", GameOptions.NetName);
        _name.PlaceholderText = "Your nickname";
        _name.MaxLength = 20;
        HBoxContainer look = new();
        look.AddThemeConstantOverride("separation", 10);
        _playingAs = UiStyle.MakeLabel("", UiStyle.TextSize, false, UiStyle.Ink);
        _playingAs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _playingAs.VerticalAlignment = VerticalAlignment.Center;
        look.AddChild(_playingAs);
        _shirtSwatch = Swatch();
        look.AddChild(_shirtSwatch);
        _packSwatch = Swatch();
        look.AddChild(_packSwatch);
        _character = Button("Character…");
        _character.CustomMinimumSize = new Vector2(160.0f, 42.0f);
        _character.Pressed += () =>
        {
            SaveName();
            CharacterRequested?.Invoke();
        };
        look.AddChild(_character);
        _column.AddChild(look);
        _address = Field("Host's address (to join)", DefaultAddress);
        _port = Field("Port", "7777");
        HBoxContainer buttons = new() { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 16);
        _host = Button("Host");
        _host.Pressed += OnHost;
        buttons.AddChild(_host);
        _join = Button("Join");
        _join.Pressed += OnJoin;
        buttons.AddChild(_join);
        _column.AddChild(buttons);
        _status = UiStyle.MakeLabel("", UiStyle.TextSize - 2, false, UiStyle.Warning);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _column.AddChild(_status);
        _back = Button("Back");
        _back.Pressed += () => BackPressed?.Invoke();
        AddChild(_back);
        _session = NetSession.Instance;
        if (_session != null)
        {
            _session.Changed += ShowStatus;
            _session.Welcomed += OnWelcomed;
            if (_session.DefaultPort > 0)
            {
                _port.Text = _session.DefaultPort.ToString();
            }
        }
        Resized += LayoutContent;
        LayoutContent();
    }

    public override void _ExitTree()
    {
        if (_session != null)
        {
            _session.Changed -= ShowStatus;
            _session.Welcomed -= OnWelcomed;
        }
    }

    private LineEdit Field(string label, string text)
    {
        _column?.AddChild(UiStyle.MakeLabel(label, UiStyle.SmallSize + 2, false, UiStyle.Ink));
        LineEdit field = new() { Text = text, MaxLength = 64, CustomMinimumSize = new Vector2(0.0f, 36.0f) };
        field.AddThemeFontOverride("font", UiStyle.Hand);
        field.AddThemeFontSizeOverride("font_size", UiStyle.TextSize);
        _column?.AddChild(field);
        return field;
    }

    private static ColorRect Swatch()
    {
        return new ColorRect { CustomMinimumSize = new Vector2(26.0f, 26.0f), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
    }

    /// <summary>Shows the chosen carrier and colours (after the creator, or on opening).</summary>
    private void ShowLook()
    {
        string name = CarrierRoster.Load()?.Pick(GameOptions.Character)?.DisplayName ?? GameOptions.Character;
        if (_playingAs != null)
        {
            _playingAs.Text = $"Playing as {name}";
        }
        if (_shirtSwatch != null)
        {
            _shirtSwatch.Color = GameOptions.BaseShirtColour;
        }
        if (_packSwatch != null)
        {
            _packSwatch.Color = GameOptions.BasePackColour;
        }
    }

    /// <summary>The nickname typed, remembered for next time; empty (and a word in the status line) if none.</summary>
    private string Nickname()
    {
        string name = (_name?.Text ?? "").Trim();
        if (name.Length == 0)
        {
            if (_status != null)
            {
                _status.Text = "Choose a nickname first: the others see it over your head.";
            }
            _name?.GrabFocus();
            return "";
        }
        SaveName();
        return name;
    }

    private void SaveName()
    {
        string name = (_name?.Text ?? "").Trim();
        if (name.Length > 0 && name != GameOptions.NetName)
        {
            GameOptions.SetNetName(name);
        }
    }

    private static InkButton Button(string text)
    {
        return new InkButton { Text = text, Paper = true, Focusable = true, FontSize = UiStyle.TextSize + 2, CustomMinimumSize = new Vector2(130.0f, 42.0f) };
    }

    private int Port()
    {
        if (_port != null && int.TryParse(_port.Text.Trim(), out int port) && port > 0 && port < 65536)
        {
            return port;
        }
        return _session?.DefaultPort ?? 7777;
    }

    private void OnHost()
    {
        if (_session == null)
        {
            return;
        }
        string name = Nickname();
        if (name.Length == 0)
        {
            return;
        }
        if (_session.Host(name, Port()))
        {
            RunReady?.Invoke();
        }
    }

    private void OnJoin()
    {
        string name = Nickname();
        if (name.Length == 0)
        {
            return;
        }
        _session?.Join(name, _address?.Text ?? DefaultAddress, Port());
    }

    private void OnWelcomed(ulong seed)
    {
        if (IsOpen)
        {
            RunReady?.Invoke();
        }
    }

    private void ShowStatus()
    {
        if (_status != null && _session != null)
        {
            _status.Text = _session.Status;
            if (!_session.Active && _session.Status == "Not connected")
            {
                _status.Text = "";
            }
        }
    }

    public void FitInto(Rect2 area)
    {
        Vector2 size = new(Mathf.Min(MaxSize.X, area.Size.X), Mathf.Min(MaxSize.Y, area.Size.Y));
        Position = area.Position + (area.Size - size) * 0.5f;
        Size = size;
        LayoutContent();
    }

    private void LayoutContent()
    {
        if (_column == null || _back == null)
        {
            return;
        }
        float top = HeaderHeight + 8.0f;
        _column.Position = new Vector2(Margin, top);
        _column.Size = new Vector2(Size.X - Margin * 2.0f, Mathf.Max(Size.Y - top - 74.0f, 40.0f));
        _back.Position = new Vector2((Size.X - 130.0f) * 0.5f, Size.Y - 60.0f);
        _back.Size = new Vector2(130.0f, 42.0f);
    }

    public void OpenSheet()
    {
        ShowStatus();
        ShowLook();
        Open();
        _name?.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || !IsOpen || !IsVisibleInTree())
        {
            return;
        }
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            BackPressed?.Invoke();
        }
    }
}
