using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Builds the locomotion test course from primitives when the scene loads, in the editor too.
/// Layout: a start platform at the top, parallel slope lanes of different angles descending
/// toward -Z onto a flat runout, then stairs of increasing step height, a cross-slope and a
/// rock field. A second platform holds same-angle lanes with different SurfaceTypes (tinted).
/// Every body is tagged with a SurfaceType. Generated nodes are not saved into the scene.
/// </summary>
[Tool]
public partial class TestCourseBuilder : Node3D
{
    [Export] public Material? SurfaceMaterial { get; set; }
    /// <summary>SurfaceType for everything except the surface lanes.</summary>
    [Export(PropertyHint.File, "*.tres")] public string DefaultSurfacePath { get; set; } = "res://terrain/surfaces/rock.tres";

    [ExportGroup("Surface lanes")]
    [Export] public float SurfaceLaneAngle { get; set; } = 25.0f;
    [Export] public float SurfaceLanesCenterX { get; set; } = 45.0f;
    [Export] public string[] SurfaceLanePaths { get; set; } =
    {
        "res://terrain/surfaces/rock.tres",
        "res://terrain/surfaces/grass.tres",
        "res://terrain/surfaces/snow.tres",
        "res://terrain/surfaces/mud.tres",
        "res://terrain/surfaces/scree.tres",
    };

    [ExportGroup("Start and lanes")]
    /// <summary>Height of the start platform above the runout; every lane descends this much, in m.</summary>
    [Export] public float Drop { get; set; } = 15.0f;
    [Export] public float StartDepth { get; set; } = 20.0f;
    [Export] public float[] LaneAngles { get; set; } = { 10.0f, 20.0f, 30.0f, 40.0f };
    [Export] public float LaneWidth { get; set; } = 8.0f;
    [Export] public float LaneGap { get; set; } = 1.5f;
    [Export] public float SlabThickness { get; set; } = 1.0f;

    [ExportGroup("Runout")]
    /// <summary>Width (X) and length (-Z from the platform edge) of the flat ground, in m.</summary>
    [Export] public Vector2 GroundSize { get; set; } = new(140.0f, 240.0f);

    [ExportGroup("Stairs")]
    [Export] public float[] StepHeights { get; set; } = { 0.1f, 0.2f, 0.3f, 0.4f };
    [Export] public int StepCount { get; set; } = 4;
    [Export] public float StepDepth { get; set; } = 1.2f;
    [Export] public float StepWidth { get; set; } = 3.0f;
    [Export] public float StairSpacing { get; set; } = 6.0f;
    /// <summary>Z of the first step's front face.</summary>
    [Export] public float StairsZ { get; set; } = -110.0f;
    /// <summary>Distance in front of each staircase where tests start walking, in m.</summary>
    [Export] public float StairApproach { get; set; } = 3.0f;

    [ExportGroup("Cross slope")]
    [Export] public float CrossSlopeAngle { get; set; } = 15.0f;
    /// <summary>Width (X) and length (Z) of the tilted slab, in m.</summary>
    [Export] public Vector2 CrossSlopeSize { get; set; } = new(12.0f, 30.0f);
    [Export] public Vector3 CrossSlopeCenter { get; set; } = new(-30.0f, 0.0f, -140.0f);

    [ExportGroup("Rock field")]
    [Export] public Vector3 RockFieldCenter { get; set; } = new(25.0f, 0.0f, -150.0f);
    [Export] public Vector2 RockFieldSize { get; set; } = new(16.0f, 24.0f);
    [Export] public int RockCount { get; set; } = 70;
    /// <summary>Smallest and largest rock edge length, in m.</summary>
    [Export] public Vector2 RockSizeRange { get; set; } = new(0.2f, 0.7f);
    [Export] public float RockMaxTilt { get; set; } = 30.0f;
    /// <summary>How far each rock's centre sits below the ground, as a fraction of its height.</summary>
    [Export] public float RockBuryFraction { get; set; } = 0.25f;
    [Export] public ulong RockSeed { get; set; } = 1234;

    [ExportGroup("Labels")]
    [Export] public float LabelHeight { get; set; } = 1.5f;
    [Export] public int LabelFontSize { get; set; } = 96;
    [Export] public float LabelPixelSize { get; set; } = 0.01f;

    private Node3D? _generated;
    private SurfaceType? _defaultSurface;
    private readonly List<SurfaceType> _surfaceLaneTypes = new();
    private readonly Dictionary<SurfaceType, Material> _surfaceMaterials = new();

    public override void _Ready()
    {
        Rebuild();
    }

    public override void _ExitTree()
    {
        // Managed references keep Godot resources alive until the GC runs, which can be after exit.
        // Script resources (SurfaceType) are owned by Godot, so only drop them; the engine-class
        // material wrappers hold a reference of their own, so release it.
        foreach (Material material in _surfaceMaterials.Values)
        {
            material.Dispose();
        }
        _surfaceMaterials.Clear();
        _surfaceLaneTypes.Clear();
        _defaultSurface = null;
    }

    public void Rebuild()
    {
        _generated?.QueueFree();
        _generated = new Node3D { Name = "Generated" };
        AddChild(_generated);
        LoadSurfaces();

        BuildStartAndLanes();
        BuildGround();
        BuildStairs();
        BuildCrossSlope();
        BuildRockField();
        BuildSurfaceLanes();
    }

    public IReadOnlyList<SurfaceType> SurfaceLaneTypes => _surfaceLaneTypes;

    /// <summary>Index of the surface lane with this display name, or -1.</summary>
    public int SurfaceLaneIndex(string displayName)
    {
        for (int i = 0; i < _surfaceLaneTypes.Count; i++)
        {
            if (_surfaceLaneTypes[i].DisplayName == displayName)
            {
                return i;
            }
        }
        return -1;
    }

    public float SurfaceLaneX(int lane)
    {
        float total = RowWidth(_surfaceLaneTypes.Count);
        return SurfaceLanesCenterX - total * 0.5f + LaneWidth * 0.5f + lane * (LaneWidth + LaneGap);
    }

    public float SurfaceLaneLength()
    {
        return Drop / Mathf.Sin(Mathf.DegToRad(SurfaceLaneAngle));
    }

    /// <summary>Point on a surface lane, measured along the slope from its top edge.</summary>
    public Vector3 SurfaceLanePoint(int lane, float distanceFromTop)
    {
        return SlopePoint(SurfaceLaneX(lane), SurfaceLaneAngle, distanceFromTop);
    }

    /// <summary>Centre of the start platform's top surface.</summary>
    public Vector3 StartPoint => new(0.0f, Drop, StartDepth * 0.5f);

    public float LaneX(int lane)
    {
        float total = RowWidth(LaneAngles.Length);
        return -total * 0.5f + LaneWidth * 0.5f + lane * (LaneWidth + LaneGap);
    }

    public float LaneLength(int lane)
    {
        return Drop / Mathf.Sin(Mathf.DegToRad(LaneAngles[lane]));
    }

    /// <summary>Point on a lane's surface, measured along the slope from its top edge.</summary>
    public Vector3 LanePoint(int lane, float distanceFromTop)
    {
        return SlopePoint(LaneX(lane), LaneAngles[lane], distanceFromTop);
    }

    private Vector3 SlopePoint(float x, float angleDegrees, float distanceFromTop)
    {
        float angle = Mathf.DegToRad(angleDegrees);
        return new Vector3(x, Drop - distanceFromTop * Mathf.Sin(angle), -distanceFromTop * Mathf.Cos(angle));
    }

    public float StairX(int stair)
    {
        return (stair - (StepHeights.Length - 1) * 0.5f) * StairSpacing;
    }

    /// <summary>Ground point in front of a staircase.</summary>
    public Vector3 StairStart(int stair)
    {
        return new Vector3(StairX(stair), 0.0f, StairsZ + StairApproach);
    }

    /// <summary>Point on the raised half of the cross-slope, one metre in from its +Z end.</summary>
    public Vector3 CrossSlopeStart()
    {
        float sideOffset = CrossSlopeSize.X * 0.25f;
        float height = sideOffset * Mathf.Tan(Mathf.DegToRad(CrossSlopeAngle));
        return new Vector3(CrossSlopeCenter.X + sideOffset, height, CrossSlopeCenter.Z + CrossSlopeSize.Y * 0.5f - 1.0f);
    }

    /// <summary>Ground point just in front of the rock field's +Z edge.</summary>
    public Vector3 RockFieldStart()
    {
        return new Vector3(RockFieldCenter.X, 0.0f, RockFieldCenter.Z + RockFieldSize.Y * 0.5f + 2.0f);
    }

    private float RowWidth(int laneCount)
    {
        return laneCount * LaneWidth + Mathf.Max(0, laneCount - 1) * LaneGap;
    }

    private void LoadSurfaces()
    {
        _defaultSurface = LoadSurface(DefaultSurfacePath);
        _surfaceLaneTypes.Clear();
        foreach (string path in SurfaceLanePaths)
        {
            SurfaceType? surface = LoadSurface(path);
            if (surface != null)
            {
                _surfaceLaneTypes.Add(surface);
            }
        }
    }

    private static SurfaceType? LoadSurface(string path)
    {
        if (string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path))
        {
            return null;
        }
        // While the editor imports, the C# script may not be attached yet and the file loads as a
        // plain Resource; Load<T> would throw, so cast softly and skip tagging in that case.
        return ResourceLoader.Load(path) as SurfaceType;
    }

    private void BuildStartAndLanes()
    {
        float total = RowWidth(LaneAngles.Length);
        AddBox("StartPlatform", new Vector3(total, Drop, StartDepth),
            new Transform3D(Basis.Identity, new Vector3(0.0f, Drop * 0.5f, StartDepth * 0.5f)));

        for (int lane = 0; lane < LaneAngles.Length; lane++)
        {
            AddSlopeLane($"Lane{LaneAngles[lane]:0}", LaneX(lane), LaneAngles[lane], null);
            AddLabel($"{LaneAngles[lane]:0}°", new Vector3(LaneX(lane), Drop + LabelHeight, LabelHeight));
        }
    }

    private void BuildSurfaceLanes()
    {
        if (_surfaceLaneTypes.Count == 0)
        {
            return;
        }

        float total = RowWidth(_surfaceLaneTypes.Count);
        AddBox("SurfacePlatform", new Vector3(total, Drop, StartDepth),
            new Transform3D(Basis.Identity, new Vector3(SurfaceLanesCenterX, Drop * 0.5f, StartDepth * 0.5f)));

        for (int lane = 0; lane < _surfaceLaneTypes.Count; lane++)
        {
            SurfaceType surface = _surfaceLaneTypes[lane];
            AddSlopeLane($"Surface{surface.DisplayName}", SurfaceLaneX(lane), SurfaceLaneAngle, surface);
            AddLabel($"{surface.DisplayName} {SurfaceLaneAngle:0}°", new Vector3(SurfaceLaneX(lane), Drop + LabelHeight, LabelHeight));
        }
    }

    /// <summary>A slab descending toward -Z from the platform edge (z = 0, y = Drop) to the runout.</summary>
    private void AddSlopeLane(string name, float x, float angleDegrees, SurfaceType? surface)
    {
        float angle = Mathf.DegToRad(angleDegrees);
        float length = Drop / Mathf.Sin(angle);

        // Rotating about +X by -angle points the slab's -Z down the slope.
        Basis basis = new(Vector3.Right, -angle);
        Vector3 topMid = SlopePoint(x, angleDegrees, length * 0.5f);
        Vector3 center = topMid - basis.Y * SlabThickness * 0.5f;
        AddBox(name, new Vector3(LaneWidth, SlabThickness, length), new Transform3D(basis, center), surface);
    }

    private void BuildGround()
    {
        AddBox("Runout", new Vector3(GroundSize.X, 1.0f, GroundSize.Y),
            new Transform3D(Basis.Identity, new Vector3(0.0f, -0.5f, -GroundSize.Y * 0.5f)));
    }

    private void BuildStairs()
    {
        for (int stair = 0; stair < StepHeights.Length; stair++)
        {
            float rise = StepHeights[stair];
            float x = StairX(stair);
            for (int step = 1; step <= StepCount; step++)
            {
                float height = rise * step;
                float z = StairsZ - (step - 1) * StepDepth - StepDepth * 0.5f;
                AddBox($"Stair{rise * 100.0f:0}cm_{step}", new Vector3(StepWidth, height, StepDepth),
                    new Transform3D(Basis.Identity, new Vector3(x, height * 0.5f, z)));
            }

            AddLabel($"{rise * 100.0f:0} cm", new Vector3(x, rise * StepCount + LabelHeight, StairsZ - StepCount * StepDepth));
        }
    }

    private void BuildCrossSlope()
    {
        // Rolled about Z with its centre line at ground level: the +X half rises out of the runout.
        Basis basis = new(Vector3.Back, Mathf.DegToRad(CrossSlopeAngle));
        Vector3 center = CrossSlopeCenter - basis.Y * SlabThickness * 0.5f;
        AddBox("CrossSlope", new Vector3(CrossSlopeSize.X, SlabThickness, CrossSlopeSize.Y), new Transform3D(basis, center));

        AddLabel($"{CrossSlopeAngle:0}° cross", CrossSlopeStart() + Vector3.Up * LabelHeight);
    }

    private void BuildRockField()
    {
        RandomNumberGenerator rng = new() { Seed = RockSeed };
        float maxTilt = Mathf.DegToRad(RockMaxTilt);

        for (int i = 0; i < RockCount; i++)
        {
            Vector3 size = new(
                rng.RandfRange(RockSizeRange.X, RockSizeRange.Y),
                rng.RandfRange(RockSizeRange.X, RockSizeRange.Y),
                rng.RandfRange(RockSizeRange.X, RockSizeRange.Y));
            Vector3 position = new(
                RockFieldCenter.X + rng.RandfRange(-0.5f, 0.5f) * RockFieldSize.X,
                -size.Y * RockBuryFraction,
                RockFieldCenter.Z + rng.RandfRange(-0.5f, 0.5f) * RockFieldSize.Y);
            Basis basis = Basis.FromEuler(new Vector3(
                rng.RandfRange(-maxTilt, maxTilt),
                rng.RandfRange(-Mathf.Pi, Mathf.Pi),
                rng.RandfRange(-maxTilt, maxTilt)));
            AddBox($"Rock{i}", size, new Transform3D(basis, position));
        }

        AddLabel("rock field", RockFieldStart() + Vector3.Up * LabelHeight);
    }

    private void AddBox(string name, Vector3 size, Transform3D transform, SurfaceType? surface = null)
    {
        SurfaceType? tag = surface ?? _defaultSurface;
        Material? material = SurfaceMaterial;
        if (surface != null)
        {
            material = TintedMaterial(surface);
        }

        StaticBody3D body = new() { Name = name, Transform = transform };
        if (tag != null)
        {
            body.SetMeta(SurfaceType.MetaKey, tag);
        }
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        _generated?.AddChild(body);
    }

    /// <summary>The checker material recoloured with the surface's debug colour.</summary>
    private Material? TintedMaterial(SurfaceType surface)
    {
        if (_surfaceMaterials.TryGetValue(surface, out Material? cached))
        {
            return cached;
        }
        if (SurfaceMaterial is not ShaderMaterial checker)
        {
            return SurfaceMaterial;
        }

        ShaderMaterial tinted = (ShaderMaterial)checker.Duplicate();
        tinted.SetShaderParameter("color_a", surface.DebugColor);
        tinted.SetShaderParameter("color_b", surface.DebugColor.Darkened(0.25f));
        _surfaceMaterials[surface] = tinted;
        return tinted;
    }

    private void AddLabel(string text, Vector3 position)
    {
        _generated?.AddChild(new Label3D
        {
            Text = text,
            FontSize = LabelFontSize,
            PixelSize = LabelPixelSize,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = position,
        });
    }
}
