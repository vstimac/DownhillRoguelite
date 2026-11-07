using DownhillRoguelite.Core;
using DownhillRoguelite.Map;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The map page: the field map's hand-drawn chart (seen ground, paths, stations, waypoints, pencil
/// lines, the carrier) on an unfolding parchment sheet with a compass in its corner. M opens and
/// closes it through the field map (which slows the carrier as before). Click to set a waypoint,
/// right-click to remove one, the wheel zooms, and dragging draws with paper and pencil.
/// </summary>
public partial class MapScreen : ParchmentPanel
{
    public FieldMap? Map { get; set; }

    private TextureRect? _chart;
    private CompassDial? _compass;

    public override void _Ready()
    {
        Title = "Map";
        Closable = true;
        Seed = 53.0f;
        Size = new Vector2(620, 690);
        base._Ready();
        _chart = new TextureRect
        {
            Position = new Vector2(24, 58),
            Size = new Vector2(Size.X - 48, Size.X - 48),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Stop,
        };
        _chart.GuiInput += OnChartInput;
        AddChild(_chart);
        _compass = new CompassDial { Position = new Vector2(Size.X - 130, 66), Size = new Vector2(96, 96) };
        AddChild(_compass);
        Label help = UiStyle.MakeLabel("M  close   ·   click  waypoint   ·   right-click  remove   ·   wheel  zoom   ·   drag  draw (paper and pencil)", 15);
        help.Position = new Vector2(24, Size.Y - 34);
        AddChild(help);
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

    private void OnMapOpened(bool open)
    {
        if (open)
        {
            if (_chart != null && Map != null)
            {
                _chart.Texture = Map.SheetTexture;
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
}
