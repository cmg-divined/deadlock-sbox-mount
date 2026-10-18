using System;
using System.Numerics;
namespace Deadlock.Animation;


using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
using AnimationGraph = Deadlock.Animation.ModelAnimation2.AnimationGraph;
/// <summary>A percentage in the 0 to 1 range.</summary>
[KV3Transfer]
public readonly partial struct Percent
{
    /// <summary>The percentage as a 0 to 1 fraction.</summary>
    public float Value { get; }
}
