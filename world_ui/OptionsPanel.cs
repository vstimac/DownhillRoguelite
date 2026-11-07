using System;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// The pause menu's Options page: gameplay (pause while sorting, low-stat alerts, path lines, wheel
/// on the hotbar, dials or bars, field of view) and accessibility (colour-blind filter, interface
/// size). Every change is saved at once (GameOptions); Back returns to the menu.
/// </summary>
public partial class OptionsPanel : VBoxContainer
{
    public int TextSize { get; set; } = 20;

    /// <summary>Raised when Back is pressed.</summary>
    public event Action? BackPressed;

    public override void _Ready()
    {
        GameOptions.Load();
        CustomMinimumSize = new Vector2(560.0f, 0.0f);
        AddThemeConstantOverride("separation", 10);

        Heading("Options");
        Toggle("Stop the world while the inventory or pack is open", GameOptions.PauseWhileOpen, GameOptions.SetPauseWhileOpen);
        Toggle("Flashing list of low stats (top left)", GameOptions.LowStatAlerts, GameOptions.SetLowStatAlerts);
        Toggle("Route and debug lines in the world (F3)", GameOptions.PathLines, GameOptions.SetPathLines);
        Toggle("Mouse wheel picks on the hotbar (otherwise keys 1-9)", GameOptions.WheelSelectsHotbar, GameOptions.SetWheelSelectsHotbar);
        Choice("Survival stats", new[] { "Dials", "Bars (0-100)" }, (int)GameOptions.Stats, i => GameOptions.SetStats((StatStyle)i));
        Slider("Field of view", 60.0, 120.0, 1.0, GameOptions.FieldOfView, "{0:0}°", v => GameOptions.SetFieldOfView((float)v));

        Heading("Accessibility");
        Choice("Colour-blind filter", new[] { "Off", "Protanopia (red)", "Deuteranopia (green)", "Tritanopia (blue)" }, (int)GameOptions.Filter,
            i => GameOptions.SetFilter((ColourFilter)i));
        Slider("Interface and text size", 0.75, 1.5, 0.05, GameOptions.UiScale, "{0:0.00}x", v => GameOptions.SetUiScale((float)v));

        Button back = new() { Text = "Back", CustomMinimumSize = new Vector2(0.0f, 44.0f) };
        back.AddThemeFontSizeOverride("font_size", TextSize + 2);
        back.Pressed += () => BackPressed?.Invoke();
        AddChild(back);
    }

    private void Heading(string text)
    {
        Label label = new() { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", TextSize + 8);
        AddChild(label);
    }

    private void Toggle(string text, bool on, Action<bool> set)
    {
        CheckButton check = new() { Text = text, ButtonPressed = on };
        check.AddThemeFontSizeOverride("font_size", TextSize);
        check.Toggled += value => set(value);
        AddChild(check);
    }

    private void Choice(string text, string[] options, int selected, Action<int> set)
    {
        HBoxContainer row = new();
        Label name = new() { Text = text, CustomMinimumSize = new Vector2(220.0f, 0.0f) };
        name.AddThemeFontSizeOverride("font_size", TextSize);
        row.AddChild(name);
        OptionButton pick = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        pick.AddThemeFontSizeOverride("font_size", TextSize);
        foreach (string option in options)
        {
            pick.AddItem(option);
        }
        pick.Selected = selected;
        pick.ItemSelected += index => set((int)index);
        row.AddChild(pick);
        AddChild(row);
    }

    private void Slider(string text, double min, double max, double step, double value, string format, Action<double> set)
    {
        HBoxContainer row = new();
        Label name = new() { Text = text, CustomMinimumSize = new Vector2(220.0f, 0.0f) };
        name.AddThemeFontSizeOverride("font_size", TextSize);
        row.AddChild(name);
        HSlider slider = new()
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        Label shown = new() { Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, value), CustomMinimumSize = new Vector2(70.0f, 0.0f) };
        shown.AddThemeFontSizeOverride("font_size", TextSize);
        // The interface size applies when the slider is let go, so the slider doesn't move under the mouse.
        slider.ValueChanged += v => shown.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, v);
        slider.DragEnded += changed => set(slider.Value);
        row.AddChild(slider);
        row.AddChild(shown);
        AddChild(row);
    }
}
