using System;
using Deadlock.Particles.Utils;

namespace Deadlock.Particles.Renderers;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

/// <summary>
/// Renders the particles as a ribbon through them in order: a Catmull-Rom spline, tessellated by how much of the
/// screen it covers, its texture running along it by world distance.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderRopes">C_OP_RenderRopes</seealso>
internal sealed class RenderRopes : ParticleFunctionRenderer
{
	private const string DefaultTextureName = "materials/particle/base_trail.vtex";
	private const int MaxTessellationLevel = 7;

	private readonly ParticleBlendMode blendMode = ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA;
	private readonly ParticleTextureLayer[] layers;
	private readonly ParticleOrientation orientationType = ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED;
	private readonly ParticleField orientationField = ParticleField.Normal;
	private readonly float animationRate = 0.1f;
	private readonly ParticleAnimationType animationType = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;
	private readonly bool animateInFps;
	private readonly bool blendFrames = true;
	private readonly int minTessellation = 1;
	private readonly int maxTessellation = 128;
	private readonly float tessScale = 1f;
	private readonly INumberProvider textureVWorldSize = new LiteralNumberProvider( 10f );
	private readonly INumberProvider textureVScrollRate = new LiteralNumberProvider( 0f );
	private readonly INumberProvider textureVOffset = new LiteralNumberProvider( 0f );
	private readonly int textureVParamsControlPoint = -1;
	private readonly bool clampV;
	private readonly int scaleControlPoint1 = -1;
	private readonly int scaleControlPoint2 = -1;
	private readonly float scaleVSizeByControlPointDistance;
	private readonly float scaleVScrollByControlPointDistance;
	private readonly float scaleVOffsetByControlPointDistance;
	private readonly bool useScalarForTextureCoordinate;
	private readonly ParticleField scalarFieldForTextureCoordinate = ParticleField.CreationTime;
	private readonly float scalarAttributeTextureCoordScale = 1f;
	private readonly bool reverseOrder;
	private readonly bool closedLoop;
	private readonly ParticleField splitField = ParticleField.RopeSegmentId;
	private readonly bool sortBySegmentId;
	private readonly bool drawAsOpaque;
	private readonly bool enableFadingAndClamping;
	private readonly float minSize;
	private readonly float maxSize = 2000f;
	private readonly INumberProvider startFadeSize = new LiteralNumberProvider( 1000f );
	private readonly INumberProvider endFadeSize = new LiteralNumberProvider( 2000f );
	private readonly float startFadeDot = 1f;
	private readonly float endFadeDot = 2f;

	private RopeNode[] nodeScratch = [];
	private long[] sortKeyScratch = [];
	private readonly Dictionary<float, int> segmentRanks = [];

	private struct RopeNode
	{
		public float Segment;
		public Vector3 Position;
		public float Radius;
		public Vector3 Color;
		public float Alpha;
		public Vector3 Orientation;
		public float ScalarCoordinate;
	}

	private struct RopeSample
	{
		public Vector3 Position;
		public Vector3 WidthAxis;
		public Vector3 Tangent;
		public float Radius;
		public Vector4 Color;
	}

	private readonly struct SheetFrame
	{
		public Vector2 Offset { get; init; }
		public Vector2 Scale { get; init; }
		public Vector2 NextOffset { get; init; }
		public Vector2 NextScale { get; init; }
		public float Blend { get; init; }
	}

	public RenderRopes( ParticleDefinitionParser parse, DeadlockParticleSystem resource ) : base( parse )
	{
		blendMode = parse.Enum( "m_nOutputBlendMode", blendMode );
		layers = ParticleTextureLayer.Build( parse, resource, DefaultTextureName );

		orientationType = parse.Enum( "m_nOrientationType", orientationType );
		orientationField = parse.ParticleField( "m_nVectorFieldForOrientation", orientationField );
		animationRate = parse.Float( "m_flAnimationRate", animationRate );
		animationType = parse.Enum( "m_nAnimationType", animationType );
		animateInFps = parse.Boolean( "m_bAnimateInFPS", animateInFps );
		blendFrames = parse.Boolean( "m_bBlendFramesSeq0", blendFrames );
		minTessellation = parse.Int32( "m_nMinTesselation", minTessellation );
		maxTessellation = parse.Int32( "m_nMaxTesselation", maxTessellation );
		tessScale = parse.Float( "m_flTessScale", tessScale );
		textureVWorldSize = parse.NumberProvider( "m_flTextureVWorldSize", textureVWorldSize );
		textureVScrollRate = parse.NumberProvider( "m_flTextureVScrollRate", textureVScrollRate );
		textureVOffset = parse.NumberProvider( "m_flTextureVOffset", textureVOffset );
		textureVParamsControlPoint = parse.Int32( "m_nTextureVParamsCP", textureVParamsControlPoint );
		clampV = parse.Boolean( "m_bClampV", clampV );
		scaleControlPoint1 = parse.Int32( "m_nScaleCP1", scaleControlPoint1 );
		scaleControlPoint2 = parse.Int32( "m_nScaleCP2", scaleControlPoint2 );
		scaleVSizeByControlPointDistance = parse.Float( "m_flScaleVSizeByControlPointDistance", scaleVSizeByControlPointDistance );
		scaleVScrollByControlPointDistance = parse.Float( "m_flScaleVScrollByControlPointDistance", scaleVScrollByControlPointDistance );
		scaleVOffsetByControlPointDistance = parse.Float( "m_flScaleVOffsetByControlPointDistance", scaleVOffsetByControlPointDistance );
		useScalarForTextureCoordinate = parse.Boolean( "m_bUseScalarForTextureCoordinate", useScalarForTextureCoordinate );
		scalarFieldForTextureCoordinate = parse.ParticleField( "m_nScalarFieldForTextureCoordinate", scalarFieldForTextureCoordinate );
		scalarAttributeTextureCoordScale = parse.Float( "m_flScalarAttributeTextureCoordScale", scalarAttributeTextureCoordScale );
		reverseOrder = parse.Boolean( "m_bReverseOrder", reverseOrder );
		closedLoop = parse.Boolean( "m_bClosedLoop", closedLoop );
		splitField = parse.ParticleField( "m_nSplitField", splitField );
		sortBySegmentId = parse.Boolean( "m_bSortBySegmentID", sortBySegmentId );
		drawAsOpaque = parse.Boolean( "m_bDrawAsOpaque", drawAsOpaque );
		enableFadingAndClamping = parse.Boolean( "m_bEnableFadingAndClamping", enableFadingAndClamping );
		minSize = parse.Float( "m_flMinSize", minSize );
		maxSize = parse.Float( "m_flMaxSize", maxSize );
		startFadeSize = parse.NumberProvider( "m_flStartFadeSize", startFadeSize );
		endFadeSize = parse.NumberProvider( "m_flEndFadeSize", endFadeSize );
		startFadeDot = parse.Float( "m_flStartFadeDot", startFadeDot );
		endFadeDot = parse.Float( "m_flEndFadeDot", endFadeDot );

		// Anything outside 0..4 becomes 4.
		if ( (uint)orientationType > (uint)ParticleOrientation.PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL )
			orientationType = ParticleOrientation.PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL;

		Pass = drawAsOpaque ? ParticleRenderPass.Opaque : ParticleRenderPass.Translucent;
	}

	public override ParticleSpriteSheet SpriteSheet => ParticleTextureLayer.FindSpriteSheet( layers );

	private int ResolveIndex( int index, int count )
	{
		if ( closedLoop )
		{
			if ( index < 0 )
				return count - 1;

			return index >= count ? index - count : index;
		}

		return Math.Clamp( index, 0, count - 1 );
	}

	private int ComputeTessellationLevel( ReadOnlySpan<RopeNode> nodes, in ParticleView view )
	{
		var min = new Vector3( float.MaxValue );
		var max = new Vector3( float.MinValue );

		foreach ( ref readonly var node in nodes )
		{
			min = Vector3.Min( min, node.Position );
			max = Vector3.Max( max, node.Position );
		}

		var diagonal = Vector3.Distance( max, min );
		var centre = (min + max) * 0.5f;
		var distanceSquared = Vector3.DistanceSquared( centre, view.Location );
		var coverage = distanceSquared <= diagonal * diagonal
			? 1f
			: MathUtils.Saturate( diagonal * view.ProjectionScale / MathF.Sqrt( distanceSquared ) );

		var metric = MathF.Sqrt( coverage ) * 1024f / nodes.Length;
		var raw = (int)(metric * tessScale * 0.1f);
		var subdivisions = maxTessellation >= raw ? raw : maxTessellation;

		if ( minTessellation > subdivisions )
			subdivisions = minTessellation;

		if ( subdivisions is > 128 or < 1 )
			subdivisions = subdivisions > 128 ? 128 : 1;

		var level = 0;
		while ( level < MaxTessellationLevel && 1 << level < subdivisions )
			level++;

		return level;
	}

	private (float ScrollRate, float Offset, float OneOverWorldSize) ResolveTextureV( ParticleSystemState systemState )
	{
		var scrollRate = textureVScrollRate.NextNumber( systemState );
		var offset = textureVOffset.NextNumber( systemState );
		var worldSize = textureVWorldSize.NextNumber( systemState );

		if ( textureVParamsControlPoint >= 0 )
		{
			var parameters = systemState.GetControlPoint( textureVParamsControlPoint ).Position;
			return (parameters.Y, parameters.Z, 1f / MathF.Max( float.Epsilon, parameters.X ));
		}

		var modulated = (scaleVSizeByControlPointDistance > 0f
				|| scaleVScrollByControlPointDistance > 0f
				|| scaleVOffsetByControlPointDistance > 0f)
			&& scaleControlPoint1 > -1
			&& scaleControlPoint2 > -1;

		if ( modulated )
		{
			var distance = Vector3.Distance(
				systemState.GetControlPoint( scaleControlPoint1 ).Position,
				systemState.GetControlPoint( scaleControlPoint2 ).Position );

			if ( scaleVSizeByControlPointDistance > 0f )
				worldSize = (scaleVSizeByControlPointDistance * distance) + worldSize;

			if ( scaleVScrollByControlPointDistance > 0f )
				scrollRate = (scaleVScrollByControlPointDistance * distance) + scrollRate;

			if ( scaleVOffsetByControlPointDistance > 0f )
				offset = (scaleVOffsetByControlPointDistance * distance) + offset;
		}

		return (scrollRate, offset, 1f / MathF.Max( float.Epsilon, worldSize ));
	}

	private int CollectNodes( ParticleCollection particles, ParticleSystemState systemState, in ParticleView view )
	{
		var count = particles.Count;
		if ( nodeScratch.Length < count )
			nodeScratch = new RopeNode[count];

		var startFadeSlope = startFadeSize.NextNumber( systemState );
		var endFadeSlope = endFadeSize.NextNumber( systemState );
		var splits = splitField is >= 0 and not ParticleField.NoneDisabled;

		for ( var i = 0; i < count; i++ )
		{
			ref var particle = ref particles.Current[reverseOrder ? count - i - 1 : i];
			var radius = particle.Radius * RadiusScale.NextNumber( ref particle, systemState );
			var alpha = particle.Alpha * particle.AlphaAlternate * AlphaScale.NextNumber( ref particle, systemState );

			if ( enableFadingAndClamping )
			{
				var cameraDistance = Vector3.Distance( view.Location, particle.Position );
				var fadeStart = startFadeSlope * cameraDistance;
				var fadeEnd = endFadeSlope * cameraDistance;

				if ( radius > fadeStart && fadeEnd > fadeStart )
					alpha *= MathF.Max( 0f, 1f - MathUtils.Remap( radius, fadeStart, fadeEnd ) );

				radius = MathF.Min( MathF.Max( radius, minSize * cameraDistance ), maxSize * cameraDistance );
			}

			nodeScratch[i] = new RopeNode
			{
				Segment = splits ? particle.GetScalar( splitField ) : 0f,
				Position = particle.Position,
				Radius = radius,
				Color = particle.Color,
				Alpha = alpha,
				Orientation = particle.GetVector( orientationField ),
				ScalarCoordinate = useScalarForTextureCoordinate
					? particle.GetScalar( scalarFieldForTextureCoordinate ) * scalarAttributeTextureCoordScale
					: 0f,
			};
		}

		if ( sortBySegmentId )
			GroupBySegment( nodeScratch.AsSpan( 0, count ) );

		return count;
	}

	// Gathers each segment's nodes together, segments in order of first appearance, nodes in their order within it.
	private void GroupBySegment( Span<RopeNode> nodes )
	{
		if ( sortKeyScratch.Length < nodes.Length )
			sortKeyScratch = new long[nodes.Length];

		var keys = sortKeyScratch.AsSpan( 0, nodes.Length );
		segmentRanks.Clear();

		for ( var i = 0; i < nodes.Length; i++ )
		{
			if ( !segmentRanks.TryGetValue( nodes[i].Segment, out var rank ) )
			{
				rank = segmentRanks.Count;
				segmentRanks.Add( nodes[i].Segment, rank );
			}

			keys[i] = ((long)rank << 32) | (uint)i;
		}

		keys.Sort( nodes );
	}

	public override void Draw( ParticleCollection particles, ParticleSystemState systemState, in ParticleView view )
	{
		if ( particles.Count < 2 )
			return;

		var nodeCount = CollectNodes( particles, systemState, view );
		if ( nodeCount < 2 )
			return;

		var nodes = nodeScratch.AsSpan( 0, nodeCount );
		var segmentCount = closedLoop ? nodeCount : nodeCount - 1;
		var subdivisions = 1 << ComputeTessellationLevel( nodes, view );
		var (scrollRate, vOffset, oneOverWorldSize) = ResolveTextureV( systemState );
		var viewAngleFadeActive = startFadeDot < 1f && endFadeDot > startFadeDot;

		Span<SheetFrame> sheets = stackalloc SheetFrame[ParticleTextureLayer.MaxLayers];
		Span<ParticleTextureLayer.UvTransform> uvTransforms = stackalloc ParticleTextureLayer.UvTransform[ParticleTextureLayer.MaxLayers];
		ParticleTextureLayer.ResolveUvTransforms( layers, systemState, uvTransforms );

		for ( var layer = 0; layer < layers.Length; layer++ )
			sheets[layer] = ResolveSheetFrame( particles, layers[layer].SpriteSheet );

		var vertices = Quads.Begin( segmentCount * subdivisions );
		var maxQuads = vertices.Length / 4;
		var quadCount = 0;
		var arcLength = scrollRate * systemState.Age;
		var stripAxis = Vector3.Zero;

		for ( var segment = 0; segment < segmentCount && quadCount < maxQuads; segment++ )
		{
			ref readonly var n1 = ref nodes[ResolveIndex( segment, nodeCount )];
			ref readonly var n2 = ref nodes[ResolveIndex( segment + 1, nodeCount )];

			var vStart = arcLength;
			var vEnd = arcLength + Vector3.Distance( n1.Position, n2.Position );
			arcLength = vEnd;

			// Nodes of different segments aren't joined.
			if ( n1.Segment != n2.Segment )
			{
				stripAxis = Vector3.Zero;
				continue;
			}

			ref readonly var before = ref nodes[ResolveIndex( segment - 1, nodeCount )];
			ref readonly var after = ref nodes[ResolveIndex( segment + 2, nodeCount )];
			ref readonly var n0 = ref before.Segment == n1.Segment ? ref before : ref n1;
			ref readonly var n3 = ref after.Segment == n2.Segment ? ref after : ref n2;

			if ( useScalarForTextureCoordinate )
			{
				vStart = n1.ScalarCoordinate;
				vEnd = n2.ScalarCoordinate;
			}

			var previous = EvaluateSample( n0, n1, n2, n3, 0f, view );
			AlignWidthAxis( ref previous, ref stripAxis );

			for ( var step = 1; step <= subdivisions && quadCount < maxQuads; step++ )
			{
				var t = step / (float)subdivisions;
				var current = EvaluateSample( n0, n1, n2, n3, t, view );
				AlignWidthAxis( ref current, ref stripAxis );

				WriteQuad( vertices.Slice( quadCount * 4, 4 ), previous, current,
					MapV( float.Lerp( vStart, vEnd, (step - 1) / (float)subdivisions ), oneOverWorldSize, vOffset ),
					MapV( float.Lerp( vStart, vEnd, t ), oneOverWorldSize, vOffset ),
					sheets, uvTransforms, viewAngleFadeActive, view );

				quadCount++;
				previous = current;
			}
		}

		if ( quadCount == 0 )
			return;

		SetSharedAttributes( systemState, blendMode, drawAsOpaque, blendFrames );
		ParticleTextureLayer.Bind( Attributes, layers, systemState );
		Submit( quadCount );
	}

	private float MapV( float coordinate, float oneOverWorldSize, float vOffset )
	{
		var v = useScalarForTextureCoordinate ? coordinate : (coordinate * oneOverWorldSize) + vOffset;
		return clampV ? MathUtils.Saturate( v ) : v;
	}

	private RopeSample EvaluateSample( in RopeNode n0, in RopeNode n1, in RopeNode n2, in RopeNode n3, float t, in ParticleView view )
	{
		var position = CatmullRom( n0.Position, n1.Position, n2.Position, n3.Position, t );
		var radius = CatmullRom( n0.Radius, n1.Radius, n2.Radius, n3.Radius, t );
		var tangent = CatmullRomTangent( n0.Position, n1.Position, n2.Position, n3.Position, t );
		tangent = MathUtils.SafeNormalize( tangent, Vector3.UnitX, ParticleMath.MinimumLengthSquared );

		var planeNormal = orientationType switch
		{
			ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL => Vector3.Lerp( n1.Orientation, n2.Orientation, t ),
			ParticleOrientation.PARTICLE_ORIENTATION_WORLD_Z_ALIGNED => Vector3.UnitZ,
			_ => view.Location - position,
		};

		var widthAxis = Vector3.Cross( tangent, planeNormal );
		widthAxis = widthAxis.LengthSquared() > ParticleMath.MinimumLengthSquared
			? Vector3.Normalize( widthAxis )
			: Vector3.Normalize( Vector3.Cross( tangent, MathF.Abs( tangent.Z ) < 0.999f ? Vector3.UnitZ : Vector3.UnitX ) );

		return new RopeSample
		{
			Position = position,
			WidthAxis = widthAxis,
			Tangent = tangent,
			Radius = radius,
			Color = Vector4.Clamp( new Vector4( Vector3.Lerp( n1.Color, n2.Color, t ), float.Lerp( n1.Alpha, n2.Alpha, t ) ), Vector4.Zero, Vector4.One ),
		};
	}

	private void WriteQuad( Span<DeadlockParticleVertex> vertices, in RopeSample start, in RopeSample end, float vStart, float vEnd,
		ReadOnlySpan<SheetFrame> sheets, ReadOnlySpan<ParticleTextureLayer.UvTransform> uvTransforms, bool viewAngleFadeActive, in ParticleView view )
	{
		var startColor = start.Color;
		var endColor = end.Color;

		if ( viewAngleFadeActive )
		{
			startColor.W *= ViewAngleFade( start, view );
			endColor.W *= ViewAngleFade( end, view );
		}

		// stackalloc rather than collection expressions, which build inline arrays the sandbox doesn't allow.
		Span<Vector3> positions = stackalloc Vector3[4];
		positions[0] = start.Position - (start.WidthAxis * start.Radius);
		positions[1] = end.Position - (end.WidthAxis * end.Radius);
		positions[2] = end.Position + (end.WidthAxis * end.Radius);
		positions[3] = start.Position + (start.WidthAxis * start.Radius);

		Span<Vector2> coordinates = stackalloc Vector2[4];
		coordinates[0] = new( 0f, vStart );
		coordinates[1] = new( 0f, vEnd );
		coordinates[2] = new( 1f, vEnd );
		coordinates[3] = new( 1f, vStart );

		for ( var j = 0; j < 4; j++ )
		{
			var color = j is 0 or 3 ? startColor : endColor;
			var lighting = j is 0 or 3 ? start.Position : end.Position;
			ref var vertex = ref vertices[j];

			vertex = default;
			vertex.Position = positions[j];
			vertex.Color = new global::Vector4( color.X, color.Y, color.Z, color.W );
			vertex.Card = new global::Vector4( sheets[0].Blend, coordinates[j].X, coordinates[j].Y, 0f );
			vertex.LightingPosition = new global::Vector4( lighting.X, lighting.Y, lighting.Z, 0f );

			for ( var layer = 0; layer < layers.Length; layer++ )
			{
				ref readonly var sheet = ref sheets[layer];
				var uv = ParticleTextureLayer.PlaceCorner( coordinates[j], uvTransforms[layer], sheet.Offset, sheet.Offset + sheet.Scale );
				var uvNext = ParticleTextureLayer.PlaceCorner( coordinates[j], uvTransforms[layer], sheet.NextOffset, sheet.NextOffset + sheet.NextScale );
				vertex.SetLayer( layer, new global::Vector4( uv.X, uv.Y, uvNext.X, uvNext.Y ) );
			}
		}
	}

	// Consecutive samples keep their width axes on the same side, so the ribbon doesn't twist.
	private static void AlignWidthAxis( ref RopeSample sample, ref Vector3 stripAxis )
	{
		if ( Vector3.Dot( sample.WidthAxis, stripAxis ) < 0f )
			sample.WidthAxis = -sample.WidthAxis;

		stripAxis = sample.WidthAxis;
	}

	private float ViewAngleFade( in RopeSample sample, in ParticleView view )
	{
		var toCamera = view.Location - sample.Position;
		if ( toCamera.LengthSquared() <= ParticleMath.MinimumLengthSquared )
			return 1f;

		var facing = MathF.Abs( Vector3.Dot( sample.Tangent, Vector3.Normalize( toCamera ) ) );
		return 1f - MathUtils.Smoothstep( startFadeDot, endFadeDot, facing );
	}

	// The whole rope shows its first particle's sheet frame.
	private SheetFrame ResolveSheetFrame( ParticleCollection particles, ParticleSpriteSheet spriteSheet )
	{
		var identity = new SheetFrame { Offset = Vector2.Zero, Scale = Vector2.One, NextOffset = Vector2.Zero, NextScale = Vector2.One };

		ref var head = ref particles.Current[0];
		if ( GetSequence( spriteSheet, head.SequenceNumber ) is not { } sequence )
			return identity;

		var (frame, nextFrame, blend) = GetSheetFrame( ref head, sequence, animationRate, animationType, animateInFps );
		if ( sequence.Frames[frame].Images.Length == 0 || sequence.Frames[nextFrame].Images.Length == 0 )
			return identity;

		var currentImage = sequence.Frames[frame].Images[0];
		var nextImage = sequence.Frames[nextFrame].Images[0];

		return new SheetFrame
		{
			Offset = currentImage.UncroppedMin,
			Scale = currentImage.UncroppedMax - currentImage.UncroppedMin,
			NextOffset = nextImage.UncroppedMin,
			NextScale = nextImage.UncroppedMax - nextImage.UncroppedMin,
			Blend = blend,
		};
	}

	private static Vector3 CatmullRom( Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t )
	{
		var t2 = t * t;
		var t3 = t2 * t;

		return 0.5f * ((2f * p1)
			+ ((-p0 + p2) * t)
			+ (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t2)
			+ ((-p0 + (3f * p1) - (3f * p2) + p3) * t3));
	}

	private static float CatmullRom( float p0, float p1, float p2, float p3, float t )
	{
		var t2 = t * t;
		var t3 = t2 * t;

		return 0.5f * ((2f * p1)
			+ ((-p0 + p2) * t)
			+ (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t2)
			+ ((-p0 + (3f * p1) - (3f * p2) + p3) * t3));
	}

	// The derivative of the Catmull-Rom spline along t.
	private static Vector3 CatmullRomTangent( Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t )
	{
		var t2 = t * t;

		return 0.5f * ((-p0 + p2)
			+ (((4f * p0) - (10f * p1) + (8f * p2) - (2f * p3)) * t)
			+ (((-3f * p0) + (9f * p1) - (9f * p2) + (3f * p3)) * t2));
	}
}
