using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Representative forest descent with readable, optional vaultable debris; F7 advances checkpoints.</summary>
public partial class ForestDescentLab : Node3D
{
    [Export] public float StartDistance { get; set; } = 120;
    [Export] public float CourseLength { get; set; } = 180;
    [Export] public float ObstacleSpacing { get; set; } = 30;
    [Export] public float BenchmarkWarmup { get; set; } = 3;
    [Export] public float BenchmarkSeconds { get; set; } = 12;
    private PlayerController? _player;
    private TerrainStreamer? _terrain;
    private StartScreen? _start;
    private bool _placed;
    private int _checkpoint;
    private string _benchmark = "";
    private float _elapsed;
    private readonly List<double> _frames = new();
    private double _drawCalls;
    private double _triangles;
    private Node3D? _course;
    private bool _cleanPreview;

    public override void _Ready()
    {
        _player = GetNodeOrNull<PlayerController>("Player");
        _terrain = GetNodeOrNull<TerrainStreamer>("Terrain");
        _start = GetNodeOrNull<StartScreen>("StartScreen");
        DebugDraw3D.SetEnabled(false);
        _cleanPreview = DebugDraw3D.HideHud;
        if (_cleanPreview) { GetTree().NodeAdded += OnPreviewNodeAdded; }
        GameEvents.RespawnRequested += OnRespawn;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--forest-benchmark=", StringComparison.Ordinal)) { _benchmark = arg["--forest-benchmark=".Length..]; }
        }
    }
    public override void _ExitTree()
    {
        GameEvents.RespawnRequested -= OnRespawn;
        if (_cleanPreview) { GetTree().NodeAdded -= OnPreviewNodeAdded; }
    }
    private void OnPreviewNodeAdded(Node node)
    {
        if (node is Label3D label) { label.Visible = false; }
        else if (node is CanvasLayer canvas) { canvas.Visible = false; }
    }
    private void OnRespawn()
    {
        _course?.QueueFree();
        _course = null;
        _placed = false;
        _checkpoint = 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_placed || _terrain?.Generator == null || _player == null || GetTree().Paused) { return; }
        if (_start != null && IsInstanceValid(_start) && !_start.Started) { return; }
        _placed = true;
        PlaceCheckpoint();
        BuildCourse();
        if (DebugDraw3D.HideHud) { HideCanvases(this); }
        GameEvents.PublishHint("Forest prototype · follow the trail or negotiate the debris · F7: next checkpoint · F3: diagnostics");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F7 && _placed && !GetTree().Paused && !GameEvents.IsScreenOpen)
        {
            _checkpoint = (_checkpoint + 1) % 3;
            PlaceCheckpoint();
            GetViewport().SetInputAsHandled();
        }
    }

    private static void HideCanvases(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is CanvasLayer canvas) { canvas.Visible = false; }
            else if (child is Label3D label) { label.Visible = false; }
            HideCanvases(child);
        }
    }

    private void PlaceCheckpoint()
    {
        TerrainGenerator? generator = _terrain?.Generator;
        if (generator == null || _player == null) { return; }
        float z = generator.Settings.SpawnZ - StartDistance - _checkpoint * CourseLength / 3;
        float x = generator.SpineX(z);
        Vector3 direction = new Vector3(generator.SpineX(z - 5) - x, 0, -5).Normalized();
        _player.Teleport(new Vector3(x, generator.Height(x, z) + 0.15f, z), Mathf.Atan2(-direction.X, -direction.Z));
    }

    private void BuildCourse()
    {
        TerrainGenerator? generator = _terrain?.Generator;
        if (generator == null || ObstacleSpacing <= 0 || generator.Settings.BoulderModels.Length == 0) { return; }
        _course = new Node3D { Name = "ForestCourse" };
        AddChild(_course);
        SurfaceType? rock = ResourceLoader.Load<SurfaceType>(generator.Settings.RockPath);
        int index = 0;
        for (float distance = ObstacleSpacing; distance < CourseLength; distance += ObstacleSpacing)
        {
            float z = generator.Settings.SpawnZ - StartDistance - distance;
            float side = 0.7f;
            if (index % 2 == 0) { side = -side; }
            float x = generator.SpineX(z) + side;
            Vector3 size = new(1.4f, 0.65f, 0.9f);
            StaticBody3D body = new() { Name = "CourseRock" + index, Position = new Vector3(x, generator.Height(x, z) + size.Y * 0.25f, z) };
            body.AddToGroup(TerrainChunk.VaultableGroup);
            if (rock != null) { body.SetMeta(SurfaceType.MetaKey, rock); }
            Mesh? mesh = ModelFit.FirstMesh(generator.Settings.BoulderModels[index % generator.Settings.BoulderModels.Length], out Transform3D local);
            if (mesh != null)
            {
                body.AddChild(new MeshInstance3D { Mesh = mesh, Transform = ModelFit.FitCentred(local * mesh.GetAabb(), size) * local, MaterialOverride = _terrain?.RockMaterial });
            }
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            _course.AddChild(body);
            index++;
        }
    }

    public override void _Process(double delta)
    {
        if (_benchmark.Length == 0 || !_placed || GetTree().Paused) { return; }
        _elapsed += (float)delta;
        if (_elapsed < BenchmarkWarmup) { return; }
        _frames.Add(delta * 1000);
        _drawCalls += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        _triangles += Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        if (_elapsed < BenchmarkWarmup + BenchmarkSeconds) { return; }
        _frames.Sort();
        int count = _frames.Count;
        var report = new { Platform = OS.GetName(), Renderer = RenderingServer.GetCurrentRenderingMethod().ToString(),
            Device = RenderingServer.GetVideoAdapterName(), Samples = count, Seconds = BenchmarkSeconds,
            MedianMs = _frames[count / 2], P95Ms = _frames[Math.Min(count - 1, (int)(count * 0.95))],
            WorstMs = _frames[^1], AverageDrawCalls = _drawCalls / count, AverageTriangles = _triangles / count,
            LoadedChunks = _terrain?.LoadedCount, MaxChunkApplyMs = _terrain?.MaxApplyMilliseconds,
            MaxPlantApplyMs = TerrainChunk.MaxPlantMilliseconds, MaxSharedPlantFrameMs = TerrainChunk.MaxPlantFrameMilliseconds,
            ViewportPixels = new[] { GetViewport().GetTexture().GetWidth(), GetViewport().GetTexture().GetHeight() },
            UiCoordinates = new[] { GetViewport().GetVisibleRect().Size.X, GetViewport().GetVisibleRect().Size.Y },
            LoadKg = GameEvents.CurrentLoad.Mass, Seed = _terrain?.Generator?.Seed,
            Note = "Short native PC sample; no Android or sustained thermal performance claim. Capture must be off for measurement." };
        string path = ProjectSettings.GlobalizePath(_benchmark);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Forest benchmark: " + path);
        _benchmark = "";
        GetTree().Quit();
    }
}
