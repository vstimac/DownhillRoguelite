using System;
using System.Linq;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.WorldUi;
using Godot;
namespace DownhillRoguelite.Debug;

public partial class CharacterCustomizationTests : Node3D
{
    private int _failures;
    public override async void _Ready()
    {
        string path = "res://../../math-audit-artifacts/character-customization-options.cfg";
        GameOptions.UseFile(path);
        GameOptions.SetCharacter("modular");
        CarrierModel look = GetNode<CarrierModel>("World/Player/CarrierModel");
        for (int i = 0; i < 8; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        Node3D? original = look.Model;
        float mass = CarriedLoad.Mass;
        Color shirt = new(0.25f, 0.34f, 0.22f), pack = new(0.68f, 0.61f, 0.47f);
        GameOptions.SetBaseOutfitColours(shirt, pack);
        Check("live base appearance changes preserve the character and gameplay weight", original != null && original == look.Model && mass == CarriedLoad.Mass);
        // A Mixamo rig wears the prototype shirt; the modular carrier's shirt is cut from his own body.
        MeshInstance3D? baseShirt = look.Clothing?.Skeleton?.GetNodeOrNull<MeshInstance3D>("BaseShirtPrototype")
            ?? look.Clothing?.Skeleton?.FindChild("BodyShirt", true, false) as MeshInstance3D;
        Check("the current rig's shirt material receives the selected colour", baseShirt?.MaterialOverride is ShaderMaterial material
            && material.GetShaderParameter("cloth_colour").AsColor().IsEqualApprox(shirt));
        GameOptions.UseFile(path);
        Check("preset and both outfit colours persist", GameOptions.Character == "modular" && GameOptions.BaseShirtColour.IsEqualApprox(shirt)
            && GameOptions.BasePackColour.IsEqualApprox(pack));
        GameOptions.RestoreDefaults();
        Check("resetting gameplay options keeps character appearance", GameOptions.Character == "modular" && GameOptions.BaseShirtColour.IsEqualApprox(shirt)
            && GameOptions.BasePackColour.IsEqualApprox(pack));
        CharacterPage page = new(); AddChild(page);
        page.FitInto(new Rect2(0, 0, 600, 640));
        Check("the creator includes the Foreman outfit preset", page.Carousel?.Characters.Any(character => character.Id == "carrier_foreman" && character.Available) == true);
        MenuCamera camera = new(); AddChild(camera);
        camera.GoTo(MenuCamera.Shot.Portrait, true);
        Vector3 front = camera.Pose(MenuCamera.Shot.Portrait).Origin;
        camera.TurnPortrait(180);
        Check("portrait rotation exposes the back for equipment inspection", camera.PortraitAngle == 180
            && front.DistanceTo(camera.Pose(MenuCamera.Shot.Portrait).Origin) > 3);
        GameOptions.UseFile(GameOptions.DefaultPath);
        GetTree().Quit(Math.Min(_failures, 1));
    }
    private void Check(string name, bool pass)
    {
        if (pass) { GD.Print("PASS  " + name); }
        else { GD.Print("FAIL  " + name); _failures++; }
    }
}
