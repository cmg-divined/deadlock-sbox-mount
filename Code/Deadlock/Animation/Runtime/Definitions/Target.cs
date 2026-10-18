using System;
using System.Numerics;
namespace Deadlock.Animation;


using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
using AnimationGraph = Deadlock.Animation.ModelAnimation2.AnimationGraph;
using Transform = Deadlock.Animation.ModelAnimation.FrameBone;
[KV3Transfer]
public partial struct Target
{
    /// <summary>
    /// Either the actual transform or the offsets that need to be applied
    /// </summary>
    public Transform Transform { get; set; }
    public StringToken BoneID { get; }
    public bool IsBoneTarget { get; }
    public bool IsUsingBoneSpaceOffsets { get; set; } = true;
    public bool HasOffsets { get; set; }
    public bool IsSet { get; set; }
}
