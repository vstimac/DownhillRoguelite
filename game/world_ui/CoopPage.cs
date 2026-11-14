using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Network;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// The main menu's Co-op page (beta): your nickname (remembered; others see it over your head) and
/// your carrier (Character… opens the character creator), then a server browser: "Available
/// expeditions" lists the expeditions hosted on the home network (found by LAN discovery while the
/// page is open: name, address:port, players, Waiting / In progress, ping). Pick one and Join, or
/// type an address and port and use Direct Connect (over the internet, through a forwarded port).
/// Host Expedition starts the run at once with the host settings beside it (expedition name, room
/// for 2-4, shown on the home network or not). Copy Address copies the picked expedition's address,
/// or this computer's for friends to type. The status line follows the session (connecting, full,
/// another version, refused, no answer). Back, Esc or B returns.
/// </summary>
public partial class CoopPage : ParchmentPanel
{
    [Export] public Vector2 MaxSize { get; set; } = new(900.0f, 760.0f);
    [Export] public float Margin { get; set; } = 30.0f;
    [Export] public string DefaultAddress { get; set; } = "127.0.0.1";
    [Export] public float ListHeight { get; set; } = 210.0f;
    [Export] public float MinimumListHeight { get; set; } = 70.0f;
    [Export] public int MinPlayers { get; set; } = 2;
    [Export(PropertyHint.MultilineText)] public string Explainer { get; set; } =
        "Co-op beta: up to four carriers on the same mountain. Expeditions on your home network show up below; " +
        "for one over the internet, type the host's address and port (with the port forwarded) and use Direct Connect.";
    [Export(PropertyHint.MultilineText)] public string EmptyText { get; set; } =
        "No expeditions found on your network yet.\nHost one, or ask a friend to host; it appears here within a few seconds.";

    public event Action? BackPressed;
    /// <summary>The session started (hosting) or the host let this player in: the run can begin.</summary>
    public event Action? RunReady;
    /// <summary>Character… was pressed: the start screen opens the character creator (and comes back here).</summary>
    public event Action? CharacterRequested;

    public LineEdit? NameField => _name;
    public LineEdit? AddressField => _address;
    public LineEdit? PortField => _port;
    public Label? StatusLabel => _status;
    public LanScanner? Scanner => _scanner;

    private LineEdit? _name;
    private LineEdit? _serverName;
    private OptionButton? _maxPlayers;
    private CheckBox? _lanVisible;
    private LineEdit? _address;
    private LineEdit? _port;
    private Label? _status;
    private Label? _explainer;
    private Label? _found;
    private Label? _empty;
    private VBoxContainer? _column;
    private Control? _browserFrame;
    private VBoxContainer? _list;
    private ScrollContainer? _scroll;
    private InkButton? _back;
    private Label? _playingAs;
    private ColorRect? _shirtSwatch;
    private ColorRect? _packSwatch;
    private NetSession? _session;
    private LanScanner? _scanner;
    private readonly List<ExpeditionRow> _rows = new();
    private string _selectedId = "";

    public CoopPage()
    {
        Title = "Co-op (beta)";
        Seed = 47.0f;
    }

    public override void _Ready()
    {
        base._Ready();
        _session = NetSession.Instance;
        _column = new VBoxContainer();
        _column.AddThemeConstantOverride("separation", 6);
        AddChild(_column);
        _explainer = UiStyle.MakeLabel(Explainer, UiStyle.SmallSize, false, new Color(UiStyle.Ink, 0.85f));
        _explainer.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _column.AddChild(_explainer);

        // You: nickname and carrier.
        HBoxContainer you = Row(14);
        _name = Field(you, "Nickname (the others see it over your head)", GameOptions.NetName, 1.0f);
        _name.PlaceholderText = "Your nickname";
        _name.MaxLength = 20;
        HBoxContainer look = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.2f, Alignment = BoxContainer.AlignmentMode.End };
        look.AddThemeConstantOverride("separation", 8);
        _playingAs = UiStyle.MakeLabel("", UiStyle.TextSize - 2, false, UiStyle.Ink);
        _playingAs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _playingAs.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        _playingAs.ClipText = true;
        look.AddChild(_playingAs);
        _shirtSwatch = Swatch();
        look.AddChild(_shirtSwatch);
        _packSwatch = Swatch();
        look.AddChild(_packSwatch);
        InkButton character = Button("Character…", 150.0f);
        character.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        character.Pressed += () =>
        {
            SaveName();
            CharacterRequested?.Invoke();
        };
        look.AddChild(character);
        you.AddChild(look);

        // The browser.
        HBoxContainer heading = Row(10);
        Label title = UiStyle.MakeLabel("Available expeditions", UiStyle.TextSize + 4, true, UiStyle.Ink);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        heading.AddChild(title);
        _found = UiStyle.MakeLabel("", UiStyle.SmallSize, false, UiStyle.InkSoft);
        _found.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        heading.AddChild(_found);
        InkButton copy = Button("Copy Address", 160.0f);
        copy.Pressed += OnCopy;
        heading.AddChild(copy);
        InkButton refresh = Button("Refresh", 120.0f);
        refresh.Pressed += OnRefresh;
        heading.AddChild(refresh);
        Control frame = new() { CustomMinimumSize = new Vector2(0.0f, ListHeight), SizeFlagsVertical = SizeFlags.ExpandFill };
        _browserFrame = frame;
        _column.AddChild(frame);
        _scroll = new ScrollContainer { Name = "ExpeditionBrowser", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        frame.AddChild(_scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 8);
        _scroll.AddChild(_list);
        _empty = UiStyle.MakeLabel(EmptyText, UiStyle.TextSize - 2, false, UiStyle.InkSoft);
        _empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.SetAnchorsPreset(LayoutPreset.FullRect);
        frame.AddChild(_empty);

        // Direct connect: the address field, checked before connecting.
        HBoxContainer direct = Row(10);
        _address = Field(direct, "Host's address (direct connect)", DefaultAddress, 2.0f);
        _address.PlaceholderText = "192.168.1.5 or a host name";
        _port = Field(direct, "Port", PortText(), 0.6f);
        _port.MaxLength = 5;

        // Host settings.
        HBoxContainer settings = Row(10);
        _serverName = Field(settings, "Expedition name (when hosting)", "", 2.0f);
        _serverName.MaxLength = LanDiscovery.MaxNameLength;
        _serverName.PlaceholderText = "Your nickname's Expedition";
        VBoxContainer room = Labelled(settings, "Room", 0.6f);
        _maxPlayers = new OptionButton { CustomMinimumSize = new Vector2(0.0f, 36.0f) };
        _maxPlayers.AddThemeFontOverride("font", UiStyle.Hand);
        _maxPlayers.AddThemeFontSizeOverride("font_size", UiStyle.TextSize);
        int most = MostPlayers();
        for (int count = MinPlayers; count <= most; count++)
        {
            _maxPlayers.AddItem($"{count} players", count);
        }
        _maxPlayers.Select(_maxPlayers.ItemCount - 1);
        room.AddChild(_maxPlayers);
        VBoxContainer visible = Labelled(settings, " ", 1.0f);
        _lanVisible = new CheckBox { Text = "Show on home network", ButtonPressed = true };
        _lanVisible.AddThemeFontOverride("font", UiStyle.Hand);
        _lanVisible.AddThemeFontSizeOverride("font_size", UiStyle.TextSize - 2);
        _lanVisible.AddThemeColorOverride("font_color", UiStyle.Ink);
        _lanVisible.AddThemeColorOverride("font_pressed_color", UiStyle.Ink);
        _lanVisible.AddThemeColorOverride("font_hover_color", UiStyle.Ink);
        visible.AddChild(_lanVisible);

        HBoxContainer buttons = Row(14);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        InkButton host = Button("Host Expedition", 190.0f);
        host.Pressed += OnHost;
        buttons.AddChild(host);
        InkButton join = Button("Join", 130.0f);
        join.Pressed += OnJoin;
        buttons.AddChild(join);
        InkButton connect = Button("Direct Connect", 180.0f);
        connect.Pressed += OnDirect;
        buttons.AddChild(connect);
        _status = UiStyle.MakeLabel("", UiStyle.TextSize - 2, false, UiStyle.Warning);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _column.AddChild(_status);
        _back = Button("Back", 130.0f);
        _back.Pressed += () => BackPressed?.Invoke();
        AddChild(_back);

        _scanner = new LanScanner { Name = "LanScanner" };
        if (_session != null)
        {
            _scanner.DiscoveryPort = _session.DiscoveryPort;
            _session.Changed += ShowStatus;
            _session.Welcomed += OnWelcomed;
        }
        _scanner.Changed += ShowServers;
        AddChild(_scanner);
        ShowServers();
        // Wrapped labels settle after their first layout. Refit the column then,
        // otherwise its initial narrow width can leave thousands of pixels of height.
        _column.MinimumSizeChanged += () => Callable.From(LayoutContent).CallDeferred();
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

    public override void _Process(double delta)
    {
        // The browser listens only while the page is up: closing it (Back, a run starting) closes the socket.
        if (_scanner != null && _scanner.Running && (!IsOpen || !IsVisibleInTree()))
        {
            _scanner.Stop();
        }
    }

    private HBoxContainer Row(int separation)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", separation);
        _column?.AddChild(row);
        return row;
    }

    private static VBoxContainer Labelled(HBoxContainer row, string label, float ratio)
    {
        VBoxContainer box = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = ratio };
        box.AddThemeConstantOverride("separation", 2);
        box.AddChild(UiStyle.MakeLabel(label, UiStyle.SmallSize, false, UiStyle.Ink));
        row.AddChild(box);
        return box;
    }

    private static LineEdit Field(HBoxContainer row, string label, string text, float ratio)
    {
        VBoxContainer box = Labelled(row, label, ratio);
        LineEdit field = new() { Text = text, MaxLength = 64, CustomMinimumSize = new Vector2(0.0f, 36.0f) };
        field.AddThemeFontOverride("font", UiStyle.Hand);
        field.AddThemeFontSizeOverride("font_size", UiStyle.TextSize);
        box.AddChild(field);
        return field;
    }

    private static ColorRect Swatch()
    {
        return new ColorRect { CustomMinimumSize = new Vector2(26.0f, 26.0f), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
    }

    private static InkButton Button(string text, float width)
    {
        return new InkButton { Text = text, Paper = true, Focusable = true, FontSize = UiStyle.TextSize, CustomMinimumSize = new Vector2(width, 40.0f) };
    }

    private int MostPlayers()
    {
        int most = 4;
        if (_session != null)
        {
            most = _session.MaxPlayers;
        }
        return Mathf.Max(most, MinPlayers);
    }

    private string PortText()
    {
        if (_session != null && _session.DefaultPort > 0)
        {
            return _session.DefaultPort.ToString();
        }
        return "7777";
    }

    // --- The list ---

    private void ShowServers()
    {
        if (_list == null || _empty == null || _scanner == null)
        {
            return;
        }
        IReadOnlyList<LanServerInfo> servers = _scanner.Servers;
        while (_rows.Count < servers.Count)
        {
            ExpeditionRow row = new();
            row.Chosen += Select;
            row.Activated += picked =>
            {
                Select(picked);
                OnJoin();
            };
            _list.AddChild(row);
            _rows.Add(row);
        }
        bool stillThere = false;
        for (int i = 0; i < _rows.Count; i++)
        {
            ExpeditionRow row = _rows[i];
            if (i < servers.Count)
            {
                row.Visible = true;
                row.Display(servers[i]);
                bool picked = servers[i].Id == _selectedId;
                row.SetSelected(picked);
                if (picked)
                {
                    stillThere = true;
                }
            }
            else
            {
                row.Visible = false;
                row.Display(null);
                row.SetSelected(false);
            }
        }
        if (!stillThere)
        {
            _selectedId = "";
        }
        _empty.Visible = servers.Count == 0;
        if (_found != null)
        {
            if (servers.Count == 1)
            {
                _found.Text = "1 found";
            }
            else
            {
                _found.Text = $"{servers.Count} found";
            }
        }
    }

    private void Select(ExpeditionRow picked)
    {
        if (picked.Info == null)
        {
            return;
        }
        _selectedId = picked.Info.Id;
        foreach (ExpeditionRow row in _rows)
        {
            row.SetSelected(row == picked);
        }
    }

    private LanServerInfo? SelectedServer()
    {
        if (_scanner == null || _selectedId.Length == 0)
        {
            return null;
        }
        foreach (LanServerInfo server in _scanner.Servers)
        {
            if (server.Id == _selectedId)
            {
                return server;
            }
        }
        return null;
    }

    private void OnRefresh()
    {
        if (_scanner == null)
        {
            return;
        }
        if (!_scanner.Start())
        {
            SetStatus("Couldn't look for expeditions on the home network (no network socket). Direct Connect still works.");
            return;
        }
        SetStatus("Looking for expeditions on your network…");
    }

    private void OnCopy()
    {
        LanServerInfo? server = SelectedServer();
        string text;
        if (server != null)
        {
            text = server.Endpoint;
        }
        else
        {
            string address = NetSession.LocalAddresses().Split(' ')[0];
            if (address.Length == 0)
            {
                address = "127.0.0.1";
            }
            text = $"{address}:{Port(false)}";
        }
        DisplayServer.ClipboardSet(text);
        SetStatus($"Copied {text}.");
    }

    // --- Hosting and joining ---

    /// <summary>The nickname typed, remembered for next time; empty (and a word in the status line) if none.</summary>
    private string Nickname()
    {
        string name = (_name?.Text ?? "").Trim();
        if (name.Length == 0)
        {
            SetStatus("Choose a nickname first: the others see it over your head.");
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

    /// <summary>The port typed, or -1 (and, if <paramref name="complain"/>, a word in the status line) when it isn't 1-65535.</summary>
    private int Port(bool complain)
    {
        string text = (_port?.Text ?? "").Trim();
        if (int.TryParse(text, out int port) && port > 0 && port < 65536)
        {
            return port;
        }
        if (complain)
        {
            SetStatus($"\"{text}\" isn't a port: use a number from 1 to 65535 (the game's is {PortText()}).");
            _port?.GrabFocus();
            return -1;
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
        int port = Port(true);
        if (name.Length == 0 || port < 0)
        {
            return;
        }
        string expedition = (_serverName?.Text ?? "").Trim();
        if (expedition.Length == 0)
        {
            expedition = $"{name}'s Expedition";
        }
        _session.ServerName = expedition;
        _session.LanVisible = _lanVisible?.ButtonPressed ?? true;
        int room = MostPlayers();
        if (_maxPlayers != null && _maxPlayers.Selected >= 0)
        {
            room = _maxPlayers.GetItemId(_maxPlayers.Selected);
        }
        _scanner?.Stop();
        if (_session.Host(name, port, room))
        {
            RunReady?.Invoke();
        }
    }

    /// <summary>Joins the expedition picked in the list (or, with none picked, the address typed).</summary>
    private void OnJoin()
    {
        LanServerInfo? server = SelectedServer();
        if (server == null)
        {
            if (_scanner != null && _scanner.Servers.Count > 0)
            {
                SetStatus("Pick an expedition in the list first (or type an address and use Direct Connect).");
                return;
            }
            OnDirect();
            return;
        }
        if (!server.SameVersion)
        {
            SetStatus($"{server.Name} runs another version of the game. Update both to the same version.");
            return;
        }
        if (server.Full)
        {
            SetStatus($"{server.Name} is full ({server.Players} of {server.MaxPlayers}).");
            return;
        }
        if (!server.Joinable)
        {
            SetStatus($"{server.Name} can't be joined right now.");
            return;
        }
        string name = Nickname();
        if (name.Length == 0)
        {
            return;
        }
        _session?.Join(name, server.Address, server.Port);
    }

    private void OnDirect()
    {
        string address = (_address?.Text ?? "").Trim();
        if (address.Length == 0)
        {
            SetStatus("Type the host's address first (for example 192.168.1.5).");
            _address?.GrabFocus();
            return;
        }
        // "192.168.1.5:7777" in the address field fills the port too.
        int colon = address.LastIndexOf(':');
        if (colon > 0 && address.IndexOf(':') == colon && _port != null)
        {
            _port.Text = address[(colon + 1)..];
            address = address[..colon];
            if (_address != null)
            {
                _address.Text = address;
            }
        }
        if (!ValidAddress(address))
        {
            SetStatus($"\"{address}\" isn't an address: use numbers like 192.168.1.5, or a host name.");
            _address?.GrabFocus();
            return;
        }
        int port = Port(true);
        string name = Nickname();
        if (port < 0 || name.Length == 0)
        {
            return;
        }
        _session?.Join(name, address, port);
    }

    /// <summary>An IPv4/IPv6 address, or a host name of letters, digits, dots and dashes.</summary>
    private static bool ValidAddress(string address)
    {
        if (address.IsValidIPAddress())
        {
            return true;
        }
        if (address.Length > 253 || address.StartsWith('.') || address.EndsWith('.') || address.StartsWith('-'))
        {
            return false;
        }
        bool digitsAndDots = true;
        foreach (char c in address)
        {
            bool ok = char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-';
            if (!ok)
            {
                return false;
            }
            if (!char.IsAsciiDigit(c) && c != '.')
            {
                digitsAndDots = false;
            }
        }
        // "192.168.1" or "300.1.1.1" look like addresses but aren't.
        return !digitsAndDots;
    }

    private void OnWelcomed(ulong seed)
    {
        if (IsOpen)
        {
            RunReady?.Invoke();
        }
    }

    private void SetStatus(string text)
    {
        if (_status != null)
        {
            _status.Text = text;
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

    public void FitInto(Rect2 area)
    {
        Vector2 size = new(Mathf.Min(MaxSize.X, area.Size.X), Mathf.Min(MaxSize.Y, area.Size.Y));
        Position = area.Position + (area.Size - size) * 0.5f;
        Size = size;
        LayoutContent();
    }

    private void LayoutContent()
    {
        if (_column == null || _back == null || _browserFrame == null || !IsInsideTree())
        {
            return;
        }
        float top = HeaderHeight + 4.0f;
        float height = Mathf.Max(Size.Y - top - 66.0f, 40.0f);
        float fixedHeight = _column.GetCombinedMinimumSize().Y - _browserFrame.CustomMinimumSize.Y;
        float listHeight = Mathf.Min(ListHeight, Mathf.Max(MinimumListHeight, height - fixedHeight));
        if (!Mathf.IsEqualApprox(_browserFrame.CustomMinimumSize.Y, listHeight))
        {
            _browserFrame.CustomMinimumSize = new Vector2(0, listHeight);
        }
        _column.Position = new Vector2(Margin, top);
        _column.Size = new Vector2(Size.X - Margin * 2.0f, height);
        _back.Position = new Vector2((Size.X - 130.0f) * 0.5f, Size.Y - 56.0f);
        _back.Size = new Vector2(130.0f, 40.0f);
    }

    public void OpenSheet()
    {
        ShowStatus();
        ShowLook();
        Open();
        if (_scanner != null && !_scanner.Start())
        {
            SetStatus("Couldn't look for expeditions on the home network. Direct Connect still works.");
        }
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
