using System;

namespace Deadlock.Particles.Renderers;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

/// <summary>
/// A light a particle renderer places for the frame, in world space, in the terms the game lights with: a colour, a
/// falloff over distance, and an optional cone and luminaire.
/// </summary>
internal struct ParticleLight
{
	public Vector3 Position;

	/// <summary>
	/// Linear colour, scaled the way the game scales it.
	/// </summary>
	public Vector3 Color;

	/// <summary>
	/// How far the light reaches.
	/// </summary>
	public float Range;

	/// <summary>
	/// saturate( 1 / ( x d² + y d + z ) - w ), with the distance at least one unit.
	/// </summary>
	public Vector4 Falloff;

	/// <summary>
	/// smoothstep( 0, 1, x + y d ): a light2 light's skirt, (1, 0) for none.
	/// </summary>
	public Vector2 DistanceFade;

	/// <summary>
	/// saturate( x + y cos ), by the cosine between <see cref="Direction"/> and the direction to the light; (1, 0) for none.
	/// </summary>
	public Vector2 ConeFade;

	/// <summary>
	/// The cone's axis, the way the light faces.
	/// </summary>
	public Vector3 Direction;

	/// <summary>
	/// Half the luminaire, along its axis.
	/// </summary>
	public Vector3 Segment;

	/// <summary>
	/// Whether distances are to the nearest point of the luminaire (capsule lights) rather than its centre.
	/// </summary>
	public bool CapsuleDistance;

	/// <summary>
	/// How much specular the light gives: 0 for diffuse only.
	/// </summary>
	public float Specular;

	/// <summary>
	/// The cone's inner and outer half angles in degrees, for s&amp;box's own lights; 0 for none.
	/// </summary>
	public float ConeInner;
	public float ConeOuter;
}

/// <summary>
/// A renderer that places lights rather than drawing (the <c>C_OP_Render*Light</c> renderers), as particles.dll builds
/// them. Its lights are those of the particles as of the last step; the scene gathers them each frame.
/// </summary>
internal abstract class ParticleLightRenderer : ParticleFunctionRenderer
{
	/// <summary>
	/// The engine's minimum brightness: the standard lights' reach ends where they fall below it.
	/// </summary>
	protected const float BrightnessThreshold = 0.001f;

	/// <summary>
	/// light2 brightness: one candela is this much intensity (40π), one lumen of an omni light a tenth of it.
	/// </summary>
	protected const float CandelaIntensity = 1f / 0.0079577472f;

	private static readonly float[] SrgbToLinearTable = BuildSrgbTable();

	protected readonly List<ParticleLight> lights = [];

	protected int MaxAllowed { get; } = 32;

	protected IVectorProvider ColorBlend { get; } = new LiteralVectorProvider( Vector3.One );
	protected ParticleColorBlendType ColorBlendType { get; } = ParticleColorBlendType.PARTICLE_COLOR_BLEND_MULTIPLY;

	protected ParticleLightRenderer( ParticleDefinitionParser parse, string colorKey ) : base( parse )
	{
		MaxAllowed = parse.Int32( "m_nMaxAllowed", MaxAllowed );
		ColorBlend = parse.VectorProvider( colorKey, ColorBlend );
		ColorBlendType = parse.Enum( "m_nColorBlendType", ColorBlendType );
	}

	/// <summary>
	/// The lights placed at the last step.
	/// </summary>
	public IReadOnlyList<ParticleLight> Lights => lights;

	public override void Act( ParticleCollection particles, ParticleSystemState systemState )
	{
		lights.Clear();

		if ( particles.Count > 0 )
			PlaceLights( particles, systemState );
	}

	protected abstract void PlaceLights( ParticleCollection particles, ParticleSystemState systemState );

	public override void Hide() => lights.Clear();

	// Lights draw nothing themselves.
	public override void Draw( ParticleCollection particles, ParticleSystemState systemState, in ParticleView view )
	{
	}

	public override void Dispose()
	{
		lights.Clear();
		base.Dispose();
	}

	/// <summary>
	/// The renderer's colour blend, adjusted for its blend type the way particles.dll does before blending: doubled
	/// for the 2x types, out of 255 for those that combine it with the tint additively, inverted for screen.
	/// </summary>
	protected Vector3 GetBlendColor( ParticleSystemState systemState )
	{
		var color = ColorBlend.NextVector( systemState );

		return ColorBlendType switch
		{
			ParticleColorBlendType.PARTICLE_COLOR_BLEND_MULTIPLY2X or ParticleColorBlendType.PARTICLE_COLOR_BLEND_MOD2X => color * 2f,
			ParticleColorBlendType.PARTICLE_COLOR_BLEND_ADD or ParticleColorBlendType.PARTICLE_COLOR_BLEND_SUBTRACT
				or ParticleColorBlendType.PARTICLE_COLOR_BLEND_MAX or ParticleColorBlendType.PARTICLE_COLOR_BLEND_MIN
				or ParticleColorBlendType.PARTICLE_COLOR_BLEND_REPLACE or ParticleColorBlendType.PARTICLE_COLOR_BLEND_AVERAGE => color * 255f,
			ParticleColorBlendType.PARTICLE_COLOR_BLEND_SCREEN => Vector3.One - color,
			_ => color,
		};
	}

	/// <summary>
	/// A particle's light colour, linear: its tint as 8 bit values, blended with the renderer's colour, back to 8 bits
	/// and through the engine's sRGB table.
	/// </summary>
	protected Vector3 GetLightColor( Vector3 tint, Vector3 blend )
	{
		var t = new Vector3( Quantize( tint.X ), Quantize( tint.Y ), Quantize( tint.Z ) );

		var color = ColorBlendType switch
		{
			ParticleColorBlendType.PARTICLE_COLOR_BLEND_DIVIDE => t / blend,
			ParticleColorBlendType.PARTICLE_COLOR_BLEND_ADD => blend + t,
			ParticleColorBlendType.PARTICLE_COLOR_BLEND_SUBTRACT => t - blend,
			ParticleColorBlendType.PARTICLE_COLOR_BLEND_SCREEN => new Vector3( 255f ) - (new Vector3( 255f ) - t) * blend,
			ParticleColorBlendType.PARTICLE_COLOR_BLEND_MAX => Vector3.Max( t, blend ),
			ParticleColorBlendType.PARTICLE_COLOR_BLEND_MIN => Vector3.Min( t, blend ),
			ParticleColorBlendType.PARTICLE_COLOR_BLEND_REPLACE => blend,
			ParticleColorBlendType.PARTICLE_COLOR_BLEND_AVERAGE => (blend + t) * 0.5f,
			ParticleColorBlendType.PARTICLE_COLOR_BLEND_NEGATE => new Vector3( 255f ) - t * blend,
			ParticleColorBlendType.PARTICLE_COLOR_BLEND_LUMINANCE => t * Luminance( blend ),
			_ => t * blend,
		};

		return new Vector3( ToLinear( color.X ), ToLinear( color.Y ), ToLinear( color.Z ) );
	}

	// A float colour channel as the engine stores it in a byte: truncated, keeping the low 8 bits.
	private static float Quantize( float value ) => (byte)(int)(value * 255f);

	// Rounded to the nearest byte, saturated, then looked up.
	private static float ToLinear( float value ) => SrgbToLinearTable[(int)Math.Clamp( MathF.Round( value ), 0f, 255f )];

	private static float[] BuildSrgbTable()
	{
		var table = new float[256];
		for ( var i = 0; i < table.Length; i++ )
		{
			var c = i / 255f;
			table[i] = c <= 0.04045f ? c / 12.92f : MathF.Pow( (c + 0.055f) / 1.055f, 2.4f );
		}

		return table;
	}

	protected static float Luminance( Vector3 color ) => color.X * 0.2125f + color.Y * 0.7154f + color.Z * 0.0721f;

	/// <summary>
	/// Forward, left and up from pitch, yaw and roll in degrees, as the engine's AngleVectors gives them (right negated).
	/// </summary>
	protected static (Vector3 Forward, Vector3 Left, Vector3 Up) AngleVectors( float pitch, float yaw, float roll )
	{
		var (sp, cp) = MathF.SinCos( pitch * MathF.PI / 180f );
		var (sy, cy) = MathF.SinCos( yaw * MathF.PI / 180f );
		var (sr, cr) = MathF.SinCos( roll * MathF.PI / 180f );

		var forward = new Vector3( cp * cy, cp * sy, -sp );
		var right = new Vector3( -sr * sp * cy + cr * sy, -sr * sp * sy - cr * cy, -sr * cp );
		var up = new Vector3( cr * sp * cy + sr * sy, cr * sp * sy - sr * cy, cr * cp );

		return (forward, -right, up);
	}

	/// <summary>
	/// A particle's facing from its rotation attributes (yaw, pitch and roll, in radians).
	/// </summary>
	protected static (Vector3 Forward, Vector3 Left, Vector3 Up) ParticleAxes( in Particle particle )
	{
		var rotation = particle.Rotation;
		return AngleVectors( rotation.Y * 180f / MathF.PI, rotation.X * 180f / MathF.PI, rotation.Z * 180f / MathF.PI );
	}

	/// <summary>
	/// A light2 light's skirt: smoothstep( offset + rate * distance ), from full brightness to nothing at its range.
	/// </summary>
	protected static Vector2 SkirtFade( float range, float skirt )
	{
		if ( skirt <= 0f )
			return new Vector2( 1f, 0f );

		if ( skirt >= 1f )
			return new Vector2( 1f, -1f / range );

		return new Vector2( 1f / skirt, -1f / (range * skirt) );
	}

	/// <summary>
	/// A cone's fade, saturate( bias + scale * cos ), from full at the inner half angle to nothing at the outer.
	/// </summary>
	protected static Vector2 ConeFade( float outerAngle, float innerAngle )
	{
		if ( outerAngle >= 180f )
			return new Vector2( 1f, 0f );

		if ( outerAngle <= 0f )
			return Vector2.Zero;

		var cosOuter = MathF.Min( MathF.Cos( outerAngle * MathF.PI / 180f ), 1f - 1e-7f );
		var cosInner = innerAngle > 0f ? MathF.Cos( Math.Clamp( innerAngle, 0f, outerAngle - 0.001f ) * MathF.PI / 180f ) : 1f;

		var denominator = cosOuter - cosInner;
		if ( denominator >= 0f )
			denominator = -1e-7f;

		return new Vector2( cosOuter / denominator, 1f / denominator );
	}
}
