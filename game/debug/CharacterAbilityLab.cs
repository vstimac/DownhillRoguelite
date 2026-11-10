using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Ui;
using Godot;
namespace DownhillRoguelite.Debug;

public partial class CharacterAbilityLab : Node3D
{
    private RunState _run = null!;
    private PlayerController _player = null!;
    private Label _status = null!;
    public override void _Ready()
    {
        _run = GetNode<RunState>("/root/RunState");
        _player = GetNode<PlayerController>("World/Player");
        CanvasLayer ui = new(); AddChild(ui);
        ParchmentPanel paper = new() { Title = "Character abilities", Position = new Vector2(20, 70), Size = new Vector2(310, 500) };
        ui.AddChild(paper);
        VBoxContainer rows = new() { Position = new Vector2(18, 62), Size = new Vector2(274, 420) }; paper.AddChild(rows);
        foreach (CarrierCharacter character in CarrierRoster.Load()!.Available())
        {
            AddButton(rows, character.DisplayName, () => { _run.BeginCharacterRun(character.Id); _player.Health!.Refill(); });
        }
        AddButton(rows, "Spend 70% stamina", () => _player.Stamina!.SetCurrent(_player.Stamina.EffectiveMax * 0.3f));
        AddButton(rows, "Take 25 damage", () => _player.Health!.TakeDamage(25, "workshop"));
        AddButton(rows, "Use ability (H)", () => _run.TryUseCharacterAbility());
        _status = UiStyle.MakeLabel("", 16); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; rows.AddChild(_status);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--ability-demo") >= 0)
        {
            _run.BeginCharacterRun("vucic");
            _player.Stamina!.SetCurrent(_player.Stamina.EffectiveMax * 0.3f);
            _run.TryUseCharacterAbility();
        }
    }
    private static void AddButton(Node parent, string text, System.Action action)
    {
        InkButton button = new() { Text = text, Paper = true, FontSize = 16, CustomMinimumSize = new Vector2(270, 34) };
        button.Pressed += action; parent.AddChild(button);
    }
    public override void _Process(double delta)
    {
        if (_status == null) { return; }
        _status.Text = _run.Character.Stats.Summary() + $"\nLive HP {_player.Health!.Current:0}/{_player.Health.EffectiveMax:0} · Stamina {_player.Stamina!.Current:0}/{_player.Stamina.EffectiveMax:0}";
    }
}
