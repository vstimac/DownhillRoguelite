using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// Health beside the carrier, the mirror of the stamina ring (ContextRing) on his other side: a
/// small column of hearts that is there only when it matters. It shows when health changes (a hit,
/// a mend) and for a few seconds after, stays while health is low, and fades away otherwise, so
/// the fight is read without a look at the corner. The hearts themselves are a HeartRow (the same
/// flash on a lost heart, the same beat on the last one). Presentation only: Hud feeds it.
/// </summary>
public partial class SideHearts : Control
{
    /// <summary>The ring whose side of the carrier this mirrors (it also projects the carrier on screen).</summary>
    public ContextRing? Ring { get; set; }

    [Export] public float HeartSize { get; set; } = 18.0f;
    [Export] public float Gap { get; set; } = 3.0f;
    /// <summary>Seconds the hearts stay after health last changed.</summary>
    [Export] public float HoldSeconds { get; set; } = 3.0f;
    /// <summary>Below this share of full health they stay up.</summary>
    [Export] public float LowBelow { get; set; } = 0.5f;
    /// <summary>A change smaller than this share of full health doesn't call them up (slow drains).</summary>
    [Export] public float ChangeShare { get; set; } = 0.01f;
    [Export] public float FadeInSeconds { get; set; } = 0.12f;
    [Export] public float FadeOutSeconds { get; set; } = 0.6f;
    [Export] public float EdgeMargin { get; set; } = 8.0f;

    /// <summary>True once the player has put the hearts somewhere by hand: they stay there instead of following.</summary>
    public System.Func<bool>? Pinned { get; set; }
    /// <summary>True while the HUD is being arranged (Alt held): the hearts show so they can be grabbed.</summary>
    public System.Func<bool>? Arranging { get; set; }

    public HeartRow? Hearts { get; private set; }
    /// <summary>How visible the hearts are right now, 0..1.</summary>
    public float Alpha { get; private set; }
    /// <summary>True while they are called up (a recent change, or low health).</summary>
    public bool Wanted => _hold > 0.0f || (_max > 0.0f && _value / _max < LowBelow && _value > 0.0f);

    private float _value = -1.0f;
    private float _max = 100.0f;
    private float _hold;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Hearts = new HeartRow { Name = "Hearts", PerRow = 1, Rows = 8, HeartSize = HeartSize, Gap = Gap, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(Hearts);
        Modulate = new Color(1, 1, 1, 0);
    }

    /// <summary>Feeds the real health; a change calls the hearts up.</summary>
    public void SetValue(float value, float max)
    {
        if (_value >= 0.0f && max > 0.0f && Mathf.Abs(value - _value) / max >= ChangeShare)
        {
            _hold = HoldSeconds;
        }
        _value = value;
        _max = max;
        Hearts?.SetValue(value, max);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _hold = Mathf.Max(_hold - dt, 0.0f);
        bool arranging = Arranging?.Invoke() == true;
        bool placed = true;
        if (Pinned?.Invoke() != true && !arranging)
        {
            placed = Follow();
        }
        if (Hearts != null)
        {
            Size = Hearts.Size;
        }
        if (!Core.GameOptions.SideHearts)
        {
            Alpha = 0.0f;
        }
        else if ((Wanted && placed) || arranging)
        {
            Alpha = Mathf.MoveToward(Alpha, 1.0f, dt / Mathf.Max(FadeInSeconds, 0.001f));
        }
        else
        {
            Alpha = Mathf.MoveToward(Alpha, 0.0f, dt / Mathf.Max(FadeOutSeconds, 0.001f));
        }
        Modulate = new Color(1, 1, 1, Alpha);
        Visible = Alpha > 0.01f && !Debug.DebugDraw3D.HideHud;
    }

    /// <summary>Keeps the column on the carrier's other side from the ring; false when he isn't on screen.</summary>
    private bool Follow()
    {
        Viewport? viewport = GetViewport();
        Camera3D? camera = viewport?.GetCamera3D();
        if (viewport == null || camera == null || Ring == null || Hearts == null || !Ring.Project(camera, out Vector2 at))
        {
            return false;
        }
        Vector2 screen = viewport.GetVisibleRect().Size;
        Vector2 size = Hearts.Size;
        // The ring sits at +ScreenOffset.X from the carrier; the hearts at the same distance on the other side.
        Vector2 centre = at + new Vector2(-Ring.ScreenOffset.X, Ring.ScreenOffset.Y);
        Vector2 corner = centre - size * 0.5f;
        corner.X = Mathf.Clamp(corner.X, EdgeMargin, Mathf.Max(screen.X - size.X - EdgeMargin, EdgeMargin));
        corner.Y = Mathf.Clamp(corner.Y, EdgeMargin, Mathf.Max(screen.Y - size.Y - EdgeMargin, EdgeMargin));
        Position = corner;
        return true;
    }
}
