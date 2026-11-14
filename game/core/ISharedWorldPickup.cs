namespace DownhillRoguelite.Core;

/// <summary>A shared world choice/job uses the same host reservation as physical loot.</summary>
public interface ISharedWorldPickup : IInteractable
{
    string SharedPickupKey { get; }
    bool RemoveWhenTaken => true;
    void SharedTaken() { }
}
