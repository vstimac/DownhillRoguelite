using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Independent F6 showroom for the generated GLB asset library.
/// It never modifies gameplay, saves, autoloads or project settings.
/// </summary>
public partial class Descent1000Browser : Node3D
{
    [Export] public string CatalogPath { get; set; } = "res://art/descent_1000/manifest.tsv";
    [Export] public float RotationSpeed { get; set; } = 0.35f;

    private readonly List<AssetInfo> _assets = new();
    private Node3D? _anchor;
    private Node3D? _active;
    private Camera3D? _camera;
    private Label? _label;
    private int _index;
    private bool _spin = true;

    private sealed class AssetInfo
    {
        public string Name = "";
        public string Category = "";
        public string Archetype = "";
        public string Path = "";
        public float Width;
        public float Height;
        public float Depth;
        public int Triangles;
    }

    public override void _Ready()
    {
        string catalog = FileAccess.GetFileAsString(CatalogPath);
        string[] rows = catalog.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (string row in rows)
        {
            string[] fields = row.Trim().Split('|');
            if (fields.Length != 8)
            {
                continue;
            }
            AssetInfo info = new();
            info.Name = fields[0];
            info.Category = fields[1];
            info.Archetype = fields[2];
            info.Path = fields[3];
            info.Width = float.Parse(fields[4], CultureInfo.InvariantCulture);
            info.Height = float.Parse(fields[5], CultureInfo.InvariantCulture);
            info.Depth = float.Parse(fields[6], CultureInfo.InvariantCulture);
            info.Triangles = int.Parse(fields[7], CultureInfo.InvariantCulture);
            _assets.Add(info);
        }

        _anchor = new Node3D { Name = "ModelAnchor" };
        AddChild(_anchor);
        _camera = new Camera3D { Name = "GalleryCamera", Current = true, Fov = 37.0f };
        AddChild(_camera);
        DirectionalLight3D key = new() { LightEnergy = 1.5f, ShadowEnabled = true };
        AddChild(key);
        key.RotationDegrees = new Vector3(-47, 32, 0);
        DirectionalLight3D fill = new() { LightEnergy = 0.5f };
        AddChild(fill);
        fill.RotationDegrees = new Vector3(-22, -125, 0);
        MeshInstance3D floor = new()
        {
            Mesh = new PlaneMesh { Size = new Vector2(40, 40) },
            Position = new Vector3(0, -0.035f, 0),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.15f, 0.18f, 0.18f),
                Roughness = 0.98f
            }
        };
        AddChild(floor);

        CanvasLayer ui = new();
        AddChild(ui);
        PanelContainer panel = new();
        panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        panel.OffsetLeft = 20;
        panel.OffsetTop = 18;
        panel.OffsetRight = -20;
        panel.OffsetBottom = 125;
        ui.AddChild(panel);
        _label = new Label();
        _label.AddThemeFontSizeOverride("font_size", 18);
        panel.AddChild(_label);
        ShowAsset(0);
    }

    private void ShowAsset(int requestedIndex)
    {
        if (_assets.Count == 0 || _anchor == null || _camera == null)
        {
            if (_label != null)
            {
                _label.Text = "No GLB assets found. Check manifest.tsv and import progress.";
            }
            return;
        }
        _index = (requestedIndex % _assets.Count + _assets.Count) % _assets.Count;
        AssetInfo data = _assets[_index];
        if (_active != null)
        {
            _anchor.RemoveChild(_active);
            _active.QueueFree();
            _active = null;
        }
        PackedScene? source = GD.Load<PackedScene>("res://" + data.Path);
        if (source != null)
        {
            _active = source.Instantiate<Node3D>();
            _anchor.AddChild(_active);
            _anchor.Rotation = Vector3.Zero;
        }
        float radius = Mathf.Max(1.2f, Mathf.Max(data.Width, Mathf.Max(data.Height, data.Depth)));
        _camera.Position = new Vector3(radius * 1.25f, radius * 0.90f, radius * 1.90f);
        _camera.LookAt(new Vector3(0, data.Height * .42f, 0), Vector3.Up);
        if (_label != null)
        {
            _label.Text = $"THE DESCENT | 1000 ASSET LIBRARY | {_index + 1} / {_assets.Count}\n" +
                $"{data.Name}   ·   {data.Category} / {data.Archetype}   ·   {data.Triangles} triangles\n" +
                "← / → : previous / next    ↑ / ↓ : jump 25    R : toggle rotation";
        }
    }

    public override void _Process(double delta)
    {
        if (_spin && _anchor != null)
        {
            _anchor.RotateY((float)delta * RotationSpeed);
        }
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }
        switch (key.Keycode)
        {
            case Key.Left: ShowAsset(_index - 1); break;
            case Key.Right: ShowAsset(_index + 1); break;
            case Key.Up: ShowAsset(_index - 25); break;
            case Key.Down: ShowAsset(_index + 25); break;
            case Key.R: _spin = !_spin; break;
        }
        GetViewport().SetInputAsHandled();
    }
}
