using DownhillRoguelite.Core;

namespace DownhillRoguelite.Progression;

/// <summary>
/// One contract in one run: which job, from which station to which, and where it stands. Its id
/// is unique within the run and is also its cargo's id (ItemDefinition.CargoInstanceId). Only
/// ContractResolver changes Status.
/// </summary>
public sealed class ContractInstance
{
    public ContractInstance(ContractDefinition definition, int fromStation, string cargoName, float cargoMassKg)
    {
        Definition = definition;
        FromStation = fromStation;
        ToStation = fromStation + System.Math.Max(definition.StationsAhead, 1);
        InstanceId = definition.Id + "@" + fromStation;
        CargoName = cargoName;
        CargoMassKg = cargoMassKg;
    }

    public ContractDefinition Definition { get; }
    public string InstanceId { get; }
    public int FromStation { get; }
    public int ToStation { get; }
    public string CargoName { get; }
    public float CargoMassKg { get; }
    public ContractStatus Status { get; set; } = ContractStatus.Offered;
    /// <summary>Score credited when it was delivered.</summary>
    public int ScoreAwarded { get; set; }
    /// <summary>Why it ended badly, in words.</summary>
    public string Reason { get; set; } = "";
    /// <summary>The cargo's condition, 100 (intact) to 0 (broken). Only ContractResolver lowers it.</summary>
    public float Condition { get; set; } = 100.0f;
    /// <summary>World-clock hour (WorldClock.TotalHours) it must be delivered by; negative: no deadline.</summary>
    public double DeadlineHours { get; set; } = -1.0;

    public bool IsOpen => Status == ContractStatus.Offered || Status == ContractStatus.Accepted;

    /// <summary>What the rest of the game may know about it.</summary>
    public ContractNote Note()
    {
        return new ContractNote(InstanceId, Definition.DisplayName, CargoName, CargoMassKg, FromStation, ToStation, Status, ScoreAwarded, Reason,
            Condition, Definition.MinCondition, DeadlineHours, Definition.Fragility > 0.0f);
    }
}
