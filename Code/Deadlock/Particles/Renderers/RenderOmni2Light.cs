using System;

namespace Deadlock.Particles.Renderers;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

/// <summary>
/// A light2 omni light per particle (C_OP_RenderOmni2Light), lit the way the map's light_omni2 entities are:
/// inverse square brightness from a candela or lumen value, a skirt fading it out at its range, and a cone.
/// Lumens spread over the cone's solid angle; a candela is 40π intensity, which is what the map's lights work out to.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/deadlock/particles/C_OP_RenderOmni2Light">C_OP_RenderOmni2Light</seealso>
internal sealed class RenderOmni2Light : ParticleLightRenderer
{
	private enum OrientationType
	{
		PARTICLE_OMNI2_LIGHT_ORIENTATION_ROTATIONS = 0,
		PARTICLE_OMNI2_LIGHT_ORIENTATION_NORMAL = 1,
		PARTICLE_OMNI2_LIGHT_ORIENTATION_NORMAL_ROLL = 2,
		PARTICLE_OMNI2_LIGHT_ORIENTATION_TARGET = 3,
		PARTICLE_OMNI2_LIGHT_ORIENTATION_TARGET_ROLL = 4,
	}

	private readonly ParticleOmni2LightTypeChoiceList lightType = ParticleOmni2LightTypeChoiceList.PARTICLE_OMNI2_LIGHT_TYPE_POINT;
	private readonly ParticleLightUnitChoiceList brightnessUnit = ParticleLightUnitChoiceList.PARTICLE_LIGHT_UNIT_LUMENS;
	private readonly INumberProvider brightnessLumens = new LiteralNumberProvider( 1000f );
	private readonly INumberProvider brightnessCandelas = new LiteralNumberProvider( 1000f );
	private readonly INumberProvider luminaireRadius = new LiteralNumberProvider( 1f );
	private readonly OrientationType orientationType = OrientationType.PARTICLE_OMNI2_LIGHT_ORIENTATION_ROTATIONS;
	private readonly IVectorProvider normal;
	private readonly IVectorProvider target;
	private readonly INumberProvider skirt = new LiteralNumberProvider( 0.1f );
	private readonly INumberProvider range = new LiteralNumberProvider( 512f );
	private readonly INumberProvider innerConeAngle = new LiteralNumberProvider( 180f );
	private readonly INumberProvider outerConeAngle = new LiteralNumberProvider( 180f );

	public RenderOmni2Light( ParticleDefinitionParser parse ) : base( parse, "m_vColorBlend" )
	{
		lightType = parse.Enum( "m_nLightType", lightType );
		brightnessUnit = parse.Enum( "m_nBrightnessUnit", brightnessUnit );
		brightnessLumens = parse.NumberProvider( "m_flBrightnessLumens", brightnessLumens );
		brightnessCandelas = parse.NumberProvider( "m_flBrightnessCandelas", brightnessCandelas );
		luminaireRadius = parse.NumberProvider( "m_flLuminaireRadius", luminaireRadius );
		orientationType = parse.Enum( "m_nOrientationType", orientationType );
		normal = parse.VectorProvider( "m_vNormal", null );
		target = parse.VectorProvider( "m_vTarget", null );
		skirt = parse.NumberProvider( "m_flSkirt", skirt );
		range = parse.NumberProvider( "m_flRange", range );
		innerConeAngle = parse.NumberProvider( "m_flInnerConeAngle", innerConeAngle );
		outerConeAngle = parse.NumberProvider( "m_flOuterConeAngle", outerConeAngle );
	}

	protected override void PlaceLights( ParticleCollection particles, ParticleSystemState systemState )
	{
		var blend = GetBlendColor( systemState );
		var current = particles.Current;

		for ( var i = 0; i < current.Length && lights.Count < MaxAllowed; i++ )
		{
			ref var particle = ref current[i];

			var reach = range.NextNumber( ref particle, systemState );
			if ( reach <= 0f )
				continue;

			var lumens = brightnessUnit == ParticleLightUnitChoiceList.PARTICLE_LIGHT_UNIT_LUMENS;
			var brightness = lumens ? brightnessLumens.NextNumber( ref particle, systemState ) : brightnessCandelas.NextNumber( ref particle, systemState );
			if ( brightness == 0f )
				continue;

			var outer = Math.Clamp( outerConeAngle.NextNumber( ref particle, systemState ), 0f, 180f );
			var inner = Math.Clamp( innerConeAngle.NextNumber( ref particle, systemState ), 0f, outer );

			// Candelas to intensity, or lumens over the cone's solid angle.
			var intensity = brightness * CandelaIntensity;
			if ( lumens )
			{
				var solidAngle = (1f - (MathF.Cos( inner * MathF.PI / 180f ) + MathF.Cos( outer * MathF.PI / 180f )) * 0.5f) * 2f * MathF.PI;
				if ( solidAngle <= 0f )
					continue;

				intensity /= solidAngle;
			}

			var (forward, left, up) = Orientation( ref particle, systemState );

			var light = new ParticleLight
			{
				Position = particle.Position,
				Color = GetLightColor( particle.Color, blend ) * particle.Alpha * intensity,
				Range = reach,
				Falloff = new Vector4( 1f, 0f, 0f, 0f ),
				DistanceFade = SkirtFade( reach, skirt.NextNumber( ref particle, systemState ) ),
				ConeFade = ConeFade( outer, inner ),
				Direction = forward,
				Specular = 1f,
				ConeInner = outer < 180f ? inner : 0f,
				ConeOuter = outer < 180f ? outer : 0f,
			};

			// A sphere's luminaire, as the map's omni lights carry theirs: along the light's second axis.
			if ( lightType == ParticleOmni2LightTypeChoiceList.PARTICLE_OMNI2_LIGHT_TYPE_SPHERE )
				light.Segment = left * luminaireRadius.NextNumber( ref particle, systemState );

			lights.Add( light );
		}
	}

	private (Vector3 Forward, Vector3 Left, Vector3 Up) Orientation( ref Particle particle, ParticleSystemState systemState )
	{
		var axes = ParticleAxes( particle );

		Vector3? facing = orientationType switch
		{
			OrientationType.PARTICLE_OMNI2_LIGHT_ORIENTATION_NORMAL or OrientationType.PARTICLE_OMNI2_LIGHT_ORIENTATION_NORMAL_ROLL
				=> normal?.NextVector( ref particle, systemState ) ?? particle.Normal,
			OrientationType.PARTICLE_OMNI2_LIGHT_ORIENTATION_TARGET or OrientationType.PARTICLE_OMNI2_LIGHT_ORIENTATION_TARGET_ROLL
				=> target is null ? null : target.NextVector( ref particle, systemState ) - particle.Position,
			_ => null,
		};

		if ( facing is not { } direction || direction.LengthSquared() < 1e-12f )
			return axes;

		var forward = Vector3.Normalize( direction );
		var up = MathF.Abs( forward.Z ) > 0.999f ? Vector3.UnitX : Vector3.UnitZ;
		var left = Vector3.Normalize( Vector3.Cross( up, forward ) );
		return (forward, left, Vector3.Cross( forward, left ));
	}
}
