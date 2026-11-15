using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Measures how far the pack's back stands off the torso: the top and bottom of its back face, standing still and
/// sprinting, empty and with 30 kg. A measurement only (prints numbers, no pass or fail).
/// Gap = distance from a back corner of the pack's box to the body's surface along the body's forward direction.
/// </summary>
public partial class BackpackGapProbe : Node3D
{
    private static readonly string[] TorsoBones = { "Hips", "Spine", "Spine1", "Spine2" };

    public override async void _Ready()
    {
        PrintMannequin();
        try
        {
            PlayerController player = GetNode<PlayerController>("World/Player");
            CarrierModel look = player.GetNode<CarrierModel>("CarrierModel");
            CarrierInventory inventory = new() { Name = "Inventory", Backpack = GD.Load<BackpackDefinition>("res://inventory/default_backpack.tres"), StartingItemPaths = Array.Empty<string>() };
            player.AddChild(inventory);
            if (!look.ShowCharacter("modular")) { throw new InvalidOperationException("no modular character"); }
            for (int i = 0; i < 8; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            ClothingVisualController visuals = look.Clothing!;
            BackpackAttachment mount = visuals.BackpackMount!;
            Skeleton3D rig = visuals.Skeleton!;
            BackpackHarness harness = mount.GetNode<BackpackHarness>("BackpackHarness");
            GD.Print("PROBE fitted=" + harness.Fitted + " measured_body=" + mount.MeasuredBody + " clearance=" + harness.PackClearance);
            Transform3D fit = rig.GetBoneGlobalRest(mount.BoneIdx) * mount.FitTransform;
            GD.Print("PROBE fit_in_skeleton x=" + Vec(fit.Basis.X) + " y=" + Vec(fit.Basis.Y) + " z=" + Vec(fit.Basis.Z) + " origin=" + Vec(fit.Origin));

            foreach (float kg in new[] { 0.0f, 30.0f })
            {
                harness.LoadOverride = kg;

                player.Teleport(new Vector3(-10, 0.1f, 12), 0);
                player.Health?.Refill();
                player.Stamina?.Refill();
                await Frames(60);
                Stats standing = await Sample(rig, mount, harness, 60, false);
                Print("kg=" + kg + " standing", standing);
                Transform3D hungPivot = mount.Pivot.GlobalTransform;
                Transform3D inSkeleton = rig.GlobalTransform.AffineInverse() * hungPivot;
                // The placed pose is kept at the bone's rest: undo the animated bone pose, then put the rest back.
                Transform3D fittedLocal = rig.GetBoneGlobalPose(mount.BoneIdx).AffineInverse() * inSkeleton;
                Transform3D atRest = rig.GetBoneGlobalRest(mount.BoneIdx) * fittedLocal;
                GD.Print("PROBE settled kg=" + kg + " rest_pivot_in_skeleton x=" + Vec(atRest.Basis.X) + " y=" + Vec(atRest.Basis.Y) + " z=" + Vec(atRest.Basis.Z) + " origin=" + Vec(atRest.Origin));

                player.Teleport(new Vector3(-10, 0.1f, 12), 0);
                await Frames(15);
                Input.ActionPress("player_move_forward");
                Input.ActionPress("player_sprint");
                await Frames(30);
                Stats sprint = await Sample(rig, mount, harness, 120, true);
                Input.ActionRelease("player_sprint");
                Input.ActionRelease("player_move_forward");
                Print("kg=" + kg + " sprint", sprint);
                await Frames(30);
            }
        }
        catch (Exception error) { GD.Print("PROBE exception: " + error); }
        GetTree().Quit(0);
    }

    /// <summary>The body scene as it stands alone: its skeleton's chain of local transforms up to the scene root.</summary>
    private static void PrintMannequin()
    {
        Node? root = GD.Load<PackedScene>("res://art/characters/quaternius_ual/UAL_Mannequin.glb")?.Instantiate();
        if (root == null) { GD.Print("PROBE mannequin missing"); return; }
        Skeleton3D? skeleton = root.FindChild("Skeleton3D", true, false) as Skeleton3D;
        if (skeleton == null) { GD.Print("PROBE mannequin has no Skeleton3D"); root.Free(); return; }
        Transform3D chain = Transform3D.Identity;
        Node? current = skeleton;
        while (current is Node3D link && current != root)
        {
            chain = link.Transform * chain;
            current = link.GetParent();
        }
        GD.Print("PROBE skeleton_in_root x=" + Vec(chain.Basis.X) + " y=" + Vec(chain.Basis.Y) + " z=" + Vec(chain.Basis.Z) + " origin=" + Vec(chain.Origin));
        root.Free();
    }

    private static string Vec(Vector3 v) => v.X.ToString("0.######") + ", " + v.Y.ToString("0.######") + ", " + v.Z.ToString("0.######");

    private async System.Threading.Tasks.Task Frames(int count)
    {
        for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    }

    private struct Stats
    {
        public int N;
        public float TopSum, BottomSum, TopMax, BottomMax, TopMin, BottomMin, SwingMax, InsideMin;
    }

    private async System.Threading.Tasks.Task<Stats> Sample(Skeleton3D rig, BackpackAttachment mount, BackpackHarness harness, int frames, bool moving)
    {
        Stats s = new() { TopMax = float.NegativeInfinity, BottomMax = float.NegativeInfinity, TopMin = float.PositiveInfinity, BottomMin = float.PositiveInfinity, InsideMin = float.PositiveInfinity };
        for (int i = 0; i < frames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!Gaps(rig, mount, harness, out float top, out float bottom)) { continue; }
            s.N++;
            s.TopSum += top; s.BottomSum += bottom;
            s.TopMax = Mathf.Max(s.TopMax, top); s.BottomMax = Mathf.Max(s.BottomMax, bottom);
            s.TopMin = Mathf.Min(s.TopMin, top); s.BottomMin = Mathf.Min(s.BottomMin, bottom);
            s.SwingMax = Mathf.Max(s.SwingMax, harness.SwingDistance);
            s.InsideMin = Mathf.Min(s.InsideMin, Mathf.Min(top, bottom));
        }
        return s;
    }

    private static void Print(string title, Stats s)
    {
        if (s.N == 0) { GD.Print("PROBE " + title + ": no samples"); return; }
        GD.Print("PROBE " + title + " n=" + s.N
            + " | top mean " + (s.TopSum / s.N * 100).ToString("0.0") + " cm (min " + (s.TopMin * 100).ToString("0.0") + ", max " + (s.TopMax * 100).ToString("0.0") + ")"
            + " | bottom mean " + (s.BottomSum / s.N * 100).ToString("0.0") + " cm (min " + (s.BottomMin * 100).ToString("0.0") + ", max " + (s.BottomMax * 100).ToString("0.0") + ")"
            + " | swing max " + (s.SwingMax * 100).ToString("0.0") + " cm");
    }

    /// <summary>The back face's top and bottom gaps to the torso surface, in metres, for the pack now.</summary>
    private static bool Gaps(Skeleton3D rig, BackpackAttachment mount, BackpackHarness harness, out float top, out float bottom)
    {
        top = bottom = 0.0f;
        if (harness.Hull == null || !mount.Pivot.GlobalTransform.IsFinite()) { return false; }
        ClothingRig.Frame(rig, out Basis frame, out float metres);
        // The frame is in the skeleton's space; the body's surface and the pack are in the world.
        Basis world = rig.GlobalBasis * frame;
        Vector3 up = world.Y.Normalized();
        Vector3 inward = -world.Z.Normalized();
        List<int> torso = new();
        foreach (string name in TorsoBones)
        {
            int bone = ClothingRig.Bone(rig, name);
            if (bone >= 0) { torso.Add(bone); }
        }
        Aabb box = mount.PackBounds();
        Transform3D pivot = mount.Pivot.GlobalTransform;
        List<Vector3> corners = new();
        for (int i = 0; i < 8; i++) { corners.Add(pivot * box.GetEndpoint(i)); }
        // The back face: the four corners nearest the body (largest share along the inward direction).
        corners.Sort((a, b) => b.Dot(inward).CompareTo(a.Dot(inward)));
        List<Vector3> back = corners.GetRange(0, 4);
        back.Sort((a, b) => b.Dot(up).CompareTo(a.Dot(up)));
        float topGap = float.PositiveInfinity, bottomGap = float.PositiveInfinity;
        for (int i = 0; i < 4; i++)
        {
            float gap = harness.Hull.GapAlong(back[i], inward, 0.35f, torso);
            if (i < 2) { topGap = Mathf.Min(topGap, gap); }
            else { bottomGap = Mathf.Min(bottomGap, gap); }
        }
        top = topGap;
        bottom = bottomGap;
        return float.IsFinite(top) && float.IsFinite(bottom) && metres > 0.0f;
    }
}
