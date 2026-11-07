using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// Makes an icon for every item from its own 3D model, so icons always match the world: a hidden
/// studio (its own 3D world: camera, key and fill light, transparent background) photographs each
/// requested model once, a frame apart, and keeps the picture. The ink_icon shader then draws it
/// as an illustration. In headless runs nothing renders, so Get returns null and slots draw an
/// inked block in the item's colour instead.
/// </summary>
public partial class ItemIcons : Node
{
    [Export] public int Size { get; set; } = 160;

    public static ItemIcons? Instance { get; private set; }

    private readonly Dictionary<string, Texture2D> _done = new();
    private readonly Queue<ItemDefinition> _queue = new();
    private readonly HashSet<string> _queued = new();
    private SubViewport? _studio;
    private Node3D? _stage;
    private ItemDefinition? _shooting;
    private int _wait;
    private bool _enabled;

    public override void _Ready()
    {
        Instance = this;
        _enabled = DisplayServer.GetName() != "headless";
        if (!_enabled)
        {
            return;
        }
        _studio = new SubViewport
        {
            Size = new Vector2I(Size, Size),
            TransparentBg = true,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            Msaa3D = Viewport.Msaa.Msaa4X,
        };
        AddChild(_studio);
        Camera3D camera = new() { Fov = 30.0f, Current = true };
        _studio.AddChild(camera);
        camera.LookAtFromPosition(new Vector3(1.6f, 1.4f, 1.9f), new Vector3(0.0f, 0.25f, 0.0f), Vector3.Up);
        _studio.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50.0f, 30.0f, 0.0f), LightEnergy = 1.3f });
        _studio.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-20.0f, -140.0f, 0.0f), LightEnergy = 0.5f });
        _studio.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.ClearColor, AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.8f, 0.78f, 0.72f), AmbientLightEnergy = 0.7f },
        });
        _stage = new Node3D();
        _studio.AddChild(_stage);
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>The item's icon if it has been made; otherwise asks for it and returns null for now.</summary>
    public Texture2D? Get(ItemDefinition item)
    {
        if (_done.TryGetValue(item.ItemId, out Texture2D? icon))
        {
            return icon;
        }
        if (!ItemVisual.HasModel(item))
        {
            // Items without a model show their ink outline drawing.
            return ItemVisual.Outline(item);
        }
        if (_enabled && _queued.Add(item.ItemId))
        {
            _queue.Enqueue(item);
        }
        return null;
    }

    public override void _Process(double delta)
    {
        if (!_enabled || _studio == null || _stage == null)
        {
            return;
        }
        if (_shooting != null)
        {
            // Two frames after the shot was set up, the picture is ready.
            _wait--;
            if (_wait > 0)
            {
                return;
            }
            Image image = _studio.GetTexture().GetImage();
            _done[_shooting.ItemId] = ImageTexture.CreateFromImage(image);
            _shooting = null;
            _studio.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            foreach (Node child in _stage.GetChildren())
            {
                child.QueueFree();
            }
            return;
        }
        if (_queue.Count == 0)
        {
            return;
        }
        _shooting = _queue.Dequeue();
        Node3D? holder = ItemVisual.BuildModel(_shooting);
        if (holder == null)
        {
            _shooting = null;
            return;
        }
        Aabb bounds = ModelFit.Bounds(holder);
        holder.Transform = ModelFit.FitOnGround(bounds, Vector3.One, true);
        _stage.AddChild(holder);
        _studio.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        _wait = 3;
    }
}
