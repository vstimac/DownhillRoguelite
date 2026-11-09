using System;
using System.Linq;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Progression;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Score conservation and delivery checks at the actual clock boundary, between polls.</summary>
public partial class EconomyTests : Node3D
{
    private int _failures;

    public override void _Ready()
    {
        RunState run = GetNode<RunState>("/root/RunState");
        WorldClock clock = GetNode<WorldClock>("/root/WorldClock");
        clock.Paused = true;
        run.Route = new MathTestRoute();
        CarrierInventory inventory = new()
        {
            Name = "Inventory",
            Backpack = GD.Load<BackpackDefinition>("res://inventory/default_backpack.tres"),
            StartingItemPaths = Array.Empty<string>(),
        };
        AddChild(inventory);
        ContractDefinition definition = new()
        {
            Id = "economy_boundary", DisplayName = "A timely delivery",
            CargoItemPath = "res://inventory/items/sealed_letter.tres", DeadlineDays = 1.0f,
            Score = 30,
        };
        ContractDirector director = new() { Contracts = new[] { definition } };
        AddChild(director);
        ContractInstance onTime = director.Offer(1)!;
        Check("boundary test accepts real cargo", director.Accept(onTime) == ContractResult.Ok);
        clock.AdvanceHours((float)(onTime.DeadlineHours - clock.TotalHours));
        Check("delivery exactly at the deadline remains valid", director.CanDeliverAt(onTime.ToStation) == ContractResult.Ok
            && director.Deliver(onTime.ToStation) == ContractResult.Ok && onTime.Status == ContractStatus.Delivered);
        ContractInstance late = director.Offer(2)!;
        director.Accept(late);
        clock.AdvanceHours((float)(late.DeadlineHours - clock.TotalHours) + 0.01f);
        int before = run.Telemetry.CargoScore;
        Check("delivery preview rejects a late job before the next poll", director.CanDeliverAt(late.ToStation) != ContractResult.Ok
            && late.Status == ContractStatus.Accepted);
        Check("late delivery fails once without taking cargo or paying", director.Deliver(late.ToStation) != ContractResult.Ok
            && late.Status == ContractStatus.Failed && run.Telemetry.CargoScore == before
            && GameEvents.CarriedCargo.Contains(late.InstanceId));
        Check("repeating late delivery cannot count another failure", director.Deliver(late.ToStation) != ContractResult.Ok
            && run.Telemetry.ContractsFailed == 1);

        RunTelemetry telemetry = new();
        ContractNote pending = Note("pending", ContractStatus.Accepted, 50);
        Check("an unresolved note cannot consume a contract identity", !telemetry.RecordResolved(pending)
            && telemetry.RecordResolved(pending with { Status = ContractStatus.Delivered })
            && telemetry.ContractsDelivered == 1 && telemetry.ContractsFailed == 0 && telemetry.CargoScore == 50);
        telemetry = new();
        telemetry.RecordAccepted(Note("", ContractStatus.Accepted, 0));
        telemetry.RecordAccepted(Note("offered", ContractStatus.Offered, 0));
        Check("only identified accepted contracts count as accepted", telemetry.ContractsAccepted == 0);
        Check("empty identities cannot claim rewards", !telemetry.RecordResolved(Note("", ContractStatus.Delivered, 100))
            && !telemetry.RecordRelic(new RelicNote("", "", "", false, Score: 100)) && telemetry.CargoScore == 0);
        telemetry.RecordResolved(Note("large", ContractStatus.Delivered, int.MaxValue - 10));
        telemetry.RecordResolved(Note("extra", ContractStatus.Delivered, 30));
        Check("contract score saturates instead of wrapping", telemetry.CargoScore == int.MaxValue);
        telemetry.RecordRelic(new RelicNote("relic", "Relic", "", false, Score: 100));
        Check("relic rewards preserve a saturated cargo score", telemetry.CargoScore == int.MaxValue);
        Check("duplicate final rewards remain idempotent", !telemetry.RecordResolved(Note("extra", ContractStatus.Delivered, 30))
            && !telemetry.RecordRelic(new RelicNote("relic", "Relic", "", false, Score: 100))
            && telemetry.ContractsDelivered == 2 && telemetry.RelicsDelivered == 1);
        Check("total run score saturates across depth stations and cargo", run.ScoreOf(1000, 1, int.MaxValue - 10) == int.MaxValue
            && run.ScoreOf(0, int.MaxValue, 0) == int.MaxValue);
        Check("normal run score and negative inputs retain their rules", run.ScoreOf(1000, 2, 30) == 170
            && run.ScoreOf(-100, -2, -30) == 0);
        definition.Score = int.MaxValue;
        ContractInstance damaged = new(definition, 3, "Letter", 0.1f);
        ContractResolver.Accept(damaged);
        ContractResolver.Damage(damaged, 40.0f);
        int expected = (int)Math.Round((double)int.MaxValue * 60.0 / 100.0);
        Check("proportional rewards retain integer precision at large values", ContractResolver.PayFor(damaged, 90.0f) == expected);
        GD.Print("ECONOMY TESTS failures: " + _failures);
        GetTree().Quit(Math.Min(_failures, 1));
    }

    private static ContractNote Note(string id, ContractStatus status, int score)
    {
        return new ContractNote(id, "Job", "Cargo", 1.0f, 1, 2, status, score, "");
    }

    private void Check(string name, bool passed)
    {
        if (passed) { GD.Print("PASS  " + name); }
        else { GD.Print("FAIL  " + name); _failures++; }
    }
}
