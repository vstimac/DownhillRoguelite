using DownhillRoguelite.Core;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug overlay for terrain: the spine ahead of the carrier, the borders of the carrier's chunk,
/// and a text readout (seed, depth, circle, slope and surface underfoot, chunks loaded, build and
/// add times). Hidden with F3 and while packing.
/// </summary>
public partial class TerrainDebugOverlay : CanvasLayer
{
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public Color SpineColor { get; set; } = new(1.0f, 0.85f, 0.2f);
    [Export] public Color ChunkColor { get; set; } = new(0.3f, 0.9f, 1.0f);
    /// <summary>How far ahead (downhill) and behind the spine is drawn, in m.</summary>
    [Export] public Vector2 SpineRange { get; set; } = new(80.0f, 10.0f);
    [Export] public float SpineStep { get; set; } = 2.0f;
    [Export] public float LineLift { get; set; } = 0.25f;
    [Export] public float ChunkBorderStep { get; set; } = 4.0f;
    /// <summary>Metres of depth per circle; placeholder until circles are a system of their own.</summary>
    [Export] public float CircleDepth { get; set; } = 1000.0f;
    [Export] public string[] CircleNames { get; set; } =
        { "Limbo", "Lust", "Gluttony", "Greed", "Wrath", "Heresy", "Violence", "Fraud", "Treachery" };
    [Export] public int FontSize { get; set; } = 18;
    [Export] public float TextWidth { get; set; } = 420.0f;
    [Export] public float TextTop { get; set; } = 16.0f;

    private Label? _label;
    private LocomotionSnapshot _locomotion;

    public override void _Ready()
    {
        _label = new Label { CustomMinimumSize = new Vector2(TextWidth, 0.0f), HorizontalAlignment = HorizontalAlignment.Center };
        _label.AddThemeFontSizeOverride("font_size", FontSize);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", FontSize / 4);
        AddChild(_label);
        GameEvents.LocomotionUpdated += OnLocomotionUpdated;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotionUpdated;
    }

    private void OnLocomotionUpdated(LocomotionSnapshot snapshot)
    {
        _locomotion = snapshot;
    }

    public override void _PhysicsProcess(double delta)
    {
        TerrainGenerator? generator = Streamer?.Generator;
        if (_label == null || Streamer == null || generator == null)
        {
            return;
        }

        bool show = DebugDraw3D.Enabled && !GameEvents.IsPacking;
        _label.Visible = show;
        if (!show)
        {
            return;
        }

        Vector3 position = _locomotion.Position;
        DrawSpine(generator, position.Z);
        DrawChunk(generator, generator.ChunkAt(position.X, position.Z));

        int circle = Mathf.FloorToInt(Streamer.Depth / CircleDepth);
        string circleName = "beyond the circles";
        if (circle < CircleNames.Length)
        {
            circleName = CircleNames[circle];
        }
        float spineOffset = position.X - generator.SpineX(position.Z);

        _label.Text =
            $"Seed {generator.Seed}   F2 new mountain\n" +
            $"Depth {Streamer.Depth:0} m   Circle {circle + 1}: {circleName}\n" +
            $"Ground {generator.SlopeDegrees(position.X, position.Z):0}°  {_locomotion.Surface}   spine {spineOffset:+0.0;-0.0} m\n" +
            $"Chunks {Streamer.LoadedCount}   build {Streamer.LastBuildMilliseconds:0} ms   add {Streamer.LastApplyMilliseconds:0.0} ms (max {Streamer.MaxApplyMilliseconds:0.0})";
        _label.Position = new Vector2((GetViewport().GetVisibleRect().Size.X - TextWidth) * 0.5f, TextTop);
    }

    private void DrawSpine(TerrainGenerator generator, float z)
    {
        Vector3 previous = SpinePoint(generator, z + SpineRange.Y);
        for (float d = -SpineRange.Y + SpineStep; d <= SpineRange.X; d += SpineStep)
        {
            Vector3 next = SpinePoint(generator, z - d);
            DebugDraw3D.Line(previous, next, SpineColor);
            previous = next;
        }
    }

    private Vector3 SpinePoint(TerrainGenerator generator, float z)
    {
        float x = generator.SpineX(z);
        return new Vector3(x, generator.Height(x, z) + LineLift, z);
    }

    private void DrawChunk(TerrainGenerator generator, Vector2I coord)
    {
        Vector3 corner = generator.ChunkCorner(coord);
        float size = generator.Settings.ChunkSize;
        for (float t = 0.0f; t < size; t += ChunkBorderStep)
        {
            float next = Mathf.Min(t + ChunkBorderStep, size);
            DrawGroundLine(generator, corner + new Vector3(t, 0, 0), corner + new Vector3(next, 0, 0));
            DrawGroundLine(generator, corner + new Vector3(t, 0, size), corner + new Vector3(next, 0, size));
            DrawGroundLine(generator, corner + new Vector3(0, 0, t), corner + new Vector3(0, 0, next));
            DrawGroundLine(generator, corner + new Vector3(size, 0, t), corner + new Vector3(size, 0, next));
        }
    }

    private void DrawGroundLine(TerrainGenerator generator, Vector3 from, Vector3 to)
    {
        from.Y = generator.Height(from.X, from.Z) + LineLift;
        to.Y = generator.Height(to.X, to.Z) + LineLift;
        DebugDraw3D.Line(from, to, ChunkColor);
    }
}
