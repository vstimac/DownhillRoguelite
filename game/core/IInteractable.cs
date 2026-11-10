using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Something in the world the carrier can use with the interact key (pick up, later rest or talk).
/// Nodes implementing it join the "interactable" group so the player's interactor can find them.
/// </summary>
public interface IInteractable
{
    public const string Group = "interactable";

    /// <summary>World position used for range and facing checks.</summary>
    Vector3 InteractionPoint { get; }

    /// <summary>Shows or hides the in-world prompt when the carrier is close and facing it.</summary>
    void SetFocused(bool focused);

    /// <summary>Uses it; returns false if nothing happened (e.g. no room in the pack).</summary>
    bool Interact(Node3D interactor);
}
