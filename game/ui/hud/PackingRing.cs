using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The wait while the pack comes off the back or goes back on: a ring round the key being held that
/// fills, a line saying what is happening and a smaller one saying how to stop it. The time is spent
/// in the live world, and walking off always ends it at once.
/// </summary>
public partial class PackingRing : Control
{
    [Export] public float Radius { get; set; } = 30.0f;
    /// <summary>Where the ring sits, as a share of the screen's height (under the carrier, not over him).</summary>
    [Export] public float ScreenHeight { get; set; } = 0.72f;
    [Export] public string SettingDownText { get; set; } = "Setting the pack down";
    [Export] public string PuttingOnText { get; set; } = "Shouldering the pack";

    /// <summary>How far along it is, 0 to 1; negative while nothing is happening.</summary>
    public float Progress { get; private set; } = -1.0f;
    /// <summary>The key written in the ring ("Tab", "I", "Y").</summary>
    public string Key { get; private set; } = "Tab";
    /// <summary>The smaller line: how to stop or turn it.</summary>
    public string HintText { get; private set; } = "";

    private bool _settingDown;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always;
        Size = new Vector2(420, 140);
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
        if (!Visible)
        {
            return;
        }
        Key = PackingMode.KeyLabel;
        if (!settingDown)
        {
            HintText = $"Hold {Key} to set it down again  ·  walk to go now";
        }
        else if (PackingMode.SettingDownByItself)
        {
            HintText = $"{Key} again or walk to keep it on";
        }
        else
        {
            HintText = "Let go or walk to keep it on";
        }
        Vector2 screen = GetViewportRect().Size;
        Position = new Vector2((screen.X - Size.X) * 0.5f, screen.Y * ScreenHeight);
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 centre = new(Size.X * 0.5f, Radius + 6.0f);
        DrawCircle(centre, Radius + 5.0f, new Color(UiStyle.Paper, UiStyle.PanelOpacity));
        DrawArc(centre, Radius, 0.0f, Mathf.Tau, 56, new Color(UiStyle.Line, 0.9f), 3.0f, true);
        float sweep = Mathf.Clamp(Progress, 0.0f, 1.0f) * Mathf.Tau;
        if (sweep > 0.01f)
        {
            DrawArc(centre, Radius, -Mathf.Pi * 0.5f, -Mathf.Pi * 0.5f + sweep, 56, UiStyle.Highlight, 5.0f, true);
        }

        // The key itself in the middle of the ring.
        int keySize = 20;
        if (Key.Length > 2)
        {
            keySize = 17;
        }
        Font strong = UiStyle.Strong;
        Vector2 keyText = strong.GetStringSize(Key, HorizontalAlignment.Left, -1, keySize);
        Vector2 keyAt = new(centre.X - keyText.X * 0.5f, centre.Y + (strong.GetAscent(keySize) - strong.GetDescent(keySize)) * 0.5f);
        DrawString(strong, keyAt, Key, HorizontalAlignment.Left, -1, keySize, UiStyle.Ink);

        string text = PuttingOnText;
        if (_settingDown)
        {
            text = SettingDownText;
        }
        Font hand = UiStyle.Hand;
        Vector2 size = hand.GetStringSize(text, HorizontalAlignment.Left, -1, UiStyle.TextSize);
        Vector2 hint = hand.GetStringSize(HintText, HorizontalAlignment.Left, -1, UiStyle.SmallSize);
        float width = Mathf.Max(size.X, hint.X) + 28.0f;
        float top = centre.Y + Radius + 12.0f;
        DrawRect(new Rect2((Size.X - width) * 0.5f, top, width, size.Y + hint.Y + 14.0f), new Color(UiStyle.Paper, UiStyle.PanelOpacity));
        DrawString(hand, new Vector2((Size.X - size.X) * 0.5f, top + 6.0f + hand.GetAscent(UiStyle.TextSize)), text, HorizontalAlignment.Left, -1, UiStyle.TextSize, UiStyle.Ink);
        DrawString(hand, new Vector2((Size.X - hint.X) * 0.5f, top + 8.0f + size.Y + hand.GetAscent(UiStyle.SmallSize)), HintText, HorizontalAlignment.Left, -1, UiStyle.SmallSize, UiStyle.Muted);
    }
}
