using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// A station's job board: a post by the fire with a note pinned to it. Its label says what the job
/// is, how heavy the cargo is, where it goes and what it pays; up close the note's own words show.
/// E takes the job (ContractDirector.Accept). Built from primitives until a model exists.
/// </summary>
public partial class ContractBoard : Node3D, IInteractable, IDescribed
{
    [Export] public Color WoodColor { get; set; } = new(0.38f, 0.27f, 0.17f);
    [Export] public Color PaperColor { get; set; } = new(0.92f, 0.86f, 0.7f);
    [Export] public Color TakenColor { get; set; } = new(0.55f, 0.5f, 0.42f);
    /// <summary>How long a "no room" message stays on the label, in s.</summary>
    [Export] public float MessageTime { get; set; } = 2.0f;
    [Export] public float LabelRange { get; set; } = 18.0f;

    public ContractInstance? Contract { get; private set; }

    private ItemDefinition? _cargo;
    private ContractDirector? _director;
    private Label3D? _title;
    private Label3D? _terms;
    private Label3D? _note;
    private StandardMaterial3D? _paper;
    private float _messageTimer;

    public Vector3 InteractionPoint => GlobalPosition + Vector3.Up * 1.1f;

    public string Title
    {
        get
        {
            if (Contract == null)
            {
                return "";
            }
            return Contract.Definition.DisplayName;
        }
    }

    public string Description
    {
        get
        {
            if (Contract == null)
            {
                return "";
            }
            return "\"" + Contract.Definition.OfferText + "\"  (" + Contract.Definition.Giver + ")";
        }
    }

    public string Details
    {
        get
        {
            if (Contract == null)
            {
                return "";
            }
            return ItemText.Properties(_cargo) + "\nDeliver to Station " + Contract.ToStation + " for " + Contract.Definition.Score
                + " score. One job at a time; the cargo goes in your pack.";
        }
    }

    public string Prompt
    {
        get
        {
            if (Contract != null && Contract.Status == ContractStatus.Offered)
            {
                return "E  Take the job";
            }
            return "";
        }
    }

    public Resource? IconSource => _cargo;

    public void Setup(ContractInstance contract, ItemDefinition cargo, ContractDirector director)
    {
        Contract = contract;
        _cargo = cargo;
        _director = director;
        Name = "ContractBoard" + contract.FromStation;
    }

    public override void _Ready()
    {
        AddToGroup(IInteractable.Group);
        StandardMaterial3D wood = new() { AlbedoColor = WoodColor, Roughness = 1.0f };
        _paper = new StandardMaterial3D { AlbedoColor = PaperColor, Roughness = 1.0f };
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.1f, 1.7f, 0.1f) }, Position = new Vector3(0.0f, 0.85f, 0.0f), MaterialOverride = wood });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.55f, 0.05f) }, Position = new Vector3(0.0f, 1.35f, 0.06f), MaterialOverride = wood });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.42f, 0.34f, 0.01f) }, Position = new Vector3(0.0f, 1.35f, 0.09f), RotationDegrees = new Vector3(0.0f, 0.0f, -4.0f), MaterialOverride = _paper });
        _title = Label(40, 2.25f);
        _terms = Label(30, 1.95f);
        _note = Label(26, 1.7f);
        _note.Visible = false;
        Refresh();
    }

    private Label3D Label(int size, float height)
    {
        Label3D label = new()
        {
            FontSize = size,
            PixelSize = 0.005f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            OutlineSize = 8,
            Position = Vector3.Up * height,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Width = 560.0f,
        };
        AddChild(WorldLabel.Readable(label, LabelRange));
        return label;
    }

    /// <summary>Redraws the labels from the contract's state.</summary>
    public void Refresh()
    {
        if (Contract == null || _title == null || _terms == null || _note == null)
        {
            return;
        }
        _note.Text = Description;
        if (Contract.Status == ContractStatus.Offered)
        {
            _title.Text = "Job: " + Contract.Definition.DisplayName;
            _terms.Text = Contract.CargoName + " · " + Contract.CargoMassKg.ToString("0.0") + " kg · to Station " + Contract.ToStation
                + " · pays " + Contract.Definition.Score;
            _title.Modulate = PaperColor;
        }
        else
        {
            _title.Text = "Job taken";
            _terms.Text = Contract.CargoName + " for Station " + Contract.ToStation;
            _title.Modulate = TakenColor;
            if (_paper != null)
            {
                _paper.AlbedoColor = TakenColor;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (_messageTimer <= 0.0f)
        {
            return;
        }
        _messageTimer -= (float)delta;
        if (_messageTimer <= 0.0f)
        {
            Refresh();
        }
    }

    public void SetFocused(bool focused)
    {
        if (_note != null)
        {
            _note.Visible = focused;
        }
    }

    public bool Interact(Node3D interactor)
    {
        if (Contract == null || _director == null || Contract.Status != ContractStatus.Offered)
        {
            return false;
        }
        ContractResult result = _director.Accept(Contract);
        if (result == ContractResult.Ok)
        {
            RefreshTooltip();
            return true;
        }
        if (result == ContractResult.NoRoom)
        {
            ShowMessage("No room in the pack for the " + Contract.CargoName.ToLowerInvariant());
        }
        else if (result == ContractResult.AnotherActive)
        {
            ShowMessage("You're already carrying a job");
        }
        return false;
    }

    /// <summary>The HUD's tooltip reads the prompt when focus changes: tell it again after a change while focused.</summary>
    private void RefreshTooltip()
    {
        if (ReferenceEquals(GameEvents.Focused, this))
        {
            GameEvents.PublishFocus(this);
        }
    }

    private void ShowMessage(string text)
    {
        if (_terms == null)
        {
            return;
        }
        _terms.Text = text;
        _messageTimer = MessageTime;
        GameEvents.PublishHint(text + ".");
    }
}
