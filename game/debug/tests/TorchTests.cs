using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The torch: one from the pack catches in the left hand, lights the ground round the carrier, burns
/// its time and goes out; the fire it carries is made of flames, sparks and smoke.
/// </summary>
public partial class TorchTests : Node3D
{
    [Export] public PlayerController? Player { get; set; }

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        if (Player == null)
        {
            GD.PushError("TorchTests: Player is needed.");
            GetTree().Quit(1);
            return;
        }
        await Frames(20);
        CarrierTorch torch = Player.GetNode<CarrierTorch>("Torch");
        CarrierInventory inventory = Player.GetNode<CarrierInventory>("Inventory");
        ItemDefinition? item = ItemText.Find("torch");
        Check("the torch is an item that lights when used", item != null && item.UsableAlone && item.UseLightSeconds > 0.0f,
            $"burns {item?.UseLightSeconds ?? 0:0} s");

        // Lit from the pack: it is used up, catches, and burns in the hand.
        bool packed = item != null && inventory.TryAdd(item, -1.0f, false);
        GameEvents.RequestUseItem("torch");
        await Frames(30);
        bool stillPacked = false;
        foreach (PackedItem packedItem in inventory.Grid!.Items)
        {
            if (packedItem.Definition.ItemId == "torch")
            {
                stillPacked = true;
            }
        }
        Check("using a torch from the pack lights it and uses it up", packed && !stillPacked && torch.Lit && GameEvents.CarrierTorchLit,
            $"packed {packed}, still in the pack {stillPacked}, lit {torch.Lit}, left {torch.Left:0} s");

        Vector3 feet = Player.GlobalPosition;
        Vector3 at = torch.GlobalPosition;
        float height = at.Y - feet.Y;
        float aside = new Vector2(at.X - feet.X, at.Z - feet.Z).Length();
        Check("it is held in the hand, beside the body at hand height", height > 0.4f && height < 1.6f && aside < 0.9f,
            $"{height:0.00} m up, {aside:0.00} m out");

        ParticleFire? fire = torch.Fire;
        bool burning = fire != null && fire.Flames != null && fire.Flames.Emitting && fire.SparkParticles != null && fire.SmokeParticles != null
            && fire.Light != null && fire.Light.Visible && fire.Light.LightEnergy > 0.5f;
        Check("its fire has flames, sparks, smoke and a flickering light", burning,
            $"light {fire?.Light?.LightEnergy ?? 0:0.00}, range {fire?.Light?.OmniRange ?? 0:0.0} m");

        // It burns down and goes out.
        float before = torch.Left;
        torch.Left = 1.0f;
        await Seconds(1.5f);
        Check("it burns down and goes out", !torch.Lit && !GameEvents.CarrierTorchLit && fire?.Light?.Visible == false,
            $"was {before:0} s left, lit now {torch.Lit}");

        // A second torch adds its time to a burning one.
        GameEvents.RequestTorch(30.0f);
        GameEvents.RequestTorch(30.0f);
        await Frames(2);
        Check("another torch adds its time", torch.Left > 55.0f, $"{torch.Left:0} s");

        if (_failures.Count == 0)
        {
            GD.Print("TORCH TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"TORCH TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private async System.Threading.Tasks.Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async System.Threading.Tasks.Task Seconds(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
