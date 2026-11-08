using DownhillRoguelite.Progression;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Repeated validation must keep a custom C# cargo resource valid across collection.</summary>
public partial class ContractResourceTests : Node
{
    private ContractDefinition? _definition;

    public override async void _Ready()
    {
        _definition = GD.Load<ContractDefinition>("res://progression/contracts/resources/ct_003_glass_truth.tres");
        int invalid = 0;
        for (int sample = 0; sample < 60; sample++)
        {
            string problem = _definition.Problem();
            if (problem.Length > 0)
            {
                GD.Print("FAIL  cargo resource after collection: " + problem);
                invalid++;
            }
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (invalid == 0)
        {
            GD.Print("PASS  contract cargo remains valid across 60 forced collections");
        }
        GetTree().Quit(Mathf.Min(invalid, 1));
    }
}
