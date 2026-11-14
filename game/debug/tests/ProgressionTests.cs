using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using DownhillRoguelite.Progression;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for M12's progression: a station offers three different boons, the same for the
/// same mountain; choosing one applies it for the run and takes the others away; boons really
/// change the carrier (walking cost, cold); a new run clears boons and offerings; deeper circles
/// drain needs faster; and the best depth ever unlocks gear the next run starts with.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/ProgressionTests.tscn
/// </summary>
public partial class ProgressionTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public BoonOfferings? Offerings { get; set; }
    [Export] public StartingKit? Kit { get; set; }

    private readonly List<string> _failures = new();
    private RunState? _run;

    public override async void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        CarrierInventory? inventory = PlayerBody?.GetNodeOrNull<CarrierInventory>("Inventory");
        CarrierNeeds? needs = PlayerBody?.GetNodeOrNull<CarrierNeeds>("Needs");
        if (PlayerBody?.Stamina == null || Streamer == null || Offerings == null || Kit == null || _run?.Route == null || inventory == null || needs == null)
        {
            GD.PushError("ProgressionTests: PlayerBody (with Inventory, Needs, Stamina), Streamer, Offerings, Kit and RunState.Route are needed.");
            GetTree().Quit(1);
            return;
        }
        _run.SummaryDelay = 1.0f;
        await WaitSeconds(1.0f);

        TestPicks(Offerings);
        TestBargains(Offerings);
        TestConditionalBoons(_run);
        await TestOfferingAndChoice(PlayerBody, Offerings, _run.Route);
        TestEffects(PlayerBody.Stamina);
        await TestCold(needs);
        await TestNewRunClears(Offerings);
        await TestDepthScaling(needs);
        await TestStartingKit(inventory, Kit);

        if (_failures.Count == 0)
        {
            GD.Print("PROGRESSION TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"PROGRESSION TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void TestPicks(BoonOfferings offerings)
    {
        List<BoonDefinition> first = offerings.Pick(1);
        List<BoonDefinition> again = offerings.Pick(1);
        HashSet<string> distinct = new();
        bool same = first.Count == again.Count;
        for (int i = 0; i < first.Count; i++)
        {
            distinct.Add(first[i].Id);
            same = same && first[i] == again[i];
        }
        HashSet<string> across = new();
        for (int station = 1; station <= 8; station++)
        {
            foreach (BoonDefinition boon in offerings.Pick(station))
            {
                across.Add(boon.Id);
            }
        }
        Check("a station offers three different boons, the same for the same mountain",
            first.Count == offerings.OfferCount && distinct.Count == first.Count && same && across.Count > first.Count,
            $"{first.Count} offered ({string.Join(", ", distinct)}), {across.Count} different boons over 8 stations");
    }

    /// <summary>Bargains: a price shown apart from the gift; from station 2 the middle offering is one, the others unchanged.</summary>
    private void TestBargains(BoonOfferings offerings)
    {
        BoonDefinition mule = GD.Load<BoonDefinition>("res://progression/boons/mules_bargain.tres");
        BoonDefinition goat = GD.Load<BoonDefinition>("res://progression/boons/goats_hooves.tres");
        BoonDefinition plain = offerings.Boons[0];
        bool described = mule.IsBargain && !plain.IsBargain
            && mule.Gifts() == "The load costs 40% less stamina" && mule.Costs() == "Walk 8% slower"
            && goat.Gifts() == "Grip +30%" && goat.Costs() == "Stamina recovers 15% slower"
            && plain.Costs() == "" && plain.Gifts() == plain.Effects();

        List<BoonDefinition> before1 = offerings.Pick(1);
        List<BoonDefinition> before3 = offerings.Pick(3);
        BoonDefinition[] saved = offerings.Bargains;
        offerings.Bargains = new[] { mule, goat };
        List<BoonDefinition> first = offerings.Pick(1);
        List<BoonDefinition> third = offerings.Pick(3);
        List<BoonDefinition> thirdAgain = offerings.Pick(3);
        offerings.Bargains = saved;
        bool firstPlain = first.Count == before1.Count;
        for (int i = 0; i < first.Count; i++)
        {
            firstPlain = firstPlain && first[i] == before1[i];
        }
        int middle = third.Count / 2;
        bool thirdBargain = third.Count == before3.Count && third[middle].IsBargain && third[middle] == thirdAgain[middle];
        for (int i = 0; i < third.Count; i++)
        {
            if (i != middle)
            {
                thirdBargain = thirdBargain && third[i] == before3[i];
            }
        }
        Check("a bargain shows its gift and its price apart; from station 2 the middle offering is a bargain, the rest unchanged",
            described && firstPlain && thirdBargain,
            $"mule: '{mule.Gifts()}' / '{mule.Costs()}', goat: '{goat.Gifts()}' / '{goat.Costs()}'; station 1 unchanged {firstPlain}, station 3: {string.Join(", ", third.ConvertAll(b => b.Id))}");
    }

    /// <summary>Beta 3.3: a conditional boon works only in its situation, and a save never holds its effect twice.</summary>
    private void TestConditionalBoons(RunState run)
    {
        BoonDefinition owl = GD.Load<BoonDefinition>("res://progression/boons/owls_pace.tres");
        BoonDefinition pride = GD.Load<BoonDefinition>("res://progression/boons/carriers_pride.tres");
        BoonDefinition storm = GD.Load<BoonDefinition>("res://progression/boons/storm_born.tres");
        WeatherSnapshot clear = WeatherSnapshot.Calm;
        WeatherSnapshot snow = WeatherSnapshot.Calm with { Kind = WeatherKind.Snow, Snow = 0.8f };
        float walk = run.Modifiers.WalkSpeed;
        float load = run.Modifiers.LoadDrain;
        float grip = run.Modifiers.Grip;
        bool described = owl.IsConditional && owl.Gifts() == "At night: Balance +10% · Walk 12% faster" && !owl.IsBargain
            && pride.Gifts().StartsWith("While you carry a job: ") && storm.Gifts().StartsWith("In rain or snow: ");

        run.AddConditionalBoon(owl);
        run.AddConditionalBoon(pride);
        run.AddConditionalBoon(storm);
        run.AddConditionalBoon(owl);
        int listed = run.ConditionalBoons.Count;
        run.UpdateConditionalBoons(false, false, clear);
        bool dayOff = Mathf.IsEqualApprox(run.Modifiers.WalkSpeed, walk) && Mathf.IsEqualApprox(run.Modifiers.LoadDrain, load);
        run.UpdateConditionalBoons(true, false, clear);
        run.UpdateConditionalBoons(true, false, clear);
        bool nightOn = Mathf.IsEqualApprox(run.Modifiers.WalkSpeed, walk * 1.12f) && Mathf.IsEqualApprox(run.Modifiers.LoadDrain, load);
        run.UpdateConditionalBoons(true, true, snow);
        bool allOn = run.ActiveBoons.Count == 3 && Mathf.IsEqualApprox(run.Modifiers.LoadDrain, load * 0.75f) && Mathf.IsEqualApprox(run.Modifiers.Grip, grip * 1.25f);

        RunSave save = new();
        run.CaptureInto(save);
        bool savedBase = Mathf.IsEqualApprox(save.Modifiers["WalkSpeed"], walk) && Mathf.IsEqualApprox(save.Modifiers["Grip"], grip)
            && save.ConditionalBoons.Count == 3 && save.ConditionalBoons.Contains("res://progression/boons/owls_pace.tres");

        run.UpdateConditionalBoons(false, false, clear);
        bool allOff = run.ActiveBoons.Count == 0 && Mathf.IsEqualApprox(run.Modifiers.WalkSpeed, walk)
            && Mathf.IsEqualApprox(run.Modifiers.LoadDrain, load) && Mathf.IsEqualApprox(run.Modifiers.Grip, grip);

        // Continue from the save taken at night in a snowstorm with a job: nothing is on until the situation is checked, then once.
        run.UpdateConditionalBoons(true, true, snow);
        run.RestoreFrom(save);
        bool restored = run.ConditionalBoons.Count == 3 && run.ActiveBoons.Count == 0 && Mathf.IsEqualApprox(run.Modifiers.WalkSpeed, walk);
        run.UpdateConditionalBoons(true, true, snow);
        bool once = Mathf.IsEqualApprox(run.Modifiers.WalkSpeed, walk * 1.12f) && Mathf.IsEqualApprox(run.Modifiers.Grip, grip * 1.25f);

        run.UpdateConditionalBoons(false, false, clear);
        run.ConditionalBoons.Clear();
        Check("a conditional boon works only in its situation, and a save taken while it works holds it once, not twice",
            described && listed == 3 && dayOff && nightOn && allOn && savedBase && allOff && restored && once,
            $"owl '{owl.Gifts()}'; listed {listed}; day off {dayOff}, night on {nightOn}, all on {allOn}; saved without it {savedBase}; all off {allOff}; restored {restored}, applied once {once}");
    }

    private async Task TestOfferingAndChoice(PlayerController player, BoonOfferings offerings, IRouteQuery route)
    {
        Vector3 station = route.Station(1);
        await Place(player, station + new Vector3(0.0f, 0.0f, 2.0f), 1.0f);
        int laid = offerings.Stones.Count;
        float farthest = 0.0f;
        foreach (BoonStone stone in offerings.Stones)
        {
            farthest = Mathf.Max(farthest, stone.GlobalPosition.DistanceTo(station));
        }
        // No label runs over another: under each name the effect and price stack downward, and the
        // stones stand further apart than a label is wide.
        bool stacked = true;
        string stacks = "";
        float closest = float.MaxValue;
        for (int i = 0; i < offerings.Stones.Count; i++)
        {
            (float nameBottom, float effectTop, float effectBottom, float lowest) = offerings.Stones[i].LabelStack();
            stacked = stacked && effectTop < nameBottom && effectBottom < effectTop && lowest <= effectBottom && lowest > 0.0f;
            stacks += $" [{nameBottom:0.00}/{effectTop:0.00}-{effectBottom:0.00}/{lowest:0.00}]";
            for (int j = i + 1; j < offerings.Stones.Count; j++)
            {
                closest = Mathf.Min(closest, offerings.Stones[i].GlobalPosition.DistanceTo(offerings.Stones[j].GlobalPosition));
            }
        }
        Check("the offerings' labels stack under their names and don't reach the next stone",
            stacked && closest > BoonStone.LabelWidthMetres,
            $"stacks{stacks}, stones {closest:0.00} m apart, labels {BoonStone.LabelWidthMetres:0.00} m wide");
        BoonStone? lightStep = null;
        foreach (BoonStone stone in offerings.Stones)
        {
            if (lightStep == null || stone.Boon?.WalkDrain < lightStep.Boon?.WalkDrain)
            {
                lightStep = stone;
            }
        }
        bool chosen = lightStep != null && lightStep.Interact(player);
        await WaitSeconds(0.2f);
        string name = lightStep?.Boon?.DisplayName ?? "";
        Check("reaching a station lays out its offerings; choosing one keeps it and takes the rest away",
            laid == offerings.OfferCount && farthest < 8.0f && chosen && offerings.Stones.Count == 0 && _run!.Boons.Contains(name),
            $"{laid} offerings within {farthest:0.0} m of the fire, chose {name}: {chosen}, left {offerings.Stones.Count}, boons [{string.Join(", ", _run!.Boons)}]");
    }

    private void TestEffects(CarrierStamina stamina)
    {
        RunModifiers modifiers = _run!.Modifiers;
        float with = stamina.WalkDrainPerSecond(20.0f, 0.2f, false, false);
        float saved = modifiers.WalkDrain;
        modifiers.WalkDrain = 1.0f;
        float without = stamina.WalkDrainPerSecond(20.0f, -0.2f, false, false);
        modifiers.WalkDrain = saved;
        bool lighter = saved >= 1.0f || with < without * 0.99f;
        modifiers.WalkDrain = 0.85f;
        float eased = stamina.WalkDrainPerSecond(20.0f, -0.2f, false, false);
        modifiers.WalkDrain = saved;
        Check("a boon changes what walking costs", lighter && Mathf.Abs(eased - without * 0.85f) < 0.001f,
            $"walking with 20 kg: {without:0.00}/s plain, {eased:0.00}/s with Light Step (chosen boon: {with:0.00}/s)");
    }

    private async Task TestCold(CarrierNeeds needs)
    {
        WorldClock clock = GetNode<WorldClock>("/root/WorldClock");
        clock.SetTimeOfDay(1.0f);
        Vector3 away = _run!.Route!.RoutePoint(_run.Route.Station(1).Z - 60.0f);
        await Place(PlayerBody!, away, 0.3f);
        float cold = needs.Cold;
        _run.Modifiers.ColdResistance = 0.5f;
        await WaitSeconds(0.2f);
        float resisted = needs.Cold;
        _run.Modifiers.ColdResistance = 0.0f;
        clock.SetTimeOfDay(10.0f);
        Check("cold resistance keeps the night's cold off", cold > 0.3f && Mathf.Abs(resisted - cold * 0.5f) < 0.05f,
            $"night cold {cold:0.00}, with half resisted {resisted:0.00}");
    }

    private async Task TestNewRunClears(BoonOfferings offerings)
    {
        offerings.Offer(2);
        bool laid = offerings.Stones.Count > 0;
        _run!.StartNewRun(_run.Seed);
        await WaitSeconds(0.3f);
        RunModifiers m = _run.Modifiers;
        bool reset = m.WalkDrain == 1.0f && m.Balance == 1.0f && m.ColdResistance == 0.0f && _run.Boons.Count == 0;
        Check("a new run clears boons and offerings", laid && offerings.Stones.Count == 0 && reset,
            $"offerings before {laid}, after {offerings.Stones.Count}, modifiers reset {reset}, boons {_run.Boons.Count}");
    }

    private async Task TestDepthScaling(CarrierNeeds needs)
    {
        float top = await ThirstLoss(needs, 0);
        int deep = Mathf.CeilToInt(_run!.Circles.DepthPerCircle * 4.0f) + 10;
        float lower = await ThirstLoss(needs, deep);
        _run.Depth = 0;
        float expected = 1.0f + _run.DifficultySettings.NeedsDrainPerCircle * 4.0f;
        Check("deeper circles drain food and water faster", lower > top * (expected - 0.08f),
            $"thirst lost {top:0.00} in Limbo vs {lower:0.00} four circles down (expected ×{expected:0.00})");
    }

    private async Task<float> ThirstLoss(CarrierNeeds needs, int depth)
    {
        WorldClock clock = GetNode<WorldClock>("/root/WorldClock");
        needs.Refill();
        _run!.Depth = depth;
        await WaitSeconds(0.1f);
        float before = needs.Thirst;
        clock.SetTimeOfDay(10.0f);
        clock.DebugTimeScale = 60.0f;
        await WaitSeconds(1.0f);
        clock.DebugTimeScale = 1.0f;
        clock.SetTimeOfDay(10.0f);
        return before - needs.Thirst;
    }

    private async Task TestStartingKit(CarrierInventory inventory, StartingKit kit)
    {
        bool emptyAtFirst = kit.Unlocked(0).Count == 0;
        int unlocked = kit.Unlocked(700).Count;
        _run!.Records.Add(700, 1);
        _run.StartNewRun();
        await WaitSeconds(0.5f);
        List<string> names = new();
        foreach (ItemDefinition item in inventory.AllItems)
        {
            names.Add(item.DisplayName);
        }
        Check("the best depth ever unlocks gear the next run starts with (besides the torch every run gets)",
            emptyAtFirst && unlocked == 2 && inventory.AllItems.Count == 3 && _run.StartingKit.Count == 3 && _run.StartingKit.Contains("Torch"),
            $"none at 0 m {emptyAtFirst}, {unlocked} unlocked at 700 m, new run starts with [{string.Join(", ", names)}], kit [{string.Join(", ", _run.StartingKit)}]");

        // An outfit preset starts the run in its clothes.
        CarrierCharacter? before = _run.Character.Definition;
        _run.StartNewRun();
        await WaitSeconds(0.5f);
        // (A headless new run always takes the neutral carrier; pick the preset as the start screen would.)
        _run.BeginCharacterRun("carrier_mountaineer");
        kit.Grant();
        await WaitSeconds(0.5f);
        HashSet<string> worn = new();
        foreach (EquipSlot slot in System.Enum.GetValues<EquipSlot>())
        {
            if (inventory.Equipment?.WornIn(slot) is ItemDefinition item)
            {
                worn.Add(item.ItemId);
            }
        }
        Check("the Mountaineer preset starts the run in its clothes",
            kit.PresetWorn == 5 && worn.Count == 5,
            $"put on {kit.PresetWorn}, worn [{string.Join(", ", worn)}]");
        _run.Character.Select(before);
    }

    private async Task Place(PlayerController player, Vector3 position, float settle)
    {
        player.Teleport(position + Vector3.Up * 0.1f, 0.0f);
        player.Health?.Refill();
        await WaitSeconds(settle);
    }

    private async Task WaitSeconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
