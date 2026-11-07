using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Map;

/// <summary>
/// The carrier's map and compass (M13), a sheet of paper held up in front of the camera with M,
/// never a screen overlay. The chart (MapCanvas, drawn into a SubViewport on the sheet) shows the
/// ground the carrier has seen, north up, centred on the carrier. A compass on the top edge points
/// to real north (-Z) relative to the view. While the map is up the mouse works on it: a click sets
/// a waypoint (shown in the world as a WaypointMarker), right-click removes the nearest one, the
/// wheel zooms, and dragging draws in pencil when the carrier carries paper and a pencil; the pencil
/// wears down and paper gets used up. The carrier walks slowly with the map up and can't lean or
/// brace (GameEvents.IsMapOpen). A new mountain clears the chart.
/// </summary>
public partial class FieldMap : Node3D
{
    private static readonly StringName ActionToggle = "map_toggle";
    public const string Group = "field_map";

    [ExportGroup("Chart")]
    /// <summary>Size of a discovered cell, in m.</summary>
    [Export] public float CellSize { get; set; } = 8.0f;
    /// <summary>How far the carrier sees to fill the chart by day and at full night, in m.</summary>
    [Export] public Vector2 Sight { get; set; } = new(70.0f, 25.0f);
    [Export] public int MaxWaypoints { get; set; } = 9;
    /// <summary>Zoom levels: metres across the sheet.</summary>
    [Export] public float[] Zooms { get; set; } = { 300.0f, 600.0f, 1200.0f };
    [Export] public int TextureSize { get; set; } = 768;
    /// <summary>Seconds between chart updates.</summary>
    [Export] public float RevealInterval { get; set; } = 0.25f;

    [ExportGroup("Holding")]
    /// <summary>Where the sheet is held, in camera space, in m.</summary>
    [Export] public Vector3 HeldOffset { get; set; } = new(0.0f, -0.05f, -0.62f);
    /// <summary>Where it goes when lowered (out of view), in camera space.</summary>
    [Export] public Vector3 LoweredOffset { get; set; } = new(0.0f, -0.6f, -0.45f);
    [Export] public float SheetSize { get; set; } = 0.44f;
    [Export] public float RaiseSpeed { get; set; } = 4.0f;
    /// <summary>The paper's brightness at full night (it is read by moonlight).</summary>
    [Export] public float NightBrightness { get; set; } = 0.45f;

    [ExportGroup("Drawing")]
    [Export] public string PaperItem { get; set; } = "paper";
    [Export] public string PencilItem { get; set; } = "pencil";
    /// <summary>Metres of line (in world units) a pencil draws before it is worn down.</summary>
    [Export] public float PencilMetres { get; set; } = 3000.0f;
    /// <summary>Pencil lines a sheet of paper holds before another is needed.</summary>
    [Export] public int StrokesPerPaper { get; set; } = 40;
    /// <summary>A press that moves less than this many pixels on the sheet is a click (waypoint), not a line.</summary>
    [Export] public float ClickSlop { get; set; } = 6.0f;

    public MapChart Chart { get; private set; } = new(8.0f, 9);
    public IRouteQuery? Route => _run?.Route;
    public bool IsOpen { get; private set; }
    /// <summary>Centre of the chart in world XZ (the carrier).</summary>
    public Vector2 Centre { get; private set; }
    /// <summary>The carrier's facing in world XZ.</summary>
    public Vector2 Facing { get; private set; }
    public float MetresAcross => Zooms[Mathf.Clamp(_zoom, 0, Zooms.Length - 1)];
    public bool CanDraw => HasItem(PaperItem) && HasItem(PencilItem);
    public IReadOnlyList<WaypointMarker> Markers => _markers;
    public float InkUsed => _inkUsed;
    public Node3D? Compass => _compass;
    public Node3D? Needle => _needle;

    private readonly List<WaypointMarker> _markers = new();
    private RunState? _run;
    private WorldClock? _clock;
    private SubViewport? _viewport;
    private MapCanvas? _canvas;
    private MeshInstance3D? _sheet;
    private StandardMaterial3D? _material;
    private Node3D? _compass;
    private Node3D? _needle;
    private Vector3 _carrier;
    private float _raise;
    private float _revealTimer;
    private float _redrawTimer;
    private int _zoom;
    private bool _pressing;
    private bool _drawing;
    private Vector2 _pressPixel;
    private float _inkUsed;
    private int _strokes;

    public override void _Ready()
    {
        TopLevel = true;
        AddToGroup(Group);
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        Chart = new MapChart(CellSize, MaxWaypoints);
        Build();
        GameEvents.LocomotionUpdated += OnLocomotionUpdated;
        GameEvents.RespawnRequested += OnNewRun;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotionUpdated;
        GameEvents.RespawnRequested -= OnNewRun;
        if (GameEvents.IsMapOpen)
        {
            GameEvents.PublishMapOpen(false);
        }
    }

    private void Build()
    {
        _viewport = new SubViewport
        {
            Size = new Vector2I(TextureSize, TextureSize),
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            Disable3D = true,
        };
        AddChild(_viewport);
        _canvas = new MapCanvas { Map = this, Size = new Vector2(TextureSize, TextureSize) };
        _viewport.AddChild(_canvas);

        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = _viewport.GetTexture(),
            AlbedoTextureForceSrgb = true,
            NoDepthTest = true,
            RenderPriority = 10,
        };
        _sheet = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(SheetSize, SheetSize) },
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_sheet);

        // Compass: a brass disc on the top edge, a red needle turning in the sheet's plane.
        _compass = new Node3D { Position = new Vector3(0.0f, SheetSize * 0.5f + 0.02f, 0.01f) };
        _sheet.AddChild(_compass);
        StandardMaterial3D brass = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.72f, 0.58f, 0.3f), NoDepthTest = true, RenderPriority = 11 };
        _compass.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.045f, Height = 0.01f, RadialSegments = 20 },
            RotationDegrees = new Vector3(90.0f, 0.0f, 0.0f),
            MaterialOverride = brass,
        });
        StandardMaterial3D face = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.92f, 0.9f, 0.82f), NoDepthTest = true, RenderPriority = 12 };
        _compass.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.038f, BottomRadius = 0.038f, Height = 0.002f, RadialSegments = 20 },
            Position = new Vector3(0.0f, 0.0f, 0.006f),
            RotationDegrees = new Vector3(90.0f, 0.0f, 0.0f),
            MaterialOverride = face,
        });
        _needle = new Node3D { Position = new Vector3(0.0f, 0.0f, 0.009f) };
        _compass.AddChild(_needle);
        StandardMaterial3D red = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.8f, 0.1f, 0.1f), NoDepthTest = true, RenderPriority = 13 };
        StandardMaterial3D dark = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.15f, 0.15f, 0.18f), NoDepthTest = true, RenderPriority = 13 };
        _needle.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.008f, 0.032f, 0.002f) }, Position = new Vector3(0.0f, 0.016f, 0.0f), MaterialOverride = red });
        _needle.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.008f, 0.032f, 0.002f) }, Position = new Vector3(0.0f, -0.016f, 0.0f), MaterialOverride = dark });
    }

    private void OnLocomotionUpdated(LocomotionSnapshot snapshot)
    {
        _carrier = snapshot.Position;
        Centre = new Vector2(snapshot.Position.X, snapshot.Position.Z);
        Facing = new Vector2(snapshot.Forward.X, snapshot.Forward.Z);
    }

    private void OnNewRun()
    {
        Chart.Clear();
        foreach (WaypointMarker marker in _markers)
        {
            marker.QueueFree();
        }
        _markers.Clear();
        _inkUsed = 0.0f;
        _strokes = 0;
        SetOpen(false);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(ActionToggle) && !GameEvents.IsPacking)
        {
            SetOpen(!IsOpen);
            GetViewport().SetInputAsHandled();
        }
    }

    public void SetOpen(bool open)
    {
        if (open == IsOpen)
        {
            return;
        }
        IsOpen = open;
        _pressing = false;
        _drawing = false;
        GameEvents.PublishMapOpen(open);
        if (open)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            Redraw();
        }
        else if (!GameEvents.IsPacking)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen)
        {
            return;
        }
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed)
            {
                _zoom = Mathf.Max(0, _zoom - 1);
                Redraw();
            }
            else if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed)
            {
                _zoom = Mathf.Min(Zooms.Length - 1, _zoom + 1);
                Redraw();
            }
            else if (button.ButtonIndex == MouseButton.Left)
            {
                OnLeft(button.Pressed, button.Position);
            }
            else if (button.ButtonIndex == MouseButton.Right && button.Pressed && SheetPixel(button.Position, out Vector2 pixel))
            {
                RemoveWaypointAt(PixelToWorld(pixel));
            }
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseMotion motion && _pressing)
        {
            OnDrag(motion.Position);
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnLeft(bool pressed, Vector2 screen)
    {
        if (pressed)
        {
            if (SheetPixel(screen, out Vector2 pixel))
            {
                _pressing = true;
                _drawing = false;
                _pressPixel = pixel;
            }
            return;
        }
        if (_pressing && !_drawing)
        {
            AddWaypoint(PixelToWorld(_pressPixel));
        }
        _pressing = false;
        _drawing = false;
    }

    private void OnDrag(Vector2 screen)
    {
        if (!SheetPixel(screen, out Vector2 pixel) || !CanDraw)
        {
            return;
        }
        if (!_drawing)
        {
            if (pixel.DistanceTo(_pressPixel) < ClickSlop)
            {
                return;
            }
            _drawing = true;
            Chart.BeginStroke(PixelToWorld(_pressPixel));
            UseStroke();
        }
        Draw(PixelToWorld(pixel));
    }

    /// <summary>Adds a pencil point at a world spot (extends the current line); wears the pencil.</summary>
    public void Draw(Vector2 world)
    {
        _inkUsed += Chart.ExtendStroke(world);
        if (_inkUsed >= PencilMetres)
        {
            _inkUsed = 0.0f;
            GameEvents.RequestItemUse(PencilItem);
        }
        Redraw();
    }

    /// <summary>Starts a new pencil line at a world spot (tests); counts toward using up the paper.</summary>
    public void BeginDrawing(Vector2 world)
    {
        Chart.BeginStroke(world);
        UseStroke();
    }

    private void UseStroke()
    {
        _strokes++;
        if (_strokes >= StrokesPerPaper)
        {
            _strokes = 0;
            GameEvents.RequestItemUse(PaperItem);
        }
    }

    /// <summary>Sets a waypoint at a world spot and stands a marker there.</summary>
    public void AddWaypoint(Vector2 world)
    {
        Chart.AddWaypoint(world);
        SyncMarkers();
        Redraw();
    }

    public void RemoveWaypointAt(Vector2 world)
    {
        // Within a few pixels at the current zoom.
        if (Chart.RemoveWaypointNear(world, MetresAcross / TextureSize * 24.0f))
        {
            SyncMarkers();
            Redraw();
        }
    }

    private void SyncMarkers()
    {
        foreach (WaypointMarker marker in _markers)
        {
            marker.QueueFree();
        }
        _markers.Clear();
        for (int i = 0; i < Chart.Waypoints.Count; i++)
        {
            Vector2 point = Chart.Waypoints[i];
            float y = _carrier.Y;
            if (Route != null)
            {
                y = Route.GroundHeight(point.X, point.Y);
            }
            WaypointMarker marker = new() { Name = $"Waypoint{i + 1}", Number = i + 1 };
            AddChild(marker);
            marker.TopLevel = true;
            marker.GlobalPosition = new Vector3(point.X, y, point.Y);
            _markers.Add(marker);
        }
    }

    public Vector2 PixelToWorld(Vector2 pixel)
    {
        return MapCanvas.ToWorld(pixel, Centre, MetresAcross / TextureSize, new Vector2(TextureSize, TextureSize));
    }

    /// <summary>Where on the sheet (in texture pixels) a screen point falls, if it is on the sheet.</summary>
    public bool SheetPixel(Vector2 screen, out Vector2 pixel)
    {
        pixel = Vector2.Zero;
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera == null || _sheet == null)
        {
            return false;
        }
        Vector3 origin = camera.ProjectRayOrigin(screen);
        Vector3 direction = camera.ProjectRayNormal(screen);
        Transform3D sheet = _sheet.GlobalTransform;
        Vector3 normal = sheet.Basis.Z.Normalized();
        float facing = direction.Dot(normal);
        if (Mathf.Abs(facing) < 1e-5f)
        {
            return false;
        }
        float t = (sheet.Origin - origin).Dot(normal) / facing;
        Vector3 local = sheet.AffineInverse() * (origin + direction * t);
        Vector2 uv = new(local.X / SheetSize + 0.5f, 0.5f - local.Y / SheetSize);
        if (uv.X < 0.0f || uv.X > 1.0f || uv.Y < 0.0f || uv.Y > 1.0f)
        {
            return false;
        }
        pixel = uv * TextureSize;
        return true;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _revealTimer -= dt;
        if (_revealTimer <= 0.0f && Route != null)
        {
            _revealTimer = RevealInterval;
            float darkness = 0.0f;
            if (_clock != null)
            {
                darkness = 1.0f - _clock.Daylight;
            }
            float sight = Mathf.Lerp(Sight.X, Sight.Y, darkness) * (1.0f - 0.5f * Mathf.Clamp(GameEvents.CurrentWeather.Fog, 0.0f, 1.0f));
            IRouteQuery route = Route;
            if (Chart.Reveal(Centre, sight, route.GroundHeight) > 0 && IsOpen)
            {
                Redraw();
            }
        }

        foreach (WaypointMarker marker in _markers)
        {
            marker.ShowDistance(_carrier);
        }

        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera == null || _sheet == null)
        {
            return;
        }
        float target = 0.0f;
        if (IsOpen)
        {
            target = 1.0f;
        }
        _raise = Mathf.MoveToward(_raise, target, RaiseSpeed * dt);
        _sheet.Visible = _raise > 0.01f;
        if (!_sheet.Visible)
        {
            return;
        }
        Vector3 offset = LoweredOffset.Lerp(HeldOffset, Mathf.SmoothStep(0.0f, 1.0f, _raise));
        GlobalTransform = camera.GlobalTransform * new Transform3D(Basis.Identity, offset);
        _sheet.Transform = Transform3D.Identity;

        if (_material != null && _clock != null)
        {
            float light = Mathf.Lerp(NightBrightness, 1.0f, _clock.Daylight);
            _material.AlbedoColor = new Color(light, light, light);
        }

        // Compass: real north (-Z) relative to where the camera looks, in the sheet's plane.
        if (_needle != null)
        {
            Vector3 forward = -camera.GlobalBasis.Z;
            Vector3 right = camera.GlobalBasis.X;
            Vector3 north = Vector3.Forward;
            float angle = Mathf.Atan2(north.Dot(new Vector3(right.X, 0.0f, right.Z).Normalized()), north.Dot(new Vector3(forward.X, 0.0f, forward.Z).Normalized()));
            _needle.Rotation = new Vector3(0.0f, 0.0f, -angle);
        }

        _redrawTimer -= dt;
        if (IsOpen && _redrawTimer <= 0.0f)
        {
            Redraw();
        }
    }

    private void Redraw()
    {
        _redrawTimer = 0.2f;
        _canvas?.QueueRedraw();
        if (_viewport != null)
        {
            _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        }
    }

    private static bool HasItem(string itemId)
    {
        foreach (string id in GameEvents.CarriedItems)
        {
            if (id == itemId)
            {
                return true;
            }
        }
        return false;
    }
}
