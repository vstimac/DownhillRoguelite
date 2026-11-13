using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// A compact ring that floats beside the carrier on screen, so stamina is read where the eye
/// already is. The outer arc is stamina: it appears while stamina changes or is low and fades out
/// once it is full again; a pale trail shows what was just spent, and when exhausted it pulses red.
/// The inner arc is the burning torch's time left, shown for as long as a torch is lit (with a small
/// flame in the middle that flickers faster as it dies). The carrier's position is projected to the
/// screen each frame and the ring kept at a fixed pixel offset from it. Presentation only: reads
/// GameEvents (vitals, the carrier's position, the torch).
/// </summary>
public partial class ContextRing : Control
{
    /// <summary>Where on the carrier the ring is anchored: this high above his feet, in metres.</summary>
    [Export] public float AnchorHeight { get; set; } = 1.15f;
    /// <summary>The ring's centre sits this far from the projected anchor, in pixels.</summary>
    [Export] public Vector2 ScreenOffset { get; set; } = new(78.0f, -18.0f);
    [Export] public float RingSize { get; set; } = 56.0f;
    /// <summary>The ring never gets closer than this to the screen's edge, in pixels.</summary>
    [Export] public float EdgeMargin { get; set; } = 8.0f;
    /// <summary>The carrier isn't projected when closer to the camera's plane than this (clip-space w).</summary>
    [Export] public float MinDepth { get; set; } = 0.01f;
    /// <summary>Stamina counts as full above this share.</summary>
    [Export] public float FullAbove { get; set; } = 0.995f;
    /// <summary>Stamina below this share keeps the ring up even when it isn't changing.</summary>
    [Export] public float LowBelow { get; set; } = 0.35f;
    /// <summary>A change smaller than this share per second doesn't count as changing.</summary>
    [Export] public float ChangeRate { get; set; } = 0.01f;
    /// <summary>The ring stays this long after the last change, in seconds.</summary>
    [Export] public float HoldSeconds { get; set; } = 0.7f;
    [Export] public float FadeInSeconds { get; set; } = 0.12f;
    [Export] public float FadeOutSeconds { get; set; } = 0.45f;
    [Export] public float EaseRate { get; set; } = 10.0f;
    /// <summary>A fresh torch burns this long: the inner arc is full at this many seconds.</summary>
    [Export] public float TorchFullSeconds { get; set; } = 600.0f;
    /// <summary>Below this share of a torch the inner arc pulses.</summary>
    [Export] public float TorchLowBelow { get; set; } = 0.15f;
    [Export] public Color TorchColour { get; set; } = new(0.95f, 0.62f, 0.2f);
    [Export] public Color Track { get; set; } = new(0.1f, 0.08f, 0.06f, 0.8f);
    [Export] public Color Plate { get; set; } = new(0.86f, 0.79f, 0.64f, 0.28f);

    /// <summary>Stamina as a share of its maximum, as last heard.</summary>
    public float Stamina { get; private set; } = 1.0f;
    /// <summary>The share the outer arc shows right now (easing toward Stamina).</summary>
    public float Shown { get; private set; } = 1.0f;
    public bool Exhausted { get; private set; }
    /// <summary>True while the stamina arc wants to be seen (changing, low or exhausted).</summary>
    public bool StaminaWanted { get; private set; }
    /// <summary>How visible the stamina arc is, 0 to 1.</summary>
    public float StaminaAlpha { get; private set; }
    /// <summary>How visible the torch arc is, 0 to 1.</summary>
    public float TorchAlpha { get; private set; }
    /// <summary>The torch's time left as a share of a fresh one.</summary>
    public float Torch { get; private set; }
    /// <summary>The carrier's anchor on screen, in pixels (the ring's centre is this plus ScreenOffset).</summary>
    public Vector2 CarrierPoint { get; private set; }
    /// <summary>False while the carrier is behind the camera: nothing is drawn then.</summary>
    public bool OnScreen { get; private set; } = true;
    /// <summary>The ring's centre on screen.</summary>
    public Vector2 Centre => Position + Size * 0.5f;

    private float _time;
    private float _sinceChange = 100.0f;
    private bool _heard;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(RingSize, RingSize);
        GameEvents.VitalsUpdated += OnVitals;
    }

    public override void _ExitTree()
    {
        GameEvents.VitalsUpdated -= OnVitals;
    }

    private void OnVitals(VitalsSnapshot vitals)
    {
        float share = 1.0f;
        if (vitals.MaxStamina > 0.0f)
        {
            share = Mathf.Clamp(vitals.Stamina / vitals.MaxStamina, 0.0f, 1.0f);
        }
        Exhausted = vitals.Exhausted && !vitals.Dead;
        if (!_heard)
        {
            _heard = true;
            Stamina = share;
            Shown = share;
            return;
        }
        // Vitals come once per physics tick: compare against a per-second rate.
        float tick = 1.0f / Mathf.Max(Engine.PhysicsTicksPerSecond, 1);
        if (Mathf.Abs(share - Stamina) > ChangeRate * tick)
        {
            _sinceChange = 0.0f;
        }
        Stamina = share;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        _sinceChange += dt;
        Shown = Mathf.Lerp(Shown, Stamina, 1.0f - Mathf.Exp(-EaseRate * dt));

        bool full = Stamina >= FullAbove;
        StaminaWanted = Exhausted || Stamina < LowBelow || (!full && _sinceChange < HoldSeconds) || (!full && Stamina < Shown - 0.01f);
        StaminaAlpha = Fade(StaminaAlpha, StaminaWanted, dt);

        Torch = Mathf.Clamp(GameEvents.CarrierTorchLeft / Mathf.Max(TorchFullSeconds, 0.001f), 0.0f, 1.0f);
        TorchAlpha = Fade(TorchAlpha, GameEvents.CarrierTorchLit && Torch > 0.0f, dt);

        Follow();
        Visible = OnScreen && (StaminaAlpha > 0.01f || TorchAlpha > 0.01f);
        if (Visible)
        {
            QueueRedraw();
        }
    }

    private float Fade(float alpha, bool wanted, float dt)
    {
        if (wanted)
        {
            return Mathf.MoveToward(alpha, 1.0f, dt / Mathf.Max(FadeInSeconds, 0.001f));
        }
        return Mathf.MoveToward(alpha, 0.0f, dt / Mathf.Max(FadeOutSeconds, 0.001f));
    }

    /// <summary>Where the carrier's anchor falls on a screen through this camera; false when he is behind it.</summary>
    public bool Project(Camera3D camera, out Vector2 at)
    {
        Vector3 world = GameEvents.CarrierPosition + Vector3.Up * AnchorHeight;
        at = Vector2.Zero;
        // Projected by hand (as Camera3D.UnprojectPosition does) so a point behind the camera, on its plane or seen
        // through a camera that isn't set up yet is simply "not on screen" instead of an engine error.
        Vector3 local = camera.GetCameraTransform().AffineInverse() * world;
        Vector4 clip = camera.GetCameraProjection() * new Vector4(local.X, local.Y, local.Z, 1.0f);
        if (local.Z >= 0.0f || clip.W <= MinDepth || !float.IsFinite(clip.X) || !float.IsFinite(clip.Y))
        {
            return false;
        }
        Vector2 screen = camera.GetViewport().GetVisibleRect().Size;
        at = new Vector2((clip.X / clip.W * 0.5f + 0.5f) * screen.X, (-clip.Y / clip.W * 0.5f + 0.5f) * screen.Y);
        return true;
    }

    /// <summary>Keeps the ring at its pixel offset from the carrier on screen, inside the screen's edges.</summary>
    private void Follow()
    {
        Viewport? viewport = GetViewport();
        if (viewport == null)
        {
            return;
        }
        Vector2 screen = viewport.GetVisibleRect().Size;
        Camera3D? camera = viewport.GetCamera3D();
        if (camera == null)
        {
            // No camera (a menu, a test without one): rest beside the middle of the screen.
            OnScreen = true;
            CarrierPoint = screen * 0.5f;
        }
        else
        {
            OnScreen = Project(camera, out Vector2 at);
            if (OnScreen)
            {
                CarrierPoint = at;
            }
        }
        Vector2 centre = CarrierPoint + ScreenOffset;
        float half = RingSize * 0.5f + EdgeMargin;
        if (screen.X > half * 2.0f && screen.Y > half * 2.0f)
        {
            centre = new Vector2(Mathf.Clamp(centre.X, half, screen.X - half), Mathf.Clamp(centre.Y, half, screen.Y - half));
        }
        Position = centre - Size * 0.5f;
    }

    public override void _Draw()
    {
        Vector2 c = Size * 0.5f;
        float outer = RingSize * 0.5f - 5.0f;
        float inner = outer - 9.0f;
        float plate = Mathf.Max(StaminaAlpha, TorchAlpha);
        DrawCircle(c, outer + 3.5f, new Color(Plate, Plate.A * plate));

        if (StaminaAlpha > 0.01f)
        {
            Color colour = UiStyle.Stamina.Lightened(0.15f);
            if (Exhausted || Stamina < LowBelow)
            {
                float beat = 0.5f + 0.5f * Mathf.Sin(_time * 8.0f);
                float toRed = 0.35f + 0.65f * beat;
                if (!Exhausted)
                {
                    toRed *= 0.6f;
                }
                colour = colour.Lerp(UiStyle.Warning, toRed);
            }
            InkDraw.Arc(this, c, outer, 0.0f, Mathf.Tau, 7.0f, new Color(Track, Track.A * StaminaAlpha));
            float value = Mathf.Min(Shown, Stamina);
            if (Shown > Stamina + 0.004f)
            {
                // What was just spent trails behind, pale.
                InkDraw.Arc(this, c, outer, Mathf.Tau * Stamina, Mathf.Tau * Shown, 5.0f, new Color(UiStyle.Highlight, 0.85f * StaminaAlpha));
            }
            if (value > 0.004f)
            {
                InkDraw.Arc(this, c, outer, 0.0f, Mathf.Tau * value, 5.0f, new Color(colour, StaminaAlpha));
            }
        }

        if (TorchAlpha > 0.01f)
        {
            Color flame = TorchColour;
            float flicker = 3.0f;
            if (Torch < TorchLowBelow)
            {
                flame = flame.Lerp(UiStyle.Warning, 0.5f + 0.5f * Mathf.Sin(_time * 8.0f));
                flicker = 9.0f;
            }
            InkDraw.Arc(this, c, inner, 0.0f, Mathf.Tau, 5.5f, new Color(Track, Track.A * TorchAlpha));
            if (Torch > 0.004f)
            {
                InkDraw.Arc(this, c, inner, 0.0f, Mathf.Tau * Torch, 3.5f, new Color(flame, TorchAlpha));
            }
            // A small flame in the middle: a teardrop that leans as it flickers.
            float lean = Mathf.Sin(_time * flicker) * 1.2f;
            float h = inner * 0.62f;
            Vector2[] drop =
            {
                c + new Vector2(lean, -h), c + new Vector2(h * 0.55f, -h * 0.05f), c + new Vector2(h * 0.42f, h * 0.6f), c + new Vector2(0.0f, h * 0.85f),
                c + new Vector2(-h * 0.42f, h * 0.6f), c + new Vector2(-h * 0.55f, -h * 0.05f),
            };
            DrawColoredPolygon(drop, new Color(flame, TorchAlpha));
            DrawCircle(c + new Vector2(0.0f, h * 0.35f), h * 0.28f, new Color(UiStyle.Highlight, TorchAlpha));
        }
    }
}
