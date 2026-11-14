using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// A station's altar, a little way from the fire: a stone table with candles where a relic found on
/// the mountain can be left to be studied. Walking past leaves nothing. With a relic in the pack,
/// E asks to leave it; E again within a few seconds confirms and RelicDirector.Deliver does the rest
/// (the first carried relic if there are several; the next press leaves the next).
/// </summary>
public partial class RelicAltar : Node3D, IInteractable, IDescribed
{
    [Export(PropertyHint.File, "*.glb")] public string AltarModel { get; set; } = "res://art/props/kenney_graveyard_kit/GLB format/altar-stone.glb";
    [Export(PropertyHint.File, "*.glb")] public string CandleModel { get; set; } = "res://art/props/kenney_graveyard_kit/GLB format/candle.glb";
    [Export] public float AltarHeight { get; set; } = 0.95f;
    [Export] public Color StoneColor { get; set; } = new(0.6f, 0.58f, 0.55f);
    [Export] public Color CandleColor { get; set; } = new(1.0f, 0.75f, 0.4f);
    [Export] public Color LabelColor { get; set; } = new(0.9f, 0.84f, 0.68f);
    /// <summary>The second E must come within this long of the first, in s.</summary>
    [Export] public float ConfirmTime { get; set; } = 4.0f;
    [Export] public float MessageTime { get; set; } = 2.5f;
    [Export] public float LabelRange { get; set; } = 30.0f;

    public int Station { get; private set; }
    /// <summary>Whether the first E was pressed and the next one leaves the relic.</summary>
    public bool Armed => _armTimer > 0.0f;

    private RelicDirector? _director;
    private Label3D? _status;
    private float _armTimer;
    private float _messageTimer;
    private float _top = 0.95f;

    public Vector3 InteractionPoint => GlobalPosition + Vector3.Up * _top;

    public string Title => "Altar";

    public string Description => "Relics found on the mountain can be left here to be studied.";

    public string Details
    {
        get
        {
            RelicDefinition? relic = Next();
            if (relic == null || _director == null)
            {
                return "You carry no relic. They lie off the main path, by stones with candles.";
            }
            string text = "Leave " + relic.DisplayName.ToLowerInvariant() + ": +" + _director.ScoreFor(relic) + " score";
            if (relic.Fragility > 0.0f)
            {
                text += ", condition " + ContractResolver.ConditionText(_director.ConditionOf(relic.Id));
            }
            return text + ". It stays here; what it meant goes into your journal.";
        }
    }

    public string Prompt
    {
        get
        {
            RelicDefinition? relic = Next();
            if (relic == null)
            {
                return "";
            }
            if (Armed)
            {
                return "E  Confirm: leave " + relic.DisplayName.ToLowerInvariant();
            }
            return "E  Leave " + relic.DisplayName.ToLowerInvariant();
        }
    }

    public Resource? IconSource => null;

    public void Setup(int station, RelicDirector director)
    {
        Station = station;
        _director = director;
        Name = "RelicAltar" + station;
    }

    public override void _Ready()
    {
        AddToGroup(IInteractable.Group);
        _top = AddModel(AltarModel, AltarHeight, Vector3.Zero);
        if (_top <= 0.0f)
        {
            _top = AltarHeight;
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(1.1f, AltarHeight, 0.6f) },
                Position = Vector3.Up * AltarHeight * 0.5f,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = StoneColor, Roughness = 1.0f },
            });
        }
        AddModel(CandleModel, 0.22f, new Vector3(-0.38f, _top, 0.1f));
        AddModel(CandleModel, 0.18f, new Vector3(0.4f, _top, -0.08f));
        AddChild(new OmniLight3D { LightColor = CandleColor, LightEnergy = 0.8f, OmniRange = 4.0f, Position = new Vector3(0.0f, _top + 0.4f, 0.0f) });
        Label3D title = Label(40, _top + 0.85f);
        title.Text = "Altar";
        _status = Label(28, _top + 0.55f);
        Refresh();
    }

    private float AddModel(string path, float height, Vector3 at)
    {
        if (!ResourceLoader.Exists(path) || ResourceLoader.Load(path) is not PackedScene scene)
        {
            return 0.0f;
        }
        Node3D holder = new() { Name = "Model" };
        holder.AddChild(scene.Instantiate<Node3D>());
        Aabb bounds = ModelFit.Bounds(holder);
        float scale = height / Mathf.Max(bounds.Size.Y, 0.01f);
        holder.Transform = ModelFit.FitOnGround(bounds, bounds.Size * scale, true);
        holder.Position += at;
        AddChild(holder);
        return height;
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
            Modulate = LabelColor,
        };
        AddChild(WorldLabel.Readable(label, LabelRange));
        return label;
    }

    /// <summary>The carried relic the next press would leave, if any.</summary>
    private RelicDefinition? Next()
    {
        if (_director == null)
        {
            return null;
        }
        List<RelicDefinition> carried = _director.CarriedRelics();
        foreach (RelicDefinition relic in carried)
        {
            if (relic.CanDeliver && !_director.WasDelivered(relic.Id))
            {
                return relic;
            }
        }
        return null;
    }

    private void Refresh()
    {
        if (_status == null)
        {
            return;
        }
        RelicDefinition? relic = Next();
        if (Armed && relic != null)
        {
            _status.Text = "Leave " + relic.DisplayName.ToLowerInvariant() + "? E again";
        }
        else
        {
            _status.Text = "Leave a relic you found on the way here";
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
        RelicDefinition? relic = Next();
        if (relic == null || _director == null)
        {
            _armTimer = 0.0f;
            ShowMessage("You carry no relic");
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
        if (!_director.Deliver(relic, Station))
        {
            ShowMessage("Couldn't leave it");
            return false;
        }
        Refresh();
        RefreshTooltip();
        return true;
    }

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
