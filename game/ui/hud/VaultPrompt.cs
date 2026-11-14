using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The roll cue on a hard fall (GameEvents.CurrentVault, RollSave): a key cap with the jump key on
/// it, and an inked ring that tightens onto the cap as the ground comes. The gold band is the
/// window, the bright thin ring inside it the perfect moment; inside the band the ring turns gold:
/// press now. After the chance it says how it went: "Perfect roll!", "Rolled!", or a red
/// "Too early!" over the ring frozen where it was pressed, so a miss shows why.
/// </summary>
public partial class VaultPrompt : Control
{
    /// <summary>Ring radius at the sweet spot, in px.</summary>
    [Export] public float SweetRadius { get; set; } = 40.0f;
    /// <summary>How much the ring grows per metre farther (a vault) or per second earlier (a roll), in px.</summary>
    [Export] public float PixelsPerMetre { get; set; } = 34.0f;
    [Export] public float PixelsPerSecond { get; set; } = 230.0f;
    /// <summary>Where the cue sits, as a share of the screen (x, y).</summary>
    [Export] public Vector2 CueAt { get; set; } = new(0.5f, 0.62f);
    [Export] public float ResultTime { get; set; } = 1.2f;

    private static readonly StringName ActionJump = "player_jump";

    private string _result = "";
    private float _resultLeft;
    private bool _cleared;
    private VaultCue _lastCue = VaultCue.None;
    /// <summary>The cue as it was when a press missed, drawn red while the result shows.</summary>
    private VaultCue _missedCue = VaultCue.None;

    /// <summary>What the cue says after a press (tests read it).</summary>
    public string Result => _result;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        GameEvents.Vaulted += OnVaulted;
        RollSave.Resolved += OnResolved;
    }

    public override void _ExitTree()
    {
        GameEvents.Vaulted -= OnVaulted;
        RollSave.Resolved -= OnResolved;
    }

    private void OnVaulted(bool cleared)
    {
        if (cleared && RollSave.LastGrade == RollGrade.Perfect)
        {
            Show("Perfect roll!", true);
        }
        else if (cleared)
        {
            Show("Rolled!", true);
        }
        else
        {
            Show("Hard landing!", false);
        }
    }

    private void OnResolved(RollGrade grade)
    {
        if (grade != RollGrade.Miss)
        {
            return;
        }
        _missedCue = _lastCue;
        if (_lastCue.Active && _lastCue.Distance < _lastCue.Near)
        {
            Show("Too late!", false);
        }
        else if (_lastCue.Active && _lastCue.Distance <= _lastCue.Far)
        {
            Show("Too tired to roll!", false);
        }
        else
        {
            Show("Too early!", false);
        }
    }

    private void Show(string text, bool cleared)
    {
        _result = text;
        _cleared = cleared;
        _resultLeft = ResultTime;
        if (cleared)
        {
            _missedCue = VaultCue.None;
        }
    }

    public override void _Process(double delta)
    {
        if (_resultLeft > 0.0f)
        {
            _resultLeft -= (float)delta;
        }
        if (GameEvents.CurrentVault.Active)
        {
            _lastCue = GameEvents.CurrentVault;
        }
        QueueRedraw();
    }

    private float RadiusFor(float distance, VaultCue cue)
    {
        float perUnit = PixelsPerMetre;
        if (cue.Timed)
        {
            perUnit = PixelsPerSecond;
        }
        return Mathf.Clamp(SweetRadius + (distance - cue.Sweet) * perUnit, 26.0f, 170.0f);
    }

    /// <summary>The key the jump action is on, as the player sees it on the keyboard ("Space").</summary>
    public static string KeyName()
    {
        foreach (InputEvent input in InputMap.ActionGetEvents(ActionJump))
        {
            if (input is InputEventKey key)
            {
                Key code = key.PhysicalKeycode;
                if (code == Key.None)
                {
                    code = key.Keycode;
                }
                return OS.GetKeycodeString(code);
            }
        }
        return "Space";
    }

    public override void _Draw()
    {
        Vector2 screen = GetViewportRect().Size;
        Vector2 centre = new(screen.X * CueAt.X, screen.Y * CueAt.Y);
        VaultCue cue = GameEvents.CurrentVault;
        if (cue.Active)
        {
            DrawCue(centre, cue, false, 1.0f);
        }
        else if (_resultLeft > 0.0f && _missedCue.Active)
        {
            DrawCue(centre, _missedCue, true, Mathf.Clamp(_resultLeft / ResultTime * 1.5f, 0.0f, 1.0f));
        }
        if (_resultLeft > 0.0f && _result.Length > 0)
        {
            Color colour = UiStyle.Stamina.Lightened(0.25f);
            if (!_cleared)
            {
                colour = UiStyle.Warning.Lightened(0.15f);
            }
            colour.A = Mathf.Clamp(_resultLeft / ResultTime * 1.5f, 0.0f, 1.0f);
            Vector2 size = UiStyle.Heading.GetStringSize(_result, HorizontalAlignment.Left, -1, 34);
            Vector2 at = centre + new Vector2(-size.X * 0.5f, -SweetRadius - 34.0f);
            DrawString(UiStyle.Heading, at + new Vector2(1.5f, 1.5f), _result, HorizontalAlignment.Left, -1, 34, new Color(0, 0, 0, 0.6f * colour.A));
            DrawString(UiStyle.Heading, at, _result, HorizontalAlignment.Left, -1, 34, colour);
        }
    }

    private void DrawCue(Vector2 centre, VaultCue cue, bool missed, float alpha)
    {
        float near = RadiusFor(cue.Near, cue);
        float far = RadiusFor(cue.Far, cue);
        // The window: a gold band; the perfect moment a brighter, thinner one inside it.
        DrawArc(centre, (near + far) * 0.5f, 0.0f, Mathf.Tau, 64, new Color(UiStyle.Highlight, 0.35f * alpha), far - near, true);
        if (cue.Perfect > 0.0f)
        {
            float inner = RadiusFor(cue.Sweet - cue.Perfect, cue);
            float outer = RadiusFor(cue.Sweet + cue.Perfect, cue);
            DrawArc(centre, (inner + outer) * 0.5f, 0.0f, Mathf.Tau, 64, new Color(1.0f, 0.95f, 0.75f, 0.55f * alpha), Mathf.Max(outer - inner, 2.0f), true);
        }
        float ring = RadiusFor(cue.Distance, cue);
        Color ink = new(0.08f, 0.06f, 0.04f, 0.9f * alpha);
        float width = 4.0f;
        if (missed)
        {
            ink = new Color(UiStyle.Warning, alpha);
        }
        else if (cue.InWindow)
        {
            ink = new Color(UiStyle.Highlight, alpha);
            width = 5.0f;
        }
        // A pale halo under the ring, so it reads on snow and on dark rock alike.
        DrawArc(centre, ring, 0.0f, Mathf.Tau, 64, new Color(1, 1, 1, 0.35f * alpha), width + 3.0f, true);
        DrawArc(centre, ring, 0.0f, Mathf.Tau, 64, ink, width, true);
        DrawKeyCap(centre, KeyName(), cue.InWindow && !missed, alpha);
    }

    /// <summary>A keyboard key cap with the key's name on it; lit gold while the press would count.</summary>
    private void DrawKeyCap(Vector2 centre, string key, bool lit, float alpha)
    {
        const int size = 20;
        Vector2 text = UiStyle.Heading.GetStringSize(key, HorizontalAlignment.Left, -1, size);
        Vector2 cap = new(Mathf.Max(text.X + 22.0f, 40.0f), 32.0f);
        Rect2 face = new(centre - cap * 0.5f, cap);
        Color top = new(UiStyle.Paper, 0.95f * alpha);
        Color edge = new(UiStyle.Ink, 0.9f * alpha);
        if (lit)
        {
            top = new Color(UiStyle.Highlight, alpha);
        }
        // The key's side, a little below its face, then the face and its outline.
        DrawRect(new Rect2(face.Position + new Vector2(0.0f, 4.0f), face.Size), new Color(UiStyle.Ink, 0.75f * alpha));
        DrawRect(face, top);
        DrawRect(face, edge, false, 2.0f);
        Vector2 at = centre + new Vector2(-text.X * 0.5f, size * 0.36f);
        DrawString(UiStyle.Heading, at, key, HorizontalAlignment.Left, -1, size, edge);
    }
}
