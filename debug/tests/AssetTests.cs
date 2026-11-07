using System.Collections.Generic;
using DownhillRoguelite.Audio;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks that the art and sound packs are wired in: every item's model loads and fits
/// inside its cell block; every boulder and trunk model has a mesh; every sound set and recorded
/// footstep loads. A missing or renamed file fails here instead of quietly falling back.
/// Run: godot --headless --path . res://debug/tests/AssetTests.tscn
/// </summary>
public partial class AssetTests : Node3D
{
    [Export] public SoundEffects? Sounds { get; set; }
    [Export] public Footsteps? Steps { get; set; }

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        TestItems();
        TestProps();
        TestSounds();
        if (_failures.Count == 0)
        {
            GD.Print("ASSET TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"ASSET TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void TestItems()
    {
        int withModel = 0;
        List<string> bad = new();
        foreach (string file in DirAccess.GetFilesAt("res://inventory/items"))
        {
            if (!file.EndsWith(".tres") || ResourceLoader.Load($"res://inventory/items/{file}") is not ItemDefinition item || item.Model == null)
            {
                continue;
            }
            withModel++;
            WorldItem world = new() { Definition = item };
            AddChild(world);
            Vector3 box = (Vector3)item.SizeCells * world.CellSize * world.VisualScale;
            Aabb bounds = new();
            if (world.ModelNode != null)
            {
                bounds = world.ModelNode.Transform * ModelFit.Bounds(world.ModelNode);
            }
            // The grid lines span the block exactly; the model must sit within it.
            if (world.ModelNode == null || bounds.Size.X > box.X * 1.05f || bounds.Size.Y > box.Y * 1.05f || bounds.Size.Z > box.Z * 1.05f)
            {
                bad.Add(item.ItemId);
            }
            world.QueueFree();
        }
        Check("item models load and fit inside their cell blocks", withModel >= 12 && bad.Count == 0,
            $"{withModel} items with models, oversized: [{string.Join(", ", bad)}]");
    }

    private void TestProps()
    {
        TerrainSettings settings = ResourceLoader.Load<TerrainSettings>("res://terrain/default_terrain.tres");
        List<string> missing = new();
        foreach (string path in settings.BoulderModels)
        {
            if (ModelFit.FirstMesh(path, out Transform3D _) == null)
            {
                missing.Add(path.GetFile());
            }
        }
        foreach (string path in settings.TrunkModels)
        {
            if (ModelFit.FirstMesh(path, out Transform3D _) == null)
            {
                missing.Add(path.GetFile());
            }
        }
        Check("every boulder and trunk model has a mesh", missing.Count == 0 && settings.BoulderModels.Length > 0,
            $"{settings.BoulderModels.Length} boulder and {settings.TrunkModels.Length} trunk models, missing [{string.Join(", ", missing)}]");
    }

    private void TestSounds()
    {
        if (Sounds == null)
        {
            Check("sound sets load", false, "no SoundEffects node");
            return;
        }
        string[] keys = { "land", "land_hard", "fall", "pickup", "eat", "drink", "pack_open", "pack_close", "map_open", "map_close", "boon", "station", "run_end" };
        List<string> empty = new();
        foreach (string key in keys)
        {
            if (Sounds.SetSize(key) == 0)
            {
                empty.Add(key);
            }
        }
        int recorded = 0;
        if (Steps != null)
        {
            foreach (string prefix in Steps.RecordedPrefixes)
            {
                if (ResourceLoader.Exists($"{Steps.RecordedFolder}/{prefix}_000.ogg"))
                {
                    recorded++;
                }
            }
        }
        Check("every sound set and recorded footstep loads", empty.Count == 0 && recorded == 4,
            $"{keys.Length - empty.Count}/{keys.Length} sets, empty [{string.Join(", ", empty)}], {recorded}/4 recorded footstep surfaces");
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
