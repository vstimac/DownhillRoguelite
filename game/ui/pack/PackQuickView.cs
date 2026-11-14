using DownhillRoguelite.Core;
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
    [Export] public float ScreenMargin { get; set; } = 28.0f;
    /// <summary>Where the sheet's top sits, as a share of the screen's height (under the notes, over the ability card).</summary>
    [Export] public float ScreenTop { get; set; } = 0.37f;
    [Export] public string Hint { get; set; } = "Hold Tab to set the pack down and repack";

    public PackGridView? View { get; private set; }

    private Label? _weight;
    private Label? _hint;

    public override void _Ready()
    {
        Title = "Pack";
        Seed = 57.0f;
        Size = new Vector2(300, 430);
        base._Ready();
        MouseFilter = MouseFilterEnum.Ignore;
        View = new PackGridView { Position = new Vector2(14, HeaderHeight), Size = new Vector2(Size.X - 28, Size.Y - HeaderHeight - 74) };
        AddChild(View);
        _weight = UiStyle.MakeLabel("", UiStyle.TextSize, true);
        _weight.Position = new Vector2(20, Size.Y - 74);
        _weight.Size = new Vector2(Size.X - 40, 28);
        AddChild(_weight);
        _hint = UiStyle.MakeLabel(Hint, UiStyle.SmallSize, false, UiStyle.InkSoft);
        _hint.Position = new Vector2(20, Size.Y - 46);
        _hint.Size = new Vector2(Size.X - 40, 40);
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
        Position = new Vector2(screen.X - Size.X - ScreenMargin, Mathf.Min(screen.Y * ScreenTop, screen.Y - Size.Y));
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
        _weight.Text = $"{layout.Mass:0.0} kg  ·  {packed} things";
    }
}
