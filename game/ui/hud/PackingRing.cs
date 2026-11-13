using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The wait while the pack comes off the back or goes back on: an inked ring that fills, with a
/// line saying which. It is why repacking can't be spammed: the time is spent in the live world.
/// </summary>
public partial class PackingRing : Control
{
    [Export] public float Radius { get; set; } = 26.0f;
    [Export] public string SettingDownText { get; set; } = "Setting the pack down…";
    [Export] public string PuttingOnText { get; set; } = "Shouldering the pack…";

    /// <summary>How far along it is, 0 to 1; negative while nothing is happening.</summary>
    public float Progress { get; private set; } = -1.0f;

    private bool _settingDown;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always;
        Size = new Vector2(320, 110);
        Visible = false;
        GameEvents.PackingProgress += OnProgress;
    }

    public override void _ExitTree()
    {
        GameEvents.PackingProgress -= OnProgress;
    }

    private void OnProgress(float progress, bool settingDown)
    {
        Progress = progress;
        _settingDown = settingDown;
        Visible = progress >= 0.0f;
        if (Visible)
        {
            Vector2 screen = GetViewportRect().Size;
            Position = new Vector2((screen.X - Size.X) * 0.5f, screen.Y * 0.62f);
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        Vector2 centre = new(Size.X * 0.5f, Radius + 6.0f);
        DrawCircle(centre, Radius + 4.0f, new Color(UiStyle.Paper, 0.85f));
        DrawArc(centre, Radius, 0.0f, Mathf.Tau, 48, UiStyle.InkSoft, 2.0f, true);
        float sweep = Mathf.Clamp(Progress, 0.0f, 1.0f) * Mathf.Tau;
        if (sweep > 0.01f)
        {
            DrawArc(centre, Radius, -Mathf.Pi * 0.5f, -Mathf.Pi * 0.5f + sweep, 48, UiStyle.Ink, 6.0f, true);
        }
        string text = PuttingOnText;
        if (_settingDown)
        {
            text = SettingDownText;
        }
        Vector2 size = UiStyle.Hand.GetStringSize(text, HorizontalAlignment.Left, -1, UiStyle.TextSize);
        Vector2 at = new((Size.X - size.X) * 0.5f, centre.Y + Radius + 30.0f);
        DrawRect(new Rect2(at.X - 10.0f, at.Y - size.Y + 2.0f, size.X + 20.0f, size.Y + 8.0f), new Color(UiStyle.Paper, 0.85f));
        DrawString(UiStyle.Hand, at, text, HorizontalAlignment.Left, -1, UiStyle.TextSize, UiStyle.Ink);
    }
}
