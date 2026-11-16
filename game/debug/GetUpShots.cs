using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using DownhillRoguelite.Balance;
using DownhillRoguelite.Network;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug-only: photographs a carrier falling onto his belly (or back) and getting up, seen from the
/// side, with a co-op partner's copy of him lying beside him (fed what the game sends, at the game's
/// rate and smoothing), and lays the frames out as one contact sheet. Run it windowed (headless can't
/// render): `-- --shots-directory=C:/somewhere --side=belly|back --heading=1.3 --every=6`.
/// </summary>
public partial class GetUpShots : Node3D
{
    /// <summary>Physics frames between photographs.</summary>
    [Export] public int EveryFrames { get; set; } = 6;
    /// <summary>Photographs on the contact sheet (spread evenly over the whole fall), and its columns.</summary>
    [Export] public int SheetCells { get; set; } = 24;
    [Export] public int SheetColumns { get; set; } = 6;
    [Export] public int CellWidth { get; set; } = 480;
    /// <summary>Where the partner's copy lies, from the carrier (m): along his length, so both are in view.</summary>
    [Export] public Vector3 PartnerOffset { get; set; } = new(0.0f, 0.0f, 2.6f);
    /// <summary>How often the game sends his place and his pose to a partner (Hz), as NetSession and NetPlayerPoses do.</summary>
    [Export] public float SendRate { get; set; } = 15.0f;

    private Label? _label;

    public override async void _Ready()
    {
        string directory = ProjectSettings.GlobalizePath("res://../../getup-shots");
        bool backward = false;
        float heading = 0.0f;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shots-directory=", StringComparison.Ordinal)) { directory = arg["--shots-directory=".Length..]; }
            if (arg == "--side=back") { backward = true; }
            if (arg.StartsWith("--heading=", StringComparison.Ordinal)) { heading = float.Parse(arg["--heading=".Length..], CultureInfo.InvariantCulture); }
            if (arg.StartsWith("--every=", StringComparison.Ordinal)) { EveryFrames = Math.Max(1, int.Parse(arg["--every=".Length..], CultureInfo.InvariantCulture)); }
        }
        Directory.CreateDirectory(directory);
        try
        {
            await Shoot(directory, backward, heading);
        }
        catch (Exception error)
        {
            GD.PrintErr("GetUpShots: " + error);
        }
        GetTree().Quit();
    }

    private async Task Shoot(string directory, bool backward, float heading)
    {
        Node3D world = GD.Load<PackedScene>("res://debug/LearnGodot.tscn").Instantiate<Node3D>();
        AddChild(world);
        world.GetNode("BlueStep").Free();
        world.GetNode("OrangeBlock").Free();
        PlayerController body = world.GetNode<PlayerController>("Player");
        body.DirectionOverride = Vector3.Zero;
        body.Teleport(new Vector3(0, 0.1f, 3), heading);
        body.Health?.Refill();
        await PhysicsFrames(40);
        body.GetNode<BalanceController>("Balance").SetPhysicsProcess(false);
        PlayerRagdoll ragdoll = body.GetNode<PlayerRagdoll>("Ragdoll");
        ragdoll.SafeImpactSpeed = 100;
        Skeleton3D skeleton = body.GetNode<Skeleton3D>("Model/Rig/Skeleton3D");
        Node3D model = body.GetNode<Node3D>("Model");

        // The partner's copy: what a guest's game builds from the two streams (with their smoothing).
        RemoteCarrier partner = new() { Name = "Partner" };
        partner.UseModelPlacement(model.Transform, body.GetNode<Node3D>("CarrierModel").Transform);
        world.AddChild(partner);
        int[] indices = NetPoseCodec.Bind(skeleton);
        float[] data = new float[NetPoseCodec.Size];
        int sequence = 0;
        float stateTimer = 0.0f;
        // The two streams run on their own timers in the game: start the pose half a beat later.
        float poseTimer = -0.5f / SendRate;
        Vector3 offset = Basis.FromEuler(new Vector3(0.0f, heading, 0.0f)) * PartnerOffset;
        void SendPose()
        {
            poseTimer += (float)GetProcessDeltaTime();
            if (poseTimer < 1.0f / SendRate || !NetPoseCodec.Capture(skeleton, model, null, indices, data)) { return; }
            poseTimer = 0.0f;
            int flags = ragdoll.Downed ? NetPoseCodec.Falling : 0;
            partner.Pose?.Receive(++sequence, flags, data);
        }
        skeleton.SkeletonUpdated += SendPose;

        // A side view across his length, both him and the partner's copy in the frame.
        Vector3 along = backward ? body.GlobalBasis.Z : -body.GlobalBasis.Z;
        Vector3 side = along.Cross(Vector3.Up).Normalized();
        Vector3 middle = body.GlobalPosition + along * 0.9f + offset * 0.5f + Vector3.Up * 0.45f;
        Camera3D camera = new() { Fov = 50.0f };
        world.AddChild(camera);
        camera.GlobalPosition = middle + side * 5.2f + Vector3.Up * 1.6f;
        camera.LookAt(middle, Vector3.Up);
        camera.MakeCurrent();

        CanvasLayer layer = new();
        AddChild(layer);
        _label = new Label { Position = new Vector2(16, 12) };
        _label.AddThemeFontSizeOverride("font_size", 26);
        _label.AddThemeColorOverride("font_color", Colors.White);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 8);
        layer.AddChild(_label);

        ragdoll.GoDown(along * 2, Vector3.Zero);
        List<Image> shots = new();
        int frame = 0;
        int afterStanding = -1;
        while (frame < 60 * 14 && afterStanding < 30)
        {
            await PhysicsFrames(1);
            frame++;
            stateTimer += 1.0f / Engine.PhysicsTicksPerSecond;
            if (stateTimer >= 1.0f / SendRate)
            {
                stateTimer = 0.0f;
                partner.Receive(body.GlobalPosition + offset, model.GlobalRotation.Y, "", 0.0f, 1.0f, false, 0);
            }
            if (!ragdoll.Downed && !ragdoll.Rising)
            {
                afterStanding++;
            }
            _label.Text = $"{frame / 60.0f:0.0} s   {Phase(ragdoll)}   (left: the carrier, right: a co-op partner's view)";
            if (frame % EveryFrames != 0)
            {
                continue;
            }
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Image image = GetViewport().GetTexture().GetImage();
            image.SaveJpg(Path.Combine(directory, $"frame_{shots.Count:000}.jpg"), 0.85f);
            shots.Add(image);
        }
        skeleton.SkeletonUpdated -= SendPose;
        SaveSheet(shots, Path.Combine(directory, "contact-sheet.png"));
        GD.Print($"GetUpShots: {shots.Count} frames and contact-sheet.png in {directory}");
    }

    private static string Phase(PlayerRagdoll ragdoll)
    {
        if (ragdoll.TurningOver) { return "rolling onto his back"; }
        if (ragdoll.Downed && ragdoll.GettingUp) { return "getting up (" + ragdoll.RiseClip + ")"; }
        if (ragdoll.Downed) { return "down"; }
        if (ragdoll.Rising) { return "getting up (" + ragdoll.RiseClip + ")"; }
        return "standing";
    }

    /// <summary>Lays SheetCells photographs, spread evenly over the run, out in a grid.</summary>
    private void SaveSheet(List<Image> shots, string path)
    {
        if (shots.Count == 0)
        {
            return;
        }
        int cells = Math.Min(SheetCells, shots.Count);
        int columns = Math.Min(SheetColumns, cells);
        int rows = (cells + columns - 1) / columns;
        int width = CellWidth;
        int height = width * shots[0].GetHeight() / Math.Max(shots[0].GetWidth(), 1);
        Image sheet = Image.CreateEmpty(columns * width, rows * height, false, Image.Format.Rgb8);
        for (int i = 0; i < cells; i++)
        {
            int pick = cells == 1 ? 0 : i * (shots.Count - 1) / (cells - 1);
            Image cell = (Image)shots[pick].Duplicate();
            cell.Convert(Image.Format.Rgb8);
            cell.Resize(width, height, Image.Interpolation.Bilinear);
            sheet.BlitRect(cell, new Rect2I(0, 0, width, height), new Vector2I(i % columns * width, i / columns * height));
        }
        sheet.SavePng(path);
    }

    private async Task PhysicsFrames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }
}
