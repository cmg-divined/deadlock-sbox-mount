#nullable enable annotations

using System;
using System.Numerics;
namespace Deadlock.Particles.PreEmissionOperators
{
    using Vector2 = System.Numerics.Vector2;
    using Vector3 = System.Numerics.Vector3;
    using Vector4 = System.Numerics.Vector4;

    /// <summary>
    /// Evaluates a scalar expression of two system-level inputs, maps it through the output remap and writes it to
    /// one component of a control point, like <see cref="Operators.SetAttributeToScalarExpression"/> does for a
    /// particle attribute.
    /// </summary>
    /// <remarks>
    /// The output control point defaults to 2, as <see cref="SetControlPointToVectorExpression"/>'s does: Deadlock's
    /// koth_indicator_vortex_model leaves it out and reads the result from control point 2.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointFieldToScalarExpression">C_OP_SetControlPointFieldToScalarExpression</seealso>
    class SetControlPointFieldToScalarExpression : ParticleFunctionPreEmissionOperator
    {
        private readonly ScalarExpressionType expression = ScalarExpressionType.SCALAR_EXPRESSION_ADD;
        private readonly INumberProvider input1 = new LiteralNumberProvider(1f);
        private readonly INumberProvider input2 = new LiteralNumberProvider(0f);
        private readonly AttributeMapping? outputRemap;
        private readonly int outputCP = 2;
        private readonly int outVectorField;

        public SetControlPointFieldToScalarExpression(ParticleDefinitionParser parse) : base(parse)
        {
            expression = parse.Enum<ScalarExpressionType>("m_nExpression", expression);
            input1 = parse.NumberProvider("m_flInput1", input1);
            input2 = parse.NumberProvider("m_flInput2", input2);
            outputCP = parse.Int32("m_nOutputCP", outputCP);
            outVectorField = parse.Int32("m_nOutVectorField", outVectorField);

            if (parse.Data.GetSubCollection("m_flOutputRemap") is { IsCollection: true } remap)
            {
                outputRemap = new AttributeMapping(parse.Nested(remap));
            }
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime)
        {
            var value1 = input1.NextNumber(particleSystemState);
            var value2 = input2.NextNumber(particleSystemState);

            var output = expression switch
            {
                ScalarExpressionType.SCALAR_EXPRESSION_ADD => value1 + value2,
                ScalarExpressionType.SCALAR_EXPRESSION_SUBTRACT => value1 - value2,
                ScalarExpressionType.SCALAR_EXPRESSION_MUL => value1 * value2,
                ScalarExpressionType.SCALAR_EXPRESSION_DIVIDE => value2 == 0f ? 0f : value1 / value2,
                ScalarExpressionType.SCALAR_EXPRESSION_INPUT_1 => value1,
                ScalarExpressionType.SCALAR_EXPRESSION_MIN => Math.Min(value1, value2),
                ScalarExpressionType.SCALAR_EXPRESSION_MAX => Math.Max(value1, value2),
                ScalarExpressionType.SCALAR_EXPRESSION_MOD => value2 == 0f ? 0f : value1 % value2,
                ScalarExpressionType.SCALAR_EXPRESSION_EQUAL => value1 == value2 ? 1f : 0f,
                ScalarExpressionType.SCALAR_EXPRESSION_GT => value1 > value2 ? 1f : 0f,
                ScalarExpressionType.SCALAR_EXPRESSION_LT => value1 < value2 ? 1f : 0f,
                _ => 0f,
            };

            if (outputRemap != null)
            {
                output = outputRemap.ApplyMapping(output);
            }

            if (outVectorField is >= 0 and < 3)
            {
                particleSystemState.SetControlPointValueComponent(outputCP, outVectorField, output);
            }
        }
    }
}
