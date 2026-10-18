using System;
using System.Numerics;
namespace Deadlock.Particles
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>
    /// Particle light unit choice list.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleLightUnitChoiceList_t">ParticleLightUnitChoiceList_t</seealso>
    public enum ParticleLightUnitChoiceList
    {
        /// <summary>Brightness is specified in candelas (cd).</summary>
        PARTICLE_LIGHT_UNIT_CANDELAS = 0,
        /// <summary>Brightness is specified in lumens (lm).</summary>
        PARTICLE_LIGHT_UNIT_LUMENS = 1,
    }
}
