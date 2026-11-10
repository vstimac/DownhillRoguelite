using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// A box drawn in ink, ticked when Ticked: the journal's checkmark for a done thing (achievements).
/// The tick is a quick two-stroke pen mark, a little rough and running past the box's edge, in red-brown
/// ink; an empty box is faint. Wobbled by Seed so no two look alike.
/// </summary>
public partial class TickBox : Control
{
    [Export] public bool Ticked { get; set; }
    [Export] public int Seed { get; set; } = 1;
    [Export] public float BoxWidth { get; set; } = 2.2f;
    [Export] public float TickWidth { get; set; } = 4.0f;
    [Export] public Color TickColor { get; set; } = new(0.55f, 0.13f, 0.08f);

    public TickBox()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(34.0f, 34.0f);
    }

    public void SetTicked(bool ticked)
    {
        Ticked = ticked;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float side = Mathf.Min(Size.X, Size.Y);
        Vector2 origin = (Size - new Vector2(side, side)) * 0.5f;
        float inset = side * 0.18f;
        Vector2 a = origin + new Vector2(inset, inset);
        Vector2 b = origin + new Vector2(side - inset, side - inset);
        Vector2[] box = { a, new(b.X, a.Y), b, new(a.X, b.Y) };
        Color ink = UiStyle.Ink;
        if (!Ticked)
        {
            ink = UiStyle.InkSoft;
        }
        Vector2[] wobbled = InkDraw.Wobble(box, BoxWidth * 0.5f, Seed);
        Vector2[] closed = { wobbled[0], wobbled[1], wobbled[2], wobbled[3], wobbled[0] };
        DrawPolyline(closed, ink, BoxWidth, true);
        if (!Ticked)
        {
            return;
        }
        // Down into the box's lower middle, then a long stroke up past its top right corner.
        float s = side;
        Vector2[] tick =
        {
            origin + new Vector2(s * 0.24f, s * 0.50f),
            origin + new Vector2(s * 0.36f, s * 0.64f),
            origin + new Vector2(s * 0.45f, s * 0.76f),
            origin + new Vector2(s * 0.62f, s * 0.46f),
            origin + new Vector2(s * 0.80f, s * 0.18f),
            origin + new Vector2(s * 0.98f, s * -0.04f),
        };
        DrawPolyline(InkDraw.Wobble(tick, TickWidth * 0.2f, Seed + 7), TickColor, TickWidth, true);
    }
}
