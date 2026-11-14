using DownhillRoguelite.Core;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// Where a job ends: a low table with a lantern at the destination station, labelled with the job.
/// Walking past never hands anything over. E asks to hand the cargo over; E again within a few
/// seconds confirms, and ContractDirector.Deliver does the rest. Built from primitives.
/// </summary>
public partial class DeliveryPost : Node3D, IInteractable, IDescribed
{
    [Export] public Color WoodColor { get; set; } = new(0.42f, 0.3f, 0.19f);
    [Export] public Color LampColor { get; set; } = new(1.0f, 0.8f, 0.45f);
    /// <summary>The second E must come within this long of the first, in s.</summary>
    [Export] public float ConfirmTime { get; set; } = 4.0f;
    [Export] public float MessageTime { get; set; } = 2.5f;
    [Export] public float LabelRange { get; set; } = 40.0f;

    public ContractInstance? Contract { get; private set; }
    public bool SharedProxy { get; set; }
    public void ShowSharedCondition(float condition)
    { if (SharedProxy && Contract != null) { Contract.Condition = condition; Refresh(); } }
    /// <summary>Whether the first E was pressed and the next one hands the cargo over.</summary>
    public bool Armed => _armTimer > 0.0f;

    private ContractDirector? _director;
    private Label3D? _title;
    private Label3D? _status;
    private float _armTimer;
    private float _messageTimer;

    public Vector3 InteractionPoint => GlobalPosition + Vector3.Up * 0.8f;

    public string Title
    {
        get
        {
            if (Contract == null)
            {
                return "";
            }
            return "Delivery: " + Contract.Definition.DisplayName;
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
            return "The " + Contract.CargoName.ToLowerInvariant() + " from Station " + Contract.FromStation + " is handed over here.";
        }
    }

    public string Details
    {
        get
        {
            string text = "It must be in your pack. Once handed over, it's gone from the pack for good.";
            if (Contract != null && Contract.Definition.Fragility > 0.0f)
            {
                text += "\nIts condition: " + ContractResolver.ConditionText(Contract.Condition);
                if (Contract.Definition.MinCondition > 0.0f)
                {
                    text += " (the job needs " + ContractResolver.ConditionText(Contract.Definition.MinCondition) + ")";
                }
                text += ".";
            }
            return text;
        }
    }

    public string Prompt
    {
        get
        {
            if (Armed)
            {
                return "E  Confirm: hand it over";
            }
            return "E  Hand over the cargo";
        }
    }

    public Resource? IconSource => null;

    public void Setup(ContractInstance contract, ContractDirector director)
    {
        Contract = contract;
        _director = director;
        Name = "DeliveryPost" + contract.ToStation;
    }

    public override void _Ready()
    {
        AddToGroup(IInteractable.Group);
        StandardMaterial3D wood = new() { AlbedoColor = WoodColor, Roughness = 1.0f };
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.0f, 0.06f, 0.6f) }, Position = new Vector3(0.0f, 0.75f, 0.0f), MaterialOverride = wood });
        Vector3[] legs = { new(-0.42f, 0.0f, -0.24f), new(0.42f, 0.0f, -0.24f), new(-0.42f, 0.0f, 0.24f), new(0.42f, 0.0f, 0.24f) };
        foreach (Vector3 leg in legs)
        {
            AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.75f, 0.06f) }, Position = leg + new Vector3(0.0f, 0.375f, 0.0f), MaterialOverride = wood });
        }
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.18f, 0.12f) },
            Position = new Vector3(0.36f, 0.87f, 0.18f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = LampColor, EmissionEnabled = true, Emission = LampColor, EmissionEnergyMultiplier = 2.0f },
        });
        AddChild(new OmniLight3D { LightColor = LampColor, LightEnergy = 0.9f, OmniRange = 4.0f, Position = new Vector3(0.36f, 1.1f, 0.18f) });
        _title = Label(40, 1.75f);
        _status = Label(30, 1.45f);
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

    private void Refresh()
    {
        if (Contract == null || _title == null || _status == null)
        {
            return;
        }
        _title.Text = "Deliveries";
        if (Armed)
        {
            _status.Text = "Hand over the " + Contract.CargoName.ToLowerInvariant() + "? E again";
            if (Contract.Condition < Contract.Definition.MinCondition)
            {
                _status.Text = "Too damaged for the job: E again hands it over for nothing";
            }
        }
        else
        {
            _status.Text = Contract.Definition.DisplayName + " · " + Contract.CargoName;
            if (Contract.Definition.Fragility > 0.0f && Contract.Condition < 100.0f)
            {
                _status.Text += " · " + ContractResolver.ConditionText(Contract.Condition);
            }
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_armTimer > 0.0f)
        {
            _armTimer -= dt;
            if (_armTimer <= 0.0f)
            {
                Refresh();
                RefreshTooltip();
            }
        }
        if (_messageTimer > 0.0f)
        {
            _messageTimer -= dt;
            if (_messageTimer <= 0.0f)
            {
                Refresh();
            }
        }
    }

    public void SetFocused(bool focused)
    {
        if (!focused && _armTimer > 0.0f)
        {
            _armTimer = 0.0f;
            Refresh();
        }
    }

    public bool Interact(Node3D interactor)
    {
        if (SharedProxy) { GameEvents.PublishHint("Your teammate carries this delivery."); return false; }
        if (Contract == null || _director == null)
        {
            return false;
        }
        ContractResult check = _director.CanDeliverAt(Contract.ToStation);
        if (check != ContractResult.Ok)
        {
            _armTimer = 0.0f;
            if (check == ContractResult.CargoMissing)
            {
                ShowMessage("The " + Contract.CargoName.ToLowerInvariant() + " isn't in your pack");
            }
            else if (check == ContractResult.Expired)
            {
                ShowMessage("The job's deadline has passed");
            }
            return false;
        }
        if (!Armed)
        {
            _armTimer = ConfirmTime;
            Refresh();
            RefreshTooltip();
            return true;
        }
        _armTimer = 0.0f;
        ContractResult result = _director.Deliver(Contract.ToStation);
        if (result != ContractResult.Ok)
        {
            ShowMessage("Couldn't hand it over");
            return false;
        }
        return true;
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
        if (_status == null)
        {
            return;
        }
        _status.Text = text;
        _messageTimer = MessageTime;
        GameEvents.PublishHint(text + ".");
    }
}
