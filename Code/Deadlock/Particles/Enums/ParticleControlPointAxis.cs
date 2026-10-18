using System;
using System.Numerics;
namespace Deadlock.Particles
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>
    /// An axis of a control point's frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleControlPointAxis_t">ParticleControlPointAxis_t</seealso>
    public enum ParticleControlPointAxis
    {
        /// <summary>The forward axis.</summary>
        PARTICLE_CP_AXIS_X = 0,

        /// <summary>The left axis.</summary>
        PARTICLE_CP_AXIS_Y = 1,

        /// <summary>The up axis.</summary>
        PARTICLE_CP_AXIS_Z = 2,

        /// <summary>The backward axis.</summary>
        PARTICLE_CP_AXIS_NEGATIVE_X = 3,

        /// <summary>The right axis.</summary>
        PARTICLE_CP_AXIS_NEGATIVE_Y = 4,

        /// <summary>The down axis.</summary>
        PARTICLE_CP_AXIS_NEGATIVE_Z = 5,
    }
}
