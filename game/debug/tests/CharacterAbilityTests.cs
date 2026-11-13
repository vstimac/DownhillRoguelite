using System;
using System.Linq;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;
namespace DownhillRoguelite.Debug;

public partial class CharacterAbilityTests : Node3D
{
    private int _failures;
    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GameOptions.UseFile("user://character-ability-tests.cfg");
        RunState run = GetNode<RunState>("/root/RunState");
        PlayerController player = GetNode<PlayerController>("World/Player");
        CarrierStamina stamina = player.Stamina!;
        CarrierHealth health = player.Health!;
        for (int i = 0; i < 5; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        run.SetProcess(false);
        CarrierCharacter[] profiles = { GD.Load<CarrierCharacter>("res://art/characters/quaternius_ubc/modular.tres"),
            GD.Load<CarrierCharacter>("res://art/characters/quaternius_ubc/presets/carrier_foreman.tres"), GD.Load<CarrierCharacter>("res://art/characters/quaternius_ubc/presets/carrier_winter.tres") };
        Check("all roster profiles have valid and distinct abilities", profiles.All(profile => profile.Stats?.Valid() == true)
            && profiles.Select(profile => profile.Stats!.AbilityName).Distinct().Count() == profiles.Length);
        run.BeginCharacterRun("modular"); health.Refill(); stamina.Refill();
        health.TakeDamage(50, "test"); stamina.SetCurrent(50);
        run.BeginCharacterRun("carrier_foreman");
        Check("the Foreman has distinct health and stamina capacities", Near(health.EffectiveMax, 110) && Near(stamina.EffectiveMax, 90));
        Check("changing origin preserves health and stamina fractions without refilling", Near(health.Current, 55) && Near(stamina.Current, 45));
        float baseCost = stamina.WalkDrainPerSecond(0, 1, false, false);
        Check("Iron Resolve activates its unique temporary effects", run.TryUseCharacterAbility() && run.Character.Active
            && Near(stamina.WalkDrainPerSecond(0, 1, false, false), baseCost * 0.65f) && Near(run.Character.ImpactFactor, 0.8f));
        float beforeSpend = stamina.Current;
        Check("active ability applies to fixed stamina costs once", stamina.TrySpend(10) && Near(beforeSpend - stamina.Current, 6.5f));
        Check("cooldown prevents repeated activation", !run.TryUseCharacterAbility() && Near(run.Character.CooldownLeft, 60));
        GameOptions.SetCharacter("modular");
        Check("changing saved appearance cannot swap a live origin or reset its cooldown", run.Character.Id == "carrier_foreman"
            && run.Character.Active && Near(run.Character.CooldownLeft, 60) && Near(health.EffectiveMax, 110));
        run.SetProcess(true);
        float pausedCooldown = run.Character.CooldownLeft;
        GetTree().Paused = true;
        for (int i = 0; i < 3; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        Check("pausing freezes ability clocks and blocks activation", Near(run.Character.CooldownLeft, pausedCooldown) && !run.TryUseCharacterAbility());
        GetTree().Paused = false;
        run.SetProcess(false);
        run.Character.Tick(8);
        Check("temporary effects expire without changing permanent stats", !run.Character.Active && Near(run.Character.ImpactFactor, 1)
            && Near(stamina.WalkDrainPerSecond(0, 1, false, false), baseCost) && Near(stamina.EffectiveMax, 90));
        run.BeginCharacterRun("modular"); stamina.SetCurrent(0);
        Check("Restore 40% stamina restores stamina and clears reachable exhaustion", run.TryUseCharacterAbility() && Near(stamina.Current, 40) && !stamina.Exhausted);
        run.Character.Tick(74);
        Check("ability remains unavailable until its full cooldown ends", !run.TryUseCharacterAbility());
        run.Character.Tick(1);
        Check("ability becomes usable exactly at cooldown completion", run.TryUseCharacterAbility() && Near(stamina.Current, 80));
        run.BeginCharacterRun("modular"); stamina.SetCurrent(20);
        Input.ParseInputEvent(new InputEventAction { Action = CharacterAbility.Action, Pressed = true });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventAction { Action = CharacterAbility.Action, Pressed = false });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check("the bound ability input reaches the real player effect", Near(stamina.Current, 60) && Near(run.Character.CooldownLeft, 75));
        run.BeginCharacterRun("carrier_foreman");
        GameEvents.PublishPackingMode(true);
        Check("packing blocks ready abilities without consuming cooldown", !run.TryUseCharacterAbility() && run.Character.CooldownLeft == 0);
        GameEvents.PublishPackingMode(false);
        run.Modifiers.WalkSpeed = 1.2f;
        Check("character movement stacks with boons once in actual speed and ETA source", Near(player.WalkingSpeed(0), player.Settings.StrideSpeed * 1.2f * 1.05f));
        run.BeginCharacterRun("carrier_foreman");
        Check("selecting the same profile cannot stack its stats", Near(health.EffectiveMax, 110) && Near(stamina.EffectiveMax, 90));
        run.BeginCharacterRun("plenki");
        Check("a missing character model remains unavailable", run.Character.Id != "plenki" && !CarrierRoster.Load()!.Available().Any(profile => profile.Id == "plenki"));
        run.BeginCharacterRun("carrier_foreman");
        health.TakeDamage(1000, "test");
        Check("dead characters cannot use a ready ability", run.RunOver && !run.TryUseCharacterAbility());
        run.StartNewRun(1);
        Check("new runs clear cooldowns and temporary effects", !run.RunOver && !run.Character.Active && run.Character.CooldownLeft == 0);
        run.SetProcess(true);
        GD.Print("CHARACTER ABILITY TESTS failures: " + _failures);
        GameOptions.UseFile(GameOptions.DefaultPath);
        GetTree().Quit(Math.Min(_failures, 1));
    }
    private static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) < 0.002f;
    private void Check(string name, bool pass)
    {
        if (pass) { GD.Print("PASS  " + name); }
        else { GD.Print("FAIL  " + name); _failures++; }
    }
}
