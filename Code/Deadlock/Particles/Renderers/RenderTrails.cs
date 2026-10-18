using System;
using Deadlock.Particles.Utils;

namespace Deadlock.Particles.Renderers;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

/// <summary>
/// Renders each particle as a quad stretched along its motion, from where it is back to where it was, with its
/// head and tail tapered and tinted separately.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderTrails">C_OP_RenderTrails</seealso>
internal sealed class RenderTrails : ParticleFunctionRenderer
{
	private const string DefaultTextureName = "materials/particle/base_trail.vtex";

	private readonly float animationRate = 0.1f;
	private readonly ParticleAnimationType animationType = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;
	private readonly bool animateInFps;
	private readonly bool blendFrames = true;

	// m_bEnableFadingAndClamping gates the size clamp, the distance fade and the view angle fade together.
	private readonly bool enableFadingAndClamping;
	private readonly float minSize;
	private readonly float maxSize = 2000f;
	private readonly INumberProvider startFadeSize = new LiteralNumberProvider( 1000f );
	private readonly INumberProvider endFadeSize = new LiteralNumberProvider( 2000f );
	private readonly float startFadeDot = 1f;
	private readonly float endFadeDot = 2f;

	private readonly ParticleBlendMode blendMode = ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA;
	private readonly ParticleTextureLayer[] layers;
	private readonly ParticleOrientation orientationType;
	private readonly ParticleField prevPositionSource = ParticleField.PositionPrevious;
	private readonly float maxLength = 2000f;
	private readonly float minLength;
	private readonly float lengthScale = 1f;
	private readonly float lengthFadeInTime;
	private readonly bool ignoreDeltaTime;
	private readonly float constrainRadiusToLengthRatio = 1f;
	private readonly float forwardShift;
	private readonly INumberProvider headRadiusTaper = new LiteralNumberProvider( 1f );
	private readonly INumberProvider tailRadiusTaper = new LiteralNumberProvider( 1f );
	private readonly INumberProvider headAlphaScale = new LiteralNumberProvider( 1f );
	private readonly INumberProvider tailAlphaScale = new LiteralNumberProvider( 1f );
	private readonly IVectorProvider headColorScale = new LiteralVectorProvider( Vector3.One );
	private readonly IVectorProvider tailColorScale = new LiteralVectorProvider( Vector3.One );

	private ulong[] drawOrder = [];

	public RenderTrails( ParticleDefinitionParser parse, DeadlockParticleSystem resource ) : base( parse )
	{
		blendMode = parse.Enum( "m_nOutputBlendMode", blendMode );
		layers = ParticleTextureLayer.Build( parse, resource, DefaultTextureName );

		orientationType = parse.Enum( "m_nOrientationType", orientationType );
		animationRate = parse.Float( "m_flAnimationRate", animationRate );
		maxLength = parse.Float( "m_flMaxLength", maxLength );
		minLength = parse.Float( "m_flMinLength", minLength );
		lengthScale = parse.Float( "m_flLengthScale", lengthScale );
		lengthFadeInTime = parse.Float( "m_flLengthFadeInTime", lengthFadeInTime );
		ignoreDeltaTime = parse.Boolean( "m_bIgnoreDT", ignoreDeltaTime );
		constrainRadiusToLengthRatio = parse.Float( "m_flConstrainRadiusToLengthRatio", constrainRadiusToLengthRatio );
		forwardShift = parse.Float( "m_flForwardShift", forwardShift );
		animationType = parse.Enum( "m_nAnimationType", animationType );
		animateInFps = parse.Boolean( "m_bAnimateInFPS", animateInFps );
		blendFrames = parse.Boolean( "m_bBlendFramesSeq0", blendFrames );
		enableFadingAndClamping = parse.Boolean( "m_bEnableFadingAndClamping", enableFadingAndClamping );
		minSize = parse.Float( "m_flMinSize", minSize );
		maxSize = parse.Float( "m_flMaxSize", maxSize );
		startFadeSize = parse.NumberProvider( "m_flStartFadeSize", startFadeSize );
		endFadeSize = parse.NumberProvider( "m_flEndFadeSize", endFadeSize );
		startFadeDot = parse.Float( "m_flStartFadeDot", startFadeDot );
		endFadeDot = parse.Float( "m_flEndFadeDot", endFadeDot );
		prevPositionSource = parse.ParticleField( "m_nPrevPntSource", prevPositionSource );
		headRadiusTaper = parse.NumberProvider( "m_flRadiusHeadTaper", headRadiusTaper );
		tailRadiusTaper = parse.NumberProvider( "m_flRadiusTaper", tailRadiusTaper );
		headAlphaScale = parse.NumberProvider( "m_flHeadAlphaScale", headAlphaScale );
		tailAlphaScale = parse.NumberProvider( "m_flTailAlphaScale", tailAlphaScale );
		headColorScale = parse.VectorProvider( "m_vecHeadColorScale", headColorScale );
		tailColorScale = parse.VectorProvider( "m_vecTailColorScale", tailColorScale );
	}

	public override ParticleSpriteSheet SpriteSheet => ParticleTextureLayer.FindSpriteSheet( layers );

	// The quad's corners in order around it: x across the ribbon, y from head (-1) to tail (+1).
	private static readonly Vector2[] Corners = [new( -1f, 1f ), new( -1f, -1f ), new( 1f, -1f ), new( 1f, 1f )];

	public override void Draw( ParticleCollection particles, ParticleSystemState systemState, in ParticleView view )
	{
		if ( particles.Count == 0 )
			return;

		var headColor = headColorScale.NextVector( systemState );
		var tailColor = tailColorScale.NextVector( systemState );

		// The moved distance becomes a velocity (distance / dt) before the trail length scales it, when the previous
		// point is the Verlet one; the operator can opt out of it.
		var usesVerletDelta = prevPositionSource == ParticleField.PositionPrevious;
		var oneOverDt = ignoreDeltaTime || !usesVerletDelta || particles.CurrentFrameTime == 0f
			? 1f
			: 1f / particles.CurrentFrameTime;

		var startFadeSlope = startFadeSize.NextNumber( systemState );
		var endFadeSlope = endFadeSize.NextNumber( systemState );
		var viewAngleFadeActive = startFadeDot < 1f && endFadeDot > startFadeDot;

		Span<ParticleTextureLayer.UvTransform> transforms = stackalloc ParticleTextureLayer.UvTransform[ParticleTextureLayer.MaxLayers];
		ParticleTextureLayer.ResolveUvTransforms( layers, systemState, transforms );

		Span<(Vector2 Min, Vector2 Max, Vector2 NextMin, Vector2 NextMax)> layerRects
			= stackalloc (Vector2, Vector2, Vector2, Vector2)[ParticleTextureLayer.MaxLayers];

		var current = particles.Current;
		var sorted = SortsByDistance( blendMode ) && current.Length > 1;

		if ( sorted )
		{
			if ( drawOrder.Length < current.Length )
				drawOrder = new ulong[current.Length];

			SortBackToFront( current, view.Location, drawOrder.AsSpan( 0, current.Length ) );
		}

		var vertices = Quads.Begin( current.Length );
		var maxQuads = vertices.Length / 4;
		var quadCount = 0;

		for ( var n = 0; n < current.Length && quadCount < maxQuads; n++ )
		{
			ref var particle = ref current[sorted ? (int)(uint)drawOrder[n] : n];
			var position = particle.Position;
			var previousPosition = particle.GetVector( prevPositionSource );

			// A particle that hasn't moved has no direction to run in.
			var difference = previousPosition - position;
			if ( difference == Vector3.Zero )
				continue;

			var direction = Vector3.Normalize( difference );
			var length = lengthScale * particle.TrailLength * difference.Length() * oneOverDt;

			// A particle drawn before its birth, with a negative age, gets the minimum length.
			length *= MathF.Min( 1f, particle.Age / MathF.Max( lengthFadeInTime, 1e-9f ) );
			length = MathF.Max( minLength, MathF.Min( maxLength, length ) );

			if ( length == 0f )
				continue;

			var particleRadius = particle.Radius * RadiusScale.NextNumber( ref particle, systemState );
			var colorFade = 1f;
			var alphaFade = 1f;

			if ( enableFadingAndClamping )
			{
				var cameraDistance = Vector3.Distance( view.Location, particle.Position );
				var fadeStart = startFadeSlope * cameraDistance;
				var fadeEnd = endFadeSlope * cameraDistance;

				if ( particleRadius > fadeStart )
				{
					if ( particleRadius >= fadeEnd )
						continue;

					colorFade = 1f - MathUtils.Remap( particleRadius, fadeStart, fadeEnd );
				}
			}

			if ( viewAngleFadeActive )
			{
				var toCamera = view.Location - particle.Position;

				if ( toCamera.LengthSquared() > ParticleMath.MinimumLengthSquared )
				{
					// Only the normal aligned mode has a normal to face with; the others use the ribbon's direction.
					var facingAxis = orientationType == ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL
						? particle.Normal
						: direction;
					var facing = MathF.Abs( Vector3.Dot( Vector3.Normalize( facingAxis ), Vector3.Normalize( toCamera ) ) );
					alphaFade = 1f - MathUtils.Smoothstep( startFadeDot, endFadeDot, facing );
				}
			}

			// A short trail is narrowed so it can't render wider than it is long.
			var radius = MathF.Min( particleRadius, constrainRadiusToLengthRatio * length );

			// The ribbon runs along the motion; the orientation picks the plane it's flattened into.
			var planeNormal = orientationType switch
			{
				ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL => particle.Normal,
				ParticleOrientation.PARTICLE_ORIENTATION_WORLD_Z_ALIGNED => Vector3.UnitZ,
				_ => view.Location - position,
			};

			var widthAxis = Vector3.Cross( direction, planeNormal );
			widthAxis = widthAxis.LengthSquared() > ParticleMath.MinimumLengthSquared
				? Vector3.Normalize( widthAxis )
				: Vector3.Normalize( Vector3.Cross( direction, MathF.Abs( direction.Z ) < 0.999f ? Vector3.UnitZ : Vector3.UnitX ) );

			var halfLength = length * 0.5f;

			// The trail slides along the motion by m_flForwardShift lengths; direction runs backwards along travel.
			var center = position + (direction * (length * (0.5f - forwardShift)));

			var headHalfWidth = radius * headRadiusTaper.NextNumber( ref particle, systemState );
			var tailHalfWidth = radius * tailRadiusTaper.NextNumber( ref particle, systemState );

			// Each end is clamped against its own distance.
			if ( enableFadingAndClamping )
			{
				var headDistance = Vector3.Distance( view.Location, center - (direction * halfLength) );
				var tailDistance = Vector3.Distance( view.Location, center + (direction * halfLength) );
				headHalfWidth = MathF.Min( MathF.Max( headHalfWidth, minSize * headDistance ), maxSize * headDistance );
				tailHalfWidth = MathF.Min( MathF.Max( tailHalfWidth, minSize * tailDistance ), maxSize * tailDistance );
			}

			var frameBlend = 0f;

			for ( var layer = 0; layer < layers.Length; layer++ )
			{
				var min = Vector2.Zero;
				var max = Vector2.One;
				var nextMin = min;
				var nextMax = max;

				if ( GetSequence( layers[layer].SpriteSheet, particle.SequenceNumber ) is { } sequence
					&& sequence.Frames[0].Images.Length > 0 )
				{
					var (frame, nextFrame, blend) = GetSheetFrame( ref particle, sequence, animationRate, animationType, animateInFps );
					var currentImage = sequence.Frames[frame].Images[0];
					var nextImage = sequence.Frames[nextFrame].Images.Length > 0 ? sequence.Frames[nextFrame].Images[0] : currentImage;

					min = currentImage.UncroppedMin;
					max = currentImage.UncroppedMax;
					nextMin = nextImage.UncroppedMin;
					nextMax = nextImage.UncroppedMax;

					if ( layer == 0 )
						frameBlend = blend;
				}

				layerRects[layer] = (min, max, nextMin, nextMax);
			}

			var alpha = particle.Alpha * particle.AlphaAlternate * colorFade * alphaFade * AlphaScale.NextNumber( ref particle, systemState );
			var tint = particle.Color * colorFade;
			var head = Vector4.Clamp( new Vector4( tint * headColor, alpha * headAlphaScale.NextNumber( ref particle, systemState ) ), Vector4.Zero, Vector4.One );
			var tail = Vector4.Clamp( new Vector4( tint * tailColor, alpha * tailAlphaScale.NextNumber( ref particle, systemState ) ), Vector4.Zero, Vector4.One );

			var lightingPosition = new global::Vector4( position.X, position.Y, position.Z, 0f );

			for ( var corner = 0; corner < 4; corner++ )
			{
				ref var vertex = ref vertices[quadCount * 4 + corner];
				var c = Corners[corner];
				var isHead = c.Y < 0f;
				var cardUv = (c * 0.5f) + new Vector2( 0.5f );
				var color = isHead ? head : tail;

				vertex.Position = center + (widthAxis * (c.X * (isHead ? headHalfWidth : tailHalfWidth))) + (direction * (halfLength * c.Y));
				vertex.Color = new global::Vector4( color.X, color.Y, color.Z, color.W );
				vertex.Card = new global::Vector4( frameBlend, cardUv.X, cardUv.Y, 0f );
				vertex.LightingPosition = lightingPosition;

				for ( var layer = 0; layer < layers.Length; layer++ )
				{
					var (min, max, nextMin, nextMax) = layerRects[layer];
					var uv = ParticleTextureLayer.PlaceCorner( cardUv, transforms[layer], min, max );
					var uvNext = ParticleTextureLayer.PlaceCorner( cardUv, transforms[layer], nextMin, nextMax );
					vertex.SetLayer( layer, new global::Vector4( uv.X, uv.Y, uvNext.X, uvNext.Y ) );
				}
			}

			quadCount++;
		}

		if ( quadCount == 0 )
			return;

		SetSharedAttributes( systemState, blendMode, false, blendFrames );
		ParticleTextureLayer.Bind( Attributes, layers, systemState );
		Submit( quadCount );
	}
}
