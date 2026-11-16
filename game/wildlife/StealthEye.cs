using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// The stealth eye: a small inked eye above the hotbar that tells how the animals near the carrier
/// take it: nearly shut (none of them knows), half open in amber (one is suspicious: head up,
/// frozen), wide open in red (one is alerted). Shown while the carrier crouches (the brake key) or
/// while an animal near is suspicious or alerted; hidden behind open pages and while packing. Reads
/// every animal in the "wildlife" group, so a guest sees it too, from the puppets' streamed clips
/// (WildlifeAnimal.Aware). Added by WildlifeSpawner.
/// </summary>
public partial class StealthEye : CanvasLayer
{
    public const string AnimalGroup = "wildlife";

    /// <summary>Animals this close to the carrier count, in m.</summary>
    [Export] public float Range { get; set; } = 35.0f;
    /// <summary>Its centre sits this far above the bottom of the screen, in px.</summary>
    [Export] public float Bottom { get; set; } = 150.0f;
    /// <summary>Seconds between looks over the animals.</summary>
    [Export] public float ScanInterval { get; set; } = 0.15f;

    /// <summary>The strongest awareness among the animals near.</summary>
    public WildlifeAnimal.Awareness Level { get; private set; }
    /// <summary>Whether the eye is wanted on screen now (it fades in and out).</summary>
    public bool Wanted { get; private set; }

    private EyeMark? _eye;
    private float _scan;
    private bool _crouching;

    public override void _Ready()
    {
        Layer = 4;
        _eye = new EyeMark { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        AddChild(_eye);
        GameEvents.LocomotionUpdated += OnLocomotion;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotion;
    }

    public override void _Process(double delta)
    {
        if (_eye == null)
        {
            return;
        }
        float dt = (float)delta;
        _scan -= dt;
        if (_scan <= 0.0f)
        {
            _scan = ScanInterval;
            Level = Strongest(GetTree().GetNodesInGroup(AnimalGroup), GameEvents.CarrierPosition, Range);
            if (_eye.Level != Level)
            {
                _eye.Level = Level;
                _eye.QueueRedraw();
            }
        }
        bool hidden = GameEvents.IsScreenOpen || GameEvents.IsPacking || GameEvents.MenuOpen;
        Wanted = !hidden && (_crouching || Level != WildlifeAnimal.Awareness.Unaware);
        Color tint = _eye.Modulate;
        tint.A = Mathf.MoveToward(tint.A, Wanted ? 1.0f : 0.0f, dt * 4.0f);
        _eye.Modulate = tint;
        Vector2 screen = _eye.GetViewportRect().Size;
        _eye.Size = new Vector2(EyeMark.Width, EyeMark.Width);
        _eye.Position = new Vector2((screen.X - EyeMark.Width) * 0.5f, screen.Y - Bottom - EyeMark.Width * 0.5f);
    }

    /// <summary>The strongest awareness among living animals within range of a point.</summary>
    public static WildlifeAnimal.Awareness Strongest(IEnumerable<Node> animals, Vector3 at, float range)
    {
        WildlifeAnimal.Awareness strongest = WildlifeAnimal.Awareness.Unaware;
        foreach (Node node in animals)
        {
            if (node is not WildlifeAnimal animal || animal.IsDead || !animal.IsInsideTree())
            {
                continue;
            }
            Vector3 offset = animal.GlobalPosition - at;
            if (new Vector2(offset.X, offset.Z).Length() > range)
            {
                continue;
            }
            WildlifeAnimal.Awareness level = animal.Aware;
            if (level > strongest)
            {
                strongest = level;
            }
        }
        return strongest;
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        _crouching = snapshot.State == LocomotionState.Braking
            || (snapshot.State == LocomotionState.Idle && Input.IsActionPressed(WildlifeSpawner.CrouchAction));
    }

    /// <summary>The eye itself, drawn in ink on a parchment disc like the rest of the journal HUD.</summary>
    private partial class EyeMark : Control
    {
        public const float Width = 46.0f;
        private static readonly Color Paper = new(0.93f, 0.87f, 0.74f, 0.88f);
        private static readonly Color Ink = new(0.24f, 0.17f, 0.11f, 1.0f);
        private static readonly Color Amber = new(0.80f, 0.55f, 0.12f, 1.0f);
        private static readonly Color Red = new(0.72f, 0.12f, 0.08f, 1.0f);

        public WildlifeAnimal.Awareness Level { get; set; }

        public override void _Draw()
        {
            Vector2 centre = new(Width * 0.5f, Width * 0.5f);
            DrawCircle(centre, Width * 0.5f, Paper);
            DrawArc(centre, Width * 0.5f - 1.0f, 0.0f, Mathf.Tau, 32, Ink, 1.5f, true);
            float open = Level switch
            {
                WildlifeAnimal.Awareness.Alerted => 1.0f,
                WildlifeAnimal.Awareness.Suspicious => 0.55f,
                _ => 0.12f,
            };
            Color iris = Level switch
            {
                WildlifeAnimal.Awareness.Alerted => Red,
                WildlifeAnimal.Awareness.Suspicious => Amber,
                _ => Ink,
            };
            float half = Width * 0.36f;
            float lid = Width * 0.26f * open;
            const int steps = 14;
            Vector2[] outline = new Vector2[steps * 2 + 1];
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float x = Mathf.Lerp(-half, half, t);
                float bulge = Mathf.Sin(t * Mathf.Pi);
                outline[i] = centre + new Vector2(x, -lid * bulge);
                if (i < steps)
                {
                    outline[steps * 2 - i] = centre + new Vector2(x, lid * bulge);
                }
            }
            outline[steps * 2] = outline[0];
            if (open > 0.3f)
            {
                DrawCircle(centre, Mathf.Min(lid, Width * 0.13f), iris);
                DrawCircle(centre, Mathf.Min(lid, Width * 0.13f) * 0.45f, Ink);
            }
            DrawPolyline(outline, Ink, 2.0f, true);
            if (Level == WildlifeAnimal.Awareness.Alerted)
            {
                // A few strokes over the lid: it has seen you.
                for (int i = -1; i <= 1; i++)
                {
                    Vector2 from = centre + new Vector2(i * Width * 0.13f, -lid - 2.0f);
                    DrawLine(from, from + new Vector2(i * 2.5f, -5.0f), Red, 2.0f, true);
                }
            }
        }
    }
}
