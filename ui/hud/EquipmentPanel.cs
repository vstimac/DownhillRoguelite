using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The carrier's equipment sheet on the left: a drawn figure with the slots around it. The
/// backpack slot shows the pack actually carried (its name and weight from the load); the clothing
/// slots (head, torso, legs, hands, feet, cape) stay empty outlines until gear exists (M14). It
/// sinks to half opacity when nothing changes, so the world stays clear, and comes back on hover,
/// when the load changes or while a UI page is open.
/// </summary>
public partial class EquipmentPanel : ParchmentPanel
{
    public string PackName { get; private set; } = "";

    private readonly (string Name, InkIcon Glyph, Vector2 At)[] _layout =
    {
        ("Head", InkIcon.Hat, new Vector2(118, 64)),
        ("Torso", InkIcon.Shirt, new Vector2(118, 128)),
        ("Legs", InkIcon.Trousers, new Vector2(118, 192)),
        ("Feet", InkIcon.Shoe, new Vector2(118, 256)),
        ("Hands", InkIcon.Glove, new Vector2(14, 160)),
        ("Cape", InkIcon.Cape, new Vector2(14, 224)),
    };

    private ItemSlot? _pack;
    private Label? _packLabel;
    private Control? _figure;
    private float _idle;
    private bool _hovered;

    public override void _Ready()
    {
        Seed = 11.0f;
        base._Ready();
        _figure = new FigureView { Position = new Vector2(66, 44), Size = new Vector2(52, 260) };
        AddChild(_figure);
        _pack = new ItemSlot { Position = new Vector2(14, 62), Size = new Vector2(54, 54), EmptyGlyph = InkIcon.Backpack, Index = 90 };
        AddChild(_pack);
        _packLabel = UiStyle.MakeLabel("", 14);
        _packLabel.Position = new Vector2(12, 10);
        AddChild(_packLabel);
        foreach ((string name, InkIcon glyph, Vector2 at) in _layout)
        {
            ItemSlot slot = new() { Position = at, Size = new Vector2(48, 48), EmptyGlyph = glyph, Index = (int)glyph, TooltipText = $"{name}: nothing worn yet" };
            AddChild(slot);
        }
        MouseEntered += () => _hovered = true;
        MouseExited += () => _hovered = false;
        GameEvents.LoadChanged += OnLoad;
        OnLoad(GameEvents.CurrentLoad);
    }

    public override void _ExitTree()
    {
        GameEvents.LoadChanged -= OnLoad;
    }

    private void OnLoad(LoadSnapshot load)
    {
        _idle = 0.0f;
        string name = load.Name;
        int cut = name.IndexOf(" (");
        if (cut > 0)
        {
            name = name.Substring(0, cut);
        }
        PackName = name;
        if (_packLabel != null)
        {
            _packLabel.Text = $"{name}  {load.Mass:0.0} kg";
        }
        if (_pack != null)
        {
            _pack.TooltipText = $"Backpack: {name}\n{load.Mass:0.0} kg carried";
            _pack.Pop();
        }
    }

    public override void _Process(double delta)
    {
        _idle += (float)delta;
        float target = 0.55f;
        if (_hovered || _idle < 4.0f || GameEvents.IsScreenOpen)
        {
            target = 1.0f;
        }
        if (IsOpen)
        {
            Modulate = new Color(1, 1, 1, Mathf.MoveToward(Modulate.A, target, (float)delta * 1.5f));
        }
    }

    private partial class FigureView : Control
    {
        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            // The figure is drawn in a square as tall as the view, so it fills the sheet's height.
            float side = Size.Y * 0.9f;
            InkDraw.Icon(this, InkIcon.Figure, new Rect2(new Vector2(Size.X * 0.5f - side * 0.5f, Size.Y * 0.05f), new Vector2(side, side)), 71);
        }
    }
}
