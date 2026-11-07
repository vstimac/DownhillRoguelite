using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The world around the inventory page: drag an item off the page and drop it here to throw it out
/// of the way (InventoryScreen.ThrowSlot). Only there while the page is open; while an item is being
/// dragged it says so at the top of the screen.
/// </summary>
public partial class ThrowZone : Control
{
    public InventoryScreen? Inventory { get; set; }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        bool open = Inventory != null && Inventory.Visible && Inventory.IsOpen;
        if (Visible != open)
        {
            Visible = open;
        }
        if (Visible)
        {
            // Catch the mouse only while something is dragged, so clicks reach the HUD (the equipment sheet) otherwise.
            if (GetViewport().GuiIsDragging())
            {
                MouseFilter = MouseFilterEnum.Stop;
            }
            else
            {
                MouseFilter = MouseFilterEnum.Ignore;
            }
            QueueRedraw();
        }
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        return Inventory != null && data.VariantType == Variant.Type.Int;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        Inventory?.ThrowSlot(data.AsInt32());
    }

    public override void _Draw()
    {
        if (!GetViewport().GuiIsDragging())
        {
            return;
        }
        string text = "Drop outside the page to throw it away";
        Vector2 size = UiStyle.Hand.GetStringSize(text, HorizontalAlignment.Left, -1, UiStyle.TextSize);
        Vector2 at = new((Size.X - size.X) * 0.5f, 46.0f);
        DrawRect(new Rect2(at - new Vector2(14, 30), size + new Vector2(28, 16)), new Color(UiStyle.Paper, 0.9f));
        DrawString(UiStyle.Hand, at, text, HorizontalAlignment.Left, -1, UiStyle.TextSize, UiStyle.Ink);
    }
}
