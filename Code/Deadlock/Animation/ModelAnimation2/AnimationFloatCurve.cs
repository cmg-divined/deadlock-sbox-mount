using System;
using System.Numerics;
namespace Deadlock.Animation.ModelAnimation2
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>
    /// A named per-frame scalar channel of an animation clip (the clip's float curves, e.g. graph
    /// parameters like <c>health</c> or <c>sitting_pose</c>).
    /// </summary>
    public sealed class AnimationFloatCurve
    {
        /// <summary>
        /// Gets the curve name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the decoded curve value for each frame of the clip.
        /// </summary>
        public float[] Values { get; }

        internal AnimationFloatCurve(string name, float[] values)
        {
            Name = name;
            Values = values;
        }
    }
}
