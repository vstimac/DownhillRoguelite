using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug scene helper (TestWildlife.tscn): stands one of every species in rows on flat ground
/// facing the carrier, so their scale, orientation and clips can be checked by eye. The spawner's
/// own spawning is off here. Shows a list of species with their circles. 1-3 switch rows' clips:
/// 1 idle, 2 walk in place, 3 flee from the carrier.
/// </summary>
public partial class WildlifeLineup : Node3D
{
    [Export] public WildlifeSpawner? Spawner { get; set; }
    [Export] public Camera3D? ViewCamera { get; set; }
    [Export] public float Spacing { get; set; } = 3.2f;
    [Export] public int PerRow { get; set; } = 10;
    [Export] public float RowDepth { get; set; } = 5.0f;

    public override void _Ready()
    {
        if (Spawner?.Table == null)
        {
            return;
        }
        WildlifeSpecies[] species = Spawner.Table.Species;
        for (int i = 0; i < species.Length; i++)
        {
            int row = i / PerRow;
            int column = i % PerRow;
            float width = (Mathf.Min(PerRow, species.Length - row * PerRow) - 1) * Spacing;
            Vector3 at = new(column * Spacing - width * 0.5f, 0.0f, -row * RowDepth);
            WildlifeAnimal animal = Spawner.Spawn(species[i], at);
            animal.Rotation = new Vector3(0.0f, Mathf.Pi, 0.0f);
            AddChild(new Label3D
            {
                Text = species[i].DisplayName,
                FontSize = 48,
                PixelSize = 0.008f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Position = at + new Vector3(0.0f, -0.1f, 1.2f),
                OutlineSize = 8,
            });
        }
        // A person-sized pole (1.8 m) at the left end of the first row for scale.
        float firstRowWidth = (Mathf.Min(PerRow, species.Length) - 1) * Spacing;
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 1.8f },
            Position = new Vector3(-firstRowWidth * 0.5f - Spacing, 0.9f, 0.0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.2f, 0.2f) },
        });
        CallDeferred(MethodName.TakeCamera);
    }

    private void TakeCamera()
    {
        ViewCamera?.MakeCurrent();
    }
}
