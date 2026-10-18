using System;
using System.Numerics;
namespace Deadlock.Particles.Operators
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>
    /// Kills every particle at once, once the endcap has been running for <c>m_flDecayTime</c>.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_EndCapTimedDecay">C_OP_EndCapTimedDecay</seealso>
    class EndCapTimedDecay : ParticleFunctionOperator
    {
        private readonly INumberProvider decayTime = new LiteralNumberProvider(0f);

        public EndCapTimedDecay(ParticleDefinitionParser parse) : base(parse)
        {
            decayTime = parse.NumberProvider("m_flDecayTime", decayTime);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (!particleSystemState.InEndCap)
            {
                return;
            }

            if (particleSystemState.EndCapAge <= decayTime.NextNumber(particleSystemState))
            {
                return;
            }

            foreach (ref var particle in particles.Current)
            {
                particle.MarkedAsKilled = true;
            }
        }
    }
}
