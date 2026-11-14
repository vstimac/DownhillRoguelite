using DownhillRoguelite.Core;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// One offering at a station: a small glowing stone with the boon's name above it and what it does
/// under the name (a bargain's price under that, in red); up close (when the carrier faces it) the
/// description shows above the name. The name sits on a fixed line and everything else stacks away
/// from it by its own measured height, so a long or conditional effect never runs over the name, and
/// the labels are narrow enough not to reach the neighbouring stones. E chooses it (PickupInteractor).
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
    private Label3D? _price;

    /// <summary>A bargain's price is written in this colour under its gift.</summary>
    private static readonly Color PriceColour = new(1.0f, 0.55f, 0.5f);

    /// <summary>Metres per label pixel, and how wide a label may grow before it wraps (px).</summary>
    private const float PixelSize = 0.005f;
    private const float LabelWidth = 340.0f;
    /// <summary>The line the name stands on, and the gap between stacked labels, in m.</summary>
    private const float NameLine = 1.3f;
    private const float Gap = 0.05f;

    public Vector3 InteractionPoint => GlobalPosition + Vector3.Up * 0.5f;

    public string Title => Boon?.DisplayName ?? "";
    public string Description => DescribeEffects();
    public string Details => DescribeDetails();
    public string Prompt => PromptText();
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
        // The name on its line; what it does hangs under it, the price under that.
        _name = Label(Boon?.DisplayName ?? "", 44, NameLine, VerticalAlignment.Bottom);
        _name.Modulate = glow.Lightened(0.3f);
        float y = NameLine - Gap;
        _effect = Label(EffectText(), 32, y, VerticalAlignment.Top);
        _effect.Modulate = new Color(1.0f, 0.97f, 0.88f);
        y -= Height(_effect) + Gap;
        if (Boon != null && Boon.IsBargain)
        {
            _price = Label("Price: " + Boon.Costs(), 30, y, VerticalAlignment.Top);
            _price.Modulate = PriceColour;
        }
        // The flavour line, when looked at, sits above the name.
        _description = Label(Boon?.Description ?? "", 26, NameLine + Height(_name) + Gap, VerticalAlignment.Bottom);
        _description.Visible = false;
    }

    /// <summary>What it gives; a boon that only works sometimes says when on a line of its own.</summary>
    private string EffectText()
    {
        if (Boon == null)
        {
            return "";
        }
        string gifts = string.Join(" · ", Boon.Parts(true));
        if (Boon.IsConditional && gifts.Length > 0)
        {
            return Boon.ConditionText() + ":\n" + gifts;
        }
        return Boon.Gifts();
    }

    /// <summary>How tall a label's text stands once wrapped, in m.</summary>
    private static float Height(Label3D label)
    {
        Font font = label.Font ?? ThemeDB.FallbackFont;
        Vector2 size = font.GetMultilineStringSize(label.Text, HorizontalAlignment.Center, label.Width, label.FontSize);
        return size.Y * label.PixelSize;
    }

    private Label3D Label(string text, int size, float height, VerticalAlignment anchor)
    {
        Label3D label = new()
        {
            Text = text,
            FontSize = size,
            PixelSize = PixelSize,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            OutlineSize = 8,
            Position = Vector3.Up * height,
            VerticalAlignment = anchor,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Width = LabelWidth,
        };
        AddChild(WorldLabel.Readable(label, 15.0f));
        return label;
    }

    /// <summary>The top of the name and the bottom of the lowest label above the stone, in m (tests check the stacking).</summary>
    public (float NameBottom, float EffectTop, float EffectBottom, float LowestBottom) LabelStack()
    {
        float nameBottom = _name?.Position.Y ?? 0.0f;
        float effectTop = _effect?.Position.Y ?? 0.0f;
        float effectBottom = effectTop;
        if (_effect != null)
        {
            effectBottom = effectTop - Height(_effect);
        }
        float lowest = effectBottom;
        if (_price != null)
        {
            lowest = _price.Position.Y - Height(_price);
        }
        return (nameBottom, effectTop, effectBottom, lowest);
    }

    /// <summary>The widest a label of this stone can be, in m.</summary>
    public static float LabelWidthMetres => LabelWidth * PixelSize;

    private string DescribeEffects()
    {
        if (Boon == null)
        {
            return "";
        }
        if (Boon.IsBargain)
        {
            return Boon.Gifts() + "\nPrice: " + Boon.Costs();
        }
        return Boon.Effects();
    }

    private string DescribeDetails()
    {
        string text = Boon?.Description ?? "";
        if (Boon != null && Boon.IsBargain)
        {
            return text + "\nA bargain: the gift and the price both last the rest of this run. One boon per station: E to take it.";
        }
        if (Boon != null && Boon.IsConditional)
        {
            return text + "\n" + Boon.ConditionText() + " only, for the rest of this run. One boon per station: E to take it.";
        }
        return text + "\nLasts the rest of this run. One boon per station: E to take it.";
    }

    private string PromptText()
    {
        if (Boon != null && Boon.IsBargain)
        {
            return "E  Take the bargain";
        }
        return "E  Take this boon";
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
