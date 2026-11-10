using DownhillRoguelite.Narrative;

namespace DownhillRoguelite.Debug;

/// <summary>A do-nothing host for the president lab: a fresh save in memory, no quests, no shop.</summary>
public sealed class PresidentLabHost : IPresidentHost
{
    public PresidentLabHost(TextTable text)
    {
        Text = text;
    }

    public PresidentSave Save { get; } = new(false);
    public TextTable Text { get; }
    public bool LowHealth => false;
    public bool HeavyLoad => false;
    public bool Late => true;
    public bool DeathGreetingPending => false;
    public bool ReturnGreetingPending => false;
    public bool ContractorsReminderPending => false;
    public bool VictoryTorn => false;
    public int ShopCount => 0;
    public bool PodiumUsed => false;

    public void MarkDeathGreeted()
    {
    }

    public void MarkReturnGreeted()
    {
    }

    public void MarkContractorsReminded()
    {
    }

    public void MarkMet()
    {
    }

    public void AcceptResults()
    {
    }

    public void HandInResults()
    {
    }

    public void FinishResults(string ending)
    {
    }

    public void AcceptContractors()
    {
    }

    public void FinishContractors()
    {
    }

    public bool AcceptVictory() => false;

    public void FinishVictory()
    {
    }

    public void Rest()
    {
    }

    public string ShopTextId(int index) => "";

    public int ShopCost(int index) => 0;

    public bool Buy(int index) => false;

    public void Podium(bool speak)
    {
    }

    public void FinishSecret()
    {
    }
}
