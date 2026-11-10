using System;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// The start screen's About page: what the game is, who made it and whose work it stands on
/// (art/CREDITS.md in short). Up/down or the wheel scrolls; Back, Esc or B returns.
/// </summary>
public partial class CreditsPage : ParchmentPanel
{
    [Export] public Vector2 MaxSize { get; set; } = new(760.0f, 600.0f);
    [Export] public float Margin { get; set; } = 26.0f;
    [Export] public float ScrollStep { get; set; } = 48.0f;
    [Export] public string MadeBy { get; set; } = "DankPsycho";
    [Export] public string Version { get; set; } = "Playtest 1";
    [Export(PropertyHint.MultilineText)] public string About { get; set; } =
        "An endless descent. Every run starts on the summit of a new mountain and goes down: through snow and scree, past the stations " +
        "where travellers keep their fires, and on through the nine circles. Nobody has reached the bottom. Death is permanent; the mountain is always new.";
    [Export(PropertyHint.MultilineText)] public string Credits { get; set; } =
        "Characters and animals: Quaternius (Universal Animation Library, Easy Enemies, Farm Animals), Mixamo (the Carrier's model and animations)\n" +
        "Items, props and pets: Kenney (Survival Kit, Food Kit, Graveyard Kit, Nature Kit, Cube Pets)\n" +
        "Sound: Kenney (Impact Sounds, RPG Audio, Interface Sounds); music, weather and footsteps made for this game\n" +
        "Narrator: lines written for this game, voiced with ElevenLabs\n" +
        "Fonts: Patrick Hand (Patrick Wagesreiter) and IM Fell English SC (Igino Marini), SIL Open Font License\n" +
        "Conversation: NobodyWho, running a local language model\n" +
        "Made with the Godot Engine\n\n" +
        "Thank you to everyone who played the playtests and said what they thought.";

    public event Action? BackPressed;

    public ScrollContainer? Scroll => _scroll;
    public InkButton? BackButton => _back;

    private ScrollContainer? _scroll;
    private VBoxContainer? _column;
    private InkButton? _back;

    public CreditsPage()
    {
        Title = "About";
        Seed = 33.0f;
    }

    public override void _Ready()
    {
        base._Ready();
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(_scroll);
        _column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _column.AddThemeConstantOverride("separation", 10);
        _scroll.AddChild(_column);
        Paragraph("Downhill Roguelite", UiStyle.HeadingSize + 4, true, UiStyle.Ink);
        Paragraph($"{Version}  ·  made by {MadeBy}", UiStyle.SmallSize + 2, false, new Color(UiStyle.Ink, 0.7f));
        Paragraph(About, UiStyle.TextSize, false, UiStyle.Ink);
        Paragraph("Built with", UiStyle.HeadingSize - 6, true, UiStyle.Ink);
        Paragraph(Credits, UiStyle.TextSize - 2, false, new Color(UiStyle.Ink, 0.9f));

        _back = new InkButton { Text = "Back", Paper = true, Focusable = true, FontSize = UiStyle.TextSize + 2, CustomMinimumSize = new Vector2(130.0f, 42.0f) };
        _back.Pressed += () => BackPressed?.Invoke();
        AddChild(_back);
        Resized += LayoutContent;
        LayoutContent();
    }

    private void Paragraph(string text, int size, bool heading, Color colour)
    {
        Label label = UiStyle.MakeLabel(text, size, heading, colour);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _column?.AddChild(label);
    }

    public void FitInto(Rect2 area)
    {
        Vector2 size = new(Mathf.Min(MaxSize.X, area.Size.X), Mathf.Min(MaxSize.Y, area.Size.Y));
        Position = area.Position + (area.Size - size) * 0.5f;
        Size = size;
        LayoutContent();
    }

    private void LayoutContent()
    {
        if (_scroll == null || _back == null)
        {
            return;
        }
        float top = HeaderHeight + 10.0f;
        _scroll.Position = new Vector2(Margin, top);
        _scroll.Size = new Vector2(Size.X - Margin * 2.0f, Mathf.Max(Size.Y - top - 74.0f, 40.0f));
        _back.Position = new Vector2((Size.X - 130.0f) * 0.5f, Size.Y - 60.0f);
        _back.Size = new Vector2(130.0f, 42.0f);
    }

    public void OpenSheet()
    {
        Open();
        if (_scroll != null)
        {
            _scroll.ScrollVertical = 0;
        }
        _back?.GrabFocus();
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
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Up and down scroll the text (the Back button is the only thing to focus).
        if (!Visible || !IsOpen || _scroll == null || !IsVisibleInTree())
        {
            return;
        }
        int direction = 0;
        if (@event.IsActionPressed("ui_up", true))
        {
            direction = -1;
        }
        else if (@event.IsActionPressed("ui_down", true))
        {
            direction = 1;
        }
        if (direction != 0)
        {
            _scroll.ScrollVertical += (int)(ScrollStep * direction);
            GetViewport().SetInputAsHandled();
        }
    }
}
