using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The Jump Over cue (GameEvents.CurrentVault): an inked ring around "SPACE" that tightens as the
/// carrier closes in on a low boulder or log. A gold band marks the window; when the ring is inside
/// it, the ring turns gold: press Space now. After a try it says "Vault!" or "Tripped!" for a moment.
/// </summary>
public partial class VaultPrompt : Control
{
    /// <summary>Ring radius at the sweet spot, and how much it grows per metre farther, in px.</summary>
    [Export] public float SweetRadius { get; set; } = 34.0f;
    [Export] public float PixelsPerMetre { get; set; } = 34.0f;
    /// <summary>Where the cue sits, as a share of the screen (x, y).</summary>
    [Export] public Vector2 CueAt { get; set; } = new(0.5f, 0.62f);
    [Export] public float ResultTime { get; set; } = 1.2f;

    private string _result = "";
    private float _resultLeft;
    private bool _cleared;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        GameEvents.Vaulted += OnVaulted;
    }

    public override void _ExitTree()
    {
        GameEvents.Vaulted -= OnVaulted;
    }

    private void OnVaulted(bool cleared)
    {
        _cleared = cleared;
        _resultLeft = ResultTime;
        if (cleared)
        {
            _result = "Rolled!";
        }
        else
        {
            _result = "Hard landing!";
        }
    }

    public override void _Process(double delta)
    {
        if (_resultLeft > 0.0f)
        {
            _resultLeft -= (float)delta;
        }
        QueueRedraw();
    }

    private float RadiusFor(float distance, VaultCue cue)
    {
        return Mathf.Clamp(SweetRadius + (distance - cue.Sweet) * PixelsPerMetre, 6.0f, 150.0f);
    }

    public override void _Draw()
    {
        Vector2 screen = GetViewportRect().Size;
        Vector2 centre = new(screen.X * CueAt.X, screen.Y * CueAt.Y);
        VaultCue cue = GameEvents.CurrentVault;
        if (cue.Active)
        {
            float near = RadiusFor(cue.Near, cue);
            float far = RadiusFor(cue.Far, cue);
            // The window: a gold band between its near and far radius.
            DrawArc(centre, (near + far) * 0.5f, 0.0f, Mathf.Tau, 48, new Color(UiStyle.Highlight, 0.45f), far - near, true);
            float ring = RadiusFor(cue.Distance, cue);
            Color ink = UiStyle.Ink;
            if (cue.InWindow)
            {
                ink = UiStyle.Highlight;
            }
            DrawArc(centre, ring, 0.0f, Mathf.Tau, 48, ink, 3.5f, true);
            string text = "SPACE";
            Vector2 size = UiStyle.Heading.GetStringSize(text, HorizontalAlignment.Left, -1, 22);
            DrawString(UiStyle.Heading, centre + new Vector2(-size.X * 0.5f + 1.0f, 8.0f), text, HorizontalAlignment.Left, -1, 22, new Color(0, 0, 0, 0.6f));
            DrawString(UiStyle.Heading, centre + new Vector2(-size.X * 0.5f, 7.0f), text, HorizontalAlignment.Left, -1, 22, ink.Lightened(0.6f));
        }
        if (_resultLeft > 0.0f && _result.Length > 0)
        {
            Color colour = UiStyle.Stamina;
            if (!_cleared)
            {
                colour = UiStyle.Warning;
            }
            colour.A = Mathf.Clamp(_resultLeft / ResultTime * 1.5f, 0.0f, 1.0f);
            Vector2 size = UiStyle.Heading.GetStringSize(_result, HorizontalAlignment.Left, -1, 34);
            DrawString(UiStyle.Heading, centre + new Vector2(-size.X * 0.5f, -SweetRadius - 30.0f), _result, HorizontalAlignment.Left, -1, 34, colour);
        }
    }
}
