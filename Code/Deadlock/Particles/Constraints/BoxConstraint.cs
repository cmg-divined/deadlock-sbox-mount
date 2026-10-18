using System;
using System.Numerics;
namespace Deadlock.Particles.Constraints
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>
    /// Keeps particles inside a box around a control point: the minimum and maximum corners are offsets from it,
    /// along the world's axes, or along the control point's own with local space.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_BoxConstraint">C_OP_BoxConstraint</seealso>
    class BoxConstraint : ParticleFunctionConstraint
    {
        private readonly IVectorProvider min = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider max = new LiteralVectorProvider(Vector3.Zero);
        private readonly int controlPoint;
        private readonly bool localSpace;

        public BoxConstraint(ParticleDefinitionParser parse) : base(parse)
        {
            min = parse.VectorProvider("m_vecMin", min);
            max = parse.VectorProvider("m_vecMax", max);
            controlPoint = parse.Int32("m_nCP", controlPoint);
            localSpace = parse.Boolean("m_bLocalSpace", localSpace);
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState)
        {
            var point = particleSystemState.GetControlPoint(controlPoint);
            var origin = point.Position;
            var rotation = localSpace ? point.GetRotation() : Quaternion.Identity;
            var inverse = Quaternion.Inverse(rotation);
            var moved = false;

            foreach (ref var particle in particles.Current)
            {
                var low = min.NextVector(ref particle, particleSystemState);
                var high = max.NextVector(ref particle, particleSystemState);

                var local = Vector3.Transform(particle.Position - origin, inverse);
                var clamped = Vector3.Min(high, Vector3.Max(local, low));

                if (clamped != local)
                {
                    particle.Position = origin + Vector3.Transform(clamped, rotation);
                    moved = true;
                }
            }

            return moved;
        }
    }
}
