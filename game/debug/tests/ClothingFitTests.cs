using System.Collections.Generic;
using System.Threading.Tasks;
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
        Check("every worn piece has a handle, gloves one per hand", Has(handles, "trail_shirt") && Has(handles, "wool_trousers")
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

        try
        {
            await TestBackpackIntegration(fitting);
        }
        catch (System.Exception error)
        {
            Check("combined backpack fitting workflow completes", false, error.ToString());
        }
        ClothingFit.Reload();
        GetTree().Quit(System.Math.Min(_failures, 1));
    }

    private async Task TestBackpackIntegration(ClothingFitting fitting)
    {
        ClothingVisualController clothing = fitting.GetNode<ClothingVisualController>("Preview/ClothingVisuals");
        CarrierWornGear worn = fitting.GetNode<CarrierWornGear>("Preview/WornGear");
        BackpackAttachment? previewPack = clothing.BackpackMount;
        Node3D? previewHat = Piece(worn, "wool_hat-Head");
        GearSnapshot liveGear = GameEvents.CurrentGear;
        GameEvents.PublishGear(GearSnapshot.None);
        await Frame();
        Check("live equipment changes do not replace the fitting preview's backpack",
            previewPack != null && clothing.BackpackMount == previewPack && !previewPack.IsQueuedForDeletion()
            && clothing.Visuals.ContainsKey(EquipSlot.Back), "");
        Check("live equipment changes do not remove the fitting preview's legacy hat",
            previewHat != null && Piece(worn, "wool_hat-Head") == previewHat && !previewHat.IsQueuedForDeletion(), "");
        GameEvents.PublishGear(liveGear);

        ClothingFit saved = new();
        saved.Set("modular", "trail_backpack", new Vector3(0.015f, 0.03f, 0.08f),
            new Vector3(9, -12, 5), new Vector3(1.08f, 0.93f, 1.04f), 0.0005f);
        saved.Set("modular", "base_pack", new Vector3(0, 0, 0.04f), Vector3.Zero, Vector3.One, 0.0005f);
        const string savePath = "user://clothing-fit-roundtrip-tests.tres";
        Error saveError = ResourceSaver.Save(saved, savePath);
        ClothingFit? restored = null;
        if (saveError == Error.Ok)
        {
            restored = ResourceLoader.Load<ClothingFit>(savePath, "", ResourceLoader.CacheMode.Ignore);
        }
        Check("fit resources round-trip character, piece, rotation and scale through an isolated save",
            restored?.Entries.Count == 2
            && restored.Find("modular", "trail_backpack")?.Local.IsEqualApprox(saved.Find("modular", "trail_backpack")!.Local) == true
            && restored.Find("modular", "base_pack")?.Offset.IsEqualApprox(new Vector3(0, 0, 0.04f)) == true
            && restored.Find("vucic", "trail_backpack") == null, saveError.ToString());
        if (restored == null) { return; }

        ClothingFit.Use(restored);
        fitting.Rebuild();
        await Frame();
        clothing = fitting.GetNode<ClothingVisualController>("Preview/ClothingVisuals");
        BackpackAttachment mount = clothing.BackpackMount!;
        Transform3D fitted = mount.FitTransform;
        Transform3D authored = mount.Model.Transform;
        Vector3 originalOffset = mount.LocalOffset;
        bool stable = true;
        for (int i = 0; i < 6; i++)
        {
            stable &= mount.Refit() && mount.FitTransform.IsEqualApprox(fitted);
        }
        Check("automatic refitting preserves a rotated and scaled saved hand fit without drift", stable, "");
        mount.LocalOffset += new Vector3(0, 0.025f, 0.02f);
        bool changed = mount.Refit() && !mount.FitTransform.IsEqualApprox(fitted);
        mount.LocalOffset = originalOffset;
        Check("changing automatic placement and restoring it also restores the same hand fit",
            changed && mount.Refit() && mount.FitTransform.IsEqualApprox(fitted), "");
        for (int i = 0; i < 6; i++)
        {
            ClothingFitter.Fit(clothing.Skeleton, "modular", "trail_backpack", mount);
        }
        Check("reapplying saved fitting does not accumulate transforms or alter the authored model root",
            mount.FitTransform.IsEqualApprox(fitted) && mount.Model.Transform.IsEqualApprox(authored), "");
        Transform3D singular = new(Basis.FromScale(Vector3.Zero), Vector3.Zero);
        Check("an invalid hand fit leaves the current valid fit intact",
            !mount.ApplyAdjustment(singular) && mount.FitTransform.IsEqualApprox(fitted), "");

        fitting.Outfit = System.Array.Empty<string>();
        ClothingFit.Use(new ClothingFit());
        fitting.Rebuild();
        await Frame();
        clothing = fitting.GetNode<ClothingVisualController>("Preview/ClothingVisuals");
        Vector3 basePosition = clothing.BackpackMount!.FitTransform.Origin;
        Check("an empty outfit exposes exactly one selectable base pack in the isolated preview",
            Handles(fitting).ContainsKey("base_pack") && CountPacks(clothing.Skeleton!) == 1
            && clothing.BackpackMount!.EquippedItemId.Length == 0, "");
        ClothingFit.Use(restored);
        fitting.Rebuild();
        await Frame();
        clothing = fitting.GetNode<ClothingVisualController>("Preview/ClothingVisuals");
        Transform3D fittedBase = clothing.BackpackMount!.FitTransform;
        Check("a saved base-pack fit is applied when the cosmetic pack is created",
            Mathf.Abs(clothing.BackpackMount!.FitTransform.Origin.DistanceTo(basePosition) - 0.04f) < 0.001f, "");
        ItemDefinition trailPack = GD.Load<ItemDefinition>("res://inventory/items/trail_backpack.tres");
        WornPiece[] slots = new WornPiece[EquipmentSlots.Count];
        slots[(int)EquipSlot.Back] = new WornPiece(trailPack.ItemId, trailPack.DisplayName, trailPack.MaxDurability, trailPack.MaxDurability, trailPack);
        GearSnapshot equipped = GearSnapshot.None with { Slots = slots };
        bool restoredOnce = true;
        for (int i = 0; i < 4; i++)
        {
            clothing.ShowGear(equipped);
            restoredOnce &= clothing.BackpackMount?.EquippedItemId == trailPack.ItemId && CountPacks(clothing.Skeleton!) == 1;
            clothing.ShowGear(GearSnapshot.None);
            restoredOnce &= clothing.BackpackMount?.FitTransform.IsEqualApprox(fittedBase) == true && CountPacks(clothing.Skeleton!) == 1;
        }
        Check("removing equipped packs repeatedly restores one base pack with its own saved fit", restoredOnce, "");

        CarrierCharacter? other = CarrierRoster.Load()?.Pick("vucic");
        if (other?.Id == "vucic")
        {
            fitting.CharacterId = "vucic";
            ClothingFit.Use(new ClothingFit());
            fitting.Rebuild();
            await Frame();
            clothing = fitting.GetNode<ClothingVisualController>("Preview/ClothingVisuals");
            Transform3D otherOriginal = clothing.BackpackMount!.FitTransform;
            ClothingFit.Use(restored);
            fitting.Rebuild();
            await Frame();
            clothing = fitting.GetNode<ClothingVisualController>("Preview/ClothingVisuals");
            Check("another character never inherits the modular character's backpack fit",
                clothing.BackpackMount!.FitTransform.IsEqualApprox(otherOriginal)
                && Handles(fitting)["base_pack"].Transform.IsEqualApprox(Transform3D.Identity), "");
        }
        else { GD.Print("SKIP  cross-character fit check: vucic model is not installed"); }
    }

    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private static Node3D? Piece(CarrierWornGear worn, string key)
    {
        foreach ((string pieceKey, Node3D node) in worn.Pieces()) { if (pieceKey == key) { return node; } }
        return null;
    }

    private static int CountPacks(Skeleton3D skeleton)
    {
        int count = 0;
        foreach (Node child in skeleton.GetChildren()) { if (child is BackpackAttachment && !child.IsQueuedForDeletion()) { count++; } }
        return count;
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

    /// <summary>A handle for this piece, or for one of its parts (a modelled garment's meshes and sides).</summary>
    private static bool Has(Dictionary<string, Marker3D> handles, string piece)
    {
        foreach (string key in handles.Keys)
        {
            if (key == piece || key.StartsWith(piece + "-"))
            {
                return true;
            }
        }
        return false;
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
