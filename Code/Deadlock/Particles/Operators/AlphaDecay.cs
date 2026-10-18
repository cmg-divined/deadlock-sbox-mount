using System;
using System.Numerics;
namespace Deadlock.Particles.Operators
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>
    /// Kills a particle when its alpha falls to or below the specified minimum alpha threshold.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_AlphaDecay">C_OP_AlphaDecay</seealso>
    class AlphaDecay : ParticleFunctionOperator
    {
        private readonly float minAlpha;
        public AlphaDecay(ParticleDefinitionParser parse) : base(parse)
        {
            minAlpha = parse.Float("m_flMinAlpha", minAlpha);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                if (particle.Alpha <= minAlpha)
                {
                    particle.Kill();
                }
            }
        }
    }
}
