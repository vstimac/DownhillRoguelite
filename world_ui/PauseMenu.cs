using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// Esc (or P) pauses the run and shows the one flat screen the game has: resume, music and voice
/// volume, debug overlays on or off, the controls, and quit. It exists only while the run is
/// stopped, so the "no flat UI during runs" rule holds. Volumes set the "Music" and "Voice" buses.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    private static readonly StringName ActionPause = "pause";

    [Export] public Color DimColor { get; set; } = new(0.0f, 0.0f, 0.0f, 0.6f);
    [Export] public int TitleSize { get; set; } = 40;
    [Export] public int TextSize { get; set; } = 18;
    [Export(PropertyHint.MultilineText)] public string ControlsText { get; set; } =
        "WASD walk · Shift sprint · C brake (careful steps) · Space jump\n" +
        "Mouse look · LMB / RMB lean left / right · both brace\n" +
        "E pick up / sleep at a bedroll · F eat · Q drink · Tab pack (at stations)\n" +
        "R respawn · T skip an hour · F3 debug overlays · F4 cycle weather · F2 new mountain\n\n" +
        "Reach the next station before night. Stamina runs out walking — rest by standing still.\n" +
        "Cairns on the route show the time, rests and stamina to the next station.";

    public bool IsOpen => Visible;

    private Control? _root;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 50;
        Build();
        Visible = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(ActionPause))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Toggle()
    {
        if (Visible)
        {
            Resume();
        }
        else
        {
            Open();
        }
    }

    public void Open()
    {
        Visible = true;
        GetTree().Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void Resume()
    {
        Visible = false;
        GetTree().Paused = false;
        if (!GameEvents.IsPacking)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    private void Build()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        ColorRect dim = new() { Color = DimColor, MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);

        CenterContainer center = new();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        VBoxContainer column = new() { CustomMinimumSize = new Vector2(560.0f, 0.0f) };
        column.AddThemeConstantOverride("separation", 14);
        center.AddChild(column);

        Label title = new() { Text = "Paused", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", TitleSize);
        column.AddChild(title);

        Button resume = AddButton(column, "Resume");
        resume.Pressed += Resume;

        AddVolume(column, "Music", "Music");
        AddVolume(column, "Voice", "Voice");

        CheckButton overlays = new() { Text = "Debug overlays (F3)", ButtonPressed = Debug.DebugDraw3D.Enabled };
        overlays.AddThemeFontSizeOverride("font_size", TextSize);
        overlays.Toggled += on => Debug.DebugDraw3D.SetEnabled(on);
        column.AddChild(overlays);

        Label controls = new() { Text = ControlsText, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        controls.AddThemeFontSizeOverride("font_size", TextSize - 2);
        controls.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        column.AddChild(controls);

        Button quit = AddButton(column, "Quit");
        quit.Pressed += () => GetTree().Quit();

        // Keep the toggle in sync when F3 is used while the menu is closed.
        VisibilityChanged += () => overlays.SetPressedNoSignal(Debug.DebugDraw3D.Enabled);
    }

    private Button AddButton(Container parent, string text)
    {
        Button button = new() { Text = text, CustomMinimumSize = new Vector2(0.0f, 44.0f) };
        button.AddThemeFontSizeOverride("font_size", TextSize + 2);
        parent.AddChild(button);
        return button;
    }

    private void AddVolume(Container parent, string label, string bus)
    {
        int index = AudioServer.GetBusIndex(bus);
        HBoxContainer row = new();
        Label name = new() { Text = label, CustomMinimumSize = new Vector2(90.0f, 0.0f) };
        name.AddThemeFontSizeOverride("font_size", TextSize);
        row.AddChild(name);
        HSlider slider = new()
        {
            MinValue = 0.0,
            MaxValue = 1.0,
            Step = 0.01,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        if (index >= 0)
        {
            slider.Value = Mathf.DbToLinear(AudioServer.GetBusVolumeDb(index));
        }
        slider.ValueChanged += value =>
        {
            if (index >= 0)
            {
                AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max((float)value, 0.0001f)));
                AudioServer.SetBusMute(index, value <= 0.001);
            }
        };
        row.AddChild(slider);
        parent.AddChild(row);
    }
}
