using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The clothing fitting scene and the saved fits: one handle per part (gloves per hand), a moved handle
/// moves its part (skinned garments one side at a time, pieces on bones), and saved fits are put on
/// the pieces when they're built and come back as the handles' values.
/// </summary>
public partial class ClothingFitTests : Node3D
{
    private int _failures;

    public override async void _Ready()
    {
        ClothingFit.Use(new ClothingFit());
        ClothingFitting fitting = GD.Load<PackedScene>("res://debug/ClothingFitting.tscn").Instantiate<ClothingFitting>();
        fitting.CharacterId = "modular";
        fitting.Outfit = new[] { "trail_shirt", "wool_trousers", "leather_gloves", "trail_backpack", "wool_hat" };
        AddChild(fitting);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Dictionary<string, Marker3D> handles = Handles(fitting);
        Check("every worn piece has a handle, gloves one per hand", handles.ContainsKey("trail_shirt") && handles.ContainsKey("wool_trousers")
            && handles.ContainsKey("leather_gloves-Left") && handles.ContainsKey("leather_gloves-Right")
            && handles.ContainsKey("trail_backpack") && handles.ContainsKey("wool_hat-Head"), string.Join(", ", handles.Keys));

        ClothingVisualController? clothing = fitting.GetNodeOrNull<ClothingVisualController>("Preview/ClothingVisuals");
        MeshInstance3D? gloves = null;
        Node3D? pack = null;
        if (clothing != null && clothing.Visuals.TryGetValue(EquipSlot.Hands, out Node3D? glovesNode))
        {
            gloves = glovesNode as MeshInstance3D;
        }
        if (clothing != null && clothing.Visuals.TryGetValue(EquipSlot.Back, out Node3D? packNode))
        {
            pack = packNode?.GetChildOrNull<Node3D>(0);
        }
        if (gloves == null || pack == null || !handles.ContainsKey("leather_gloves-Left") || !handles.ContainsKey("trail_backpack"))
        {
            Check("the gloves and pack are built", false, "");
            GetTree().Quit(1);
            return;
        }
        Aabb glovesBefore = gloves.Mesh.GetAabb();
        Vector3 packBefore = pack.GlobalPosition;
        handles["leather_gloves-Left"].Position = new Vector3(0.0f, 0.2f, 0.0f);
        handles["trail_backpack"].Position = new Vector3(0.0f, 0.0f, 0.1f);
        fitting._Process(0.0);
        Aabb glovesAfter = gloves.Mesh.GetAabb();
        Check("moving the left glove's handle up lifts only the left glove", Mathf.Abs(glovesAfter.End.Y - glovesBefore.End.Y - 0.2f) < 0.02f
            && Mathf.Abs(glovesAfter.Position.Y - glovesBefore.Position.Y) < 0.002f,
            $"top {glovesBefore.End.Y:0.000} → {glovesAfter.End.Y:0.000}, bottom {glovesBefore.Position.Y:0.000} → {glovesAfter.Position.Y:0.000}");
        Vector3 packMoved = pack.GlobalPosition - packBefore;
        Check("moving the pack's handle back moves the pack 10 cm back", Mathf.Abs(packMoved.Length() - 0.1f) < 0.01f, $"moved {packMoved}");

        handles["leather_gloves-Left"].Position = Vector3.Zero;
        handles["trail_backpack"].Position = Vector3.Zero;
        fitting._Process(0.0);
        Check("a handle back at zero puts the piece back", gloves.Mesh.GetAabb().IsEqualApprox(glovesBefore) && pack.GlobalPosition.IsEqualApprox(packBefore), "");

        Node3D probe = new() { Position = new Vector3(0.1f, -0.2f, 0.3f), RotationDegrees = new Vector3(10.0f, 20.0f, 30.0f), Scale = new Vector3(1.1f, 0.9f, 1.2f) };
        ClothingFitEntry entry = new() { Offset = probe.Position, RotationDegrees = probe.RotationDegrees, Scale = probe.Scale };
        Check("a saved fit is the same transform as the handle it came from", entry.Local.IsEqualApprox(probe.Transform), "");
        probe.Free();

        ClothingFit saved = new();
        saved.Set("modular", "trail_backpack", new Vector3(0.0f, 0.0f, 0.1f), Vector3.Zero, Vector3.One, 0.0005f);
        saved.Set("modular", "wool_hat-Head", Vector3.Zero, Vector3.Zero, Vector3.One, 0.0005f);
        Check("a fit of no change isn't stored", saved.Entries.Count == 1 && saved.Find("modular", "wool_hat-Head") == null, "");
        ClothingFit.Use(saved);
        fitting.Rebuild();
        handles = Handles(fitting);
        clothing = fitting.GetNodeOrNull<ClothingVisualController>("Preview/ClothingVisuals");
        Node3D? fittedPack = null;
        if (clothing != null && clothing.Visuals.TryGetValue(EquipSlot.Back, out Node3D? fittedNode))
        {
            fittedPack = fittedNode?.GetChildOrNull<Node3D>(0);
        }
        Check("a saved fit is put on the piece when it's built and shows on its handle",
            fittedPack != null && handles.TryGetValue("trail_backpack", out Marker3D? packHandle)
            && packHandle.Position.IsEqualApprox(new Vector3(0.0f, 0.0f, 0.1f))
            && Mathf.Abs((fittedPack.GlobalPosition - packBefore).Length() - 0.1f) < 0.01f, "");

        ClothingFit.Reload();
        GetTree().Quit(System.Math.Min(_failures, 1));
    }

    private static Dictionary<string, Marker3D> Handles(ClothingFitting fitting)
    {
        Dictionary<string, Marker3D> handles = new();
        foreach (Node node in fitting.GetNode("Fits").FindChildren("*", "Marker3D", true, false))
        {
            if (node is Marker3D handle && !handle.IsQueuedForDeletion())
            {
                handles[handle.Name] = handle;
            }
        }
        return handles;
    }

    private void Check(string name, bool pass, string detail)
    {
        if (pass)
        {
            GD.Print("PASS  " + name + "  " + detail);
        }
        else
        {
            GD.Print("FAIL  " + name + "  " + detail);
            _failures++;
        }
    }
}
