using DownhillRoguelite.Core;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// One offering at a station: a small glowing stone with the boon's name above it; up close (when
/// the carrier faces it) the description shows too. E chooses it (through PickupInteractor).
/// </summary>
public partial class BoonStone : Node3D, ISharedWorldPickup, IDescribed
{
    public string SharedPickupKey => "boon:" + Station;
    public BoonDefinition? Boon { get; private set; }
    public int Station { get; private set; }

    private BoonOfferings? _offerings;
    private Label3D? _name;
    private Label3D? _description;
    private Label3D? _effect;

    public Vector3 InteractionPoint => GlobalPosition + Vector3.Up * 0.5f;

    public string Title => Boon?.DisplayName ?? "";
    public string Description => Boon?.Effects() ?? "";
    public string Details => (Boon?.Description ?? "") + "\nLasts the rest of this run. One boon per station: E to take it.";
    public string Prompt => "E  Take this boon";
    public Resource? IconSource => null;

    public void Setup(BoonDefinition boon, int station, BoonOfferings offerings)
    {
        Boon = boon;
        Station = station;
        _offerings = offerings;
        Name = $"Boon_{boon.Id}";
    }

    public override void _Ready()
    {
        AddToGroup(IInteractable.Group);
        Color glow = Boon?.Glow ?? Colors.Orange;
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.28f, Height = 0.42f, RadialSegments = 8, Rings = 4 },
            Position = new Vector3(0.0f, 0.2f, 0.0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = glow, EmissionEnabled = true, Emission = glow, EmissionEnergyMultiplier = 1.5f },
        });
        AddChild(new OmniLight3D { LightColor = glow, LightEnergy = 0.8f, OmniRange = 3.0f, Position = new Vector3(0.0f, 0.6f, 0.0f) });
        _name = Label(Boon?.DisplayName ?? "", 44, 1.15f);
        _name.Modulate = glow.Lightened(0.3f);
        // What it does, plainly, always shown under its name; the flavour line when looked at.
        _effect = Label(Boon?.Effects() ?? "", 34, 0.9f);
        _effect.Modulate = new Color(1.0f, 0.97f, 0.88f);
        _description = Label(Boon?.Description ?? "", 26, 0.62f);
        _description.Visible = false;
    }

    private Label3D Label(string text, int size, float height)
    {
        Label3D label = new()
        {
            Text = text,
            FontSize = size,
            PixelSize = 0.005f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            OutlineSize = 8,
            Position = Vector3.Up * height,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Width = 520.0f,
        };
        AddChild(WorldLabel.Readable(label, 15.0f));
        return label;
    }

    public void SetFocused(bool focused)
    {
        if (_description != null)
        {
            _description.Visible = focused;
        }
    }

    public bool Interact(Node3D interactor)
    {
        if (_offerings == null || Boon == null)
        {
            return false;
        }
        if (!GameEvents.MayPickUp(SharedPickupKey, this)) { return false; }
        bool chosen = _offerings.Choose(this);
        if (chosen) { GameEvents.PublishSupplyPickedUp(SharedPickupKey); }
        return chosen;
    }
}
