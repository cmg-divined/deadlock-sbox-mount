using System;
using System.Numerics;
namespace Deadlock.Particles.Initializers
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>
    /// A <see cref="CreateWithinSphere"/> that centres and orients its sphere on a transform input
    /// instead of on control point 0.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateWithinSphereTransform">C_INIT_CreateWithinSphereTransform</seealso>
    class CreateWithinSphereTransform : CreateWithinSphere
    {
        public CreateWithinSphereTransform(ParticleDefinitionParser parse)
            : base(parse, parse.TransformInput("m_TransformInput", new ControlPointTransformProvider()))
        {
        }
    }
}
