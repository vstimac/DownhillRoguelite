using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug controls for the terrain scene: puts the carrier on the spine at the start, F2 builds a
/// new mountain from a random seed, and a carrier that ends up below the ground is put back on
/// the spine. Place it after the streamer and the player, and before DebugSceneControls (which
/// remembers the spawn point in its _Ready).
/// </summary>
public partial class TerrainDebugControls : Node
{
    private static readonly StringName ActionReseed = "debug_reseed";

    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public PlayerController? PlayerBody { get; set; }
    /// <summary>The carrier counts as fallen through when this far below the ground, in m.</summary>
    [Export] public float FallThroughMargin { get; set; } = 5.0f;
    /// <summary>Height above the ground the carrier is placed at, in m.</summary>
    [Export] public float PlaceLift { get; set; } = 0.1f;

    public override void _Ready()
    {
        PlaceAtSpawn();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(ActionReseed) && Streamer != null)
        {
            Streamer.Reseed(new SplitMix64((ulong)Time.GetTicksUsec()).Next());
            PlaceAtSpawn();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        TerrainGenerator? generator = Streamer?.Generator;
        if (generator == null || PlayerBody == null)
        {
            return;
        }

        Vector3 position = PlayerBody.GlobalPosition;
        if (position.Y < generator.Height(position.X, position.Z) - FallThroughMargin)
        {
            float x = generator.SpineX(position.Z);
            PlayerBody.Teleport(new Vector3(x, generator.Height(x, position.Z) + PlaceLift, position.Z), 0.0f);
        }
    }

    private void PlaceAtSpawn()
    {
        if (Streamer != null && PlayerBody != null)
        {
            PlayerBody.Teleport(Streamer.SpawnPoint + Vector3.Up * PlaceLift, 0.0f);
        }
    }
}
