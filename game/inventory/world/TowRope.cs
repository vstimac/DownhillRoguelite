using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// A rope drawn between two points (HandCart: from the carrier's waist to the cart's handle bar): a
/// thin tube along a curve that hangs in a sag when the rope has slack and pulls straight when it is
/// taut. Rebuilt each time its ends move; shown only while something sets them.
/// </summary>
public partial class TowRope : MeshInstance3D
{
    [Export] public float Thickness { get; set; } = 0.018f;
    [Export] public int Segments { get; set; } = 14;
    [Export] public int Sides { get; set; } = 6;
    [Export] public Color RopeColor { get; set; } = new(0.55f, 0.45f, 0.3f);

    /// <summary>How far the middle hangs below the straight line, in m (tests).</summary>
    public float Sag { get; private set; }

    private ImmediateMesh? _mesh;

    public override void _Ready()
    {
        TopLevel = true;
        _mesh = new ImmediateMesh();
        Mesh = _mesh;
        MaterialOverride = new StandardMaterial3D { AlbedoColor = RopeColor, Roughness = 1.0f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        CastShadow = ShadowCastingSetting.On;
        GlobalTransform = Transform3D.Identity;
        Visible = false;
    }

    /// <summary>Hangs the rope from <paramref name="a"/> to <paramref name="b"/>, <paramref name="length"/> long (slack sags).</summary>
    public void Hang(Vector3 a, Vector3 b, float length)
    {
        if (_mesh == null)
        {
            return;
        }
        Visible = true;
        GlobalTransform = Transform3D.Identity;
        float span = a.DistanceTo(b);
        // A rope of this length over this span hangs about this low in the middle (a parabola's sag).
        Sag = 0.0f;
        if (length > span)
        {
            Sag = Mathf.Sqrt(3.0f * span * (length - span) / 8.0f);
        }
        Vector3[] points = new Vector3[Segments + 1];
        for (int i = 0; i <= Segments; i++)
        {
            float t = (float)i / Segments;
            points[i] = a.Lerp(b, t) + Vector3.Down * Sag * 4.0f * t * (1.0f - t);
        }
        _mesh.ClearSurfaces();
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < Segments; i++)
        {
            Ring(points, i, out Vector3[] near, out Vector3[] nearNormals);
            Ring(points, i + 1, out Vector3[] far, out Vector3[] farNormals);
            for (int s = 0; s < Sides; s++)
            {
                int n = (s + 1) % Sides;
                Quad(near[s], nearNormals[s], near[n], nearNormals[n], far[n], farNormals[n], far[s], farNormals[s]);
            }
        }
        _mesh.SurfaceEnd();
    }

    public void Hide3D()
    {
        Visible = false;
        _mesh?.ClearSurfaces();
    }

    /// <summary>The ring of the tube around point <paramref name="i"/>.</summary>
    private void Ring(Vector3[] points, int i, out Vector3[] ring, out Vector3[] normals)
    {
        Vector3 along = points[Mathf.Min(i + 1, points.Length - 1)] - points[Mathf.Max(i - 1, 0)];
        if (along.LengthSquared() < 0.000001f)
        {
            along = Vector3.Forward;
        }
        along = along.Normalized();
        Vector3 side = along.Cross(Vector3.Up);
        if (side.LengthSquared() < 0.0001f)
        {
            side = along.Cross(Vector3.Right);
        }
        side = side.Normalized();
        Vector3 up = side.Cross(along).Normalized();
        ring = new Vector3[Sides];
        normals = new Vector3[Sides];
        for (int s = 0; s < Sides; s++)
        {
            float angle = Mathf.Tau * s / Sides;
            Vector3 normal = side * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            normals[s] = normal;
            ring[s] = points[i] + normal * Thickness;
        }
    }

    private void Quad(Vector3 a, Vector3 na, Vector3 b, Vector3 nb, Vector3 c, Vector3 nc, Vector3 d, Vector3 nd)
    {
        if (_mesh == null)
        {
            return;
        }
        Vertex(a, na);
        Vertex(b, nb);
        Vertex(c, nc);
        Vertex(a, na);
        Vertex(c, nc);
        Vertex(d, nd);
    }

    private void Vertex(Vector3 at, Vector3 normal)
    {
        _mesh!.SurfaceSetNormal(normal);
        _mesh.SurfaceAddVertex(at);
    }
}
