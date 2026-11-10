using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// The Options sheet shared by the main menu and the pause menu: a parchment page with tabs for
/// Gameplay, Video, Audio, Accessibility and Controls. Every change is saved at once (GameOptions)
/// and applied where it acts (ui/OptionsApplier, the camera, the HUD). Mouse, keyboard and
/// controller all work: up/down picks a row, left/right changes it, Q/E or the shoulder buttons
/// change tab, Esc or B goes back. The footer explains the focused row.
/// </summary>
public partial class OptionsPanel : ParchmentPanel
{
    [Export] public Vector2 MaxSize { get; set; } = new(900.0f, 640.0f);
    [Export] public float Margin { get; set; } = 22.0f;
    [Export] public float FooterHeight { get; set; } = 64.0f;
    [Export] public int RowFontSize { get; set; } = UiStyle.TextSize;
    /// <summary>Window sizes offered when windowed (only those that fit the screen are listed).</summary>
    [Export] public Vector2[] WindowSizes { get; set; } =
    {
        new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160),
    };
    [Export] public int[] FrameCaps { get; set; } = { 0, 30, 60, 90, 120, 144, 165, 240 };

    /// <summary>Offer the character on the Gameplay tab (the pause menu; the main menu has its own page).</summary>
    public bool ShowCharacter { get; set; } = true;

    /// <summary>Raised when Back is pressed (or Esc / B).</summary>
    public event Action? BackPressed;

    public static readonly string[] TabNames = { "Gameplay", "Video", "Audio", "Accessibility", "Controls" };

    public int CurrentTab => _tabs?.Selected ?? 0;
    public TabStrip? Tabs => _tabs;

    private TabStrip? _tabs;
    private readonly List<ScrollContainer> _pages = new();
    private readonly List<List<OptionRow>> _rows = new();
    private Label? _hint;
    private InkButton? _back;
    private InkButton? _reset;
    private bool _resetArmed;

    public OptionsPanel()
    {
        Title = "Options";
        Seed = 7.0f;
    }

    public override void _Ready()
    {
        base._Ready();
        GameOptions.Load();
        _tabs = new TabStrip { FontSize = 24 };
        _tabs.SetTabs(TabNames);
        _tabs.TabPicked += i => ShowTab(i, false);
        AddChild(_tabs);

        Theme scrollTheme = ScrollTheme();
        foreach (string name in TabNames)
        {
            ScrollContainer scroll = new()
            {
                Name = name,
                FollowFocus = true,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                Theme = scrollTheme,
                Visible = false,
            };
            VBoxContainer list = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            list.AddThemeConstantOverride("separation", 2);
            scroll.AddChild(list);
            AddChild(scroll);
            _pages.Add(scroll);
            _rows.Add(new List<OptionRow>());
        }
        BuildRows();

        _hint = UiStyle.MakeLabel("", UiStyle.SmallSize, false, new Color(UiStyle.Ink, 0.75f));
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hint.VerticalAlignment = VerticalAlignment.Center;
        AddChild(_hint);

        _reset = new InkButton { Text = "Reset all", Paper = true, Focusable = true, FontSize = UiStyle.SmallSize + 2, CustomMinimumSize = new Vector2(120.0f, 40.0f) };
        _reset.Pressed += ResetPressed;
        _reset.FocusExited += () => DisarmReset();
        AddChild(_reset);
        _back = new InkButton { Text = "Back", Paper = true, Focusable = true, FontSize = UiStyle.TextSize + 2, CustomMinimumSize = new Vector2(120.0f, 40.0f) };
        _back.Pressed += () => BackPressed?.Invoke();
        _back.FocusEntered += () => SetHint("Back to the menu. Every change is already saved.");
        AddChild(_back);

        Resized += LayoutContent;
        ShowTab(0, false);
        LayoutContent();
    }

    /// <summary>Takes the largest size up to MaxSize that fits <paramref name="area"/>, centred in it.</summary>
    public void FitInto(Rect2 area)
    {
        Vector2 size = new(Mathf.Min(MaxSize.X, area.Size.X), Mathf.Min(MaxSize.Y, area.Size.Y));
        Position = area.Position + (area.Size - size) * 0.5f;
        Size = size;
        LayoutContent();
    }

    private void LayoutContent()
    {
        if (_tabs == null || _hint == null || _back == null || _reset == null)
        {
            return;
        }
        float top = HeaderHeight + 6.0f;
        _tabs.Position = new Vector2(Margin, top);
        _tabs.Size = new Vector2(Size.X - Margin * 2.0f, _tabs.StripHeight);
        float pageTop = top + _tabs.StripHeight + 8.0f;
        float pageBottom = Size.Y - FooterHeight;
        foreach (ScrollContainer page in _pages)
        {
            page.Position = new Vector2(Margin, pageTop);
            page.Size = new Vector2(Size.X - Margin * 2.0f, Mathf.Max(pageBottom - pageTop, 40.0f));
        }
        float buttonY = Size.Y - FooterHeight + (FooterHeight - 40.0f) * 0.5f;
        _back.Position = new Vector2(Size.X - Margin - 120.0f, buttonY);
        _back.Size = new Vector2(120.0f, 40.0f);
        _reset.Position = new Vector2(Size.X - Margin - 120.0f - 16.0f - 120.0f, buttonY);
        _reset.Size = new Vector2(120.0f, 40.0f);
        _hint.Position = new Vector2(Margin + 6.0f, Size.Y - FooterHeight + 4.0f);
        _hint.Size = new Vector2(Mathf.Max(_reset.Position.X - Margin - 18.0f, 40.0f), FooterHeight - 12.0f);
    }

    /// <summary>The rows of a tab, in order (tests and menus).</summary>
    public IReadOnlyList<OptionRow> Rows(int tab)
    {
        return _rows[Mathf.Clamp(tab, 0, _rows.Count - 1)];
    }

    /// <summary>The row named <paramref name="label"/> on any tab, or null.</summary>
    public OptionRow? Row(string label)
    {
        foreach (List<OptionRow> rows in _rows)
        {
            foreach (OptionRow row in rows)
            {
                if (row.Label == label)
                {
                    return row;
                }
            }
        }
        return null;
    }

    public ScrollContainer Page(int tab)
    {
        return _pages[Mathf.Clamp(tab, 0, _pages.Count - 1)];
    }

    public Label? HintLabel => _hint;
    public InkButton? BackButton => _back;

    /// <summary>Shows a tab; <paramref name="focus"/> moves the focus to its first row (keyboard and controller).</summary>
    public void ShowTab(int index, bool focus)
    {
        if (_tabs == null)
        {
            return;
        }
        _tabs.Select(index);
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Visible = i == _tabs.Selected;
        }
        if (focus)
        {
            FocusFirst();
        }
        else if (_tabs.Selected < _rows.Count && _rows[_tabs.Selected].Count > 0)
        {
            SetHint(_rows[_tabs.Selected][0].Hint);
        }
    }

    /// <summary>Opens the sheet (unfolding) on its first tab with the first row focused.</summary>
    public void OpenSheet()
    {
        Open();
        ShowTab(CurrentTab, true);
    }

    public void FocusFirst()
    {
        if (_tabs != null && _rows[_tabs.Selected].Count > 0)
        {
            _rows[_tabs.Selected][0].GrabFocus();
            _pages[_tabs.Selected].ScrollVertical = 0;
        }
        else
        {
            _back?.GrabFocus();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || !IsOpen || !IsVisibleInTree())
        {
            return;
        }
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            BackPressed?.Invoke();
            return;
        }
        int step = TabStep(@event);
        if (step != 0)
        {
            GetViewport().SetInputAsHandled();
            ShowTab(CurrentTab + step, true);
        }
    }

    /// <summary>-1 / +1 for Q / E or the controller's shoulder buttons, else 0.</summary>
    public static int TabStep(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.PhysicalKeycode == Key.Q)
            {
                return -1;
            }
            if (key.PhysicalKeycode == Key.E)
            {
                return 1;
            }
        }
        if (@event is InputEventJoypadButton button && button.Pressed)
        {
            if (button.ButtonIndex == JoyButton.LeftShoulder)
            {
                return -1;
            }
            if (button.ButtonIndex == JoyButton.RightShoulder)
            {
                return 1;
            }
        }
        return 0;
    }

    private void SetHint(string text)
    {
        if (_hint != null)
        {
            _hint.Text = text;
        }
    }

    private void ResetPressed()
    {
        if (!_resetArmed)
        {
            _resetArmed = true;
            if (_reset != null)
            {
                _reset.Text = "Sure?";
                _reset.QueueRedraw();
            }
            SetHint("Press again to put every option back to how the game starts (your character stays).");
            return;
        }
        DisarmReset();
        GameOptions.RestoreDefaults();
        int tab = CurrentTab;
        RebuildRows();
        ShowTab(tab, false);
        _reset?.GrabFocus();
        SetHint("Every option is back to its default.");
    }

    private void DisarmReset()
    {
        _resetArmed = false;
        if (_reset != null)
        {
            _reset.Text = "Reset all";
            _reset.QueueRedraw();
        }
    }

    private void RebuildRows()
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            Node list = _pages[i].GetChild(0);
            foreach (Node child in list.GetChildren())
            {
                list.RemoveChild(child);
                child.QueueFree();
            }
            _rows[i].Clear();
        }
        BuildRows();
    }

    private void BuildRows()
    {
        const int gameplay = 0;
        const int video = 1;
        const int audio = 2;
        const int access = 3;
        const int controls = 4;

        Add(gameplay, ChoiceRow.Toggle("Stop the world while sorting", GameOptions.PauseWhileOpen, GameOptions.SetPauseWhileOpen,
            "Single player: time stands still while the inventory page or the pack is open."));
        Add(gameplay, ChoiceRow.Toggle("Low-stat alerts", GameOptions.LowStatAlerts, GameOptions.SetLowStatAlerts,
            "A flashing list in the top-left corner when hunger, thirst, warmth or health run low."));
        Add(gameplay, new ChoiceRow
        {
            Label = "Survival stats",
            Hint = "Hand-drawn dials, or plain bars with numbers from 0 to 100.",
            Options = new[] { "Dials", "Bars (0-100)" },
            Selected = (int)GameOptions.Stats,
            Changed = i => GameOptions.SetStats((StatStyle)i),
        });
        Add(gameplay, ChoiceRow.Toggle("Mouse wheel picks on the hotbar", GameOptions.WheelSelectsHotbar, GameOptions.SetWheelSelectsHotbar,
            "Off: only the number keys 1-9 choose from the hotbar."));
        Add(gameplay, ChoiceRow.Toggle("Route and debug lines (F3)", GameOptions.PathLines, GameOptions.SetPathLines,
            "Lines in the world showing the planned route and the carrier's balance."));
        if (ShowCharacter)
        {
            CharacterRow(gameplay);
        }

        Add(video, new ChoiceRow
        {
            Label = "Window",
            Hint = "Borderless fills the screen and switches away instantly; fullscreen can be smoother on some systems.",
            Options = new[] { "Windowed", "Borderless fullscreen", "Fullscreen" },
            Selected = (int)GameOptions.Display,
            Changed = i => GameOptions.SetDisplay((DisplayMode)i),
        });
        WindowSizeRow(video);
        Add(video, new ChoiceRow
        {
            Label = "Quality",
            Hint = "Low: lower render resolution, short shadows, fewer trees and grass far away; for weak GPUs and laptops. High: the full look.",
            Options = new[] { "Low", "Medium", "High" },
            Selected = (int)GameOptions.Quality,
            Changed = i => GameOptions.SetQuality((GraphicsQuality)i),
        });
        Add(video, ChoiceRow.Toggle("Vertical sync", GameOptions.VSync, GameOptions.SetVSync,
            "Matches frames to the screen's refresh: no tearing, a touch more input delay."));
        FrameCapRow(video);
        Add(video, new SliderRow
        {
            Label = "Field of view",
            Hint = "How wide the camera sees. Wider shows more of the slope; narrower feels closer.",
            Min = 60.0,
            Max = 120.0,
            StepSize = 1.0,
            Value = GameOptions.FieldOfView,
            Format = "{0:0}°",
            Live = true,
            Changed = v => GameOptions.SetFieldOfView((float)v),
        });

        Add(audio, Volume("Master volume", "Everything at once.", GameOptions.MasterVolume, GameOptions.SetMasterVolume));
        Add(audio, Volume("Music", "The ambient music of the descent.", GameOptions.MusicVolume, GameOptions.SetMusicVolume));
        Add(audio, Volume("Voices", "The narrator, the companion and the travellers at the fires.", GameOptions.VoiceVolume, GameOptions.SetVoiceVolume));
        Add(audio, Volume("Weather and fire", "Wind, rain and the crackle of station fires.", GameOptions.AmbienceVolume, GameOptions.SetAmbienceVolume));

        Add(access, new ChoiceRow
        {
            Label = "Colour-blind filter",
            Hint = "Shifts the colours so reds, greens or blues stay distinct.",
            Options = new[] { "Off", "Protanopia (red)", "Deuteranopia (green)", "Tritanopia (blue)" },
            Selected = (int)GameOptions.Filter,
            Changed = i => GameOptions.SetFilter((ColourFilter)i),
        });
        Add(access, new SliderRow
        {
            Label = "Interface and text size",
            Hint = "Grows every page, dial and note together, for 2K and 4K screens. Applies when you let go.",
            Min = 0.75,
            Max = 1.5,
            StepSize = 0.05,
            Value = GameOptions.UiScale,
            Format = "{0:0.00}x",
            Changed = v => GameOptions.SetUiScale((float)v),
        });
        Add(access, ChoiceRow.Toggle("Subtitles", GameOptions.Subtitles, GameOptions.SetSubtitles,
            "Spoken lines are written on screen, on the side the voice comes from."));

        // Mouse: applied in this order (MouseLookFilter): desktop pointer settings (Raw input off only),
        // in-game acceleration, sensitivity, smoothing.
        Add(controls, new SliderRow
        {
            Label = "Mouse sensitivity",
            Hint = "How far the camera turns per mouse movement. Linear: 2.00x turns twice as far.",
            Min = 0.1,
            Max = 5.0,
            StepSize = 0.05,
            Value = GameOptions.MouseSensitivity,
            Format = "{0:0.00}x",
            Live = true,
            Changed = v => GameOptions.SetMouseSensitivity((float)v),
        });
        Add(controls, ChoiceRow.Toggle("Mouse smoothing", GameOptions.MouseSmoothing, GameOptions.SetMouseSmoothing,
            "Eases the camera's turn over a few hundredths of a second. Smoother, with a little delay; the total turn is the same."));
        Add(controls, new SliderRow
        {
            Label = "Mouse acceleration",
            Hint = "Faster flicks turn further per movement. 0% is off: the turn always matches the movement. Works with Raw input on or off.",
            Min = 0.0,
            Max = 1.0,
            StepSize = 0.05,
            Value = GameOptions.MouseAcceleration,
            ShowScale = 100.0,
            Format = "{0:0}%",
            Live = true,
            Changed = v => GameOptions.SetMouseAcceleration((float)v),
        });
        string rawHint = "Yes: the mouse's own movement, ignoring the desktop's pointer speed and \"Enhance pointer precision\". " +
            "No: the camera follows them like the desktop cursor.";
        if (OS.GetName() != "Windows")
        {
            rawHint += " (Only Windows has desktop settings to follow; elsewhere both read the raw movement.)";
        }
        int raw = 0;
        if (GameOptions.RawInput)
        {
            raw = 1;
        }
        Add(controls, new ChoiceRow
        {
            Label = "Raw input",
            Hint = rawHint,
            Options = new[] { "No", "Yes" },
            Selected = raw,
            Changed = i => GameOptions.SetRawInput(i == 1),
        });
        Add(controls, new SliderRow
        {
            Label = "Stick sensitivity",
            Hint = "How fast the camera turns with the right stick of a controller.",
            Min = 0.25,
            Max = 3.0,
            StepSize = 0.05,
            Value = GameOptions.LookSensitivity,
            Format = "{0:0.00}x",
            Live = true,
            Changed = v => GameOptions.SetLookSensitivity((float)v),
        });
        Add(controls, ChoiceRow.Toggle("Invert look", GameOptions.InvertLook, GameOptions.SetInvertLook,
            "Pushing the mouse or stick up looks down."));
        ControlsReference(controls);
    }

    private void Add(int tab, OptionRow row)
    {
        row.FontSize = RowFontSize;
        row.FocusEntered += () => SetHint(row.Hint);
        _pages[tab].GetChild(0).AddChild(row);
        _rows[tab].Add(row);
    }

    private static SliderRow Volume(string label, string hint, float value, Action<float> set)
    {
        return new SliderRow
        {
            Label = label,
            Hint = hint,
            Min = 0.0,
            Max = 1.0,
            StepSize = 0.05,
            Value = value,
            Format = "{0:0}%",
            ShowScale = 100.0,
            Live = true,
            Changed = v => set((float)v),
        };
    }

    /// <summary>The character to play (only those whose model is in the game; changes on the spot).</summary>
    private void CharacterRow(int tab)
    {
        CarrierRoster? roster = CarrierRoster.Load();
        if (roster == null)
        {
            return;
        }
        List<CarrierCharacter> characters = roster.Available();
        if (characters.Count < 2)
        {
            return;
        }
        string[] names = new string[characters.Count];
        int selected = 0;
        for (int i = 0; i < characters.Count; i++)
        {
            names[i] = characters[i].DisplayName;
            if (characters[i].Id == GameOptions.Character)
            {
                selected = i;
            }
        }
        Add(tab, new ChoiceRow
        {
            Label = "Character",
            Hint = "Who carries the load. Changes on the spot.",
            Options = names,
            Selected = selected,
            Changed = i => GameOptions.SetCharacter(characters[i].Id),
        });
    }

    private void WindowSizeRow(int tab)
    {
        Vector2I screen = Vector2I.Zero;
        if (DisplayServer.GetName() != "headless")
        {
            screen = DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());
        }
        List<Vector2I> sizes = new() { Vector2I.Zero };
        List<string> names = new() { "Default" };
        foreach (Vector2 entry in WindowSizes)
        {
            Vector2I size = new((int)entry.X, (int)entry.Y);
            if (screen == Vector2I.Zero || (size.X <= screen.X && size.Y <= screen.Y))
            {
                sizes.Add(size);
                names.Add($"{size.X} × {size.Y}");
            }
        }
        int selected = Mathf.Max(sizes.IndexOf(GameOptions.WindowSize), 0);
        Add(tab, new ChoiceRow
        {
            Label = "Window size",
            Hint = "The window's size when windowed. Fullscreen always uses the screen's own resolution.",
            Options = names.ToArray(),
            Selected = selected,
            Wrap = false,
            Changed = i => GameOptions.SetWindowSize(sizes[i]),
        });
    }

    private void FrameCapRow(int tab)
    {
        string[] names = new string[FrameCaps.Length];
        int selected = 0;
        for (int i = 0; i < FrameCaps.Length; i++)
        {
            if (FrameCaps[i] <= 0)
            {
                names[i] = "Unlimited";
            }
            else
            {
                names[i] = $"{FrameCaps[i]} fps";
            }
            if (FrameCaps[i] == GameOptions.MaxFps)
            {
                selected = i;
            }
        }
        int[] caps = FrameCaps;
        Add(tab, new ChoiceRow
        {
            Label = "Frame-rate cap",
            Hint = "A cap keeps the fans quiet and the laptop cool.",
            Options = names,
            Selected = selected,
            Wrap = false,
            Changed = i => GameOptions.SetMaxFps(caps[i]),
        });
    }

    /// <summary>What each action does and its keys and buttons, read from the project's input map.</summary>
    private void ControlsReference(int tab)
    {
        (string, string)[] lines =
        {
            ("Walk", InputGlyphs.Keys("player_move_forward", "player_move_left", "player_move_back", "player_move_right") + "  ·  Left stick"),
            ("Look around", "Mouse  ·  Right stick"),
            ("Sprint", InputGlyphs.Both("player_sprint")),
            ("Careful steps (brake)", InputGlyphs.Both("player_brake")),
            ("Jump (falling: roll on landing)", InputGlyphs.Both("player_jump")),
            ("Dive", InputGlyphs.Both("player_dive")),
            ("Lean left / right", InputGlyphs.Both("balance_lean_left") + "  |  " + InputGlyphs.Both("balance_lean_right")),
            ("Brace (lean both ways)", "Both mouse buttons"),
            ("Pick up, use, sleep at a bedroll", InputGlyphs.Both("interact")),
            ("Draw / put away the sword", "Z"),
            ("Sword: swing (again to chain)", "Left mouse"),
            ("Sword: shield up / kick", "Right mouse (hold)  |  Middle mouse"),
            ("Lock on to an enemy / let go", "Tab"),
            ("Take a cart / let go", InputGlyphs.Both("interact") + " at the handles"),
            ("Eat", InputGlyphs.Both("player_eat")),
            ("Drink", InputGlyphs.Both("player_drink")),
            ("Pack (at stations)", InputGlyphs.Both("inventory_toggle_packing")),
            ("Inventory: wear, mend, drop", InputGlyphs.Both("inventory_open")),
            ("Journal", InputGlyphs.Both("journal_open")),
            ("Map", InputGlyphs.Both("map_toggle")),
            ("Hotbar: choose / use", "1-9 or mouse wheel  ·  V"),
            ("Move any part of the interface", "Alt + drag  ·  Alt+Backspace resets"),
            ("Give up the run", "R twice"),
            ("Pause", InputGlyphs.Both("pause")),
        };
        foreach ((string what, string keys) in lines)
        {
            Add(tab, new InfoRow { Label = what, Value = keys, LabelShare = 0.46f, Hint = what + ": " + keys });
        }
    }

    /// <summary>A thin inked scroll bar instead of the default grey one.</summary>
    private static Theme ScrollTheme()
    {
        Theme theme = new();
        StyleBoxFlat track = new() { BgColor = new Color(UiStyle.Ink, 0.08f), ContentMarginLeft = 3, ContentMarginRight = 3 };
        track.SetCornerRadiusAll(3);
        StyleBoxFlat grab = new() { BgColor = new Color(UiStyle.Ink, 0.45f) };
        grab.SetCornerRadiusAll(3);
        StyleBoxFlat grabHover = new() { BgColor = new Color(UiStyle.Ink, 0.7f) };
        grabHover.SetCornerRadiusAll(3);
        theme.SetStylebox("scroll", "VScrollBar", track);
        theme.SetStylebox("grabber", "VScrollBar", grab);
        theme.SetStylebox("grabber_highlight", "VScrollBar", grabHover);
        theme.SetStylebox("grabber_pressed", "VScrollBar", grabHover);
        return theme;
    }
}
