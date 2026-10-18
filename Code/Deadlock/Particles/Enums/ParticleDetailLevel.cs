using System;
using System.Numerics;
namespace Deadlock.Particles
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>
    /// The lowest particle detail tier a child system appears at.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleDetailLevel_t">ParticleDetailLevel_t</seealso>
    public enum ParticleDetailLevel
    {
        /// <summary>Always present.</summary>
        PARTICLEDETAIL_LOW = 0,
        /// <summary>Present from the medium tier up.</summary>
        PARTICLEDETAIL_MEDIUM = 1,
        /// <summary>Present from the high tier up.</summary>
        PARTICLEDETAIL_HIGH = 2,
        /// <summary>Present only at the ultra tier.</summary>
        PARTICLEDETAIL_ULTRA = 3,
    }
}
