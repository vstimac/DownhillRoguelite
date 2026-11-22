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
            // How many cells tall: a small drawn up-arrow and the number ("▲2"), beside the grid.
            string tall = Cells.Y.ToString();
            Vector2 text = UiStyle.Strong.GetStringSize(tall, HorizontalAlignment.Left, -1, 12);
            float arrowWidth = 7.0f;
            Vector2 at = new(origin.X - text.X - arrowWidth - 6.0f, origin.Y + 9.0f);
            DrawRect(new Rect2(at - new Vector2(2.0f, 10.0f), new Vector2(text.X + arrowWidth + 5.0f, 12.0f)), new Color(UiStyle.Paper, 0.9f));
            Vector2 tip = at + new Vector2(arrowWidth * 0.5f, -8.0f);
            DrawColoredPolygon(new[] { tip, at + new Vector2(arrowWidth, -1.5f), at + new Vector2(0.0f, -1.5f) }, new Color(UiStyle.Ink, 0.85f));
            DrawString(UiStyle.Strong, at + new Vector2(arrowWidth + 2.0f, 0.0f), tall, HorizontalAlignment.Left, -1, 12, UiStyle.Ink);
        }
    }
}
