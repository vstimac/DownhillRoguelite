using DownhillRoguelite.Core;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// The "Odmorište Konačnih Rezultata" beside a station's fire: a wooden podium with a microphone on
/// no cable (where the president stands), a coffee machine, little flags, the "Historic victories"
/// board, stairs that lead into a wall, and the bridge's scaffold (built once quest 2 is done). Each
/// piece is its own node (Podium, CoffeeMachine, Flags, Board, Stairs, Bridge) built from simple
/// shapes, or from the matching PackedScene when one is set, so real models can replace them one by
/// one. Positions are in the station's frame (x across the slope, the lean-to is +x; z uphill),
/// clear of the traveller (-2.3), the job board (-4) and station 1's water reserve (-7, z 2).
/// </summary>
public partial class RestsiteSet : Node3D
{
    [Export] public PackedScene? PodiumScene { get; set; }
    [Export] public PackedScene? CoffeeMachineScene { get; set; }
    [Export] public PackedScene? FlagsScene { get; set; }
    [Export] public PackedScene? BoardScene { get; set; }
    [Export] public PackedScene? StairsScene { get; set; }
    [Export] public PackedScene? BridgeScene { get; set; }
    [Export] public Vector3 PodiumOffset { get; set; } = new(-10.0f, 0.0f, 0.0f);
    [Export] public Vector3 PresidentOffset { get; set; } = new(-10.0f, 0.0f, -0.7f);
    [Export] public Vector3 CoffeeOffset { get; set; } = new(-12.2f, 0.0f, 0.8f);
    [Export] public Vector3 FlagsOffset { get; set; } = new(-10.0f, 0.0f, -2.4f);
    [Export] public Vector3 BoardOffset { get; set; } = new(-13.2f, 0.0f, -1.2f);
    [Export] public Vector3 StairsOffset { get; set; } = new(-15.0f, 0.0f, 1.6f);
    [Export] public Vector3 BridgeOffset { get; set; } = new(-12.0f, 0.0f, 4.2f);
    [Export] public Color WoodColor { get; set; } = new(0.45f, 0.3f, 0.18f);
    [Export] public Color MetalColor { get; set; } = new(0.25f, 0.25f, 0.28f);
    [Export] public Color WallColor { get; set; } = new(0.55f, 0.52f, 0.48f);
    [Export] public Color[] FlagColors { get; set; } = { new(0.75f, 0.12f, 0.15f), new(0.12f, 0.22f, 0.6f), new(0.95f, 0.95f, 0.95f) };

    public UsableSpot? Podium { get; private set; }
    public UsableSpot? Board { get; private set; }
    public UsableSpot? Bridge { get; private set; }
    public int Station { get; private set; }

    private Label3D? _boardLabel;
    private Label3D? _bridgeLabel;
    private Node3D? _scaffold;
    private Node3D? _deck;
    private TextTable? _text;

    /// <summary>Builds the pieces; the set's node sits on the station's centre.</summary>
    public void Build(int station, TextTable text)
    {
        Station = station;
        _text = text;
        Name = $"Restsite_{station}";

        Podium = Spot("Podium", PodiumOffset, text.Get("podium_name"), text.Get("podium_description"), PodiumScene, BuildPodium);
        Spot("CoffeeMachine", CoffeeOffset, text.Get("coffee_name"), text.Get("coffee_description"), CoffeeMachineScene, BuildCoffee);
        Spot("Flags", FlagsOffset, "", "", FlagsScene, BuildFlags).RemoveFromGroup(IInteractable.Group);
        Board = Spot("Board", BoardOffset, text.Get("board_name"), "", BoardScene, BuildBoard);
        Spot("Stairs", StairsOffset, text.Get("stairs_name"), text.Get("stairs_description"), StairsScene, BuildStairs);
        Bridge = Spot("Bridge", BridgeOffset, text.Get("bridge_scaffold"), text.Get("bridge_description"), BridgeScene, BuildBridge);
        AddChild(WorldLabel.Readable(new Label3D
        {
            Text = text.Get("place_name"),
            FontSize = 48,
            PixelSize = 0.005f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            OutlineSize = 10,
            Position = PodiumOffset + new Vector3(0.0f, 3.2f, 0.0f),
        }));
    }

    /// <summary>The board's count of historic victories.</summary>
    public void SetVictories(int count)
    {
        if (_boardLabel != null && _text != null)
        {
            _boardLabel.Text = _text.Format("board_count", "count", count);
        }
        if (Board != null && _text != null)
        {
            Board.Description = _text.Format("board_count", "count", count);
        }
    }

    /// <summary>Scaffold before quest 2, the second phase after (cosmetic until the route has a bridge shortcut).</summary>
    public void SetBridgeBuilt(bool built)
    {
        if (_scaffold != null)
        {
            _scaffold.Visible = !built;
        }
        if (_deck != null)
        {
            _deck.Visible = built;
        }
        if (Bridge != null && _text != null)
        {
            string id = "bridge_scaffold";
            if (built)
            {
                id = "bridge_built";
            }
            Bridge.Title = _text.Get(id);
            if (_bridgeLabel != null)
            {
                _bridgeLabel.Text = _text.Get(id);
            }
        }
    }

    private UsableSpot Spot(string name, Vector3 offset, string title, string description, PackedScene? scene, System.Action<Node3D> build)
    {
        UsableSpot spot = new() { Name = name, Title = title, Description = description, Position = offset };
        AddChild(spot);
        if (scene != null)
        {
            spot.AddChild(scene.Instantiate<Node3D>());
        }
        else
        {
            build(spot);
        }
        return spot;
    }

    private void BuildPodium(Node3D at)
    {
        at.AddChild(Box(new Vector3(0.8f, 1.05f, 0.5f), WoodColor, new Vector3(0.0f, 0.525f, 0.0f)));
        MeshInstance3D top = Box(new Vector3(0.9f, 0.05f, 0.6f), WoodColor, new Vector3(0.0f, 1.08f, 0.02f));
        top.RotationDegrees = new Vector3(-12.0f, 0.0f, 0.0f);
        at.AddChild(top);
        // The microphone: a thin stand and a head, and no cable anywhere.
        at.AddChild(Cylinder(0.012f, 0.35f, MetalColor, new Vector3(0.0f, 1.25f, -0.1f)));
        at.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.04f, Height = 0.08f }, Position = new Vector3(0.0f, 1.44f, -0.1f), MaterialOverride = Material(MetalColor) });
    }

    private void BuildCoffee(Node3D at)
    {
        at.AddChild(Box(new Vector3(0.6f, 1.6f, 0.5f), new Color(0.6f, 0.08f, 0.08f), new Vector3(0.0f, 0.8f, 0.0f)));
        at.AddChild(Box(new Vector3(0.4f, 0.5f, 0.02f), new Color(0.08f, 0.08f, 0.08f), new Vector3(0.0f, 1.1f, 0.26f)));
        at.AddChild(Box(new Vector3(0.2f, 0.25f, 0.15f), new Color(0.15f, 0.15f, 0.15f), new Vector3(0.0f, 0.55f, 0.28f)));
    }

    private void BuildFlags(Node3D at)
    {
        for (int i = 0; i < FlagColors.Length; i++)
        {
            float x = (i - (FlagColors.Length - 1) * 0.5f) * 1.2f;
            at.AddChild(Cylinder(0.02f, 2.2f, MetalColor, new Vector3(x, 1.1f, 0.0f)));
            at.AddChild(Box(new Vector3(0.6f, 0.4f, 0.01f), FlagColors[i], new Vector3(x + 0.31f, 1.95f, 0.0f)));
        }
    }

    private void BuildBoard(Node3D at)
    {
        at.AddChild(Cylinder(0.05f, 1.6f, WoodColor, new Vector3(-0.6f, 0.8f, 0.0f)));
        at.AddChild(Cylinder(0.05f, 1.6f, WoodColor, new Vector3(0.6f, 0.8f, 0.0f)));
        at.AddChild(Box(new Vector3(1.5f, 0.8f, 0.05f), new Color(0.85f, 0.8f, 0.65f), new Vector3(0.0f, 1.45f, 0.0f)));
        _boardLabel = new Label3D
        {
            FontSize = 40,
            PixelSize = 0.0026f,
            Modulate = new Color(0.15f, 0.1f, 0.08f),
            OutlineSize = 0,
            Position = new Vector3(0.0f, 1.45f, 0.03f),
        };
        at.AddChild(_boardLabel);
    }

    private void BuildStairs(Node3D at)
    {
        for (int step = 0; step < 5; step++)
        {
            at.AddChild(Box(new Vector3(1.2f, 0.2f, 0.35f), WallColor, new Vector3(0.0f, 0.1f + 0.2f * step, -0.35f * step)));
        }
        at.AddChild(Box(new Vector3(2.0f, 2.6f, 0.4f), WallColor.Darkened(0.15f), new Vector3(0.0f, 1.3f, -1.9f)));
    }

    private void BuildBridge(Node3D at)
    {
        _scaffold = new Node3D { Name = "Scaffold" };
        at.AddChild(_scaffold);
        for (int i = 0; i < 4; i++)
        {
            _scaffold.AddChild(Cylinder(0.04f, 1.4f, WoodColor, new Vector3((i % 2) * 1.6f - 0.8f, 0.7f, (i / 2) * 1.2f - 0.6f)));
        }
        _scaffold.AddChild(Box(new Vector3(1.8f, 0.05f, 0.3f), WoodColor, new Vector3(0.0f, 1.2f, 0.0f)));
        _deck = new Node3D { Name = "Deck", Visible = false };
        at.AddChild(_deck);
        _deck.AddChild(Box(new Vector3(2.2f, 0.12f, 1.4f), WoodColor, new Vector3(0.0f, 0.3f, 0.0f)));
        _deck.AddChild(Box(new Vector3(2.2f, 0.5f, 0.05f), WoodColor, new Vector3(0.0f, 0.6f, 0.68f)));
        _bridgeLabel = new Label3D
        {
            FontSize = 36,
            PixelSize = 0.004f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            OutlineSize = 8,
            Position = new Vector3(0.0f, 1.8f, 0.0f),
        };
        at.AddChild(_bridgeLabel);
    }

    private static MeshInstance3D Box(Vector3 size, Color color, Vector3 at)
    {
        return new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = at, MaterialOverride = Material(color) };
    }

    private static MeshInstance3D Cylinder(float radius, float height, Color color, Vector3 at)
    {
        return new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height }, Position = at, MaterialOverride = Material(color) };
    }

    private static StandardMaterial3D Material(Color color)
    {
        return new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f };
    }
}
