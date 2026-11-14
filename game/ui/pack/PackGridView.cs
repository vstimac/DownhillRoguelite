using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The backpack drawn on the page in 2.5D: the real cell grid seen from above at a slant, every
/// item as its own 3D model (photographed live in a small studio and inked like the icons), the
/// pack itself as inked lines on the paper. The view turns in quarter steps, and the layers above a
/// chosen height can be cut away to see what lies underneath. It only draws and answers what the
/// pointer is on; the pages decide what a click means.
/// </summary>
public partial class PackGridView : Control
{
    /// <summary>How far the view looks down on the pack, in degrees.</summary>
    [Export] public float PitchDegrees { get; set; } = 32.0f;
    /// <summary>Which way the view looks at turn 0, in degrees about the vertical (45: onto a corner).</summary>
    [Export] public float YawDegrees { get; set; } = 35.0f;
    /// <summary>Room left around the pack, as a factor of its size.</summary>
    [Export] public float Margin { get; set; } = 1.08f;
    /// <summary>Shrinks each model's block slightly so neighbours read as separate things, in m.</summary>
    [Export] public float ItemInset { get; set; } = 0.004f;
    /// <summary>Seconds a quarter turn of the view takes.</summary>
    [Export] public float TurnTime { get; set; } = 0.22f;
    /// <summary>A model may be stretched this much beyond its own proportions to fill the cells it takes.</summary>
    [Export] public float MaxStretch { get; set; } = 1.6f;
    /// <summary>The paper block drawn behind each model: the cells it really takes.</summary>
    [Export] public Color BlockFill { get; set; } = new(0.86f, 0.79f, 0.64f, 0.5f);
    [Export] public float OutlinePixels { get; set; } = 1.6f;
    [Export] public Color WallFill { get; set; } = new(0.62f, 0.52f, 0.38f, 0.22f);
    [Export] public Color ValidFill { get; set; } = new(0.3f, 0.62f, 0.25f, 0.4f);
    [Export] public Color InvalidFill { get; set; } = new(0.8f, 0.12f, 0.08f, 0.4f);
    [Export] public float MarkerRadius { get; set; } = 6.0f;

    /// <summary>Quarter turns of the view about the vertical (0 to 3).</summary>
    public int Turn { get; private set; }
    /// <summary>Copies whose lowest cell is at or above this height are cut away (the grid's height: nothing is).</summary>
    public int CutHeight { get; private set; } = int.MaxValue;
    /// <summary>The copy under the pointer, outlined; "" for none.</summary>
    public string HoverId { get; private set; } = "";
    /// <summary>The copy in the hand: it leaves its place and is drawn at the ghost instead.</summary>
    public string HeldId { get; private set; } = "";
    public PackLayout Layout { get; private set; } = PackLayout.Empty;

    private readonly Dictionary<string, Node3D> _models = new();
    private SubViewport? _studio;
    private SubViewportContainer? _picture;
    private Camera3D? _camera;
    private Node3D? _stage;
    private Node3D? _heldModel;
    private Layer? _under;
    private Layer? _over;
    private float _yaw;
    private bool _ghost;
    private bool _ghostValid;
    private Vector3I _ghostOrigin;
    private Vector3I _ghostSize;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Pass;
        ProcessMode = ProcessModeEnum.Always;
        _yaw = YawDegrees;
        _under = new Layer { Paint = DrawUnder };
        AddChild(_under);
        // Headless runs have no renderer: the lines and the pointing still work, the models are skipped.
        if (DisplayServer.GetName() != "headless")
        {
            BuildStudio();
        }
        _over = new Layer { Paint = DrawOver };
        AddChild(_over);
        Resized += Arrange;
        Arrange();
        GameEvents.PackLayoutChanged += ShowLayout;
        ShowLayout(GameEvents.CurrentPackLayout);
    }

    public override void _ExitTree()
    {
        GameEvents.PackLayoutChanged -= ShowLayout;
        _models.Clear();
    }

    private void BuildStudio()
    {
        _picture = new SubViewportContainer
        {
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore,
            Material = new ShaderMaterial { Shader = UiStyle.InkIconShader },
        };
        ((ShaderMaterial)_picture.Material).SetShaderParameter("outline_px", OutlinePixels);
        AddChild(_picture);
        _studio = new SubViewport
        {
            TransparentBg = true,
            OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
        };
        _picture.AddChild(_studio);
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Current = true, Near = 0.05f, Far = 20.0f };
        _studio.AddChild(_camera);
        _studio.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50.0f, 30.0f, 0.0f), LightEnergy = 1.3f });
        _studio.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-20.0f, -140.0f, 0.0f), LightEnergy = 0.5f });
        _studio.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.8f, 0.78f, 0.72f),
                AmbientLightEnergy = 0.7f,
            },
        });
        _stage = new Node3D();
        _studio.AddChild(_stage);
    }

    private void Arrange()
    {
        foreach (Control? child in new Control?[] { _under, _picture, _over })
        {
            if (child != null)
            {
                child.Position = Vector2.Zero;
                child.Size = Size;
            }
        }
        PlaceCamera();
        Repaint();
    }

    public override void _Process(double delta)
    {
        float target = YawDegrees + 90.0f * Turn;
        if (Mathf.IsEqualApprox(_yaw, target))
        {
            return;
        }
        float step = 90.0f / Mathf.Max(TurnTime, 0.01f) * (float)delta;
        _yaw = Mathf.MoveToward(_yaw, target, step);
        PlaceCamera();
        Repaint();
    }

    /// <summary>Shows a layout: the models are rebuilt, the lines redrawn.</summary>
    public void ShowLayout(PackLayout layout)
    {
        Layout = layout;
        RebuildModels();
        PlaceCamera();
        Repaint();
    }

    /// <summary>Turns the view by quarter steps (positive: counter-clockwise seen from above).</summary>
    public void TurnBy(int quarters)
    {
        Turn += quarters;
        // The yaw keeps counting so the turn animates the short way; Turn is reported as 0 to 3.
        if (Turn > 3 || Turn < 0)
        {
            int wrapped = Mathf.PosMod(Turn, 4);
            _yaw += 90.0f * (wrapped - Turn);
            Turn = wrapped;
        }
    }

    /// <summary>Turns the view at once, without the animation (tests, start-up).</summary>
    public void SetTurnNow(int turn)
    {
        Turn = Mathf.PosMod(turn, 4);
        _yaw = YawDegrees + 90.0f * Turn;
        PlaceCamera();
        Repaint();
    }

    /// <summary>Cuts away everything standing at or above a height in cells, to see underneath.</summary>
    public void SetCutHeight(int height)
    {
        int top = Mathf.Max(1, Layout.GridSize.Y);
        int cut = Mathf.Clamp(height, 1, top);
        if (cut >= top)
        {
            cut = int.MaxValue;
        }
        if (cut == CutHeight)
        {
            return;
        }
        CutHeight = cut;
        ApplyVisibility();
        Repaint();
    }

    public void SetHover(string instanceId)
    {
        if (instanceId != HoverId)
        {
            HoverId = instanceId;
            Repaint();
        }
    }

    /// <summary>The copy lifted into the hand ("" for none): it leaves its place in the picture.</summary>
    public void SetHeld(string instanceId)
    {
        if (instanceId == HeldId)
        {
            return;
        }
        HeldId = instanceId;
        _heldModel?.QueueFree();
        _heldModel = null;
        if (HeldId.Length == 0)
        {
            _ghost = false;
        }
        ApplyVisibility();
        Repaint();
    }

    /// <summary>Draws the held copy where it would go: green if it fits there, red if not.</summary>
    public void ShowGhost(Vector3I origin, Vector3I size, int orientation, bool valid)
    {
        _ghost = true;
        _ghostValid = valid;
        _ghostOrigin = origin;
        _ghostSize = size;
        if (_stage != null && Layout.TryFind(HeldId, out PackLayoutEntry held))
        {
            if (_heldModel == null)
            {
                _heldModel = BuildModel(held);
                if (_heldModel != null)
                {
                    _stage.AddChild(_heldModel);
                }
            }
            if (_heldModel != null)
            {
                _heldModel.Visible = true;
                _heldModel.Transform = new Transform3D(GridOrientations.All[orientation], PackGeometry.CellBox(Layout, origin, size, 0.0f).GetCenter());
            }
        }
        Repaint();
    }

    public void HideGhost()
    {
        if (!_ghost)
        {
            return;
        }
        _ghost = false;
        if (_heldModel != null)
        {
            _heldModel.Visible = false;
        }
        Repaint();
    }

    /// <summary>The packed copy at a point of the view: the nearest one, or the one behind it.</summary>
    public bool EntryAt(Vector2 at, bool behind, out PackLayoutEntry entry)
    {
        Ray(at, out Vector3 from, out Vector3 direction);
        List<PackLayoutEntry> hits = PackGeometry.EntriesAlong(Layout, from, direction, CutHeight, HeldId);
        entry = default;
        if (hits.Count == 0)
        {
            return false;
        }
        entry = hits[0];
        if (behind && hits.Count > 1)
        {
            entry = hits[1];
        }
        return true;
    }

    /// <summary>
    /// The grid column a box of <paramref name="size"/> cells would drop into from a point of the
    /// view; false when the pointer is off the pack's open top.
    /// </summary>
    public bool ColumnAt(Vector2 at, Vector3I size, out Vector2I column)
    {
        column = Vector2I.Zero;
        Ray(at, out Vector3 from, out Vector3 direction);
        Vector3 pack = Layout.SizeMeters;
        float top = Mathf.Min(CutHeight, Layout.GridSize.Y) * Layout.CellSize;
        if (!PackGeometry.PlaneHit(top, from, direction, out Vector3 point))
        {
            return false;
        }
        float slack = Layout.CellSize;
        if (Mathf.Abs(point.X) > pack.X * 0.5f + slack || Mathf.Abs(point.Z) > pack.Z * 0.5f + slack)
        {
            return false;
        }
        column = PackGeometry.ColumnAt(Layout, point, size);
        return true;
    }

    /// <summary>A point of the view as a ray in pack space (the view is orthographic: every ray runs the same way).</summary>
    public void Ray(Vector2 at, out Vector3 from, out Vector3 direction)
    {
        Frame(out Vector3 target, out Vector3 forward, out Vector3 right, out Vector3 up, out float metresPerPixel);
        Vector2 offset = (at - Size * 0.5f) * metresPerPixel;
        from = target - forward * CameraDistance() + right * offset.X - up * offset.Y;
        direction = forward;
    }

    /// <summary>A pack-space point as a point of the view.</summary>
    public Vector2 Project(Vector3 point)
    {
        Frame(out Vector3 target, out Vector3 _, out Vector3 right, out Vector3 up, out float metresPerPixel);
        Vector3 d = point - target;
        return Size * 0.5f + new Vector2(d.Dot(right), -d.Dot(up)) / metresPerPixel;
    }

    private float CameraDistance()
    {
        return Layout.SizeMeters.Length() + 2.0f;
    }

    /// <summary>The view's frame: what it looks at, its axes in pack space and the scale of the picture.</summary>
    private void Frame(out Vector3 target, out Vector3 forward, out Vector3 right, out Vector3 up, out float metresPerPixel)
    {
        Vector3 pack = Layout.SizeMeters;
        target = new Vector3(0.0f, pack.Y * 0.5f, 0.0f);
        float yaw = Mathf.DegToRad(_yaw);
        float pitch = Mathf.DegToRad(PitchDegrees);
        forward = new Vector3(-Mathf.Sin(yaw) * Mathf.Cos(pitch), -Mathf.Sin(pitch), -Mathf.Cos(yaw) * Mathf.Cos(pitch));
        right = forward.Cross(Vector3.Up).Normalized();
        up = right.Cross(forward);
        metresPerPixel = ViewHeightMetres() / Mathf.Max(Size.Y, 1.0f);
    }

    /// <summary>How many metres the view shows from top to bottom: the whole pack from any side, with a margin.</summary>
    private float ViewHeightMetres()
    {
        // The same from every side, so the pack doesn't swell and shrink as the view turns.
        Vector3 pack = Layout.SizeMeters;
        float pitch = Mathf.DegToRad(PitchDegrees);
        float width = Mathf.Max(new Vector2(pack.X, pack.Z).Length(), 0.1f);
        float height = pack.Y * Mathf.Cos(pitch) + width * Mathf.Sin(pitch);
        float aspect = Mathf.Max(Size.X / Mathf.Max(Size.Y, 1.0f), 0.01f);
        return Mathf.Max(height, width / aspect) * Margin;
    }

    private void PlaceCamera()
    {
        if (_camera == null)
        {
            return;
        }
        Frame(out Vector3 target, out Vector3 forward, out Vector3 right, out Vector3 up, out float _);
        _camera.Size = ViewHeightMetres();
        _camera.Transform = new Transform3D(new Basis(right, up, -forward), target - forward * CameraDistance());
    }

    private void RebuildModels()
    {
        if (_stage == null)
        {
            return;
        }
        foreach (Node3D model in _models.Values)
        {
            model.QueueFree();
        }
        _models.Clear();
        _heldModel?.QueueFree();
        _heldModel = null;
        foreach (PackLayoutEntry entry in Layout.Entries)
        {
            if (!entry.Packed)
            {
                continue;
            }
            Node3D? model = BuildModel(entry);
            if (model == null)
            {
                continue;
            }
            model.Transform = new Transform3D(GridOrientations.All[entry.Orientation], PackGeometry.CellBox(Layout, entry.Origin, entry.Size, 0.0f).GetCenter());
            _stage.AddChild(model);
            _models[entry.InstanceId] = model;
        }
        ApplyVisibility();
    }

    /// <summary>
    /// A copy's model fitted into its block of cells (unturned, centred on the origin, keeping its
    /// proportions); the caller turns and places it.
    /// </summary>
    private Node3D? BuildModel(PackLayoutEntry entry)
    {
        if (ItemText.AtPath(entry.ItemPath) is not ItemDefinition item)
        {
            return null;
        }
        Node3D? holder = ItemVisual.BuildModel(item);
        if (holder == null)
        {
            return null;
        }
        Vector3 block = (Vector3)item.SizeCells * Layout.CellSize - Vector3.One * ItemInset * 2.0f;
        Aabb bounds = ModelFit.Bounds(holder);
        Vector3 extent = new(Mathf.Max(bounds.Size.X, 1e-4f), Mathf.Max(bounds.Size.Y, 1e-4f), Mathf.Max(bounds.Size.Z, 1e-4f));
        // Fill the block it takes, but never stretch a model far out of its own proportions.
        float fit = Mathf.Min(block.X / extent.X, Mathf.Min(block.Y / extent.Y, block.Z / extent.Z));
        float most = fit * Mathf.Max(MaxStretch, 1.0f);
        Vector3 scale = new(Mathf.Min(block.X / extent.X, most), Mathf.Min(block.Y / extent.Y, most), Mathf.Min(block.Z / extent.Z, most));
        holder.Transform = new Transform3D(Basis.FromScale(scale), -bounds.GetCenter() * scale);
        Node3D pivot = new();
        pivot.AddChild(holder);
        return pivot;
    }

    private void ApplyVisibility()
    {
        foreach (PackLayoutEntry entry in Layout.Entries)
        {
            if (_models.TryGetValue(entry.InstanceId, out Node3D? model))
            {
                model.Visible = entry.Origin.Y < CutHeight && entry.InstanceId != HeldId;
            }
        }
    }

    private void Repaint()
    {
        _under?.QueueRedraw();
        _over?.QueueRedraw();
    }

    /// <summary>Behind the items: the pack's floor with its cells, and the two walls on the far side.</summary>
    private void DrawUnder(Control canvas)
    {
        if (Layout.GridSize.X <= 0)
        {
            return;
        }
        Vector3 pack = Layout.SizeMeters;
        Vector3 half = new(pack.X * 0.5f, 0.0f, pack.Z * 0.5f);
        Frame(out Vector3 _, out Vector3 forward, out Vector3 _, out Vector3 _, out float _);
        Vector3[] floor =
        {
            new(-half.X, 0.0f, -half.Z), new(half.X, 0.0f, -half.Z), new(half.X, 0.0f, half.Z), new(-half.X, 0.0f, half.Z),
        };
        canvas.DrawColoredPolygon(ProjectAll(floor), WallFill);
        for (int x = 0; x <= Layout.GridSize.X; x++)
        {
            float at = -half.X + x * Layout.CellSize;
            canvas.DrawLine(Project(new Vector3(at, 0.0f, -half.Z)), Project(new Vector3(at, 0.0f, half.Z)), UiStyle.InkSoft, 1.0f, true);
        }
        for (int z = 0; z <= Layout.GridSize.Z; z++)
        {
            float at = -half.Z + z * Layout.CellSize;
            canvas.DrawLine(Project(new Vector3(-half.X, 0.0f, at)), Project(new Vector3(half.X, 0.0f, at)), UiStyle.InkSoft, 1.0f, true);
        }
        // A wall is on the far side when it faces the same way the view looks.
        for (int side = 0; side < 4; side++)
        {
            Vector3 a = floor[side];
            Vector3 b = floor[(side + 1) % 4];
            Vector3 normal = (b - a).Cross(Vector3.Up);
            if (normal.Dot(forward) <= 0.0f)
            {
                continue;
            }
            Vector3 lift = Vector3.Up * pack.Y;
            Vector2[] wall = ProjectAll(new[] { a, b, b + lift, a + lift });
            canvas.DrawColoredPolygon(wall, WallFill);
            for (int y = 2; y < Layout.GridSize.Y; y += 2)
            {
                Vector3 rise = Vector3.Up * (y * Layout.CellSize);
                canvas.DrawLine(Project(a + rise), Project(b + rise), new Color(UiStyle.Ink, 0.18f), 1.0f, true);
            }
            canvas.DrawPolyline(new[] { wall[0], wall[1], wall[2], wall[3], wall[0] }, UiStyle.Ink, 2.0f, true);
        }
        // Each thing's block of cells as paper behind its model, far ones first, so a full pack reads as full.
        List<(float Depth, Aabb Box)> blocks = new();
        foreach (PackLayoutEntry entry in Layout.Entries)
        {
            if (entry.Packed && entry.InstanceId != HeldId && entry.Origin.Y < CutHeight)
            {
                Aabb box = PackGeometry.CellBox(Layout, entry.Origin, entry.Size, ItemInset);
                blocks.Add((box.GetCenter().Dot(forward), box));
            }
        }
        blocks.Sort((a, b) => b.Depth.CompareTo(a.Depth));
        foreach ((float _, Aabb box) in blocks)
        {
            DrawBox(canvas, box, BlockFill, new Color(UiStyle.Ink, 0.35f), 1.0f);
        }
    }

    /// <summary>In front of the items: the near rim, what was cut away, the hovered copy, the ghost and the centre of mass.</summary>
    private void DrawOver(Control canvas)
    {
        if (Layout.GridSize.X <= 0)
        {
            return;
        }
        Vector3 pack = Layout.SizeMeters;
        Aabb shell = new(new Vector3(-pack.X * 0.5f, 0.0f, -pack.Z * 0.5f), pack);
        DrawBox(canvas, shell, new Color(0, 0, 0, 0), UiStyle.InkSoft, 1.5f);
        foreach (PackLayoutEntry entry in Layout.Entries)
        {
            if (!entry.Packed || entry.InstanceId == HeldId)
            {
                continue;
            }
            Aabb box = PackGeometry.CellBox(Layout, entry.Origin, entry.Size, ItemInset);
            if (entry.Origin.Y >= CutHeight)
            {
                DrawBox(canvas, box, new Color(0, 0, 0, 0), new Color(UiStyle.Ink, 0.22f), 1.0f);
            }
            else if (_studio == null)
            {
                // No renderer (headless): an inked block stands in for the model.
                DrawBox(canvas, box, new Color(UiStyle.PaperDark, 0.8f), UiStyle.Ink, 1.5f);
            }
            if (entry.InstanceId == HoverId)
            {
                DrawBox(canvas, box, new Color(UiStyle.Highlight, 0.18f), UiStyle.Highlight, 3.0f);
            }
        }
        if (_ghost)
        {
            Color fill = InvalidFill;
            if (_ghostValid)
            {
                fill = ValidFill;
            }
            DrawBox(canvas, PackGeometry.CellBox(Layout, _ghostOrigin, _ghostSize, 0.0f), fill, new Color(fill, 1.0f), 2.5f);
        }
        // The centre of mass: a dot, and a plumb line down to where it stands over the floor.
        Vector3 centre = PackGeometry.GridToPack(Layout, Layout.CenterOfMass);
        Vector2 dot = Project(centre);
        Vector2 foot = Project(new Vector3(centre.X, 0.0f, centre.Z));
        canvas.DrawDashedLine(dot, foot, UiStyle.Warning, 1.5f, 5.0f);
        canvas.DrawCircle(foot, MarkerRadius * 0.45f, new Color(UiStyle.Warning, 0.6f));
        canvas.DrawCircle(dot, MarkerRadius, UiStyle.Warning);
        canvas.DrawArc(dot, MarkerRadius, 0.0f, Mathf.Tau, 24, UiStyle.Ink, 1.5f, true);
    }

    /// <summary>A box drawn by the faces that look at the viewer: filled, then outlined.</summary>
    private void DrawBox(Control canvas, Aabb box, Color fill, Color line, float width)
    {
        Frame(out Vector3 _, out Vector3 forward, out Vector3 _, out Vector3 _, out float _);
        Vector3 p = box.Position;
        Vector3 e = box.End;
        for (int axis = 0; axis < 3; axis++)
        {
            int u = (axis + 1) % 3;
            int v = (axis + 2) % 3;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector3 normal = Vector3.Zero;
                normal[axis] = sign;
                if (normal.Dot(forward) >= 0.0f)
                {
                    continue;
                }
                Vector3 corner = p;
                if (sign > 0)
                {
                    corner[axis] = e[axis];
                }
                Vector3 du = Vector3.Zero;
                du[u] = e[u] - p[u];
                Vector3 dv = Vector3.Zero;
                dv[v] = e[v] - p[v];
                Vector2[] face = ProjectAll(new[] { corner, corner + du, corner + du + dv, corner + dv });
                if (fill.A > 0.0f)
                {
                    canvas.DrawColoredPolygon(face, fill);
                }
                canvas.DrawPolyline(new[] { face[0], face[1], face[2], face[3], face[0] }, line, width, true);
            }
        }
    }

    private Vector2[] ProjectAll(Vector3[] points)
    {
        Vector2[] flat = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            flat[i] = Project(points[i]);
        }
        return flat;
    }

    /// <summary>A transparent sheet over or under the models that the view draws its lines on.</summary>
    private partial class Layer : Control
    {
        public System.Action<Control>? Paint { get; set; }

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            Paint?.Invoke(this);
        }
    }
}
