using DownhillRoguelite.Core;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Map;

/// <summary>
/// A wooden signpost where a stage's paths split: one arm per path, each pointing at the start of
/// its path and painted with where it leads, Safe (the marked main path), Fast (the hazardous
/// shortcut) or Loot (the supply-rich detour). StationManager places it and aims the arms.
/// </summary>
public partial class JunctionSignpost : Node3D
{
    [Export] public Color WoodColor { get; set; } = new(0.42f, 0.3f, 0.19f);
    [Export] public Color BoardColor { get; set; } = new(0.55f, 0.41f, 0.26f);
    [Export] public Color SafeColor { get; set; } = new(0.78f, 1.0f, 0.74f);
    [Export] public Color FastColor { get; set; } = new(1.0f, 0.6f, 0.42f);
    [Export] public Color LootColor { get; set; } = new(1.0f, 0.86f, 0.45f);
    [Export] public float PostHeight { get; set; } = 2.3f;
    /// <summary>Height of the top arm; the others hang below it.</summary>
    [Export] public float TopArmHeight { get; set; } = 2.0f;
    [Export] public float ArmSpacing { get; set; } = 0.34f;
    [Export] public float ArmLength { get; set; } = 1.1f;
    [Export] public float ArmWidth { get; set; } = 0.24f;
    [Export] public int FontSize { get; set; } = 64;
    [Export] public float PixelSize { get; set; } = 0.0026f;
    [Export] public float LabelRange { get; set; } = 40.0f;

    public int Stage { get; set; }
    /// <summary>The arms' words, top to bottom (tests).</summary>
    public string[] Words { get; private set; } = System.Array.Empty<string>();
    /// <summary>Each arm's pointing direction on the ground (x, z), top to bottom (tests).</summary>
    public Vector2[] Directions { get; private set; } = System.Array.Empty<Vector2>();

    private StandardMaterial3D? _board;

    public override void _Ready()
    {
        StandardMaterial3D wood = new() { AlbedoColor = WoodColor, Roughness = 1.0f };
        _board = new StandardMaterial3D { AlbedoColor = BoardColor, Roughness = 0.95f };
        AddChild(new MeshInstance3D
        {
            Name = "Post",
            Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.075f, Height = PostHeight, RadialSegments = 8 },
            Position = new Vector3(0.0f, PostHeight * 0.5f, 0.0f),
            MaterialOverride = wood,
        });
    }

    /// <summary>The word on a path's arm.</summary>
    public static string Word(RouteKind kind)
    {
        if (kind == RouteKind.Hazardous)
        {
            return "Fast";
        }
        if (kind == RouteKind.SupplyRich)
        {
            return "Loot";
        }
        return "Safe";
    }

    /// <summary>
    /// Puts up one arm per path, pointing along <paramref name="directions"/> (world x, z, from the
    /// post). Earlier arms sit higher.
    /// </summary>
    public void Build(RouteKind[] kinds, Vector2[] directions)
    {
        foreach (Node child in GetChildren())
        {
            if (child.Name.ToString().StartsWith("Arm"))
            {
                child.QueueFree();
            }
        }
        int count = Mathf.Min(kinds.Length, directions.Length);
        Words = new string[count];
        Directions = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 direction = directions[i].Normalized();
            Words[i] = Word(kinds[i]);
            Directions[i] = direction;
            AddChild(MakeArm(i, Words[i], ColorFor(kinds[i]), direction));
        }
    }

    private Node3D MakeArm(int index, string word, Color colour, Vector2 direction)
    {
        // The arm runs along its local +X; turn it about Y so +X points along the direction (x, z).
        Node3D arm = new()
        {
            Name = "Arm" + index,
            Position = new Vector3(0.0f, TopArmHeight - index * ArmSpacing, 0.0f),
            Rotation = new Vector3(0.0f, Mathf.Atan2(-direction.Y, direction.X), 0.0f),
        };
        float board = ArmLength - ArmWidth * 0.5f;
        arm.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(board, ArmWidth, 0.04f) },
            Position = new Vector3(0.05f + board * 0.5f, 0.0f, 0.0f),
            MaterialOverride = _board,
        });
        // The arrow tip: a prism turned to point along +X.
        arm.AddChild(new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(ArmWidth, ArmWidth * 0.6f, 0.04f) },
            Position = new Vector3(0.05f + board + ArmWidth * 0.3f, 0.0f, 0.0f),
            RotationDegrees = new Vector3(0.0f, 0.0f, -90.0f),
            MaterialOverride = _board,
        });
        // The word on both faces, each reading along the arm from the post outwards.
        foreach (float side in new[] { 1.0f, -1.0f })
        {
            Label3D label = new()
            {
                Text = word,
                FontSize = FontSize,
                PixelSize = PixelSize,
                OutlineSize = 10,
                Modulate = colour,
                Billboard = BaseMaterial3D.BillboardModeEnum.Disabled,
                DoubleSided = false,
                Position = new Vector3(0.05f + board * 0.5f, 0.0f, 0.025f * side),
            };
            if (side < 0.0f)
            {
                label.RotationDegrees = new Vector3(0.0f, 180.0f, 0.0f);
            }
            arm.AddChild(WorldLabel.Readable(label, LabelRange));
        }
        return arm;
    }

    private Color ColorFor(RouteKind kind)
    {
        if (kind == RouteKind.Hazardous)
        {
            return FastColor;
        }
        if (kind == RouteKind.SupplyRich)
        {
            return LootColor;
        }
        return SafeColor;
    }
}
