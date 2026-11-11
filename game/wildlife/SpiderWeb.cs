using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// One spider web strung between two tree trunks (SpiderWebs places them): spokes, a spiral and the
/// anchor threads to both trunks, drawn as fine pale lines. Its local X runs from one trunk to the
/// other, Y is up and Z is the web's facing; the centre hangs at the web's middle.
/// </summary>
public partial class SpiderWeb : Node3D
{
    [Export] public int Spokes { get; set; } = 12;
    [Export] public int Rings { get; set; } = 9;
    [Export] public Color Thread { get; set; } = new(0.86f, 0.87f, 0.92f, 0.55f);

    /// <summary>The trunk pair it hangs between (SpiderWebs keeps torn ones from coming back).</summary>
    public string Key { get; private set; } = "";
    /// <summary>The web's radius, in m.</summary>
    public float Radius { get; private set; } = 1.0f;

    private Vector3 _left;
    private Vector3 _right;
    private ulong _seed;

    /// <summary>Hangs it between <paramref name="left"/> and <paramref name="right"/> (points on the trunks at the web's height).</summary>
    public void Setup(string key, Vector3 left, Vector3 right, float radius, ulong seed)
    {
        Key = key;
        Radius = radius;
        _left = left;
        _right = right;
        _seed = seed;
    }

    public override void _Ready()
    {
        Vector3 across = _right - _left;
        across.Y = 0.0f;
        if (across.LengthSquared() < 0.0001f)
        {
            across = Vector3.Right;
        }
        Vector3 x = across.Normalized();
        Vector3 z = x.Cross(Vector3.Up).Normalized();
        GlobalTransform = new Transform3D(new Basis(x, Vector3.Up, z), (_left + _right) * 0.5f);
        AddChild(new MeshInstance3D
        {
            Name = "Threads",
            Mesh = BuildMesh(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>The carrier's middle at <paramref name="point"/> (world): whether it is in the web's face.</summary>
    public bool Holds(Vector3 point, float thickness)
    {
        Vector3 local = GlobalTransform.AffineInverse() * point;
        return Mathf.Abs(local.Z) < thickness && Mathf.Abs(local.X) < Radius && Mathf.Abs(local.Y) < Radius + 0.5f;
    }

    /// <summary>Whether going from <paramref name="from"/> to <paramref name="to"/> (world) passed through the web.</summary>
    public bool Crossed(Vector3 from, Vector3 to)
    {
        Transform3D inverse = GlobalTransform.AffineInverse();
        Vector3 a = inverse * from;
        Vector3 b = inverse * to;
        if (Mathf.Sign(a.Z) == Mathf.Sign(b.Z) || Mathf.IsEqualApprox(a.Z, b.Z))
        {
            return false;
        }
        float t = a.Z / (a.Z - b.Z);
        Vector3 hit = a.Lerp(b, t);
        return Mathf.Abs(hit.X) < Radius && Mathf.Abs(hit.Y) < Radius + 0.5f;
    }

    private ArrayMesh BuildMesh()
    {
        RandomNumberGenerator random = new() { Seed = _seed };
        Vector3[] rim = new Vector3[Spokes];
        float[] angles = new float[Spokes];
        for (int i = 0; i < Spokes; i++)
        {
            angles[i] = Mathf.Tau * (i + random.RandfRange(-0.2f, 0.2f)) / Spokes;
            float reach = Radius * random.RandfRange(0.82f, 1.0f);
            rim[i] = new Vector3(Mathf.Cos(angles[i]), Mathf.Sin(angles[i]), 0.0f) * reach;
        }
        Vector3[] points = new Vector3[(Spokes * 2) + (Spokes * Rings * 2) + 8];
        int n = 0;
        // Spokes from the hub to the rim.
        for (int i = 0; i < Spokes; i++)
        {
            points[n++] = Vector3.Zero;
            points[n++] = rim[i];
        }
        // The spiral: from spoke to spoke, a little further out each turn.
        for (int ring = 1; ring <= Rings; ring++)
        {
            for (int i = 0; i < Spokes; i++)
            {
                int next = (i + 1) % Spokes;
                float share = (ring + (float)i / Spokes) / (Rings + 1.0f);
                float shareNext = (ring + (float)(i + 1) / Spokes) / (Rings + 1.0f);
                points[n++] = rim[i] * share;
                points[n++] = rim[next] * shareNext;
            }
        }
        // Anchor threads out to both trunks (and up a little on each).
        float half = (_right - _left).Length() * 0.5f;
        Vector3 leftTrunk = new(-half, 0.0f, 0.0f);
        Vector3 rightTrunk = new(half, 0.0f, 0.0f);
        points[n++] = rim[Spokes / 2];
        points[n++] = leftTrunk;
        points[n++] = rim[0];
        points[n++] = rightTrunk;
        points[n++] = rim[Spokes / 4];
        points[n++] = leftTrunk + new Vector3(0.0f, Radius * 0.9f, 0.0f);
        points[n++] = rim[Spokes / 4];
        points[n++] = rightTrunk + new Vector3(0.0f, Radius * 0.9f, 0.0f);

        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points;
        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = Thread,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        return mesh;
    }
}
