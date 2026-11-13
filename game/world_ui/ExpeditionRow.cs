using System;
using DownhillRoguelite.Network;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// One expedition in the Co-op page's server browser: a darker parchment card with the name and
/// address:port on the left, and a status pill (Waiting / In progress / Full / Other version) over
/// "players / room" and the ping on the right. Click (or Enter) selects it; a double-click joins.
/// The selected card gets an ink-gold border.
/// </summary>
public partial class ExpeditionRow : PanelContainer
{
    [Export] public float RowHeight { get; set; } = 64.0f;
    [Export] public Color Card { get; set; } = new(0.74f, 0.65f, 0.49f, 0.55f);
    [Export] public Color CardSelected { get; set; } = new(0.8f, 0.7f, 0.5f, 0.85f);
    [Export] public Color Waiting { get; set; } = new(0.3f, 0.5f, 0.22f);
    [Export] public Color Busy { get; set; } = new(0.55f, 0.42f, 0.2f);

    /// <summary>Clicked or chosen with Enter.</summary>
    public event Action<ExpeditionRow>? Chosen;
    /// <summary>Double-clicked: join it.</summary>
    public event Action<ExpeditionRow>? Activated;

    public LanServerInfo? Info { get; private set; }
    public bool Selected { get; private set; }

    private Label? _name;
    private Label? _address;
    private Label? _pill;
    private Label? _players;
    private StyleBoxFlat? _box;
    private StyleBoxFlat? _pillBox;

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(0.0f, RowHeight);
        _box = new StyleBoxFlat { BgColor = Card, BorderColor = UiStyle.InkSoft, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 6, ContentMarginBottom = 6 };
        _box.SetBorderWidthAll(1);
        _box.SetCornerRadiusAll(8);
        AddThemeStyleboxOverride("panel", _box);
        HBoxContainer row = new() { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 12);
        AddChild(row);
        VBoxContainer left = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        left.AddThemeConstantOverride("separation", 0);
        row.AddChild(left);
        _name = Text(UiStyle.TextSize + 2, UiStyle.Ink);
        _name.ClipText = true;
        left.AddChild(_name);
        _address = Text(UiStyle.SmallSize, UiStyle.InkSoft);
        left.AddChild(_address);
        VBoxContainer right = new() { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        right.AddThemeConstantOverride("separation", 2);
        row.AddChild(right);
        _pill = Text(UiStyle.SmallSize, UiStyle.Paper);
        _pill.HorizontalAlignment = HorizontalAlignment.Center;
        _pill.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        _pillBox = new StyleBoxFlat { BgColor = Waiting, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 1, ContentMarginBottom = 1 };
        _pillBox.SetCornerRadiusAll(9);
        _pill.AddThemeStyleboxOverride("normal", _pillBox);
        right.AddChild(_pill);
        _players = Text(UiStyle.SmallSize, UiStyle.Ink);
        _players.HorizontalAlignment = HorizontalAlignment.Right;
        right.AddChild(_players);
        Display(Info);
        GuiInput += OnInput;
        FocusEntered += () => Chosen?.Invoke(this);
    }

    private static Label Text(int size, Color colour)
    {
        Label label = UiStyle.MakeLabel("", size, false, colour);
        label.MouseFilter = MouseFilterEnum.Ignore;
        return label;
    }

    /// <summary>Shows this expedition (the row is reused while the list refreshes).</summary>
    public void Display(LanServerInfo? info)
    {
        Info = info;
        if (info == null || _name == null || _address == null || _pill == null || _players == null || _pillBox == null)
        {
            return;
        }
        _name.Text = info.Name;
        _address.Text = info.Endpoint;
        _pill.Text = info.StatusText();
        if (info.Joinable && !info.InProgress)
        {
            _pillBox.BgColor = Waiting;
        }
        else if (info.Joinable)
        {
            _pillBox.BgColor = Busy;
        }
        else
        {
            _pillBox.BgColor = UiStyle.Warning;
        }
        _players.Text = $"{info.Players} / {info.MaxPlayers}    {info.PingText()}";
        TooltipText = $"{info.Name}\n{info.Endpoint}";
    }

    public void SetSelected(bool selected)
    {
        Selected = selected;
        if (_box == null)
        {
            return;
        }
        if (selected)
        {
            _box.BgColor = CardSelected;
            _box.BorderColor = UiStyle.Highlight.Darkened(0.3f);
            _box.SetBorderWidthAll(3);
        }
        else
        {
            _box.BgColor = Card;
            _box.BorderColor = UiStyle.InkSoft;
            _box.SetBorderWidthAll(1);
        }
    }

    private void OnInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button && button.Pressed && button.ButtonIndex == MouseButton.Left)
        {
            GrabFocus();
            Chosen?.Invoke(this);
            if (button.DoubleClick)
            {
                Activated?.Invoke(this);
            }
            AcceptEvent();
        }
        else if (@event.IsActionPressed("ui_accept"))
        {
            Activated?.Invoke(this);
            AcceptEvent();
        }
    }
}
