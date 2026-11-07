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

    /// <summary>Marks it taken (its cargo is packed); false if it couldn't be.</summary>
    public static bool Accept(ContractInstance contract)
    {
        if (contract.Status != ContractStatus.Offered)
        {
            return false;
        }
        contract.Status = ContractStatus.Accepted;
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

    /// <summary>Marks it delivered with its score (the cargo has been handed over); false if it couldn't be.</summary>
    public static bool Deliver(ContractInstance contract)
    {
        if (contract.Status != ContractStatus.Accepted)
        {
            return false;
        }
        contract.Status = ContractStatus.Delivered;
        contract.ScoreAwarded = System.Math.Max(contract.Definition.Score, 0);
        return true;
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
