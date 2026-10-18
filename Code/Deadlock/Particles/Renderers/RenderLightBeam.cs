using System;

namespace Deadlock.Particles.Renderers;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

/// <summary>
/// light2 tube lights along a beam of particles (C_OP_RenderLightBeam): the particles split into runs, one light per
/// run along the line its particles lie on, as long as the run, as bright as its length in metres times the lumens per
/// metre, coloured by the average of its particles' colours.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/deadlock/particles/C_OP_RenderLightBeam">C_OP_RenderLightBeam</seealso>
internal sealed class RenderLightBeam : ParticleLightRenderer
{
	/// <summary>
	/// Metres per unit, which the brightness per metre is in.
	/// </summary>
	private const float MetresPerUnit = 0.0254f;

	private readonly INumberProvider brightnessLumensPerMeter = new LiteralNumberProvider( 1000f );
	private readonly INumberProvider numberOfLightsToCreate = new LiteralNumberProvider( 1f );
	private readonly INumberProvider skirt = new LiteralNumberProvider( 0.1f );
	private readonly INumberProvider range = new LiteralNumberProvider( 512f );
	private readonly INumberProvider innerConeAngle = new LiteralNumberProvider( 180f );
	private readonly INumberProvider outerConeAngle = new LiteralNumberProvider( 180f );

	public RenderLightBeam( ParticleDefinitionParser parse ) : base( parse, "m_vColorBlend" )
	{
		brightnessLumensPerMeter = parse.NumberProvider( "m_flBrightnessLumensPerMeter", brightnessLumensPerMeter );
		numberOfLightsToCreate = parse.NumberProvider( "m_flNumberOfLightsToCreate", numberOfLightsToCreate );
		skirt = parse.NumberProvider( "m_flSkirt", skirt );
		range = parse.NumberProvider( "m_flRange", range );
		innerConeAngle = parse.NumberProvider( "m_flInnerConeAngle", innerConeAngle );
		outerConeAngle = parse.NumberProvider( "m_flOuterConeAngle", outerConeAngle );
	}

	protected override void PlaceLights( ParticleCollection particles, ParticleSystemState systemState )
	{
		var current = particles.Current;
		var reach = range.NextNumber( systemState );
		var lumensPerMeter = brightnessLumensPerMeter.NextNumber( systemState );
		if ( reach <= 0f || lumensPerMeter <= 0f )
			return;

		var count = Math.Min( (int)numberOfLightsToCreate.NextNumber( systemState ), current.Length );
		if ( count <= 0 )
			return;

		var outer = Math.Clamp( outerConeAngle.NextNumber( systemState ), 0f, 180f );
		var inner = Math.Clamp( innerConeAngle.NextNumber( systemState ), 0f, outer );
		var fade = SkirtFade( reach, skirt.NextNumber( systemState ) );
		var cone = ConeFade( outer, inner );
		var blend = GetBlendColor( systemState );
		var perLight = MathF.Max( 1f, (float)current.Length / count );

		for ( var n = 0; n < count && lights.Count < MaxAllowed; n++ )
		{
			// Each light's run of particles, at least two, overlapping the next by one.
			var first = (int)MathF.Floor( n * perLight );
			var last = Math.Min( Math.Max( (int)MathF.Ceiling( (n + 1) * perLight ), first + 2 ), current.Length );
			var run = last - first;
			if ( run < 1 )
				continue;

			var color = Vector3.Zero;
			var center = Vector3.Zero;
			for ( var i = first; i < last; i++ )
			{
				color += GetLightColor( current[i].Color, blend ) * current[i].Alpha;
				center += current[i].Position;
			}

			center /= run;

			// The line the run lies along and how far it spans on it.
			var axis = run > 1 ? current[last - 1].Position - current[first].Position : Vector3.Zero;
			axis = axis.LengthSquared() > 1e-8f ? Vector3.Normalize( axis ) : Vector3.UnitZ;

			float min = float.MaxValue, max = float.MinValue;
			for ( var i = first; i < last; i++ )
			{
				var t = Vector3.Dot( current[i].Position - center, axis );
				min = MathF.Min( min, t );
				max = MathF.Max( max, t );
			}

			var length = max - min;
			if ( length <= 0f )
				continue;

			// The run's lumens over a full sphere, per particle summed into the colour.
			var intensity = length * lumensPerMeter * MetresPerUnit / (4f * MathF.PI) * CandelaIntensity / run;

			lights.Add( new ParticleLight
			{
				Position = center + axis * ((min + max) * 0.5f),
				Color = color * intensity,
				Range = reach,
				Falloff = new Vector4( 1f, 0f, 0f, 0f ),
				DistanceFade = fade,
				ConeFade = cone,
				Direction = PerpendicularTo( axis ),
				Segment = axis * (length * 0.5f),
				Specular = 1f,
				ConeInner = outer < 180f ? inner : 0f,
				ConeOuter = outer < 180f ? outer : 0f,
			} );
		}
	}

	private static Vector3 PerpendicularTo( Vector3 axis )
	{
		var other = MathF.Abs( axis.Z ) > 0.999f ? Vector3.UnitX : Vector3.UnitZ;
		return Vector3.Normalize( Vector3.Cross( axis, other ) );
	}
}
