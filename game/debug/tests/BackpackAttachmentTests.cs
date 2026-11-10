using System;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Real rigs, final evaluated bone poses, visual replacement and unchanged inventory copies.</summary>
public partial class BackpackAttachmentTests : Node3D
{
    private int _failed;

    public override async void _Ready()
    {
        try
        {
            PlayerController player = GetNode<PlayerController>("World/Player");
            CarrierModel look = player.GetNode<CarrierModel>("CarrierModel");
            CarrierInventory inventory = new() { Name = "Inventory", Backpack = GD.Load<BackpackDefinition>("res://inventory/default_backpack.tres"), StartingItemPaths = Array.Empty<string>() };
            player.AddChild(inventory);
            foreach (string id in new[] { "modular", "vucic", "default" })
            {
                if (!look.ShowCharacter(id)) { continue; }
                for (int i = 0; i < 8; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
                ClothingVisualController visuals = look.Clothing!;
                BackpackAttachment? mount = visuals.BackpackMount;
                Check(id + " uses the actual upper-spine bone", mount != null && mount.BoneIdx == ClothingRig.Bone(visuals.Skeleton!, "Spine2"));
                Check(id + " measures torso geometry and has exactly one pack", mount?.MeasuredBody == true && Count(visuals.Skeleton!) == 1,
                    "back=" + mount?.BodyBack.ToString("0.000") + " offset=" + mount?.EffectiveOffset);
                if (mount == null) { continue; }
                Skeleton3D rig = visuals.Skeleton!;
                player.Teleport(new Vector3(-10, 0.1f, 12), 0);
                player.Health?.Refill();
                player.Stamina?.Refill();
                await Frames(15);
                Check(id + " mesh front has the configured torso clearance", Mathf.Abs(FrontGap(rig, mount) - mount.Clearance) < 0.0001f);
                int observations = 0;
                float worst = 0;
                void Observe()
                {
                    Transform3D expected = rig.GlobalTransform * rig.GetBoneGlobalPose(mount.BoneIdx);
                    worst = Mathf.Max(worst, expected.Origin.DistanceTo(mount.GlobalPosition));
                    worst = Mathf.Max(worst, (expected.Basis.X - mount.GlobalBasis.X).Length());
                    observations++;
                }
                rig.SkeletonUpdated += Observe;
                Input.ActionPress("player_move_forward");
                for (int i = 0; i < 30; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
                Input.ActionPress("player_brake");
                for (int i = 0; i < 15; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
                Input.ActionRelease("player_brake"); Input.ActionRelease("player_move_forward");
                player.Teleport(new Vector3(-10, 0.1f, 12), 0);
                await Frames(15);
                Input.ActionPress("player_move_forward"); Input.ActionPress("player_sprint");
                bool sprinted = false;
                for (int i = 0; i < 30; i++) { await Frames(1); sprinted |= player.Sprinting; }
                Input.ActionRelease("player_sprint"); Input.ActionRelease("player_move_forward");
                Check(id + " samples an actual sprint", sprinted);
                player.Teleport(new Vector3(-10, 0.1f, 12), 0);
                await Frames(15);
                player.RagdollJump = false;
                Input.ActionPress("player_jump");
                await Frames(2);
                Input.ActionRelease("player_jump");
                bool jumped = player.Velocity.Y > 0;
                await Frames(90);
                Check(id + " samples a physics jump and landing", jumped && player.IsOnFloor());
                Dive dive = player.GetNode<Dive>("Dive");
                dive.RagdollDive = false;
                bool dived = dive.TryDive();
                for (int i = 0; i < 240 && dive.Diving; i++) { await Frames(1); }
                await Frames(30);
                Check(id + " samples a complete dive transition", dived && !dive.Diving && !GameEvents.IsDowned);
                player.Teleport(new Vector3(-10, 0.1f, 12), 0);
                await Frames(15);
                PlayerRagdoll ragdoll = player.GetNode<PlayerRagdoll>("Ragdoll");
                ragdoll.GoDown(Vector3.Forward * 2, Vector3.Zero);
                await Frames(20);
                bool fell = ragdoll.Downed;
                for (int i = 0; i < 510 && ragdoll.Downed; i++) { await Frames(1); }
                await Frames(45);
                Check(id + " samples native ragdoll and recovery", fell && !ragdoll.Downed);
                rig.SkeletonUpdated -= Observe;
                Check(id + " attachment follows the final evaluated animated bone", observations > 10 && worst < 0.001f, observations + " samples, max error " + worst);
                Transform3D authored = new(Basis.FromEuler(new Vector3(0, 0.2f, 0)).Scaled(Vector3.One * 0.8f), new Vector3(0.1f, 0.03f, -0.06f));
                Node3D source = new() { Transform = authored };
                source.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.4f, 0.5f, 0.2f) } });
                Vector3 adjustment = new(0, 30, 0);
                Vector3 resize = new(0.7f, 1.1f, 0.8f);
                BackpackAttachment? custom = BackpackAttachment.Create(rig, source, "Spine2", new Vector3(0, -0.08f, 0.24f), adjustment, resize, true, 0.015f);
                Check(id + " keeps model-specific transforms below a dedicated pivot", custom != null && source.Transform == authored && source.GetParent() == custom.Pivot);
                Check(id + " fits an off-centre rotated model against the same back", custom != null && Mathf.Abs(FrontGap(rig, custom) - custom.Clearance) < 0.0001f);
                Node3D reference = new() { RotationDegrees = adjustment, Scale = resize };
                ClothingRig.Frame(rig, out Basis bodyFrame, out float unit);
                Basis localBasis = bodyFrame.Transposed() * (rig.GetBoneGlobalRest(custom!.BoneIdx) * custom.Pivot.Transform).Basis;
                localBasis = localBasis.Scaled(Vector3.One * unit);
                Check(id + " rotation and per-axis scale match native Node3D local axes", localBasis.X.DistanceTo(reference.Basis.X)
                    + localBasis.Y.DistanceTo(reference.Basis.Y) + localBasis.Z.DistanceTo(reference.Basis.Z) < 0.0001f);
                reference.Free();
                custom?.Free();
            }
            ItemDefinition pack = GD.Load<ItemDefinition>("res://inventory/items/trail_backpack.tres");
            Check("backpack pickup fits existing storage", inventory.TryAdd(pack, 73, false));
            string identity = inventory.Grid!.Items[0].InstanceId;
            float mass = CarriedLoad.Mass;
            Check("equip replaces the base geometry without duplicating pack nodes", inventory.Equipment!.Wear(pack.ItemId)
                && Count(look.Clothing!.Skeleton!) == 1 && inventory.Grid.Items.Count == 0 && look.Clothing.BackpackMount?.EquippedItemId == pack.ItemId);
            Check("equipping preserves physical copy, condition and total mass", inventory.Equipment.IdentityOf(EquipSlot.Back) == identity
                && Mathf.IsEqualApprox(inventory.Equipment.DurabilityOf(EquipSlot.Back), 73) && Mathf.IsEqualApprox(CarriedLoad.Mass, mass));
            Check("equipment snapshot saves", EquipmentPersistence.Save(inventory, "user://backpack-attachment-tests.json", out _));
            Check("unequip restores one base pack and the same stored copy", inventory.Equipment.TakeOff(EquipSlot.Back)
                && Count(look.Clothing!.Skeleton!) == 1 && inventory.Grid.Items.Count == 1 && inventory.Grid.Items[0].InstanceId == identity);
            Check("loading gear replaces the base and restores the original copy once", EquipmentPersistence.Load(inventory, "user://backpack-attachment-tests.json", out _)
                && inventory.Equipment.IdentityOf(EquipSlot.Back) == identity && inventory.Grid.Items.Count == 0 && Count(look.Clothing!.Skeleton!) == 1);
            bool one = true;
            for (int i = 0; i < 5; i++)
            {
                one &= inventory.Equipment.TakeOff(EquipSlot.Back) && inventory.Equipment.Wear(pack.ItemId);
                one &= Count(look.Clothing!.Skeleton!) == 1;
            }
            Check("repeated equipment changes never accumulate attachment nodes", one);
            ItemDefinition replacement = (ItemDefinition)pack.Duplicate(true);
            replacement.ItemId = "attachment_test_pack";
            Node3D authoredRoot = new() { Name = "AuthoredFieldPack", Position = new Vector3(0.09f, 0.04f, -0.05f) };
            MeshInstance3D authoredMesh = new() { Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.6f, 0.18f) } };
            authoredRoot.AddChild(authoredMesh); authoredMesh.Owner = authoredRoot;
            PackedScene authoredScene = new(); authoredScene.Pack(authoredRoot);
            authoredRoot.Free();
            replacement.Clothing!.Kind = ClothingVisual.VisualKind.Rigid;
            replacement.Clothing.Scene = authoredScene;
            replacement.Clothing!.Scale = new Vector3(0.8f, 1.1f, 0.9f);
            Check("a different backpack swaps its model without changing ownership or capacity", inventory.TryAdd(replacement, -1, false)
                && inventory.Equipment.Wear(replacement.ItemId) && inventory.Equipment.WornIn(EquipSlot.Back)?.ItemId == replacement.ItemId
                && inventory.Grid.Items.Count == 1 && inventory.Grid.Items[0].InstanceId == identity
                && inventory.Grid.Size == inventory.Backpack!.GridSize && Count(look.Clothing!.Skeleton!) == 1
                && look.Clothing.BackpackMount?.ModelScale == replacement.Clothing.Scale
                && look.Clothing.BackpackMount.Model.Name == "AuthoredFieldPack");
            Skeleton3D driver = player.GetNode<Skeleton3D>("Model/Rig/Skeleton3D");
            BackpackAttachment? fallback = BackpackAttachment.Create(driver, ClothingPrototypes.Backpack(), "Spine2", new Vector3(0, -0.08f, 0.24f), Vector3.Zero, Vector3.One, true, 0.015f);
            Check("the fallback mannequin has a valid native attachment", fallback != null && fallback.Pivot.Transform.IsFinite());
            fallback?.Free();
            Skeleton3D target = look.Clothing!.Skeleton!;
            Node3D bad = new(); bad.AddChild(new MeshInstance3D { Mesh = new BoxMesh() });
            Check("a missing bone fails without adding a node", BackpackAttachment.Create(target, bad, "missing_pack_bone", Vector3.Zero, Vector3.Zero, Vector3.One, true, 0.015f) == null && Count(target) == 1);
            Node3D invalid = new(); invalid.AddChild(new MeshInstance3D { Mesh = new BoxMesh() });
            Check("invalid scaling fails safely", BackpackAttachment.Create(target, invalid, "Spine2", Vector3.Zero, Vector3.Zero, Vector3.Zero, true, 0.015f) == null);
            Check("a scene without mesh geometry fails safely", BackpackAttachment.Create(target, new Node3D(), "Spine2", Vector3.Zero, Vector3.Zero, Vector3.One, true, 0.015f) == null);
        }
        catch (Exception error) { _failed++; GD.Print("FAIL  backpack exception: " + error); }
        GD.Print("BACKPACK ATTACHMENT TESTS: " + _failed + " failures");
        GetTree().Quit(_failed);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    }

    private static float FrontGap(Skeleton3D rig, BackpackAttachment mount)
    {
        ClothingRig.Frame(rig, out Basis frame, out float metres);
        Transform3D rest = rig.GetBoneGlobalRest(mount.BoneIdx);
        float nearest = float.PositiveInfinity;
        foreach (Node node in mount.Model.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mesh || mesh.Mesh == null) { continue; }
            Transform3D toModel = mount.Model.GlobalTransform.AffineInverse() * mesh.GlobalTransform;
            Transform3D placement = rest * mount.Pivot.Transform * mount.Model.Transform * toModel;
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = frame.Transposed() * (placement * mesh.Mesh.GetAabb().GetEndpoint(i) - rest.Origin) * metres;
                nearest = Mathf.Min(nearest, point.Z);
            }
        }
        return nearest - mount.BodyBack;
    }

    private static int Count(Skeleton3D rig)
    {
        int count = 0;
        foreach (Node child in rig.GetChildren()) { if (child is BackpackAttachment && !child.IsQueuedForDeletion()) { count++; } }
        return count;
    }
    private void Check(string title, bool success, string detail = "")
    {
        if (success) { GD.Print("PASS  " + title + ": " + detail); }
        else { _failed++; GD.Print("FAIL  " + title + ": " + detail); }
    }
}
