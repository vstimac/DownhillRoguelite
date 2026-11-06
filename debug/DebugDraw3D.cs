using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Autoload for immediate-mode debug lines, drawn on top of everything. Call the static methods
/// from _PhysicsProcess: lines stay visible until the next physics tick, so they don't flicker
/// when the frame rate is higher than the physics rate. F3 toggles all debug drawing.
/// </summary>
public partial class DebugDraw3D : Node3D
{
    private static readonly StringName ActionToggle = "debug_toggle_overlay";
    private static readonly List<(Vector3 From, Vector3 To, Color Color)> Lines = new();
    private static bool _dirty;

    /// <summary>Whether debug drawing and debug overlays are shown.</summary>
    public static bool Enabled { get; private set; } = true;

    private ImmediateMesh? _mesh;
    private StandardMaterial3D? _material;

    public override void _Ready()
    {
        // Run before every other physics callback so this tick's lines replace the last tick's.
        ProcessPhysicsPriority = int.MinValue;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;

        _mesh = new ImmediateMesh();
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            // Colors passed in are sRGB like everywhere else in Godot; without this they render washed out.
            VertexColorIsSrgb = true,
            NoDepthTest = true,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = _mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Lines.Count > 0)
        {
            Lines.Clear();
            _dirty = true;
        }
    }

    public override void _Process(double delta)
    {
        if (_dirty)
        {
            Rebuild();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(ActionToggle))
        {
            Enabled = !Enabled;
            _dirty = true;
        }
    }

    public static void Line(Vector3 from, Vector3 to, Color color)
    {
        if (!Enabled)
        {
            return;
        }
        Lines.Add((from, to, color));
        _dirty = true;
    }

    public static void Arrow(Vector3 from, Vector3 to, Color color, float headSize = 0.12f)
    {
        Vector3 shaft = to - from;
        if (!Enabled || shaft.IsZeroApprox())
        {
            return;
        }

        Vector3 direction = shaft.Normalized();
        Vector3 side = direction.Cross(Vector3.Up);
        if (side.IsZeroApprox())
        {
            side = direction.Cross(Vector3.Right);
        }
        side = side.Normalized() * headSize * 0.5f;
        Vector3 back = to - direction * headSize;

        Line(from, to, color);
        Line(to, back + side, color);
        Line(to, back - side, color);
    }

    public static void Cross(Vector3 at, Color color, float size = 0.1f)
    {
        Line(at - Vector3.Right * size, at + Vector3.Right * size, color);
        Line(at - Vector3.Up * size, at + Vector3.Up * size, color);
        Line(at - Vector3.Back * size, at + Vector3.Back * size, color);
    }

    private void Rebuild()
    {
        _dirty = false;
        if (_mesh == null)
        {
            return;
        }

        _mesh.ClearSurfaces();
        if (!Enabled || Lines.Count == 0)
        {
            return;
        }

        _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _material);
        foreach ((Vector3 from, Vector3 to, Color color) in Lines)
        {
            _mesh.SurfaceSetColor(color);
            _mesh.SurfaceAddVertex(from);
            _mesh.SurfaceSetColor(color);
            _mesh.SurfaceAddVertex(to);
        }
        _mesh.SurfaceEnd();
    }
}
