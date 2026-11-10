namespace DownhillRoguelite.Narrative;

/// <summary>
/// What the president's dialogue needs from the world: the save, the player's words, how the carrier
/// is doing, and the actions choices lead to. PresidentDirector implements it; tests can too.
/// </summary>
public interface IPresidentHost
{
    PresidentSave Save { get; }
    TextTable Text { get; }

    bool LowHealth { get; }
    bool HeavyLoad { get; }
    /// <summary>Deep in the run or after many returns: the president turns melancholic.</summary>
    bool Late { get; }
    /// <summary>The last run ended in a death and this run hasn't been greeted for it yet.</summary>
    bool DeathGreetingPending { get; }
    void MarkDeathGreeted();
    /// <summary>A visit after the first meeting whose first word hasn't been said yet.</summary>
    bool ReturnGreetingPending { get; }
    void MarkReturnGreeted();
    /// <summary>Quest 2 is under way but not ready, and he hasn't remarked on it this visit.</summary>
    bool ContractorsReminderPending { get; }
    void MarkContractorsReminded();

    void MarkMet();
    void AcceptResults();
    void HandInResults();
    void FinishResults(string ending);
    void AcceptContractors();
    void FinishContractors();
    /// <summary>Packs the 12 kg banner; false if it doesn't fit (the quest stays offered).</summary>
    bool AcceptVictory();
    bool VictoryTorn { get; }
    void FinishVictory();
    void Rest();

    int ShopCount { get; }
    string ShopTextId(int index);
    int ShopCost(int index);
    /// <summary>Buys a shop item with favour; false if too poor or no room.</summary>
    bool Buy(int index);

    bool PodiumUsed { get; }
    void Podium(bool speak);
    void FinishSecret();
}
