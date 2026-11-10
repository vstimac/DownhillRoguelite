using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// A talking NPC's choices (GameEvents.DialogueChoicesShown): a parchment strip right of centre with
/// whom you talk to and the numbered choices. Number keys pick one (GameEvents.RequestDialogueChoice);
/// while choices show, those keys belong to them, not to the hotbar. Never pauses or blocks walking:
/// the NPC closes the conversation when the carrier walks off. Presentation only.
/// </summary>
public partial class DialogueChoices : Control
{
    [Export] public Vector2 ScreenSpot { get; set; } = new(0.62f, 0.42f);
    [Export] public float Width { get; set; } = 380.0f;
    [Export] public int TextSize { get; set; } = 22;

    public string[] Shown { get; private set; } = System.Array.Empty<string>();

    private PanelContainer? _panel;
    private VBoxContainer? _rows;
    private Label? _speaker;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _panel = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        StyleBoxFlat paper = new()
        {
            BgColor = new Color(UiStyle.Paper, 0.94f),
            BorderColor = UiStyle.PaperDark,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 8,
            ContentMarginBottom = 10,
        };
        paper.SetBorderWidthAll(2);
        paper.SetCornerRadiusAll(4);
        _panel.AddThemeStyleboxOverride("panel", paper);
        AddChild(_panel);
        _rows = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _panel.AddChild(_rows);
        _speaker = UiStyle.MakeLabel("", TextSize - 4, true, UiStyle.InkSoft);
        _rows.AddChild(_speaker);
        GameEvents.DialogueChoicesShown += OnShown;
    }

    public override void _ExitTree()
    {
        GameEvents.DialogueChoicesShown -= OnShown;
    }

    private void OnShown(string speaker, string[] choices)
    {
        Shown = choices;
        if (_panel == null || _rows == null || _speaker == null)
        {
            return;
        }
        foreach (Node child in _rows.GetChildren())
        {
            if (child != _speaker)
            {
                _rows.RemoveChild(child);
                child.QueueFree();
            }
        }
        _panel.Visible = choices.Length > 0;
        if (choices.Length == 0)
        {
            return;
        }
        _speaker.Text = speaker;
        for (int i = 0; i < choices.Length; i++)
        {
            Label row = UiStyle.MakeLabel($"{i + 1}.  {choices[i]}", TextSize);
            row.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            row.CustomMinimumSize = new Vector2(Width, 0.0f);
            _rows.AddChild(row);
        }
        Place();
    }

    public override void _Process(double delta)
    {
        if (_panel != null && _panel.Visible)
        {
            Place();
        }
    }

    private void Place()
    {
        if (_panel == null)
        {
            return;
        }
        Vector2 screen = GetViewportRect().Size;
        _panel.Size = Vector2.Zero;
        _panel.Position = new Vector2(screen.X * ScreenSpot.X, screen.Y * ScreenSpot.Y - _panel.GetCombinedMinimumSize().Y * 0.5f);
    }

    public override void _Input(InputEvent @event)
    {
        if (Shown.Length == 0 || @event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }
        if (key.PhysicalKeycode >= Key.Key1 && key.PhysicalKeycode <= Key.Key9)
        {
            int index = (int)(key.PhysicalKeycode - Key.Key1);
            if (index < Shown.Length)
            {
                GetViewport().SetInputAsHandled();
                GameEvents.RequestDialogueChoice(index);
            }
        }
    }
}
