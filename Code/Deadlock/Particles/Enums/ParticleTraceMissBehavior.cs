using System;
using System.Numerics;
namespace Deadlock.Particles
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>
    /// What a ground trace does with a particle when it hits nothing.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleTraceMissBehavior_t">ParticleTraceMissBehavior_t</seealso>
    public enum ParticleTraceMissBehavior
    {
        /// <summary>Leaves the particle where it was.</summary>
        PARTICLE_TRACE_MISS_BEHAVIOR_NONE = 0,
        /// <summary>Kills the particle.</summary>
        PARTICLE_TRACE_MISS_BEHAVIOR_KILL = 1,
        /// <summary>Places the particle at the end of the trace.</summary>
        PARTICLE_TRACE_MISS_BEHAVIOR_TRACE_END = 2,
    }
}
