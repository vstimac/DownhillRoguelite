using DownhillRoguelite.Core;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>Clearly labelled prototype workshop. The existing contract owner performs the inventory transaction.</summary>
public partial class RecoveryWorkshop : Node3D, IInteractable, IDescribed
{
    public ContractInstance? Contract { get; set; }
    public ContractDirector? Director { get; set; }
    private MeshInstance3D? _module;
    public Vector3 InteractionPoint => GlobalPosition + Vector3.Up * 0.8f;
    public string Title => "Abandoned pump workshop";
    public string Description => "Station 1 needs this pump. Recover it, protect it, and deliver it to restore a water reserve and earn a boon.";
    public string Details => "6.5 kg · 3 × 4 × 3 cells · must arrive at 60% condition or better. Pack low and close to your back. Prototype geometry.";
    public string Prompt
    {
        get
        {
            if (Contract?.Status != ContractStatus.Offered) { return "Pump already recovered"; }
            if (Director?.Active != null) { return "Finish or abandon your current job first"; }
            return "E  Recover the pump";
        }
    }
    public Resource? IconSource => Contract?.Definition.CargoItem;

    public override void _Ready()
    {
        AddToGroup(IInteractable.Group);
        StandardMaterial3D wood = new() { AlbedoColor = new Color(0.32f, 0.22f, 0.14f), Roughness = 1 };
        StandardMaterial3D flag = new() { AlbedoColor = new Color(0.95f, 0.38f, 0.08f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        AddMesh(new BoxMesh { Size = new Vector3(2.0f, 0.15f, 1.0f) }, new Vector3(0, 0.6f, 0), wood);
        foreach (float x in new[] { -0.8f, 0.8f })
        {
            AddMesh(new BoxMesh { Size = new Vector3(0.12f, 0.6f, 0.7f) }, new Vector3(x, 0.3f, 0), wood);
        }
        AddMesh(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.06f, Height = 4.0f }, new Vector3(-1.3f, 2, 0), wood);
        AddMesh(new BoxMesh { Size = new Vector3(1.2f, 0.65f, 0.04f) }, new Vector3(-0.7f, 3.5f, 0), flag);
        _module = AddMesh(new BoxMesh { Size = new Vector3(0.45f, 0.5f, 0.4f) }, new Vector3(0, 0.92f, 0),
            new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.52f, 0.56f), Metallic = 0.5f });
        AddChild(WorldLabel.Readable(new Label3D { Text = "Pump workshop\nRecover → Station 1\nWater reserve + boon", FontSize = 36,
            PixelSize = 0.006f, Position = new Vector3(0, 2.2f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, OutlineSize = 8 }, 75));
        GameEvents.ContractAccepted += OnAccepted;
    }

    public override void _ExitTree() { GameEvents.ContractAccepted -= OnAccepted; }
    private MeshInstance3D AddMesh(Mesh mesh, Vector3 at, Material material)
    {
        MeshInstance3D part = new() { Mesh = mesh, Position = at, MaterialOverride = material };
        AddChild(part);
        return part;
    }
    private void OnAccepted(ContractNote note)
    {
        if (note.InstanceId == Contract?.InstanceId && _module != null) { _module.Visible = false; }
    }
    public void SetFocused(bool focused) { }
    public bool Interact(Node3D interactor)
    {
        if (Contract == null || Director == null || Contract.Status != ContractStatus.Offered) { return false; }
        ContractResult result = Director.Accept(Contract);
        if (result == ContractResult.NoRoom) { GameEvents.PublishHint("Make room for the pump: 3 × 4 × 3 cells. It stays here until it fits."); }
        if (ReferenceEquals(GameEvents.Focused, this)) { GameEvents.PublishFocus(this); }
        return result == ContractResult.Ok;
    }
}
