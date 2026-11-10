using DownhillRoguelite.Core;
using Godot;
namespace DownhillRoguelite.Ui;

/// <summary>One compact ability button: ready, active duration or remaining cooldown.</summary>
public partial class CharacterAbilityPanel : CanvasLayer
{
    public string KeyLabel { get; set; } = "H";
    [Export] public Vector2 ButtonSize { get; set; } = new(245, 40);
    [Export] public Vector2 EdgeOffset { get; set; } = new(18, 140);
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
        // Clean screenshots (`-- --promo`) hide it with the rest of the HUD.
        _button.Visible = !Debug.DebugDraw3D.HideHud;
        CharacterRun character = _run.Character;
        float scale = GameOptions.UiScale;
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        _button.Scale = Vector2.One * scale;
        _button.Position = new Vector2(screen.X - (ButtonSize.X + EdgeOffset.X) * scale, screen.Y - EdgeOffset.Y * scale);
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
