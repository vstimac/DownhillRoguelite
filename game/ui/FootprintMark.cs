using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// A tiny top-down grid of the cells an item covers in the pack (across × deep), with a number for
/// how many cells tall it is, drawn in the top-right corner of an item slot above its picture.
/// </summary>
public partial class FootprintMark : Control
{
    /// <summary>The item's size in pack cells; zero hides the mark.</summary>
    public Vector3I Cells { get; set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        if (Cells.X <= 0 || Cells.Z <= 0)
        {
            return;
        }
        Vector2 size = Size;
        int across = Cells.X;
        int deep = Cells.Z;
        float cell = Mathf.Min(4.0f, size.X * 0.3f / Mathf.Max(across, deep));
        Vector2 grid = new(across * cell, deep * cell);
        Vector2 origin = new(size.X - grid.X - 5.0f, 5.0f);
        DrawRect(new Rect2(origin - Vector2.One * 1.5f, grid + Vector2.One * 3.0f), new Color(UiStyle.Paper, 0.9f));
        for (int x = 0; x < across; x++)
        {
            for (int z = 0; z < deep; z++)
            {
                DrawRect(new Rect2(origin + new Vector2(x * cell, z * cell), new Vector2(cell - 1.0f, cell - 1.0f)), new Color(UiStyle.Ink, 0.75f));
            }
        }
        if (Cells.Y > 1)
        {
            // An up arrow if the hand-drawn font has one, else a caret.
            string arrow = "^";
            if (UiStyle.Hand.HasChar(0x2191))
            {
                arrow = "↑";
            }
            string tall = arrow + Cells.Y;
            Vector2 text = UiStyle.Hand.GetStringSize(tall, HorizontalAlignment.Left, -1, 12);
            Vector2 at = new(origin.X - text.X - 4.0f, origin.Y + 10.0f);
            DrawRect(new Rect2(at - new Vector2(2.0f, 11.0f), text + new Vector2(4.0f, 2.0f)), new Color(UiStyle.Paper, 0.9f));
            DrawString(UiStyle.Hand, at, tall, HorizontalAlignment.Left, -1, 12, UiStyle.Ink);
        }
    }
}
