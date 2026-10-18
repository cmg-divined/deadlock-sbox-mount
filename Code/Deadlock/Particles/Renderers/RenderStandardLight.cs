using System;

namespace Deadlock.Particles.Renderers;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

/// <summary>
/// A light per particle with the engine's classic attenuation (C_OP_RenderStandardLight), as particles.dll builds its
/// light description: colour from the particle's tint and the renderer's colour blend, times intensity and alpha;
/// reach from the particle's radius; constant, linear and quadratic attenuation from the falloff linearity (old style)
/// or the fifty and zero percent distances (new style), less what's left at the reach so the light ends there.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/deadlock/particles/C_OP_RenderStandardLight">C_OP_RenderStandardLight</seealso>
internal sealed class RenderStandardLight : ParticleLightRenderer
{
	private enum LightType
	{
		PARTICLE_LIGHT_TYPE_POINT = 0,
		PARTICLE_LIGHT_TYPE_SPOT = 1,
		PARTICLE_LIGHT_TYPE_FX = 2,
		PARTICLE_LIGHT_TYPE_CAPSULE = 3,
	}

	private enum AttenuationStyle
	{
		LIGHT_STYLE_OLD = 0,
		LIGHT_STYLE_NEW = 1,
	}

	private enum CapsuleBehavior
	{
		PARTICLE_LIGHT_BEHAVIOR_FOLLOW_DIRECTION = 0,
		PARTICLE_LIGHT_BEHAVIOR_ROPE = 1,
		PARTICLE_LIGHT_BEHAVIOR_TRAILS = 2,
	}

	private readonly LightType lightType = LightType.PARTICLE_LIGHT_TYPE_POINT;
	private readonly INumberProvider intensity = new LiteralNumberProvider( 1f );
	private readonly INumberProvider theta = new LiteralNumberProvider( 45f );
	private readonly INumberProvider phi = new LiteralNumberProvider( 60f );
	private readonly INumberProvider radiusMultiplier = new LiteralNumberProvider( 8f );
	private readonly AttenuationStyle attenuationStyle = AttenuationStyle.LIGHT_STYLE_OLD;
	private readonly INumberProvider falloffLinearity = new LiteralNumberProvider( 0f );
	private readonly INumberProvider fiftyPercentFalloff = new LiteralNumberProvider( 0.5f );
	private readonly INumberProvider zeroPercentFalloff = new LiteralNumberProvider( 1f );
	private readonly bool renderSpecular = true;
	private readonly CapsuleBehavior capsuleBehavior = CapsuleBehavior.PARTICLE_LIGHT_BEHAVIOR_FOLLOW_DIRECTION;
	private readonly float capsuleLength = 100f;
	private readonly bool reverseOrder;
	private readonly bool closedLoop;
	private readonly float maxLength = 2000f;
	private readonly float minLength;
	private readonly bool ignoreDeltaTime;
	private readonly float lengthScale = 1f;
	private readonly float lengthFadeInTime;

	public RenderStandardLight( ParticleDefinitionParser parse ) : base( parse, "m_vecColorScale" )
	{
		lightType = parse.Enum( "m_nLightType", lightType );
		intensity = parse.NumberProvider( "m_flIntensity", intensity );
		theta = parse.NumberProvider( "m_flTheta", theta );
		phi = parse.NumberProvider( "m_flPhi", phi );
		radiusMultiplier = parse.NumberProvider( "m_flRadiusMultiplier", radiusMultiplier );
		attenuationStyle = parse.Enum( "m_nAttenuationStyle", attenuationStyle );
		falloffLinearity = parse.NumberProvider( "m_flFalloffLinearity", falloffLinearity );
		fiftyPercentFalloff = parse.NumberProvider( "m_flFiftyPercentFalloff", fiftyPercentFalloff );
		zeroPercentFalloff = parse.NumberProvider( "m_flZeroPercentFalloff", zeroPercentFalloff );
		renderSpecular = parse.Boolean( "m_bRenderSpecular", renderSpecular );
		capsuleBehavior = parse.Enum( "m_nCapsuleLightBehavior", capsuleBehavior );
		capsuleLength = parse.Float( "m_flCapsuleLength", capsuleLength );
		reverseOrder = parse.Boolean( "m_bReverseOrder", reverseOrder );
		closedLoop = parse.Boolean( "m_bClosedLoop", closedLoop );
		maxLength = parse.Float( "m_flMaxLength", maxLength );
		minLength = parse.Float( "m_flMinLength", minLength );
		ignoreDeltaTime = parse.Boolean( "m_bIgnoreDT", ignoreDeltaTime );
		lengthScale = parse.Float( "m_flLengthScale", lengthScale );
		lengthFadeInTime = parse.Float( "m_flLengthFadeInTime", lengthFadeInTime );
	}

	protected override void PlaceLights( ParticleCollection particles, ParticleSystemState systemState )
	{
		// The system wide inputs, read once; a zero radius multiplier places nothing.
		var multiplier = radiusMultiplier.NextNumber( systemState );
		if ( multiplier == 0f )
			return;

		var fx = lightType == LightType.PARTICLE_LIGHT_TYPE_FX;
		var linearity = fx ? 1f : Math.Clamp( falloffLinearity.NextNumber( systemState ), 0f, 1f );
		var zero = MathF.Max( zeroPercentFalloff.NextNumber( systemState ), 2.3841858e-7f );
		var fifty = Math.Clamp( fiftyPercentFalloff.NextNumber( systemState ), 1.1920929e-7f, zero - 1.1920929e-7f );
		var innerAngle = theta.NextNumber( systemState );
		var outerAngle = phi.NextNumber( systemState );
		var blend = GetBlendColor( systemState );

		var current = particles.Current;
		var count = current.Length;

		for ( var i = 0; i < count && lights.Count < MaxAllowed; i++ )
		{
			var index = reverseOrder ? count - i - 1 : i;
			ref var particle = ref current[index];

			var strength = intensity.NextNumber( ref particle, systemState );
			if ( strength <= 0f || particle.Radius == 0f || particle.Alpha == 0f )
				continue;

			var light = new ParticleLight
			{
				Position = particle.Position,
				Color = GetLightColor( particle.Color, blend ) * strength * particle.Alpha,
				Range = multiplier * particle.Radius,
				DistanceFade = new Vector2( 1f, 0f ),
				ConeFade = new Vector2( 1f, 0f ),
				Specular = fx || !renderSpecular ? 0f : 1f,
			};

			var axes = ParticleAxes( particle );
			light.Direction = axes.Forward;

			if ( lightType == LightType.PARTICLE_LIGHT_TYPE_SPOT )
			{
				light.ConeFade = ConeFade( outerAngle, innerAngle );
				light.ConeInner = innerAngle;
				light.ConeOuter = outerAngle;
			}
			else if ( lightType == LightType.PARTICLE_LIGHT_TYPE_CAPSULE && !PlaceCapsule( ref light, current, i, index, multiplier, axes.Forward, systemState ) )
			{
				continue;
			}

			if ( !Attenuate( ref light, linearity, fifty, zero ) )
				continue;

			lights.Add( light );
		}
	}

	/// <summary>
	/// A capsule's segment: along the particle's facing, to the next particle (rope) or back along its trail.
	/// </summary>
	private bool PlaceCapsule( ref ParticleLight light, Span<Particle> current, int i, int index, float multiplier, Vector3 forward, ParticleSystemState systemState )
	{
		ref var particle = ref current[index];
		Vector3 segment;

		switch ( capsuleBehavior )
		{
			case CapsuleBehavior.PARTICLE_LIGHT_BEHAVIOR_ROPE:
			{
				var count = current.Length;
				var nextStep = i + 1;
				if ( nextStep >= count )
					nextStep = closedLoop ? 0 : i;

				ref var next = ref current[reverseOrder ? count - nextStep - 1 : nextStep];
				light.Range = (particle.Radius + next.Radius) * 0.5f * multiplier;
				segment = light.Range == 0f ? Vector3.UnitZ : next.Position - particle.Position;
				break;
			}

			case CapsuleBehavior.PARTICLE_LIGHT_BEHAVIOR_TRAILS:
			{
				var trail = particle.PositionPrevious - particle.Position;
				var distance = MathF.Max( trail.Length(), 1.1920929e-7f );

				// The trail's scale is per second unless delta time is ignored.
				var scale = lengthScale;
				if ( !ignoreDeltaTime && systemState.Data?.Particles.CurrentFrameTime is float frameTime && frameTime - (systemState.Data?.Particles.PreviousFrameTime ?? 0f) is float step && step > 0f )
					scale /= step;

				var fadeIn = lengthFadeInTime > 0f ? MathF.Min( 1f, particle.Age / lengthFadeInTime ) : 1f;
				var length = MathF.Max( minLength, MathF.Min( maxLength, fadeIn * scale * particle.TrailLength * distance ) );
				segment = trail / distance * length;
				break;
			}

			default:
				segment = forward * capsuleLength;
				break;
		}

		// The engine's capsule runs from the particle along the segment.
		light.Position += segment * 0.5f;
		light.Segment = segment * 0.5f;
		light.CapsuleDistance = true;
		return true;
	}

	/// <summary>
	/// The attenuation terms and the colour scale the attenuation style gives, then the reach cut to where the light
	/// falls below the engine's threshold, and the falloff at the reach subtracted so it ends there.
	/// </summary>
	private bool Attenuate( ref ParticleLight light, float linearity, float fifty, float zero )
	{
		float quadratic, linear, constant;

		if ( attenuationStyle == AttenuationStyle.LIGHT_STYLE_NEW )
		{
			// 100% at the light, 50% at the fifty percent distance, 1/256 at the zero percent one.
			SolveInverseQuadraticMonotonic( 0f, 1f, light.Range * fifty, 2f, light.Range * zero, 256f, out quadratic, out linear, out constant );

			var atFifty = constant + light.Range * fifty * (linear + light.Range * fifty * quadratic);
			var scale = atFifty != 0f ? 2f / atFifty : 0f;
			quadratic *= scale;
			linear *= scale;
			constant *= scale;
		}
		else
		{
			quadratic = 1f - linearity < 0.001f ? 0f : 1f - linearity;
			linear = linearity < 0.001f ? 0f : linearity;
			constant = quadratic == 0f && linear == 0f ? 1f : 0f;

			// Normalised to the colour at 100 units.
			var scale = quadratic * 10000f + linear * 100f + constant;
			if ( scale > 0f )
				light.Color *= scale;
		}

		if ( constant == 0f )
		{
			var luminance = light.Color.X * 0.2126f + light.Color.Y * 0.7152f + light.Color.Z * 0.0722f;
			var reach = 0f;

			if ( luminance > 0f )
			{
				var k = BrightnessThreshold / luminance;
				if ( SolveQuadratic( k * quadratic, k * linear, -1f, out var a, out var b ) )
					reach = MathF.Max( 0f, MathF.Max( a, b ) );
			}

			light.Range = light.Range > 0f ? MathF.Min( reach, light.Range ) : reach;
		}

		if ( light.Range <= 0f )
			return false;

		var bias = constant > 0f ? 0f : 1f / (light.Range * light.Range * quadratic + light.Range * linear + 1.1920929e-7f);
		light.Falloff = new Vector4( quadratic, linear, constant, bias );
		return true;
	}

	/// <summary>
	/// The roots of a x² + b x + c, the engine's way (a linear equation when a is zero).
	/// </summary>
	private static bool SolveQuadratic( float a, float b, float c, out float root1, out float root2 )
	{
		root1 = root2 = 0f;

		if ( a == 0f )
		{
			if ( b != 0f )
			{
				root1 = root2 = -c / b;
				return true;
			}

			return c == 0f;
		}

		var discriminant = b * b - 4f * a * c;
		if ( discriminant < 0f )
			return false;

		var root = MathF.Sqrt( discriminant );
		root1 = (root - b) / (a + a);
		root2 = (-b - root) / (a + a);
		return true;
	}

	/// <summary>
	/// The quadratic through three points, its middle point blended towards the line between the others until the
	/// curve is monotonic (Source's SolveInverseQuadraticMonotonic).
	/// </summary>
	private static void SolveInverseQuadraticMonotonic( float x1, float y1, float x2, float y2, float x3, float y3, out float a, out float b, out float c )
	{
		if ( x1 > x2 )
			(x1, x2, y1, y2) = (x2, x1, y2, y1);

		if ( x2 > x3 )
			(x2, x3, y2, y3) = (x3, x2, y3, y2);

		if ( x1 > x2 )
			(x1, x2, y1, y2) = (x2, x1, y2, y1);

		a = 0f;
		b = 1f;
		c = 0f;

		var determinant = (x1 - x2) * (x1 - x3) * (x2 - x3);
		if ( determinant == 0f )
			return;

		for ( var blend = 0f; blend <= 1f; blend += 0.05f )
		{
			var y = (1f - blend) * y2 + blend * (y1 + (x2 - x1) * (y3 - y1) / (x3 - x1));

			a = (x3 * (y - y1) + x2 * (y1 - y3) + x1 * (y3 - y)) / determinant;
			b = (x3 * x3 * (y1 - y) + x1 * x1 * (y - y3) + x2 * x2 * (y3 - y1)) / determinant;
			c = (x1 * x3 * (x3 - x1) * y + x2 * x2 * (x3 * y1 - x1 * y3) + x2 * (x1 * x1 * y3 - x3 * x3 * y1)) / determinant;

			var derivative = 2f * a + b;

			if ( y1 < y2 && y2 < y3 )
			{
				if ( derivative >= 0f )
					return;
			}
			else if ( y1 > y2 && y2 > y3 )
			{
				if ( derivative <= 0f )
					return;
			}
			else
			{
				return;
			}
		}
	}
}
