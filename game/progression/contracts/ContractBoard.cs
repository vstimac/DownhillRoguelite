using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// A station's job board: a post by the fire with a note pinned to it. Its label says what the job
/// is, how heavy the cargo is, where it goes, what it pays and what it asks (the condition it must
/// arrive in, a deadline); up close the note's own words show. E takes the job
/// (ContractDirector.Accept). While a job is open, any board takes it back: E asks, E again within
/// a few seconds gives it up (ContractDirector.Abandon) and its cargo stays at the board.
/// Built from primitives until a model exists.
/// </summary>
public partial class ContractBoard : Node3D, IInteractable, IDescribed
{
    [Export] public Color WoodColor { get; set; } = new(0.38f, 0.27f, 0.17f);
    [Export] public Color PaperColor { get; set; } = new(0.92f, 0.86f, 0.7f);
    [Export] public Color TakenColor { get; set; } = new(0.55f, 0.5f, 0.42f);
    /// <summary>How long a "no room" message stays on the label, in s.</summary>
    [Export] public float MessageTime { get; set; } = 2.0f;
    /// <summary>The second E (giving a job up) must come within this long of the first, in s.</summary>
    [Export] public float ConfirmTime { get; set; } = 4.0f;
    [Export] public float LabelRange { get; set; } = 18.0f;

    public ContractInstance? Contract { get; private set; }
    /// <summary>Whether the first E was pressed and the next one gives the open job up.</summary>
    public bool ArmedToAbandon => _armTimer > 0.0f;

    private ItemDefinition? _cargo;
    private ContractDirector? _director;
    private Label3D? _title;
    private Label3D? _terms;
    private Label3D? _note;
    private StandardMaterial3D? _paper;
    private float _messageTimer;
    private float _armTimer;

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
            string text = ItemText.Properties(_cargo) + "\nDeliver to Station " + Contract.ToStation + " for " + Contract.Definition.Score + " score.";
            string asks = Asks(Contract.Definition);
            if (asks.Length > 0)
            {
                text += " " + asks;
            }
            text += "\nOne job at a time; the cargo goes in your pack. Knocks and falls damage it: packed deep inside, less.";
            ContractInstance? active = _director?.Active;
            if (active != null)
            {
                text += "\nYou're carrying \"" + active.Definition.DisplayName + "\" (" + ContractResolver.ConditionText(active.Condition)
                    + "). You can give it up here.";
            }
            return text;
        }
    }

    public string Prompt
    {
        get
        {
            if (ArmedToAbandon)
            {
                return "E  Confirm: give up your job";
            }
            if (_director?.Active != null)
            {
                return "E  Give up your job here";
            }
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

    /// <summary>What a job asks beyond getting there: "Must arrive in at least 70% condition. Within 3 days."</summary>
    public static string Asks(ContractDefinition definition)
    {
        string text = "";
        if (definition.MinCondition > 0.0f)
        {
            text += "Must arrive in at least " + ContractResolver.ConditionText(definition.MinCondition) + " condition.";
        }
        else if (definition.Fragility <= 0.0f)
        {
            text += "Nothing can break it.";
        }
        if (definition.DeadlineDays > 0.0f)
        {
            if (text.Length > 0)
            {
                text += " ";
            }
            text += "Within " + DaysText(definition.DeadlineDays) + ".";
        }
        return text;
    }

    public static string DaysText(float days)
    {
        if (Mathf.IsEqualApprox(days, 1.0f))
        {
            return "a day";
        }
        if (Mathf.IsEqualApprox(days, Mathf.Round(days)))
        {
            return Mathf.RoundToInt(days) + " days";
        }
        return days.ToString("0.#") + " days";
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
        GameEvents.ContractResolved += OnContractChanged;
        GameEvents.ContractAccepted += OnContractChanged;
        Refresh();
    }

    public override void _ExitTree()
    {
        GameEvents.ContractResolved -= OnContractChanged;
        GameEvents.ContractAccepted -= OnContractChanged;
    }

    private void OnContractChanged(ContractNote note)
    {
        Refresh();
        RefreshTooltip();
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
        if (ArmedToAbandon && _director?.Active != null)
        {
            _title.Text = "Give up \"" + _director.Active.Definition.DisplayName + "\"?";
            _terms.Text = "E again: the " + _director.Active.CargoName.ToLowerInvariant() + " stays here, no pay";
            _title.Modulate = PaperColor;
            return;
        }
        if (Contract.Status == ContractStatus.Offered)
        {
            _title.Text = "Job: " + Contract.Definition.DisplayName;
            string terms = Contract.CargoName + " · " + Contract.CargoMassKg.ToString("0.0") + " kg · to Station " + Contract.ToStation
                + " · pays " + Contract.Definition.Score;
            if (Contract.Definition.MinCondition > 0.0f)
            {
                terms += " · arrive " + ContractResolver.ConditionText(Contract.Definition.MinCondition) + "+";
            }
            if (Contract.Definition.DeadlineDays > 0.0f)
            {
                terms += " · within " + DaysText(Contract.Definition.DeadlineDays);
            }
            _terms.Text = terms;
            _title.Modulate = PaperColor;
            return;
        }
        if (Contract.Status == ContractStatus.Accepted)
        {
            _title.Text = "Job taken";
        }
        else if (Contract.Status == ContractStatus.Delivered)
        {
            _title.Text = "Job done";
        }
        else if (Contract.Status == ContractStatus.Abandoned)
        {
            _title.Text = "Job given up";
        }
        else
        {
            _title.Text = "Job failed";
        }
        _terms.Text = Contract.CargoName + " for Station " + Contract.ToStation;
        _title.Modulate = TakenColor;
        if (_paper != null)
        {
            _paper.AlbedoColor = TakenColor;
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
        if (_messageTimer <= 0.0f)
        {
            return;
        }
        _messageTimer -= dt;
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
        if (!focused && _armTimer > 0.0f)
        {
            _armTimer = 0.0f;
            Refresh();
        }
    }

    public bool Interact(Node3D interactor)
    {
        if (Contract == null || _director == null)
        {
            return false;
        }
        if (_director.Active != null)
        {
            if (!ArmedToAbandon)
            {
                _armTimer = ConfirmTime;
                Refresh();
                RefreshTooltip();
                return true;
            }
            _armTimer = 0.0f;
            bool gaveUp = _director.Abandon() == ContractResult.Ok;
            Refresh();
            RefreshTooltip();
            return gaveUp;
        }
        if (Contract.Status != ContractStatus.Offered)
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
