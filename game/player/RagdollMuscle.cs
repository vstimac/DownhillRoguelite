using Godot;

namespace DownhillRoguelite.Player;

/// <summary>One cached parent/child muscle; allocated once when the physical rig is built.</summary>
internal sealed class RagdollMuscle
{
    public PhysicalBone3D Child { get; }
    public PhysicalBone3D Parent { get; }
    public int ChildIndex { get; }
    public int ParentIndex { get; }
    public float Inertia { get; }
    public Quaternion Target { get; set; } = Quaternion.Identity;

    public RagdollMuscle(PhysicalBone3D child, PhysicalBone3D parent, int childIndex, int parentIndex, float inertia)
    {
        Child = child;
        Parent = parent;
        ChildIndex = childIndex;
        ParentIndex = parentIndex;
        Inertia = inertia;
    }
}
