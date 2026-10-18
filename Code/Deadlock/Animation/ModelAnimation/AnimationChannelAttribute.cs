using System;
using System.Numerics;
namespace Deadlock.Animation.ModelAnimation
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;
    using AnimationGraph = Deadlock.Animation.ModelAnimation2.AnimationGraph;

    /// <summary>
    /// Specifies the type of data contained in an animation channel.
    /// Derived from the <c>m_szChannelClass</c> and <c>m_szVariableName</c> fields of the channel description.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animationsystem/CAnimDataChannelDesc">CAnimDataChannelDesc</seealso>
    public enum AnimationChannelAttribute
    {
        /// <summary>Channel attribute is not recognized by VRF. The value an unset channel reads as.</summary>
        Unknown = 0,

        /// <summary>Channel encodes bone position (translation) data.</summary>
        Position = 1,

        /// <summary>Channel encodes bone rotation (orientation) data.</summary>
        Angle = 2,

        /// <summary>Channel encodes bone scale data.</summary>
        Scale = 3,

        /// <summary>Channel encodes flex controller (morph) data.</summary>
        Data = 4,

        /// <summary>
        /// Channel encodes a named value from the decode key's <c>m_userArray</c>, for example a
        /// <c>MATERIAL_ATTRIBUTE:</c> shader parameter driven by the sequence.
        /// </summary>
        User = 5,
    }
}
