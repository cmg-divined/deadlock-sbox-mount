using System;
using System.Numerics;
namespace Deadlock.Animation;


using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
using AnimationGraph = Deadlock.Animation.ModelAnimation2.AnimationGraph;
/// <summary>A named bone mask with a primary weight list and optional per-skeleton lists.</summary>
[KV3Transfer]
public partial class BoneMaskSetDefinition
{
    /// <summary>The mask ID.</summary>
    public StringToken ID { get; }
    /// <summary>The weights for the primary skeleton.</summary>
    public BoneWeightList PrimaryWeightList { get; }
    /// <summary>The weights for secondary skeletons.</summary>
    public BoneWeightList[] SecondaryWeightLists { get; } = [];
}

/// <summary>Per-bone weights of a bone mask for one skeleton.</summary>
[KV3Transfer]
public partial class BoneWeightList
{
    /// <summary>The skeleton the weights are for.</summary>
    public string SkeletonName { get; }
    /// <summary>The bones that have a weight.</summary>
    public StringToken[] BoneIDs { get; } = [];
    /// <summary>The weight of each listed bone.</summary>
    public float[] Weights { get; } = [];
}
