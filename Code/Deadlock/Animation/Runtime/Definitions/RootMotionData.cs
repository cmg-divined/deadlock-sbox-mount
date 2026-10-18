using System;
using System.Numerics;
namespace Deadlock.Animation;


using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
using AnimationGraph = Deadlock.Animation.ModelAnimation2.AnimationGraph;
using Transform = Deadlock.Animation.ModelAnimation.FrameBone;
[KV3Transfer]
public partial class RootMotionData
{
    public Transform[] Transforms { get; } = [];
    public int NumFrames { get; private set; }
    public float AverageLinearVelocity { get; private set; }
    public float AverageAngularVelocityRadians { get; private set; }
    public Transform TotalDelta { get; private set; }
}

public partial class RootMotionData
{
    public enum SamplingMode : byte
    {
        Delta = 0,
        WorldSpace = 1,
    }
}
