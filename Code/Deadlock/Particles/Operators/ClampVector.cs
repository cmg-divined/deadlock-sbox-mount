using System;
using System.Numerics;
namespace Deadlock.Particles.Operators
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>
    /// Clamps a vector particle field, component by component, between a minimum and maximum each frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ClampVector">C_OP_ClampVector</seealso>
    class ClampVector : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Position;
        private readonly IVectorProvider outputMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider outputMax = new LiteralVectorProvider(Vector3.One);

        public ClampVector(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            outputMin = parse.VectorProvider("m_vecOutputMin", outputMin);
            outputMax = parse.VectorProvider("m_vecOutputMax", outputMax);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var min = outputMin.NextVector(ref particle, particleSystemState);
                var max = outputMax.NextVector(ref particle, particleSystemState);

                // Like ClampScalar, the bounds apply in order, so with an inverted range the maximum wins.
                var current = particle.GetVector(outputField);
                var clamped = Vector3.Min(max, Vector3.Max(current, min));

                particle.SetVector(outputField, Vector3.Lerp(current, clamped, strength));
            }
        }
    }
}
