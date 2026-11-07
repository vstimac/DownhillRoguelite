using DownhillRoguelite.Core;
using DownhillRoguelite.Map;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The map page: the field map's hand-drawn chart (seen ground, paths, stations, waypoints, pencil
/// lines, the carrier, an arrow to the next station) on an unfolding parchment sheet, a compass in
/// its corner and a legend beside it. M opens and closes it through the field map (which slows the
/// carrier as before). Drag to move the map (Valheim style), click to set a waypoint, right-click
/// removes one, the wheel zooms, Shift+drag draws with paper and pencil, C or the Centre button
/// goes back to the carrier. The grip in the bottom-right corner resizes the page.
/// </summary>
public partial class MapScreen : ParchmentPanel
{
    [Export] public Vector2 MinSize { get; set; } = new(560, 460);
    [Export] public float LegendWidth { get; set; } = 210.0f;

    public FieldMap? Map { get; set; }

    private TextureRect? _chart;
    private CompassDial? _compass;
    private MapLegend? _legend;
    private Label? _help;
    private InkButton? _centre;
    private Control? _grip;
    private bool _resizing;
    private Vector2 _resizeFrom;
    private Vector2 _sizeFrom;

    public override void _Ready()
    {
        Title = "Map";
        Closable = true;
        Seed = 53.0f;
        Size = new Vector2(860, 690);
        base._Ready();
        _chart = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Stop,
        };
        _chart.GuiInput += OnChartInput;
        AddChild(_chart);
        _compass = new CompassDial { Size = new Vector2(96, 96) };
        AddChild(_compass);
        _legend = new MapLegend();
        AddChild(_legend);
        _centre = new InkButton { Text = "Centre", Paper = true, Size = new Vector2(100, 36) };
        _centre.Pressed += () => Map?.Recentre();
        AddChild(_centre);
        _help = UiStyle.MakeLabel("drag  move   ·   click  waypoint   ·   right-click  remove   ·   wheel  zoom   ·   Shift+drag  draw   ·   C  centre", 15);
        AddChild(_help);
        _grip = new ResizeGrip { Size = new Vector2(26, 26), MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.Fdiagsize };
        _grip.GuiInput += OnGripInput;
        AddChild(_grip);
        Resized += LayoutPage;
        LayoutPage();
        Closed += () =>
        {
            if (Map != null && Map.IsOpen)
            {
                Map.SetOpen(false);
            }
        };
        GameEvents.MapOpened += OnMapOpened;
        SetOpenNow(false);
    }

    public override void _ExitTree()
    {
        GameEvents.MapOpened -= OnMapOpened;
    }

    /// <summary>Places the chart (square, as large as the page allows), the legend, compass, help and grip.</summary>
    private void LayoutPage()
    {
        if (_chart == null || _compass == null || _legend == null || _help == null || _centre == null || _grip == null)
        {
            return;
        }
        float side = Mathf.Max(200.0f, Mathf.Min(Size.X - 48.0f - LegendWidth - 16.0f, Size.Y - 58.0f - 50.0f));
        _chart.Position = new Vector2(24, 58);
        _chart.Size = new Vector2(side, side);
        _compass.Position = new Vector2(24 + side - 106, 66);
        float legendX = 24 + side + 16;
        _legend.Position = new Vector2(legendX, 58);
        _legend.Size = new Vector2(Size.X - legendX - 20, side - 50);
        _legend.Canvas = Map?.Canvas;
        _legend.QueueRedraw();
        _centre.Position = new Vector2(legendX, 58 + side - 40);
        _help.Position = new Vector2(24, Size.Y - 34);
        _grip.Position = Size - _grip.Size - new Vector2(6, 6);
        PivotOffset = Size * 0.5f;
    }

    private void OnGripInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
        {
            _resizing = button.Pressed;
            _resizeFrom = button.GlobalPosition;
            _sizeFrom = Size;
            _grip?.AcceptEvent();
        }
        else if (@event is InputEventMouseMotion motion && _resizing)
        {
            Vector2 screen = GetViewportRect().Size;
            Vector2 wanted = _sizeFrom + (motion.GlobalPosition - _resizeFrom);
            Size = new Vector2(Mathf.Clamp(wanted.X, MinSize.X, screen.X - Position.X), Mathf.Clamp(wanted.Y, MinSize.Y, screen.Y - Position.Y));
            _grip?.AcceptEvent();
        }
    }

    /// <summary>Resizes the page (also used by tests); the chart and legend follow.</summary>
    public void ResizeTo(Vector2 size)
    {
        Size = new Vector2(Mathf.Max(size.X, MinSize.X), Mathf.Max(size.Y, MinSize.Y));
        LayoutPage();
    }

    public float ChartSide => _chart?.Size.X ?? 0.0f;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || !IsOpen || @event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }
        if (key.PhysicalKeycode == Key.C)
        {
            Map?.Recentre();
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnMapOpened(bool open)
    {
        if (open)
        {
            if (_chart != null && Map != null)
            {
                _chart.Texture = Map.SheetTexture;
            }
            if (_legend != null)
            {
                _legend.Canvas = Map?.Canvas;
            }
            Open();
        }
        else if (IsOpen)
        {
            Close();
        }
    }

    private Vector2 ToPixel(Vector2 local)
    {
        if (_chart == null || Map == null)
        {
            return Vector2.Zero;
        }
        return local / _chart.Size * Map.TextureSize;
    }

    private void OnChartInput(InputEvent @event)
    {
        if (Map == null)
        {
            return;
        }
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed)
            {
                Map.ZoomStep(-1);
            }
            else if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed)
            {
                Map.ZoomStep(1);
            }
            else if (button.ButtonIndex == MouseButton.Left)
            {
                if (button.Pressed)
                {
                    Map.DrawMode = button.ShiftPressed;
                    Map.PressAt(ToPixel(button.Position));
                }
                else
                {
                    Map.Release();
                }
            }
            else if (button.ButtonIndex == MouseButton.Right && button.Pressed)
            {
                Map.RightClickAt(ToPixel(button.Position));
            }
            _chart?.AcceptEvent();
        }
        else if (@event is InputEventMouseMotion motion && (motion.ButtonMask & MouseButtonMask.Left) != 0)
        {
            Map.DragTo(ToPixel(motion.Position));
            _chart?.AcceptEvent();
        }
    }

    /// <summary>A few inked diagonal strokes in the page's corner: drag to resize.</summary>
    private partial class ResizeGrip : Control
    {
        public override void _Draw()
        {
            for (int i = 0; i < 3; i++)
            {
                float o = 6.0f + i * 7.0f;
                DrawLine(new Vector2(Size.X, Size.Y - o), new Vector2(Size.X - o, Size.Y), UiStyle.InkSoft, 2.0f, true);
            }
        }
    }
}
