using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// A point of interest beside the path, in the world (the purpose patch): a tall pole with a
/// coloured pennant and a small warm light, so it is seen from the path even at dusk, and at its
/// foot a crate, a stone or a basin. E takes what it holds: each item is packed through
/// GameEvents.ItemsGranted, and whatever doesn't fit stays for later ("No room in the pack").
/// When it's empty it publishes WaysideUsed, the pennant drops and the light goes out.
/// </summary>
public partial class WaysideSite : Node3D, IInteractable, IDescribed
{
    [Export] public float PoleHeight { get; set; } = 3.2f;
    [Export] public float LightEnergy { get; set; } = 1.2f;
    [Export] public float LightRange { get; set; } = 7.0f;
    [Export] public Color LightColor { get; set; } = new(1.0f, 0.72f, 0.4f);
    [Export] public float LabelPixelSize { get; set; } = 0.004f;

    public WaysideDefinition? Definition { get; private set; }
    /// <summary>The placement's key ("stage:index"), the id its notes carry.</summary>
    public string Key { get; private set; } = "";
    public bool Used { get; private set; }
    /// <summary>Item paths still waiting to be taken.</summary>
    public IReadOnlyList<string> Left => _left;

    public Vector3 InteractionPoint => GlobalPosition + Vector3.Up * 0.7f;
    public string Title => Definition?.Title ?? "Something";
    public string Description => Definition?.Description ?? "";
    public string Details
    {
        get
        {
            if (Used)
            {
                return "Nothing left here.";
            }
            return "Holds: " + ItemList(_left);
        }
    }
    public string Prompt
    {
        get
        {
            if (Used)
            {
                return "";
            }
            return "E  " + (Definition?.Prompt ?? "Take");
        }
    }
    public Resource? IconSource => null;

    private readonly List<string> _left = new();
    private Node3D? _pennant;
    private OmniLight3D? _light;
    private Label3D? _label;

    /// <summary>Sets what it is and what's left (a site rebuilt after streaming keeps what was taken).</summary>
    public void Setup(WaysideDefinition definition, string key, IEnumerable<string> left, bool used)
    {
        Definition = definition;
        Key = key;
        Used = used;
        _left.Clear();
        _left.AddRange(left);
        Name = "Wayside_" + key.Replace(":", "_");
    }

    public override void _Ready()
    {
        AddToGroup(IInteractable.Group);
        Color pennant = Definition?.PennantColor ?? new Color(0.75f, 0.2f, 0.15f);
        StandardMaterial3D wood = new() { AlbedoColor = new Color(0.36f, 0.25f, 0.16f), Roughness = 1.0f };
        StandardMaterial3D cloth = new() { AlbedoColor = pennant, Roughness = 0.9f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        AddMesh(new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.05f, Height = PoleHeight, RadialSegments = 6 }, new Vector3(0.0f, PoleHeight * 0.5f, 0.0f), wood);
        _pennant = new Node3D { Position = new Vector3(0.0f, PoleHeight - 0.35f, 0.0f) };
        AddChild(_pennant);
        MeshInstance3D flag = new() { Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.42f, 0.02f) }, Position = new Vector3(0.37f, 0.0f, 0.0f), MaterialOverride = cloth };
        _pennant.AddChild(flag);
        int look = Definition?.Look ?? 0;
        if (look == 1)
        {
            AddMesh(new BoxMesh { Size = new Vector3(0.9f, 0.7f, 0.35f) }, new Vector3(0.6f, 0.35f, 0.2f), new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.49f, 0.46f), Roughness = 1.0f });
        }
        else if (look == 2)
        {
            AddMesh(new CylinderMesh { TopRadius = 0.55f, BottomRadius = 0.45f, Height = 0.35f, RadialSegments = 12 }, new Vector3(0.6f, 0.17f, 0.2f), new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.44f, 0.42f), Roughness = 1.0f });
            AddMesh(new CylinderMesh { TopRadius = 0.46f, BottomRadius = 0.46f, Height = 0.02f, RadialSegments = 12 }, new Vector3(0.6f, 0.33f, 0.2f), new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.4f, 0.5f), Roughness = 0.1f, Metallic = 0.2f });
        }
        else
        {
            AddMesh(new BoxMesh { Size = new Vector3(0.6f, 0.45f, 0.45f) }, new Vector3(0.6f, 0.22f, 0.2f), wood);
        }
        _light = new OmniLight3D { LightColor = LightColor, LightEnergy = LightEnergy, OmniRange = LightRange, Position = new Vector3(0.0f, 1.6f, 0.0f) };
        AddChild(_light);
        _label = new Label3D
        {
            Text = Title,
            PixelSize = LabelPixelSize,
            FontSize = 40,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            OutlineSize = 8,
            Position = Vector3.Up * (PoleHeight + 0.35f),
            Visible = false,
        };
        AddChild(_label);
        ShowUsed();
    }

    private MeshInstance3D AddMesh(Mesh mesh, Vector3 at, Material material)
    {
        MeshInstance3D instance = new() { Mesh = mesh, Position = at, MaterialOverride = material };
        AddChild(instance);
        return instance;
    }

    public void SetFocused(bool focused)
    {
        if (_label != null)
        {
            _label.Visible = focused;
        }
    }

    /// <summary>Packs what it holds, one item at a time; true if anything was taken.</summary>
    public bool Interact(Node3D interactor)
    {
        if (Used || Definition == null)
        {
            return false;
        }
        List<string> taken = new();
        for (int i = _left.Count - 1; i >= 0; i--)
        {
            string path = _left[i];
            int before = GameEvents.CarriedItems.Count;
            GameEvents.PublishItemsGranted(new[] { path });
            if (GameEvents.CarriedItems.Count > before)
            {
                taken.Add(path);
                _left.RemoveAt(i);
            }
        }
        if (_left.Count > 0)
        {
            GameEvents.PublishHint("No room in the pack for the " + ItemList(_left) + ". Make room and come back.");
        }
        if (_left.Count == 0)
        {
            Used = true;
            ShowUsed();
            string text = Definition.TakenText;
            if (text.Length == 0)
            {
                text = "I took " + ItemList(taken) + ".";
            }
            GameEvents.PublishWaysideUsed(new WaysideNote(Key, Definition.Title, text, GlobalPosition));
        }
        return taken.Count > 0;
    }

    private void ShowUsed()
    {
        if (_light != null)
        {
            _light.Visible = !Used;
        }
        if (_pennant != null && Used)
        {
            // A spent site's pennant hangs limp at the foot of the pole.
            _pennant.Position = new Vector3(0.0f, 0.9f, 0.0f);
            _pennant.RotationDegrees = new Vector3(0.0f, 0.0f, -70.0f);
        }
    }

    /// <summary>"a ration and dried fruit" from item paths (the item's own name, or the file's).</summary>
    public static string ItemList(IReadOnlyList<string> paths)
    {
        List<string> names = new();
        foreach (string path in paths)
        {
            string name = path.GetFile().GetBaseName().Replace("_", " ");
            if (ResourceLoader.Exists(path) && ResourceLoader.Load(path) is Inventory.ItemDefinition item && item.DisplayName.Length > 0)
            {
                name = item.DisplayName.ToLowerInvariant();
            }
            names.Add(name);
        }
        if (names.Count == 0)
        {
            return "nothing";
        }
        if (names.Count == 1)
        {
            return names[0];
        }
        return string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[^1];
    }
}
