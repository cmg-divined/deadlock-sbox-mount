using System;
using System.Numerics;
namespace Deadlock.Animation;


using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
using AnimationGraph = Deadlock.Animation.ModelAnimation2.AnimationGraph;
[KV3Transfer]
public readonly partial struct BitFlags
{
    [KVProperty("m_flags")]
    public uint Flags { get; }
}
