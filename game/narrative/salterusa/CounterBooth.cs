using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// The šalteruša's booth (narrative/salterusa/Salterusa.cs): a little counter office on a raised floor,
/// built from boxes. Its front (+Z) is a counter wall with a ledge and a pane of glass above it, a slot
/// between the two for papers; inside, a desk with a stamp and forms, and her chair. A sign over the
/// glass, opening hours taped to it and a notice on the side door. The floor reaches well below the
/// ground, so the booth stands on a slope; the walls are solid (StationSite's layer for solid props).
/// Every size is in metres, in the booth's own space; the floor's top is at FloorHeight.
/// </summary>
public partial class CounterBooth : Node3D
{
    [Export] public Vector3 Size { get; set; } = new(2.4f, 2.8f, 2.0f);
    /// <summary>How high the floor inside is raised (she sits higher than the people at the counter).</summary>
    [Export] public float FloorHeight { get; set; } = 0.45f;
    /// <summary>How far the floor block reaches below the ground, for slopes.</summary>
    [Export] public float FloorDepth { get; set; } = 1.1f;
    [Export] public float CounterHeight { get; set; } = 1.15f;
    [Export] public float SlotHeight { get; set; } = 0.18f;
    [Export] public float WallThickness { get; set; } = 0.1f;
    [Export] public float RoofOverhang { get; set; } = 0.25f;
    /// <summary>The chair's seat (its top), in the booth's space, and the seat's size.</summary>
    [Export] public Vector3 SeatTop { get; set; } = new(0.0f, 1.0f, -0.12f);
    [Export] public Vector3 SeatSize { get; set; } = new(0.5f, 0.06f, 0.45f);
    [Export] public float DeskHeight { get; set; } = 0.7f;
    [Export] public float DeskDepth { get; set; } = 0.5f;
    [Export] public Color WallColor { get; set; } = new(0.62f, 0.6f, 0.52f);
    [Export] public Color TrimColor { get; set; } = new(0.25f, 0.33f, 0.42f);
    [Export] public Color DeskColor { get; set; } = new(0.45f, 0.33f, 0.22f);
    [Export] public Color GlassColor { get; set; } = new(0.75f, 0.88f, 0.92f, 0.22f);
    [Export] public Color PaperColor { get; set; } = new(0.93f, 0.91f, 0.84f);
    [Export] public string SignText { get; set; } = "ŠALTER";
    [Export] public string HoursText { get; set; } = "RADNO VRIJEME\n8:00 – 8:15\npauza 8:05 – 8:15";
    [Export] public string DoorText { get; set; } = "DOZVOLE ZA SILAZAK\nisključivo na šalteru.\nNe kucati.";
    [Export(PropertyHint.Layers3DPhysics)] public uint SolidLayer { get; set; } = 16;

    /// <summary>Where her voice and the E prompt sit: the slot in the glass, in the booth's space.</summary>
    public Vector3 WindowPoint => new(0.0f, CounterHeight + SlotHeight * 0.5f, Size.Z * 0.5f + 0.15f);

    public override void _Ready()
    {
        Build();
    }

    private void Build()
    {
        StandardMaterial3D wall = Plain(WallColor);
        StandardMaterial3D trim = Plain(TrimColor);
        StandardMaterial3D desk = Plain(DeskColor);
        StandardMaterial3D paper = Plain(PaperColor);
        StandardMaterial3D glass = new()
        {
            AlbedoColor = GlassColor,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.05f,
            Metallic = 0.2f,
        };
        float halfX = Size.X * 0.5f;
        float halfZ = Size.Z * 0.5f;
        float wallTop = FloorHeight + Size.Y;
        float wallHeight = Size.Y;
        float t = WallThickness;

        // The floor block, its top at FloorHeight, reaching down into the slope.
        Box("Floor", new Vector3(Size.X, FloorHeight + FloorDepth, Size.Z), new Vector3(0.0f, (FloorHeight - FloorDepth) * 0.5f, 0.0f), trim, true);
        Box("BackWall", new Vector3(Size.X, wallHeight, t), new Vector3(0.0f, FloorHeight + wallHeight * 0.5f, -halfZ + t * 0.5f), wall, true);
        Box("LeftWall", new Vector3(t, wallHeight, Size.Z), new Vector3(-halfX + t * 0.5f, FloorHeight + wallHeight * 0.5f, 0.0f), wall, true);
        Box("RightWall", new Vector3(t, wallHeight, Size.Z), new Vector3(halfX - t * 0.5f, FloorHeight + wallHeight * 0.5f, 0.0f), wall, true);
        Box("Roof", new Vector3(Size.X + RoofOverhang * 2.0f, 0.12f, Size.Z + RoofOverhang * 2.0f), new Vector3(0.0f, wallTop + 0.06f, RoofOverhang * 0.5f), trim, false);

        // The front: the counter wall up to the ledge, the slot, the glass, the sign board above.
        float front = halfZ - t * 0.5f;
        Box("Counter", new Vector3(Size.X, CounterHeight, t), new Vector3(0.0f, CounterHeight * 0.5f, front), wall, true);
        Box("Ledge", new Vector3(Size.X, 0.05f, 0.4f), new Vector3(0.0f, CounterHeight + 0.025f, front + 0.12f), desk, false);
        float signHeight = 0.38f;
        float glassBottom = CounterHeight + SlotHeight;
        float glassTop = wallTop - signHeight;
        Box("Glass", new Vector3(Size.X - t * 2.0f, glassTop - glassBottom, 0.02f), new Vector3(0.0f, (glassBottom + glassTop) * 0.5f, front), glass, false);
        Box("SignBoard", new Vector3(Size.X, signHeight, t), new Vector3(0.0f, glassTop + signHeight * 0.5f, front), trim, false);
        // One solid slab for the whole front, so nobody walks through the glass or climbs over the ledge.
        Solid(new Vector3(Size.X, wallTop, t * 2.0f), new Vector3(0.0f, wallTop * 0.5f, front));

        Text("Sign", SignText, new Vector3(0.0f, glassTop + signHeight * 0.5f, front + t * 0.5f + 0.01f), 0.0f, 0.006f, 64, new Color(0.95f, 0.93f, 0.85f));
        Text("Hours", HoursText, new Vector3(halfX - 0.6f, glassTop - 0.3f, front + 0.02f), 0.0f, 0.002f, 40, new Color(0.12f, 0.12f, 0.14f), PaperColor);

        // Inside: the desk with forms and a stamp, the chair.
        float deskY = FloorHeight + DeskHeight;
        Box("Desk", new Vector3(Size.X - t * 2.0f, 0.05f, DeskDepth), new Vector3(0.0f, deskY, front - t * 0.5f - DeskDepth * 0.5f), desk, false);
        Box("Forms", new Vector3(0.32f, 0.05f, 0.24f), new Vector3(-0.5f, deskY + 0.05f, front - 0.3f), paper, false);
        Box("Forms2", new Vector3(0.3f, 0.03f, 0.22f), new Vector3(-0.15f, deskY + 0.04f, front - 0.26f), paper, false);
        MeshInstance3D stamp = new()
        {
            Name = "Stamp",
            Mesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.04f, Height = 0.1f, RadialSegments = 10 },
            Position = new Vector3(0.45f, deskY + 0.075f, front - 0.3f),
            MaterialOverride = trim,
        };
        AddChild(stamp);
        Box("Seat", SeatSize, SeatTop - new Vector3(0.0f, SeatSize.Y * 0.5f, 0.0f), trim, false);
        Box("SeatPost", new Vector3(0.06f, SeatTop.Y - FloorHeight, 0.06f), new Vector3(SeatTop.X, (SeatTop.Y + FloorHeight) * 0.5f, SeatTop.Z), trim, false);
        Box("SeatBack", new Vector3(SeatSize.X, 0.55f, 0.05f), new Vector3(SeatTop.X, SeatTop.Y + 0.33f, SeatTop.Z - SeatSize.Z * 0.5f), trim, false);

        // The side door (staff only) and its notice: "it says so right there on the door".
        float doorX = halfX + 0.01f;
        Box("Door", new Vector3(0.03f, 2.0f, 0.8f), new Vector3(doorX, FloorHeight + 1.0f, -0.25f), trim, false);
        Text("DoorNotice", DoorText, new Vector3(doorX + 0.02f, FloorHeight + 1.45f, -0.25f), 90.0f, 0.0022f, 40, new Color(0.12f, 0.12f, 0.14f), PaperColor);
    }

    private static StandardMaterial3D Plain(Color color)
    {
        return new StandardMaterial3D { AlbedoColor = color, Roughness = 0.9f };
    }

    private void Box(string name, Vector3 size, Vector3 at, Material material, bool solid)
    {
        AddChild(new MeshInstance3D { Name = name, Mesh = new BoxMesh { Size = size }, Position = at, MaterialOverride = material });
        if (solid)
        {
            Solid(size, at);
        }
    }

    private void Solid(Vector3 size, Vector3 at)
    {
        StaticBody3D body = new() { CollisionLayer = SolidLayer, CollisionMask = 0, Position = at };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
    }

    private void Text(string name, string text, Vector3 at, float yaw, float pixelSize, int fontSize, Color color, Color? backing = null)
    {
        if (backing is Color card)
        {
            // A sheet of paper behind the words, a little larger than them.
            string[] lines = text.Split('\n');
            int longest = 0;
            foreach (string line in lines)
            {
                longest = Mathf.Max(longest, line.Length);
            }
            Vector2 sheet = new(longest * fontSize * 0.55f * pixelSize + 0.06f, lines.Length * fontSize * 1.25f * pixelSize + 0.06f);
            Vector3 back = new Vector3(0.0f, 0.0f, -0.006f).Rotated(Vector3.Up, Mathf.DegToRad(yaw));
            MeshInstance3D paper = new()
            {
                Name = name + "Paper",
                Mesh = new QuadMesh { Size = sheet },
                Position = at + back,
                RotationDegrees = new Vector3(0.0f, yaw, 0.0f),
                MaterialOverride = Plain(card),
            };
            AddChild(paper);
        }
        AddChild(new Label3D
        {
            Name = name,
            Text = text,
            Position = at,
            RotationDegrees = new Vector3(0.0f, yaw, 0.0f),
            PixelSize = pixelSize,
            FontSize = fontSize,
            Modulate = color,
            OutlineSize = 0,
            DoubleSided = false,
            Shaded = true,
        });
    }
}
