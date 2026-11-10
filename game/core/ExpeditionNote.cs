using Godot;

namespace DownhillRoguelite.Core;

/// <summary>A run's authored expedition, read by presentation without owning contracts or inventory.</summary>
public readonly record struct ExpeditionNote(string Title, string Instructions, Vector3 Target, string Progress);
