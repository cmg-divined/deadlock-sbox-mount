using System;
using System.Numerics;
namespace Deadlock.Particles
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>
    /// A position on an entity.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleEntityPos_t">ParticleEntityPos_t</seealso>
    public enum ParticleEntityPos
    {
        /// <summary>The entity origin, which is at a player's feet.</summary>
        PARTICLE_ABS_ORIGIN = 0,

        /// <summary>The middle of the entity bounds.</summary>
        PARTICLE_WORLDSPACE_CENTER = 1,

        /// <summary>The eyes.</summary>
        PARTICLE_EYES = 2,

        /// <summary>The flashlight.</summary>
        PARTICLE_FLASHLIGHT = 3,
    }
}
