using System;
using System.Numerics;
namespace Deadlock.Particles
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>Particle depth feathering option.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleDepthFeatheringMode_t">ParticleDepthFeatheringMode_t</seealso>
    public enum ParticleDepthFeatheringMode
    {
        /// <summary>No feathering; cards intersect geometry with a hard edge.</summary>
        PARTICLE_DEPTH_FEATHERING_OFF = 0,
        /// <summary>Feathering only in high settings.</summary>
        PARTICLE_DEPTH_FEATHERING_ON_OPTIONAL = 1,
        /// <summary>Feathering always on.</summary>
        PARTICLE_DEPTH_FEATHERING_ON_REQUIRED = 2,
    }
}
