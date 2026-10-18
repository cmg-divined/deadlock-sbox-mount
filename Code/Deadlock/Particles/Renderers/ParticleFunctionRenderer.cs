using System;

namespace Deadlock.Particles.Renderers;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

/// <summary>
/// The pass a renderer draws in.
/// </summary>
internal enum ParticleRenderPass
{
	Translucent,
	Opaque,
}

/// <summary>
/// A particle system's renderer (<c>m_Renderers</c>): draws the system's particles each frame. Ported from
/// ValveResourceFormat's renderers, drawing through s&amp;box with <c>deadlock_particle.shader</c>.
/// </summary>
internal abstract class ParticleFunctionRenderer : ParticleFunction, IDisposable
{
	public const string ShaderPath = "shaders/deadlock_particle.shader";

	private static Material _material;

	/// <summary>
	/// The material every spritecard renderer draws with; what differs between them is in their draw attributes.
	/// Created with the first renderer, on the main thread: drawing happens on the render thread, where materials
	/// can't be created.
	/// </summary>
	protected static Material Material => _material;

	protected INumberProvider RadiusScale { get; } = new LiteralNumberProvider( 1f );
	protected INumberProvider AlphaScale { get; } = new LiteralNumberProvider( 1f );
	protected IVectorProvider ColorScale { get; } = new LiteralVectorProvider( Vector3.One );
	protected INumberProvider OverbrightFactor { get; } = new LiteralNumberProvider( 1f );
	protected INumberProvider DiffuseAmount { get; } = new LiteralNumberProvider( 1f );
	protected INumberProvider SelfIllumAmount { get; } = new LiteralNumberProvider( 0f );
	protected INumberProvider Desaturation { get; } = new LiteralNumberProvider( 0f );
	protected int HsvShiftControlPoint { get; } = -1;
	protected INumberProvider SourceAlphaValueToMapToZero { get; } = new LiteralNumberProvider( 0f );
	protected INumberProvider SourceAlphaValueToMapToOne { get; } = new LiteralNumberProvider( 1f );
	protected bool GammaCorrectVertexColors { get; } = true;
	protected bool MaxLuminanceFrameBlend { get; }
	protected INumberProvider AddSelfAmount { get; } = new LiteralNumberProvider( 0f );
	protected INumberProvider CenterXOffset { get; } = new LiteralNumberProvider( 0f );
	protected INumberProvider CenterYOffset { get; } = new LiteralNumberProvider( 0f );
	protected INumberProvider DepthBias { get; } = new LiteralNumberProvider( 0f );

	private ParticleSortingChoiceList SortMethod { get; }

	public bool OnlyRenderInEffectsWaterPass { get; }
	public bool OnlyRenderInEffectsBloomPass { get; }

	protected bool OutputIsColor => !OnlyRenderInEffectsWaterPass;

	private ParticleDepthFeatheringMode FeatheringMode { get; }
	private INumberProvider FeatheringMinDist { get; } = new LiteralNumberProvider( 0f );
	private INumberProvider FeatheringMaxDist { get; } = new LiteralNumberProvider( 0f );
	private INumberProvider FeatheringFilter { get; } = new LiteralNumberProvider( 1f );

	public bool WantsSceneDepth => FeatheringMode != ParticleDepthFeatheringMode.PARTICLE_DEPTH_FEATHERING_OFF;

	protected ParticleLightingQuality LightingMode { get; } = ParticleLightingQuality.PARTICLE_LIGHTING_PER_PARTICLE;

	public ParticleRenderPass Pass { get; protected set; } = ParticleRenderPass.Translucent;

	/// <summary>
	/// What the draw hands the shader; reused from draw to draw.
	/// </summary>
	protected RenderAttributes Attributes { get; } = new();

	protected ParticleQuadBuffer Quads { get; } = new();

	/// <summary>
	/// Creates the material if it doesn't exist yet. Called on the main thread.
	/// </summary>
	public static void PrepareMaterial()
	{
		if ( !_material.IsValid() )
			_material = Material.FromShader( ShaderPath );
	}

	protected ParticleFunctionRenderer( ParticleDefinitionParser parse ) : base( parse )
	{
		PrepareMaterial();

		OnlyRenderInEffectsWaterPass = parse.Boolean( "m_bOnlyRenderInEffectsWaterPass", false );
		OnlyRenderInEffectsBloomPass = parse.Boolean( "m_bOnlyRenderInEffectsBloomPass", false );
		LightingMode = parse.Enum( "m_nLightingMode", LightingMode );
		RadiusScale = parse.NumberProvider( "m_flRadiusScale", RadiusScale );
		AlphaScale = parse.NumberProvider( "m_flAlphaScale", AlphaScale );
		ColorScale = parse.VectorProvider( "m_vecColorScale", ColorScale );
		OverbrightFactor = parse.NumberProvider( "m_flOverbrightFactor", OverbrightFactor );
		AddSelfAmount = parse.NumberProvider( "m_flAddSelfAmount", AddSelfAmount );
		DepthBias = parse.NumberProvider( "m_flDepthBias", DepthBias );
		SortMethod = parse.Enum( "m_nSortMethod", SortMethod );
		DiffuseAmount = parse.NumberProvider( "m_flDiffuseAmount", DiffuseAmount );
		SelfIllumAmount = parse.NumberProvider( "m_flSelfIllumAmount", SelfIllumAmount );
		Desaturation = parse.NumberProvider( "m_flDesaturation", Desaturation );
		HsvShiftControlPoint = parse.Int32( "m_nHSVShiftControlPoint", HsvShiftControlPoint );
		SourceAlphaValueToMapToZero = parse.NumberProvider( "m_flSourceAlphaValueToMapToZero", SourceAlphaValueToMapToZero );
		SourceAlphaValueToMapToOne = parse.NumberProvider( "m_flSourceAlphaValueToMapToOne", SourceAlphaValueToMapToOne );
		GammaCorrectVertexColors = parse.Boolean( "m_bGammaCorrectVertexColors", GammaCorrectVertexColors );
		MaxLuminanceFrameBlend = parse.Boolean( "m_bMaxLuminanceBlendingSequence0", MaxLuminanceFrameBlend );
		CenterXOffset = parse.NumberProvider( "m_flCenterXOffset", CenterXOffset );
		CenterYOffset = parse.NumberProvider( "m_flCenterYOffset", CenterYOffset );
		FeatheringMode = parse.Enum( "m_nFeatheringMode", FeatheringMode );
		FeatheringMinDist = parse.NumberProvider( "m_flFeatheringMinDist", FeatheringMinDist );
		FeatheringMaxDist = parse.NumberProvider( "m_flFeatheringMaxDist", FeatheringMaxDist );
		FeatheringFilter = parse.NumberProvider( "m_flFeatheringFilter", FeatheringFilter );

		GammaCorrectVertexColors &= OutputIsColor;
	}

	/// <summary>
	/// The sheet this renderer's sequences time against, for the simulation's sequence durations.
	/// </summary>
	public virtual ParticleSpriteSheet SpriteSheet => null;

	/// <summary>
	/// After each simulation step, with the particles as they are now.
	/// </summary>
	public virtual void Simulate( ParticleCollection particles, ParticleSystemState systemState )
	{
	}

	/// <summary>
	/// Once a frame after the simulation, for renderers that place things in the scene rather than draw.
	/// </summary>
	public virtual void Act( ParticleCollection particles, ParticleSystemState systemState )
	{
	}

	/// <summary>
	/// Hides what the renderer placed in the scene, while its system doesn't draw.
	/// </summary>
	public virtual void Hide()
	{
	}

	/// <summary>
	/// Draws the particles for a view. Called during the scene's render, in this renderer's pass.
	/// </summary>
	public abstract void Draw( ParticleCollection particles, ParticleSystemState systemState, in ParticleView view );

	public virtual void Dispose()
	{
		Quads.Dispose();
	}

	protected Vector3 GetHsvShift( ParticleSystemState systemState )
		=> HsvShiftControlPoint >= 0
			? systemState.GetControlPoint( HsvShiftControlPoint ).Position
			: new Vector3( 0f, 1f, 1f );

	protected Vector2 GetAlphaRemapRange( ParticleSystemState systemState )
		=> new( SourceAlphaValueToMapToZero.NextNumber( systemState ), SourceAlphaValueToMapToOne.NextNumber( systemState ) );

	/// <summary>
	/// Whether draws sort their particles back to front. Additive and modulate cards composite the same in any order.
	/// </summary>
	protected bool SortsByDistance( ParticleBlendMode blendMode )
		=> SortMethod == ParticleSortingChoiceList.PARTICLE_SORTING_NEAREST
			&& blendMode is not (ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ADD or ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_MOD2X);

	protected static void SortBackToFront( ReadOnlySpan<Particle> particles, Vector3 cameraPosition, Span<ulong> order )
	{
		for ( var i = 0; i < order.Length; i++ )
		{
			var distance = Vector3.DistanceSquared( particles[i].Position, cameraPosition );

			// A distance is never negative, so its bits order as an integer, and inverting them puts the farthest
			// first. The index below them keeps coincident particles in a steady order from frame to frame.
			order[i] = ((ulong)~BitConverter.SingleToUInt32Bits( distance ) << 32) | (uint)i;
		}

		order.Sort();
	}

	/// <summary>
	/// Sets the attributes every spritecard draw shares: the blend mode, colour and alpha treatment, lighting amounts
	/// and depth feathering.
	/// </summary>
	protected void SetSharedAttributes( ParticleSystemState systemState, ParticleBlendMode blendMode, bool opaque, bool blendFrames )
	{
		var attributes = Attributes;

		attributes.SetCombo( "D_MOD2X", blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_MOD2X && !opaque );
		attributes.SetCombo( "D_OPAQUE", opaque );

		attributes.Set( "BlendMode", (int)blendMode );
		attributes.Set( "OverbrightFactor", OverbrightFactor.NextNumber( systemState ) );
		attributes.Set( "AddSelfAmount", 1f + AddSelfAmount.NextNumber( systemState ) );
		attributes.Set( "DiffuseAmount", OutputIsColor ? DiffuseAmount.NextNumber( systemState ) : 0f );
		attributes.Set( "SelfIllumAmount", SelfIllumAmount.NextNumber( systemState ) );
		attributes.Set( "Desaturation", Desaturation.NextNumber( systemState ) );
		attributes.Set( "HsvShift", (global::Vector3)GetHsvShift( systemState ) );
		attributes.Set( "AlphaRemapRange", (global::Vector2)GetAlphaRemapRange( systemState ) );
		attributes.Set( "ColorScale", (global::Vector3)ColorScale.NextVector( systemState ) );
		attributes.Set( "GammaCorrectVertexColors", GammaCorrectVertexColors ? 1 : 0 );
		attributes.Set( "DepthBias", DepthBias.NextNumber( systemState ) );
		attributes.Set( "MaxLuminanceFrameBlend", MaxLuminanceFrameBlend ? 1 : 0 );
		attributes.Set( "BlendFrames", blendFrames ? 1 : 0 );
		attributes.Set( "Outline", 0 );

		attributes.Set( "FeatheringRange", WantsSceneDepth
			? new global::Vector2( FeatheringMinDist.NextNumber( systemState ), FeatheringMaxDist.NextNumber( systemState ) )
			: global::Vector2.Zero );
		attributes.Set( "FeatheringFilter", FeatheringFilter.NextNumber( systemState ) );
	}

	protected static Vector4 Rect( Vector2 min, Vector2 max ) => new( min.X, min.Y, max.X, max.Y );

	/// <summary>
	/// The sheet frame a particle shows, the next and how far between them, by the renderer's animation settings.
	/// </summary>
	protected static (int Frame, int NextFrame, float Blend) GetSheetFrame( ref Particle particle,
		ParticleSpriteSheet.Sequence sequence, float animationRate, ParticleAnimationType animationType, bool animateInFps )
	{
		var frameCount = sequence.Frames.Length;
		if ( frameCount < 2 )
			return (0, 0, 0f);

		var totalTime = sequence.EffectiveTotalTime;
		float passes;

		if ( animationType == ParticleAnimationType.ANIMATION_TYPE_MANUAL_FRAMES )
		{
			passes = particle.ManualAnimationFrame;
		}
		else
		{
			// The type picks the time; animating in FPS only changes how the rate is read.
			var animationTime = animationType switch
			{
				ParticleAnimationType.ANIMATION_TYPE_FIT_LIFETIME => particle.NormalizedAge,
				_ => particle.Age,
			};

			passes = animateInFps
				? animationTime * animationRate / totalTime
				: animationTime * animationRate;
		}

		var position = totalTime * (sequence.Clamp ? MathUtils.Saturate( passes ) : MathUtils.Fract( passes ));
		return sequence.GetFrameAtPosition( position );
	}

	/// <summary>
	/// A sheet sequence of a layer, by a particle's sequence number, or null when the layer has no sheet.
	/// </summary>
	protected static ParticleSpriteSheet.Sequence GetSequence( ParticleSpriteSheet sheet, int sequenceNumber )
	{
		if ( sheet is null || sheet.Sequences.Length == 0 )
			return null;

		var sequence = sheet.Sequences[(uint)sequenceNumber < (uint)sheet.Sequences.Length ? sequenceNumber : 0];
		return sequence.Frames.Length > 0 ? sequence : null;
	}

	/// <summary>
	/// Draws what was written to <see cref="Quads"/>.
	/// </summary>
	protected void Submit( int quadCount )
	{
		if ( quadCount <= 0 || Material is null )
			return;

		Quads.Draw( quadCount, Material, Attributes );
	}
}
