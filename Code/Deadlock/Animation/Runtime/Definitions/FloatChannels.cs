using System;
using System.Numerics;
namespace Deadlock.Animation;


using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
using AnimationGraph = Deadlock.Animation.ModelAnimation2.AnimationGraph;
/// <summary>A named set of float animation channels.</summary>
[KV3Transfer]
public partial class FloatChannelSet
{
    /// <summary>The set ID.</summary>
    public StringToken ID { get; }
    /// <summary>The channels in the set.</summary>
    public StringToken[] ChannelIDs { get; } = [];
}
