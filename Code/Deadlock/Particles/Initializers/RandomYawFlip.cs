using System;
using System.Numerics;
namespace Deadlock.Particles.Initializers
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>
    /// Randomly flips the yaw of a particle by 180 degrees based on a configurable flip percentage.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomYawFlip">C_INIT_RandomYawFlip</seealso>
    class RandomYawFlip : ParticleFunctionInitializer
    {
        private readonly float percent;

        public RandomYawFlip(ParticleDefinitionParser parse) : base(parse)
        {
            percent = parse.Float("m_flPercent", 0.5f);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Yaw);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (particleSystemState.Random.Next() < percent)
            {
                particle.SetScalar(ParticleField.Yaw, particle.GetScalar(ParticleField.Yaw) + MathF.PI);
            }

            return particle;
        }
    }
}
