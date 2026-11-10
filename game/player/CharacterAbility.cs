using DownhillRoguelite.Core;
using DownhillRoguelite.Ui;
using Godot;
namespace DownhillRoguelite.Player;

/// <summary>Actor-side ability effects and input; run state owns identity and cooldowns.</summary>
public partial class CharacterAbility : Node
{
    public const string Action = "character_ability";
    [Export] public Key ActivationKey { get; set; } = Key.H;
    private RunState? _run;
    private PlayerController? _body;
    private CarrierStamina? _stamina;
    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _body = GetParentOrNull<PlayerController>();
        _stamina = GetNodeOrNull<CarrierStamina>("../Stamina");
        if (!InputMap.HasAction(Action))
        {
            InputMap.AddAction(Action);
            InputMap.ActionAddEvent(Action, new InputEventKey { PhysicalKeycode = ActivationKey });
        }
        if (_run != null) { _run.CharacterAbilityActivated += Apply; }
        AddChild(new CharacterAbilityPanel { KeyLabel = OS.GetKeycodeString(ActivationKey) });
    }
    public override void _ExitTree() { if (_run != null) { _run.CharacterAbilityActivated -= Apply; } }
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(Action) && _body?.InputEnabled == true && _body.Health?.Dead != true)
        {
            if (_run?.TryUseCharacterAbility() != true) { GameEvents.PublishHint("Ability is unavailable or still recovering."); }
            GetViewport().SetInputAsHandled();
        }
    }
    private void Apply(CharacterStats stats) { _stamina?.Restore(_stamina.EffectiveMax * stats.RestoreStaminaFraction); }
}
