using DownhillRoguelite.Core;
using Godot;
namespace DownhillRoguelite.Ui;

/// <summary>
/// One compact ability button: ready, active duration or remaining cooldown. It sits in the bottom
/// right corner on the HUD's margin, its bottom on the hotbar's line and as wide as the right-hand
/// column of notes, so the corner reads as one edge.
/// </summary>
public partial class CharacterAbilityPanel : CanvasLayer
{
    public string KeyLabel { get; set; } = "H";
    [Export] public Vector2 ButtonSize { get; set; } = new(300, 44);
    private RunState? _run;
    private InkButton? _button;
    private float _refresh;
    public override void _Ready()
    {
        Layer = 12;
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _button = new InkButton { Paper = true, FontSize = 16 };
        _button.Pressed += () => _run?.TryUseCharacterAbility();
        AddChild(_button);
        _button.Size = ButtonSize;
    }
    public override void _Process(double delta)
    {
        _refresh -= (float)delta;
        if (_run == null || _button == null || _refresh > 0) { return; }
        _refresh = 0.1f;
        // Clean screenshots (`-- --promo`) hide it with the rest of the HUD; so do the pages and menus.
        _button.Visible = !Debug.DebugDraw3D.HideHud && !GameEvents.IsPacking && !GameEvents.MenuOpen
            && !(GetTree().Paused && !GameEvents.PagesFrozen);
        CharacterRun character = _run.Character;
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        _button.Size = ButtonSize;
        _button.Position = new Vector2(screen.X - ButtonSize.X - UiStyle.ScreenMargin, screen.Y - ButtonSize.Y - UiStyle.ScreenMargin);
        string status = "Ready";
        if (character.Active) { status = $"Active {character.ActiveLeft:0.0}s"; }
        else if (character.CooldownLeft > 0) { status = $"{character.CooldownLeft:0}s"; }
        _button.Text = KeyLabel + " · " + character.Stats.AbilityName + " · " + status;
        _button.TooltipText = character.Stats.AbilityDescription;
        _button.Disabled = character.CooldownLeft > 0 || _run.RunOver || character.Stats.AbilityName.Length == 0
            || GameEvents.IsDowned || GameEvents.IsPacking || GameEvents.IsConversing;
        _button.QueueRedraw();
    }
}
