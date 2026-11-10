namespace DownhillRoguelite.Core;

/// <summary>
/// Something the carrier can look at that can describe itself for the UI's tooltip: what it is,
/// a line about it, its properties and what pressing the interact key does. IconSource is the
/// resource the UI turns into an icon (an item definition), or null.
/// </summary>
public interface IDescribed
{
    string Title { get; }
    string Description { get; }
    string Details { get; }
    string Prompt { get; }
    Godot.Resource? IconSource { get; }
}
