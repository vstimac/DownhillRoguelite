using System.Collections.Generic;

namespace DownhillRoguelite.Core;

/// <summary>
/// Counters for one run that the run summary needs (owned by RunState, reset with every run):
/// contracts taken and how they ended, and the score earned with cargo. Each contract is counted
/// once when it ends, however many times its ending is announced.
/// </summary>
public sealed class RunTelemetry
{
    public int ContractsAccepted { get; private set; }
    public int ContractsDelivered { get; private set; }
    public int ContractsFailed { get; private set; }
    public int CargoScore { get; private set; }
    public int RelicsDelivered { get; private set; }

    private readonly HashSet<string> _accepted = new();
    private readonly HashSet<string> _resolved = new();
    private readonly HashSet<string> _relics = new();

    /// <summary>Counts a relic left at an altar (its score joins the cargo score); false if this relic was already counted.</summary>
    public bool RecordRelic(RelicNote note)
    {
        if (string.IsNullOrWhiteSpace(note.Id) || !_relics.Add(note.Id))
        {
            return false;
        }
        RelicsDelivered++;
        if (note.Score > 0)
        {
            CargoScore = (int)System.Math.Min(int.MaxValue, (long)CargoScore + note.Score);
        }
        return true;
    }

    public void RecordAccepted(ContractNote note)
    {
        if (note.Status == ContractStatus.Accepted && !string.IsNullOrWhiteSpace(note.InstanceId) && _accepted.Add(note.InstanceId))
        {
            ContractsAccepted++;
        }
    }

    /// <summary>Counts a contract's ending; false if this contract was already counted.</summary>
    public bool RecordResolved(ContractNote note)
    {
        if (note.Status != ContractStatus.Delivered && note.Status != ContractStatus.Failed && note.Status != ContractStatus.Abandoned)
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(note.InstanceId) || !_resolved.Add(note.InstanceId))
        {
            return false;
        }
        if (note.Status == ContractStatus.Delivered)
        {
            ContractsDelivered++;
            if (note.Score > 0)
            {
                CargoScore = (int)System.Math.Min(int.MaxValue, (long)CargoScore + note.Score);
            }
        }
        else
        {
            ContractsFailed++;
        }
        return true;
    }

    public void Reset()
    {
        ContractsAccepted = 0;
        ContractsDelivered = 0;
        ContractsFailed = 0;
        CargoScore = 0;
        RelicsDelivered = 0;
        _relics.Clear();
        _accepted.Clear();
        _resolved.Clear();
    }
}
