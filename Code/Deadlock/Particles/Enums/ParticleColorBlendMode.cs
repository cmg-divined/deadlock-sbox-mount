using System;
using System.Numerics;
namespace Deadlock.Particles
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>
    /// How a particle colour initializer combines the colour it picked with a tint.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/ParticleColorBlendMode_t">ParticleColorBlendMode_t</seealso>
    public enum ParticleColorBlendMode
    {
        /// <summary>Replace.</summary>
        PARTICLEBLEND_DEFAULT = 0,
        /// <summary>Overlay.</summary>
        PARTICLEBLEND_OVERLAY = 1,
        /// <summary>Darken: the per-channel minimum.</summary>
        PARTICLEBLEND_DARKEN = 2,
        /// <summary>Lighten: the per-channel maximum.</summary>
        PARTICLEBLEND_LIGHTEN = 3,
        /// <summary>Multiply.</summary>
        PARTICLEBLEND_MULTIPLY = 4,
    }
}
