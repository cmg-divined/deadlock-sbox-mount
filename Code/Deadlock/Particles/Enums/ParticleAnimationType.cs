using System;
using System.Numerics;
namespace Deadlock.Particles
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>
    /// Particle animation timing modes.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/AnimationType_t">AnimationType_t</seealso>
    public enum ParticleAnimationType
    {
        /// <summary>Animation advances at a fixed rate regardless of lifetime.</summary>
        ANIMATION_TYPE_FIXED_RATE,
        /// <summary>Animation is stretched to fit the particle lifetime.</summary>
        ANIMATION_TYPE_FIT_LIFETIME,
        /// <summary>Animation frame is set manually by operators.</summary>
        ANIMATION_TYPE_MANUAL_FRAMES,
    }
}
