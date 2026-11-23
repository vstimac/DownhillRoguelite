using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The quick look into the pack (a short press of the pack key): a small sheet at the side of the
/// screen with the pack in 2.5D, its weight and where its weight sits. Looking only: the world goes
/// on, the mouse stays with the carrier, and nothing can be moved. Rearranging means setting the
/// pack down (holding the key).
/// </summary>
public partial class PackQuickView : ParchmentPanel
{
    /// <summary>Gap between the sheet and the screen's right edge, in px.</summary>
    [Export] public float ScreenMargin { get; set; } = UiStyle.ScreenMargin;
    /// <summary>Room kept under the sheet for the ability card in the corner, in px (the sheet sits right on top of it).</summary>
    [Export] public float CornerRoom { get; set; } = 44.0f + UiStyle.PieceGap;
    /// <summary>The second line under the figures ({0}: the pack key as last used, Tab or Y).</summary>
    [Export] public string Hint { get; set; } = "Hold {0}: set it down and sort\nI: the same, without holding\nHotbar 1–9: eat and drink on the move";

    public PackGridView? View { get; private set; }

    private Label? _weight;
    private Label? _hint;

    public override void _Ready()
    {
        Title = "Pack";
        Seed = 57.0f;
        Size = new Vector2(320, 480);
        base._Ready();
        MouseFilter = MouseFilterEnum.Ignore;
        View = new PackGridView { Position = new Vector2(14, HeaderHeight), Size = new Vector2(Size.X - 28, Size.Y - HeaderHeight - 150) };
        AddChild(View);
        _weight = UiStyle.MakeLabel("", UiStyle.TextSize, true);
        _weight.Position = new Vector2(16, Size.Y - 148);
        _weight.Size = new Vector2(Size.X - 32, 50);
        AddChild(_weight);
        _hint = UiStyle.MakeLabel(string.Format(Hint, "Tab"), UiStyle.SmallSize, false, UiStyle.InkSoft);
        _hint.Position = new Vector2(16, Size.Y - 94);
        _hint.Size = new Vector2(Size.X - 32, 82);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_hint);
        GameEvents.PackQuickViewRequested += OnRequested;
        GameEvents.PackingModeChanged += OnPacking;
        GameEvents.PackLayoutChanged += OnLayout;
        OnLayout(GameEvents.CurrentPackLayout);
        SetOpenNow(false);
    }

    public override void _ExitTree()
    {
        GameEvents.PackQuickViewRequested -= OnRequested;
        GameEvents.PackingModeChanged -= OnPacking;
        GameEvents.PackLayoutChanged -= OnLayout;
    }

    private void OnRequested()
    {
        if (IsOpen && Visible)
        {
            Close();
            return;
        }
        Vector2 screen = GetViewportRect().Size;
        // Right column, on the HUD margin, standing on the ability card: one edge with the notes above and the card below.
        Position = new Vector2(screen.X - Size.X - ScreenMargin, Mathf.Max(ScreenMargin, screen.Y - ScreenMargin - CornerRoom - Size.Y));
        if (_hint != null)
        {
            _hint.Text = string.Format(Hint, PackingMode.KeyLabel == "Y" ? "Y" : "Tab");
        }
        OnLayout(GameEvents.CurrentPackLayout);
        Open();
    }

    // The pack coming off the back replaces the quick look with the real thing.
    private void OnPacking(bool packing)
    {
        if (packing && IsOpen)
        {
            Close();
        }
    }

    private void OnLayout(PackLayout layout)
    {
        if (_weight == null)
        {
            return;
        }
        int packed = 0;
        foreach (PackLayoutEntry entry in layout.Entries)
        {
            if (entry.Packed)
            {
                packed++;
            }
        }
        LoadSnapshot load = GameEvents.CurrentLoad;
        string side = "centred";
        float offset = load.CenterOfMass.X * 100.0f;
        if (offset > 0.5f)
        {
            side = $"{offset:0} cm right";
        }
        else if (offset < -0.5f)
        {
            side = $"{-offset:0} cm left";
        }
        _weight.Text = $"{layout.Mass:0.0} kg  ·  {packed} things\nWeight sits {load.CenterOfMass.Y:0.00} m up, {side}";
    }
}
