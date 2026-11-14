namespace DownhillRoguelite.Network;

/// <summary>One reused receive slot; no growing pose history or per-sample frame allocation.</summary>
public sealed class NetPoseFrame
{
    public float[] Data { get; } = new float[NetPoseCodec.Size];
    public double Time { get; set; }
    public int Sequence { get; set; }
    public int Flags { get; set; }
}
