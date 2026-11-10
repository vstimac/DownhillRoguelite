using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// A read-only line (the controls reference: what an action does, and its keys and buttons).
/// It still takes focus, so a controller can scroll through the list.
/// </summary>
public partial class InfoRow : OptionRow
{
    public string Value { get; set; } = "";

    public override string ValueText => Value;

    public override bool Fits()
    {
        return UiStyle.Hand.GetStringSize(Value, HorizontalAlignment.Left, -1, FontSize - 2).X <= ValueRect().Size.X && base.Fits();
    }

    protected override void DrawValue()
    {
        Rect2 box = ValueRect();
        Font font = UiStyle.Hand;
        int size = FontSize - 2;
        DrawString(font, new Vector2(box.Position.X, Baseline(size)), Value, HorizontalAlignment.Right, box.Size.X, size, new Color(UiStyle.Ink, 0.85f));
    }
}
