using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The balance dial over the carrier, shown once the load sets him wobbling (BalanceSnapshot): a
/// half-round gauge like a speedometer, steady in the middle, amber where the wobble grows and red
/// from the tripping point (the stumble) out. The needle shows which way the load is tipping him.
/// Beside it the two lean keys (left and right click by default): the one that pulls him back over
/// his feet is lit, and while he stumbles a line says both together brace. Hidden again once steady.
/// </summary>
public partial class BalanceGauge : Control
{
    /// <summary>Shown from this imbalance up (1 = the centre of mass at the edge of the feet).</summary>
    [Export] public float ShowAbove { get; set; } = 0.45f;
    /// <summary>Where the wobble turns amber and where it trips (red); match BalanceSettings' Wobble and Stumble thresholds.</summary>
    [Export] public float WobbleAt { get; set; } = 0.5f;
    [Export] public float TripAt { get; set; } = 1.0f;
    /// <summary>The imbalance at either end of the dial.</summary>
    [Export] public float Range { get; set; } = 1.5f;
    [Export] public float Radius { get; set; } = 58.0f;
    [Export] public float Width { get; set; } = 11.0f;
    /// <summary>The dial's centre this high over the carrier (m), and this many px above that on screen.</summary>
    [Export] public float AnchorHeight { get; set; } = 2.0f;
    [Export] public float ScreenLift { get; set; } = 30.0f;
    [Export] public float HoldSeconds { get; set; } = 1.2f;
    [Export] public float FadeInSeconds { get; set; } = 0.15f;
    [Export] public float FadeOutSeconds { get; set; } = 0.5f;
    [Export] public float NeedleEase { get; set; } = 14.0f;
    [Export] public float EdgeMargin { get; set; } = 12.0f;

    public static readonly Color Steady = new(0.42f, 0.55f, 0.25f);
    public static readonly Color Wobble = new(0.9f, 0.62f, 0.18f);

    private static readonly StringName ActionLeanLeft = "balance_lean_left";
    private static readonly StringName ActionLeanRight = "balance_lean_right";

    private BalanceSnapshot _snapshot;
    private bool _has;
    private float _hold;
    private float _needle;
    private float _time;

    /// <summary>How visible the dial is (0 hidden, 1 shown).</summary>
    public float Alpha { get; private set; }
    /// <summary>Where the needle points, as an imbalance (negative: tipping left).</summary>
    public float Needle => _needle;
    /// <summary>The lean key that pulls him back: -1 left, 1 right, 0 none.</summary>
    public int LitSide { get; private set; }
    /// <summary>The line under the dial (tests read it).</summary>
    public string Hint { get; private set; } = "";

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(Radius * 2.0f + 180.0f, Radius + 70.0f);
        GameEvents.BalanceUpdated += OnBalance;
    }

    public override void _ExitTree()
    {
        GameEvents.BalanceUpdated -= OnBalance;
    }

    private void OnBalance(BalanceSnapshot snapshot)
    {
        _snapshot = snapshot;
        _has = true;
    }

    /// <summary>Whether this snapshot asks for the dial: wobbling or worse, and not down already.</summary>
    public bool Wants(BalanceSnapshot snapshot)
    {
        if (snapshot.State == BalanceState.Fallen)
        {
            return false;
        }
        return snapshot.State == BalanceState.Stumbling || snapshot.ImbalanceMagnitude >= ShowAbove;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        bool wanted = _has && Wants(_snapshot) && !GameEvents.IsPacking && !GameEvents.IsMapOpen && !GameEvents.IsScreenOpen && !GameEvents.IsDowned;
        if (wanted)
        {
            _hold = HoldSeconds;
        }
        else
        {
            _hold = Mathf.Max(0.0f, _hold - dt);
        }
        if (_has && _snapshot.State == BalanceState.Fallen)
        {
            _hold = 0.0f;
        }
        float target = 0.0f;
        if (_hold > 0.0f)
        {
            target = 1.0f;
        }
        float rate = 1.0f / Mathf.Max(FadeOutSeconds, 0.01f);
        if (target > Alpha)
        {
            rate = 1.0f / Mathf.Max(FadeInSeconds, 0.01f);
        }
        Alpha = Mathf.MoveToward(Alpha, target, rate * dt);
        if (_has)
        {
            float roll = Mathf.Clamp(_snapshot.Imbalance.X, -Range, Range);
            _needle = Mathf.Lerp(_needle, roll, 1.0f - Mathf.Exp(-NeedleEase * dt));
        }
        UpdateHint();
        Visible = Alpha > 0.01f && !Debug.DebugDraw3D.HideHud;
        if (Visible)
        {
            Follow();
            QueueRedraw();
        }
    }

    private void UpdateHint()
    {
        LitSide = 0;
        Hint = "";
        if (!_has)
        {
            return;
        }
        if (GameEvents.SwordDrawn)
        {
            Hint = "Put the sword away to keep your balance";
            return;
        }
        float roll = _snapshot.Imbalance.X;
        float pitch = _snapshot.Imbalance.Y;
        // Tipping right: lean left to bring him back over his feet, and the other way round.
        if (Mathf.Abs(roll) >= WobbleAt * 0.6f)
        {
            if (roll > 0.0f)
            {
                LitSide = -1;
            }
            else
            {
                LitSide = 1;
            }
        }
        string both = KeyLabel(ActionLeanLeft) + " + " + KeyLabel(ActionLeanRight);
        if (_snapshot.State == BalanceState.Stumbling)
        {
            Hint = "Stumbling! Lean against it, or hold " + both + " to brace";
        }
        else if (Mathf.Abs(pitch) > Mathf.Abs(roll) && Mathf.Abs(pitch) >= WobbleAt)
        {
            Hint = "Hold " + both + " to brace";
        }
        else if (LitSide != 0)
        {
            Hint = "Lean against the wobble";
        }
    }

    /// <summary>A short name for an action's first key: "LMB", "RMB", or the key itself.</summary>
    public static string KeyLabel(StringName action)
    {
        string keys = InputGlyphs.Keys(action.ToString());
        int split = keys.IndexOf(" / ", System.StringComparison.Ordinal);
        if (split >= 0)
        {
            keys = keys.Substring(0, split);
        }
        if (keys == "Left mouse")
        {
            return "LMB";
        }
        if (keys == "Right mouse")
        {
            return "RMB";
        }
        if (keys.Length == 0)
        {
            return "?";
        }
        return keys;
    }

    /// <summary>Over the carrier's head on screen, inside the screen's edges; centred near the top when he's off screen.</summary>
    private void Follow()
    {
        Viewport? viewport = GetViewport();
        if (viewport == null)
        {
            return;
        }
        Vector2 screen = viewport.GetVisibleRect().Size;
        Vector2 centre = new(screen.X * 0.5f, screen.Y * 0.3f);
        Camera3D? camera = viewport.GetCamera3D();
        if (camera != null && Project(camera, out Vector2 at))
        {
            centre = at - new Vector2(0.0f, ScreenLift);
        }
        // The dial's centre sits at (Size.X / 2, Radius + 8) inside the control.
        Vector2 corner = centre - new Vector2(Size.X * 0.5f, Radius + 8.0f);
        corner.X = Mathf.Clamp(corner.X, EdgeMargin, Mathf.Max(screen.X - Size.X - EdgeMargin, EdgeMargin));
        corner.Y = Mathf.Clamp(corner.Y, EdgeMargin, Mathf.Max(screen.Y - Size.Y - EdgeMargin, EdgeMargin));
        Position = corner;
    }

    private bool Project(Camera3D camera, out Vector2 at)
    {
        Vector3 world = GameEvents.CarrierPosition + Vector3.Up * AnchorHeight;
        at = Vector2.Zero;
        Vector3 local = camera.GetCameraTransform().AffineInverse() * world;
        Vector4 clip = camera.GetCameraProjection() * new Vector4(local.X, local.Y, local.Z, 1.0f);
        if (local.Z >= 0.0f || clip.W <= 0.01f || !float.IsFinite(clip.X) || !float.IsFinite(clip.Y))
        {
            return false;
        }
        Vector2 screen = camera.GetViewport().GetVisibleRect().Size;
        at = new Vector2((clip.X / clip.W * 0.5f + 0.5f) * screen.X, (-clip.Y / clip.W * 0.5f + 0.5f) * screen.Y);
        return true;
    }

    /// <summary>The dial's angle for an imbalance: straight up for steady, down to either side at the ends.</summary>
    private float Angle(float imbalance)
    {
        return -Mathf.Pi * 0.5f + Mathf.Clamp(imbalance / Range, -1.0f, 1.0f) * Mathf.Pi * 0.5f;
    }

    public override void _Draw()
    {
        float a = Alpha;
        Vector2 centre = new(Size.X * 0.5f, Radius + 8.0f);
        Color ink = new(UiStyle.Ink, 0.85f * a);
        // The paper face behind the dial.
        DrawCircle(centre, Radius + Width, new Color(UiStyle.Paper, 0.55f * a));
        DrawRect(new Rect2(centre.X - Radius - Width, centre.Y, (Radius + Width) * 2.0f, 4.0f), new Color(UiStyle.Paper, 0.55f * a));
        // The zones: steady, wobbling, tripping, mirrored on both sides.
        Zone(centre, -WobbleAt, WobbleAt, Steady, a);
        Zone(centre, WobbleAt, TripAt, Wobble, a);
        Zone(centre, -TripAt, -WobbleAt, Wobble, a);
        Zone(centre, TripAt, Range, UiStyle.Warning, a);
        Zone(centre, -Range, -TripAt, UiStyle.Warning, a);
        DrawArc(centre, Radius + Width * 0.5f + 1.0f, Angle(-Range), Angle(Range), 48, ink, 2.0f, true);
        // The tripping point marked on both sides.
        foreach (float side in new[] { -1.0f, 1.0f })
        {
            Vector2 dir = Vector2.FromAngle(Angle(side * TripAt));
            DrawLine(centre + dir * (Radius - Width * 0.7f), centre + dir * (Radius + Width * 0.9f), ink, 3.0f, true);
        }
        // The needle, coloured by where it points; it shivers a little in the red.
        float needle = _needle;
        float size = Mathf.Abs(needle);
        Color tip = Steady;
        if (size >= TripAt)
        {
            tip = UiStyle.Warning;
            needle += Mathf.Sin(_time * 40.0f) * 0.02f;
        }
        else if (size >= WobbleAt)
        {
            tip = Wobble;
        }
        Vector2 point = centre + Vector2.FromAngle(Angle(needle)) * (Radius + 2.0f);
        DrawLine(centre, point, new Color(0, 0, 0, 0.45f * a), 5.0f, true);
        DrawLine(centre, point, new Color(tip.Darkened(0.2f), a), 3.0f, true);
        DrawCircle(centre, 6.0f, ink);
        // The lean keys at the dial's ends: the one to press is lit and pulses.
        float pulse = 0.75f + 0.25f * Mathf.Sin(_time * 9.0f);
        DrawKeyCap(centre + new Vector2(-Radius - Width - 40.0f, -6.0f), KeyLabel(ActionLeanLeft), LitSide < 0, a, pulse, -1.0f);
        DrawKeyCap(centre + new Vector2(Radius + Width + 40.0f, -6.0f), KeyLabel(ActionLeanRight), LitSide > 0, a, pulse, 1.0f);
        if (Hint.Length > 0)
        {
            const int font = 18;
            Vector2 text = UiStyle.Hand.GetStringSize(Hint, HorizontalAlignment.Left, -1, font);
            Vector2 at = new(centre.X - text.X * 0.5f, centre.Y + 30.0f);
            Color colour = new(UiStyle.Paper, a);
            if (_snapshot.State == BalanceState.Stumbling)
            {
                colour = new Color(UiStyle.Warning.Lightened(0.35f), a);
            }
            DrawString(UiStyle.Hand, at + new Vector2(1.5f, 1.5f), Hint, HorizontalAlignment.Left, -1, font, new Color(0, 0, 0, 0.7f * a));
            DrawString(UiStyle.Hand, at, Hint, HorizontalAlignment.Left, -1, font, colour);
        }
    }

    private void Zone(Vector2 centre, float from, float to, Color colour, float a)
    {
        DrawArc(centre, Radius, Angle(from), Angle(to), 24, new Color(colour, 0.9f * a), Width, true);
    }

    /// <summary>A key cap with the key's name and an arrow (-1 left, 1 right) for the way it leans; gold and pulsing when it's the one to press.</summary>
    private void DrawKeyCap(Vector2 centre, string key, bool lit, float a, float pulse, float arrow)
    {
        const int size = 18;
        Vector2 text = UiStyle.Heading.GetStringSize(key, HorizontalAlignment.Left, -1, size);
        Vector2 cap = new(Mathf.Max(text.X + 18.0f, 44.0f), 30.0f);
        if (lit)
        {
            cap *= 1.0f + 0.08f * pulse;
        }
        Rect2 face = new(centre - cap * 0.5f, cap);
        float shown = 0.55f;
        Color top = new(UiStyle.Paper, 0.9f * a);
        if (lit)
        {
            shown = 1.0f;
            top = new Color(UiStyle.Highlight, a * pulse);
        }
        Color edge = new(UiStyle.Ink, 0.9f * a * shown);
        DrawRect(new Rect2(face.Position + new Vector2(0.0f, 4.0f), face.Size), new Color(UiStyle.Ink, 0.7f * a * shown));
        DrawRect(face, new Color(top, top.A * shown));
        DrawRect(face, edge, false, 2.0f);
        DrawString(UiStyle.Heading, centre + new Vector2(-text.X * 0.5f, size * 0.36f), key, HorizontalAlignment.Left, -1, size, edge);
        // An arrow under the cap for the way it leans.
        Vector2 tip = centre + new Vector2(arrow * 9.0f, cap.Y * 0.5f + 14.0f);
        Vector2 back = centre + new Vector2(-arrow * 7.0f, cap.Y * 0.5f + 14.0f);
        Vector2[] triangle = { tip, back + new Vector2(0.0f, -7.0f), back + new Vector2(0.0f, 7.0f) };
        DrawColoredPolygon(triangle, new Color(UiStyle.Paper, a * shown));
    }
}
