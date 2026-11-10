using DownhillRoguelite.Core;

namespace DownhillRoguelite.Progression;

/// <summary>
/// The rules of a contract's life, with no nodes and no side effects beyond the instance itself:
/// Offered → Accepted → Delivered, or → Failed / Abandoned. A final status never changes again,
/// so a contract can be credited only once. ContractDirector asks here before doing anything.
/// </summary>
public static class ContractResolver
{
    /// <summary>Whether the carrier may take <paramref name="contract"/> while working on <paramref name="active"/>.</summary>
    public static ContractResult CanAccept(ContractInstance? contract, ContractInstance? active)
    {
        if (contract == null)
        {
            return ContractResult.Invalid;
        }
        if (contract.Status == ContractStatus.Accepted)
        {
            return ContractResult.AlreadyAccepted;
        }
        if (!contract.IsOpen)
        {
            return ContractResult.AlreadyResolved;
        }
        if (active != null && active != contract)
        {
            return ContractResult.AnotherActive;
        }
        return ContractResult.Ok;
    }

    /// <summary>
    /// Marks it taken (its cargo is packed) at world-clock hour <paramref name="nowHours"/>, which
    /// starts its deadline if it has one; false if it couldn't be taken.
    /// </summary>
    public static bool Accept(ContractInstance contract, double nowHours = 0.0)
    {
        if (contract.Status != ContractStatus.Offered)
        {
            return false;
        }
        contract.Status = ContractStatus.Accepted;
        contract.Condition = 100.0f;
        contract.DeadlineHours = -1.0;
        if (contract.Definition.DeadlineDays > 0.0f)
        {
            contract.DeadlineHours = nowHours + contract.Definition.DeadlineDays * 24.0;
        }
        return true;
    }

    /// <summary>Whether it can be handed over at <paramref name="station"/>, the cargo being carried or not.</summary>
    public static ContractResult CanDeliver(ContractInstance? contract, int station, bool cargoCarried)
    {
        if (contract == null)
        {
            return ContractResult.Invalid;
        }
        if (!contract.IsOpen)
        {
            return ContractResult.AlreadyResolved;
        }
        if (contract.Status != ContractStatus.Accepted)
        {
            return ContractResult.NotAccepted;
        }
        if (station != contract.ToStation)
        {
            return ContractResult.WrongStation;
        }
        if (!cargoCarried)
        {
            return ContractResult.CargoMissing;
        }
        return ContractResult.Ok;
    }

    /// <summary>
    /// Settles a job whose cargo has been handed over: below its required condition it fails with
    /// nothing credited; otherwise it is delivered, paid in full when it arrives at least
    /// <paramref name="intactCondition"/>, and in proportion to its condition when damaged.
    /// False if it wasn't open and taken.
    /// </summary>
    public static bool Deliver(ContractInstance contract, float intactCondition = 90.0f)
    {
        if (contract.Status != ContractStatus.Accepted)
        {
            return false;
        }
        if (contract.Condition < contract.Definition.MinCondition)
        {
            contract.Status = ContractStatus.Failed;
            contract.Reason = "arrived too damaged (" + ConditionText(contract.Condition) + ", needed " + ConditionText(contract.Definition.MinCondition) + ")";
            contract.ScoreAwarded = 0;
            return true;
        }
        contract.Status = ContractStatus.Delivered;
        contract.ScoreAwarded = PayFor(contract, intactCondition);
        return true;
    }

    /// <summary>What delivering it now would pay: in full when intact, in proportion to its condition when damaged.</summary>
    public static int PayFor(ContractInstance contract, float intactCondition)
    {
        int score = System.Math.Max(contract.Definition.Score, 0);
        if (contract.Condition >= intactCondition)
        {
            return score;
        }
        return (int)System.Math.Round(score * (double)System.Math.Clamp(contract.Condition, 0.0f, 100.0f) / 100.0);
    }

    /// <summary>
    /// Knocks the carried cargo: its condition drops by <paramref name="amount"/> times its fragility
    /// (never below 0). Returns how much it dropped; nothing happens to a job that isn't taken.
    /// </summary>
    public static float Damage(ContractInstance contract, float amount)
    {
        if (contract.Status != ContractStatus.Accepted || amount <= 0.0f || contract.Definition.Fragility <= 0.0f)
        {
            return 0.0f;
        }
        float before = contract.Condition;
        contract.Condition = System.Math.Max(0.0f, before - amount * contract.Definition.Fragility);
        return before - contract.Condition;
    }

    /// <summary>Whether a taken job's deadline has passed at world-clock hour <paramref name="nowHours"/>.</summary>
    public static bool IsLate(ContractInstance contract, double nowHours)
    {
        return contract.Status == ContractStatus.Accepted && contract.DeadlineHours >= 0.0 && nowHours > contract.DeadlineHours;
    }

    /// <summary>"72%".</summary>
    public static string ConditionText(float condition)
    {
        return ((int)System.Math.Round(condition)).ToString() + "%";
    }

    /// <summary>Ends an open contract badly (Failed or Abandoned) with a reason; false if it had already ended.</summary>
    public static bool End(ContractInstance contract, ContractStatus ending, string reason)
    {
        if (!contract.IsOpen || ending == ContractStatus.Offered || ending == ContractStatus.Accepted || ending == ContractStatus.Delivered)
        {
            return false;
        }
        contract.Status = ending;
        contract.Reason = reason;
        contract.ScoreAwarded = 0;
        return true;
    }
}
