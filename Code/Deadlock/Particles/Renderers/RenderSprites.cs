using System;
using System.Numerics;
using Deadlock.Particles.Utils;

namespace Deadlock.Particles.Renderers;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

/// <summary>
/// Renders particles as camera facing or orientation aligned textured quads (sprites), with sprite sheet animation,
/// blend modes and per particle colour and alpha. The workhorse renderer most effects use.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderSprites">C_OP_RenderSprites</seealso>
internal sealed class RenderSprites : ParticleFunctionRenderer
{
	private const string DefaultTextureName = "materials/particle/base_sprite.vtex";

	private readonly ParticleTextureLayer[] layers;

	private readonly float animationRate = 0.1f;
	private readonly ParticleAnimationType animationType = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;
	private readonly INumberProvider minSize = new LiteralNumberProvider( 0f );
	private readonly INumberProvider maxSize = new LiteralNumberProvider( 5000f );
	private readonly INumberProvider startFadeSize = new LiteralNumberProvider( 100000000f );
	private readonly INumberProvider endFadeSize = new LiteralNumberProvider( 200000000f );

	/// <summary>
	/// The signed distance field alpha treatment, which drives soft edges and outlines.
	/// </summary>
	private readonly bool distanceAlpha;
	private readonly bool softEdges;
	private readonly float edgeSoftnessStart = 0.6f;
	private readonly float edgeSoftnessEnd = 0.5f;

	// The normal aligned modes fade out as the card turns edge on to the camera. The defaults span 1..2 against a
	// value that never exceeds 1, so no fade by default.
	private readonly float startFadeDot = 1f;
	private readonly float endFadeDot = 2f;

	private readonly bool blendFrames = true;
	private readonly bool animateInFps;
	private readonly ParticleBlendMode blendMode = ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA;
	private readonly ParticleOrientation orientationType;

	private readonly bool outline;
	private readonly Vector4 outlineColor = Vector4.One;
	private readonly Vector4 outlineRanges = new( 0.5f, 0.7f, 0.6f, 0.8f );

	private ulong[] drawOrder = [];

	public RenderSprites( ParticleDefinitionParser parse, DeadlockParticleSystem resource ) : base( parse )
	{
		blendMode = parse.Enum( "m_nOutputBlendMode", blendMode );
		layers = ParticleTextureLayer.Build( parse, resource, DefaultTextureName );

		animateInFps = parse.Boolean( "m_bAnimateInFPS", animateInFps );
		orientationType = parse.Enum( "m_nOrientationType", orientationType );
		animationRate = parse.Float( "m_flAnimationRate", animationRate );
		minSize = parse.NumberProvider( "m_flMinSize", minSize );
		maxSize = parse.NumberProvider( "m_flMaxSize", maxSize );
		startFadeSize = parse.NumberProvider( "m_flStartFadeSize", startFadeSize );
		endFadeSize = parse.NumberProvider( "m_flEndFadeSize", endFadeSize );
		distanceAlpha = parse.Boolean( "m_bDistanceAlpha", distanceAlpha );
		softEdges = parse.Boolean( "m_bSoftEdges", softEdges );
		edgeSoftnessStart = parse.Float( "m_flEdgeSoftnessStart", edgeSoftnessStart );
		edgeSoftnessEnd = parse.Float( "m_flEdgeSoftnessEnd", edgeSoftnessEnd );
		startFadeDot = parse.Float( "m_flStartFadeDot", startFadeDot );
		endFadeDot = parse.Float( "m_flEndFadeDot", endFadeDot );
		animationType = parse.BehaviorVersion >= 10
			? parse.Enum( "m_nAnimationType", animationType )
			: ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;
		blendFrames = parse.Boolean( "m_bBlendFramesSeq0", blendFrames );

		outline = parse.Boolean( "m_bOutline", outline );

		if ( outline )
		{
			var color = parse.Color24( "m_OutlineColor", new Vector3( 1f ) );
			outlineColor = new Vector4( color, parse.Int32( "m_nOutlineAlpha", 255 ) / 255f );
			outlineRanges = new Vector4(
				parse.Float( "m_flOutlineStart0", outlineRanges.X ),
				parse.Float( "m_flOutlineEnd0", outlineRanges.Y ),
				parse.Float( "m_flOutlineStart1", outlineRanges.Z ),
				parse.Float( "m_flOutlineEnd1", outlineRanges.W ) );
		}
	}

	public override ParticleSpriteSheet SpriteSheet => ParticleTextureLayer.FindSpriteSheet( layers );

	private readonly record struct CropWindow( Vector2 Min, Vector2 Max )
	{
		public static CropWindow Full { get; } = new( Vector2.Zero, Vector2.One );

		public Vector2 Size => Max - Min;
	}

	private readonly record struct LayerFrames( ParticleSpriteSheet.Image Current, ParticleSpriteSheet.Image Next, float Blend );

	private static Vector2 Normalize( Vector2 point, Vector2 min, Vector2 size ) => new(
		size.X != 0f ? (point.X - min.X) / size.X : 0f,
		size.Y != 0f ? (point.Y - min.Y) / size.Y : 0f );

	private static CropWindow TileWindow( ParticleSpriteSheet.Image image )
	{
		var size = image.UncroppedMax - image.UncroppedMin;

		return new CropWindow(
			Normalize( image.CroppedMin, image.UncroppedMin, size ),
			Normalize( image.CroppedMax, image.UncroppedMin, size ) );
	}

	private static (Vector2 Min, Vector2 Max) WindowRect( ParticleSpriteSheet.Image image, CropWindow window )
	{
		var size = image.UncroppedMax - image.UncroppedMin;

		return (image.UncroppedMin + (window.Min * size), image.UncroppedMin + (window.Max * size));
	}

	private LayerFrames? GetLayerFrames( int layer, ref Particle particle )
	{
		var sequence = GetSequence( layers[layer].SpriteSheet, layer > 0 ? particle.SecondSequenceNumber : particle.SequenceNumber );
		if ( sequence is null )
			return null;

		var (frame, nextFrame, blend) = GetSheetFrame( ref particle, sequence, animationRate, animationType, animateInFps );

		return new LayerFrames( LayerImage( sequence.Frames[frame], layer ), LayerImage( sequence.Frames[nextFrame], layer ), blend );
	}

	private static ParticleSpriteSheet.Image LayerImage( ParticleSpriteSheet.Frame frame, int layer )
		=> frame.Images.Length == 0 ? null : frame.Images[Math.Min( layer, frame.Images.Length - 1 )];

	// Cropped frames shrink the card to what they draw: the window over both frames being blended.
	private static CropWindow GetCardCropWindow( LayerFrames? frames )
	{
		if ( frames is not { } cardFrames || cardFrames.Current is null || cardFrames.Next is null )
			return CropWindow.Full;

		var current = TileWindow( cardFrames.Current );
		var next = TileWindow( cardFrames.Next );

		return new CropWindow( Vector2.Min( current.Min, next.Min ), Vector2.Max( current.Max, next.Max ) );
	}

	private static (Vector2 UvMin, Vector2 UvMax, Vector2 NextMin, Vector2 NextMax) GetLayerSheetUvs( LayerFrames? frames, CropWindow window )
	{
		if ( frames is not { } layerFrames || layerFrames.Current is null || layerFrames.Next is null )
			return (Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.One);

		var (uvMin, uvMax) = WindowRect( layerFrames.Current, window );
		var (nextMin, nextMax) = WindowRect( layerFrames.Next, window );

		return (uvMin, uvMax, nextMin, nextMax);
	}

	// The quad's (right, up) axes from a base pair with the particle's roll folded in, as the spritecard vertex
	// shader builds them. They're intentionally not renormalized: some modes rely on that.
	private static (Vector3 Right, Vector3 Up) QuadBasis( Vector3 baseRight, Vector3 baseUp, float roll, float yaw )
	{
		var c = MathF.Cos( roll );
		var s = MathF.Sin( roll );
		var right = (baseRight * c) + (baseUp * s);
		var up = (baseUp * c) - (baseRight * s);

		// Yaw turns the card about its own up axis, foreshortening it horizontally to nothing at 90 degrees.
		if ( yaw != 0f && up.LengthSquared() > ParticleMath.MinimumLengthSquared )
			right = Vector3.Transform( right, Matrix4x4.CreateFromAxisAngle( Vector3.Normalize( up ), yaw ) );

		return (right, up);
	}

	private static (Vector3 Right, Vector3 Up) RotationBasis( Matrix4x4 rotation )
		=> (new Vector3( rotation.M11, rotation.M12, rotation.M13 ), new Vector3( rotation.M21, rotation.M22, rotation.M23 ));

	// SCREEN_ALIGNED: the plain camera billboard.
	private static (Vector3 Right, Vector3 Up) ScreenAlignedBasis( in ParticleView view, float roll, float yaw )
		=> QuadBasis( view.Right, view.Up, roll, yaw );

	// SCREEN_Z_ALIGNED: up locked to world +Z, so the sprite yaws to face the camera and foreshortens as the view tilts.
	private static (Vector3 Right, Vector3 Up) ScreenZAlignedBasis( in ParticleView view, float roll, float yaw )
		=> QuadBasis( Vector3.Cross( Vector3.UnitZ, view.Forward ), Vector3.UnitZ, roll, yaw );

	// WORLD_Z_ALIGNED: the quad lies flat in the world's XY plane, independent of the camera.
	private static (Vector3 Right, Vector3 Up) WorldZAlignedBasis( float roll, float yaw )
		=> QuadBasis( new Vector3( 0f, -1f, 0f ), new Vector3( 1f, 0f, 0f ), roll, yaw );

	// ALIGN_TO_PARTICLE_NORMAL: the quad's plane is perpendicular to the particle normal.
	private static (Vector3 Right, Vector3 Up) ParticleNormalBasis( Vector3 normal, float roll, float yaw )
	{
		var reference = MathF.Abs( normal.Z ) > 0.1f ? new Vector3( 0f, -1f, 0f ) : new Vector3( 0f, 0f, 1f );
		var up = Vector3.Normalize( Vector3.Cross( normal, reference ) );
		var right = Vector3.Cross( up, normal );
		return QuadBasis( right, up, roll, yaw );
	}

	// SCREENALIGN_TO_PARTICLE_NORMAL: the right edge follows the particle normal while the card turns toward the camera.
	private static (Vector3 Right, Vector3 Up) ScreenAlignToNormalBasis( in ParticleView view, Vector3 normal, float roll, float yaw )
	{
		var n = Vector3.Normalize( normal );
		var w = Vector3.Cross( n, view.Forward );

		if ( w.LengthSquared() < ParticleMath.MinimumLengthSquared )
			return (view.Right, view.Up);

		return QuadBasis( n, Vector3.Normalize( w ), roll, yaw );
	}

	// The card's corners, in order around it, and where each is on the card.
	private static readonly Vector2[] Corners = [new( -1f, 1f ), new( -1f, -1f ), new( 1f, -1f ), new( 1f, 1f )];
	private static readonly Vector2[] CardUvs = [new( 0f, 0f ), new( 0f, 1f ), new( 1f, 1f ), new( 1f, 0f )];

	public override void Draw( ParticleCollection particles, ParticleSystemState systemState, in ParticleView view )
	{
		if ( particles.Count == 0 )
			return;

		var minSizeSlope = minSize.NextNumber( systemState );
		var maxSizeSlope = maxSize.NextNumber( systemState );
		var startFadeSlope = startFadeSize.NextNumber( systemState );
		var endFadeSlope = endFadeSize.NextNumber( systemState );

		var centerOffset = new Vector2( CenterXOffset.NextNumber( systemState ), CenterYOffset.NextNumber( systemState ) );

		// Only the two normal aligned modes fade by view angle, and only when the range can actually be entered.
		var viewAngleFadeActive = startFadeDot < 1f
			&& endFadeDot > startFadeDot
			&& orientationType is ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL
				or ParticleOrientation.PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL;

		Span<ParticleTextureLayer.UvTransform> transforms = stackalloc ParticleTextureLayer.UvTransform[ParticleTextureLayer.MaxLayers];
		ParticleTextureLayer.ResolveUvTransforms( layers, systemState, transforms );

		var current = particles.Current;
		var sorted = SortsByDistance( blendMode ) && current.Length > 1;

		if ( sorted )
		{
			if ( drawOrder.Length < current.Length )
				drawOrder = new ulong[current.Length];

			SortBackToFront( current, view.Location, drawOrder.AsSpan( 0, current.Length ) );
		}

		var vertices = Quads.Begin( current.Length );
		var quadCount = 0;
		var maxQuads = vertices.Length / 4;

		Span<Vector4> rects = stackalloc Vector4[ParticleTextureLayer.MaxLayers * 2];

		for ( var n = 0; n < current.Length && quadCount < maxQuads; n++ )
		{
			ref var particle = ref current[sorted ? (int)(uint)drawOrder[n] : n];
			var radiusScale = RadiusScale.NextNumber( ref particle, systemState );

			// The size fade scales colour and alpha alike; the view angle fade touches alpha only.
			var colorFade = 1f;
			var alphaFade = 1f;

			if ( viewAngleFadeActive )
			{
				var toCamera = view.Location - particle.Position;

				if ( toCamera.LengthSquared() > ParticleMath.MinimumLengthSquared )
				{
					var facing = MathF.Abs( Vector3.Dot( Vector3.Normalize( particle.Normal ), Vector3.Normalize( toCamera ) ) );
					alphaFade = 1f - MathUtils.Smoothstep( startFadeDot, endFadeDot, facing );
				}
			}

			var cameraDistance = Vector3.Distance( view.Location, particle.Position );
			var radius = particle.Radius * radiusScale;
			var fadeStart = startFadeSlope * cameraDistance;
			var fadeEnd = endFadeSlope * cameraDistance;

			// The fade reads the raw radius, independently of the size clamp below.
			if ( radius > fadeStart )
			{
				if ( radius >= fadeEnd )
					continue;

				colorFade = 1f - MathUtils.Remap( radius, fadeStart, fadeEnd );
			}

			// Nested min/max rather than a clamp, so an inverted range resolves to the maximum.
			if ( particle.Radius > 0f )
				radiusScale = MathF.Min( MathF.Max( radius, minSizeSlope * cameraDistance ), maxSizeSlope * cameraDistance ) / particle.Radius;

			var alpha = particle.Alpha * AlphaScale.NextNumber( ref particle, systemState ) * colorFade * alphaFade;
			var halfWidth = particle.Radius * radiusScale;

			if ( halfWidth <= 0f || alpha < 1f / 255f )
				continue;

			var roll = particle.Rotation.Z;
			var yaw = particle.Rotation.X;
			var (right, up) = orientationType switch
			{
				ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED => ScreenAlignedBasis( view, roll, yaw ),
				ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_Z_ALIGNED => ScreenZAlignedBasis( view, roll, yaw ),
				ParticleOrientation.PARTICLE_ORIENTATION_WORLD_Z_ALIGNED => WorldZAlignedBasis( roll, yaw ),
				ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL => ParticleNormalBasis( particle.Normal, roll, yaw ),
				ParticleOrientation.PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL => ScreenAlignToNormalBasis( view, particle.Normal, roll, yaw ),
				_ => RotationBasis( particle.GetRotationMatrix() ),
			};

			right *= halfWidth;
			up *= halfWidth;

			// The centre offset is measured in half widths, along the card's own axes.
			var origin = particle.Position + (centerOffset.X * right) + (centerOffset.Y * up);

			var cardFrames = GetLayerFrames( 0, ref particle );
			var window = GetCardCropWindow( cardFrames );
			var windowSize = window.Size;

			origin += ((window.Max.X + window.Min.X - 1f) * right) + ((1f - window.Min.Y - window.Max.Y) * up);

			right *= windowSize.X;
			up *= windowSize.Y;

			// Each layer resolves rects against its own sheet, timed by the base sequence.
			for ( var layer = 0; layer < layers.Length; layer++ )
			{
				var (uvMin, uvMax, nextMin, nextMax) = GetLayerSheetUvs( layer == 0 ? cardFrames : GetLayerFrames( layer, ref particle ), window );
				rects[layer * 2] = Rect( uvMin, uvMax );
				rects[layer * 2 + 1] = Rect( nextMin, nextMax );
			}

			var color = new global::Vector4( particle.Color.X * colorFade, particle.Color.Y * colorFade, particle.Color.Z * colorFade, alpha );
			var card = cardFrames?.Blend ?? 0f;
			var lightingPosition = new global::Vector4( particle.Position.X, particle.Position.Y, particle.Position.Z, 0f );

			for ( var corner = 0; corner < 4; corner++ )
			{
				ref var vertex = ref vertices[quadCount * 4 + corner];
				var c = Corners[corner];
				var cardUv = CardUvs[corner];

				vertex.Position = origin + (right * c.X) + (up * c.Y);
				vertex.Color = color;
				vertex.Card = new global::Vector4( card, cardUv.X, cardUv.Y, 0f );
				vertex.LightingPosition = lightingPosition;

				for ( var layer = 0; layer < layers.Length; layer++ )
				{
					var rect = rects[layer * 2];
					var next = rects[layer * 2 + 1];
					var uv = ParticleTextureLayer.PlaceCorner( cardUv, transforms[layer], new Vector2( rect.X, rect.Y ), new Vector2( rect.Z, rect.W ) );
					var uvNext = ParticleTextureLayer.PlaceCorner( cardUv, transforms[layer], new Vector2( next.X, next.Y ), new Vector2( next.Z, next.W ) );
					vertex.SetLayer( layer, new global::Vector4( uv.X, uv.Y, uvNext.X, uvNext.Y ) );
				}
			}

			quadCount++;
		}

		if ( quadCount == 0 )
			return;

		SetSharedAttributes( systemState, blendMode, false, blendFrames );
		ParticleTextureLayer.Bind( Attributes, layers, systemState );

		Attributes.Set( "Outline", outline ? 1 : 0 );
		Attributes.Set( "OutlineColor", new global::Vector4( outlineColor.X, outlineColor.Y, outlineColor.Z, outlineColor.W ) );
		Attributes.Set( "OutlineRanges", new global::Vector4( outlineRanges.X, outlineRanges.Y, outlineRanges.Z, outlineRanges.W ) );

		// Distance alpha renders a signed distance field through the alpha remap smoothstep: the edge softness pair
		// replaces the remap range, or a hard threshold just under 0.5.
		if ( distanceAlpha )
		{
			var remap = softEdges
				? new Vector2( edgeSoftnessEnd, edgeSoftnessStart )
				: new Vector2( GetAlphaRemapRange( systemState ).X, 0.499f );
			Attributes.Set( "AlphaRemapRange", (global::Vector2)remap );
		}

		Submit( quadCount );
	}
}
