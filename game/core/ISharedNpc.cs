namespace DownhillRoguelite.Core;

public interface ISharedNpc
{
    public const string Group = "shared_npcs";
    string SharedNpcKey { get; }
    bool SharedNpcProxy { get; set; }
    SharedNpcFrame CaptureSharedNpc();
    void ApplySharedNpc(SharedNpcFrame frame, long author);
}
