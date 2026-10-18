using System;

namespace Deadlock.Particles.Renderers;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

/// <summary>
/// One of a spritecard renderer's texture layers (<c>m_vecTexturesInput</c>): its texture and sprite sheet, how its
/// channels mix into the layers before it, and how its coordinates are transformed.
/// </summary>
internal sealed class ParticleTextureLayer
{
	public const int MaxLayers = 5;

	private static readonly INumberProvider One = new LiteralNumberProvider( 1f );
	private static readonly INumberProvider Zero = new LiteralNumberProvider( 0f );

	public Texture Texture { get; init; }
	public ParticleSpriteSheet SpriteSheet { get; init; }

	/// <summary>
	/// Whether the texture is colour, read through its sRGB view.
	/// </summary>
	public bool Srgb { get; init; } = true;

	public SpriteCardTextureChannel Channels { get; init; } = SpriteCardTextureChannel.SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA;
	public SpriteCardTextureType EffectMode { get; init; } = SpriteCardTextureType.SPRITECARD_TEXTURE_DIFFUSE;
	public ParticleTextureLayerBlendType BlendMode { get; init; } = ParticleTextureLayerBlendType.SPRITECARD_TEXTURE_BLEND_MULTIPLY;
	public INumberProvider Blend { get; init; } = One;
	public INumberProvider ScaleU { get; init; } = One;
	public INumberProvider ScaleV { get; init; } = One;
	public INumberProvider OffsetU { get; init; } = Zero;
	public INumberProvider OffsetV { get; init; } = Zero;
	public INumberProvider Distortion { get; init; } = Zero;
	public INumberProvider ZoomScale { get; init; } = Zero;
	public INumberProvider Rotation { get; init; } = Zero;
	public bool ClampUVs { get; init; }

	/// <summary>
	/// Motion vector sheets store how far their texels move, up to this many texels.
	/// </summary>
	public float MotionVectorsMaxDistance { get; init; }

	public readonly record struct UvTransform( Vector2 Scale, Vector2 Offset, float Rotation, bool Clamp )
	{
		public (float Sin, float Cos) RotationSinCos { get; } = MathF.SinCos( Rotation );

		public Vector2 WrappedOrigin { get; } = new( MathUtils.Fract( Offset.X + 0.5f ), MathUtils.Fract( Offset.Y + 0.5f ) );
	}

	public UvTransform ResolveUvTransform( ParticleSystemState systemState ) => new(
		new Vector2( ScaleU.NextNumber( systemState ), ScaleV.NextNumber( systemState ) ),
		new Vector2( OffsetU.NextNumber( systemState ), OffsetV.NextNumber( systemState ) ),
		Rotation.NextNumber( systemState ),
		ClampUVs );

	/// <summary>
	/// The transforms of every layer for a draw. Motion vector layers move with the base layer.
	/// </summary>
	public static void ResolveUvTransforms( ParticleTextureLayer[] layers, ParticleSystemState systemState, Span<UvTransform> transforms )
	{
		for ( var layer = 0; layer < layers.Length; layer++ )
		{
			var source = layers[layer].EffectMode == SpriteCardTextureType.SPRITECARD_TEXTURE_ANIMMOTIONVEC ? layers[0] : layers[layer];
			transforms[layer] = source.ResolveUvTransform( systemState );
		}
	}

	/// <summary>
	/// Where a corner of the card samples a layer: the card coordinate turned, scaled and offset by the layer's
	/// transform, inside the sheet frame's rectangle.
	/// </summary>
	public static Vector2 PlaceCorner( Vector2 cardUv, in UvTransform transform, Vector2 rectMin, Vector2 rectMax )
	{
		var centred = cardUv - new Vector2( 0.5f );
		var (sin, cos) = transform.RotationSinCos;

		var rotated = new Vector2(
			(centred.X * cos) - (centred.Y * sin),
			(centred.X * sin) + (centred.Y * cos) );

		var placed = (rotated / transform.Scale) + transform.WrappedOrigin;

		if ( transform.Clamp )
			placed = Vector2.Clamp( placed, Vector2.Zero, Vector2.One );

		return rectMin + (placed * (rectMax - rectMin));
	}

	public static ParticleSpriteSheet FindSpriteSheet( ParticleTextureLayer[] layers )
	{
		foreach ( var layer in layers )
		{
			if ( layer.SpriteSheet is { Sequences.Length: > 0 } sheet )
				return sheet;
		}

		return null;
	}

	/// <summary>
	/// Sets the layers' textures and settings on a draw's attributes, read by <c>deadlock_particle.shader</c>.
	/// </summary>
	public static void Bind( RenderAttributes attributes, ParticleTextureLayer[] layers, ParticleSystemState systemState )
	{
		attributes.Set( "LayerCount", layers.Length );

		for ( var layer = 0; layer < MaxLayers; layer++ )
		{
			if ( layer >= layers.Length )
			{
				attributes.Set( LayerNames.Texture[layer], 0 );
				continue;
			}

			var source = layers[layer];
			var index = source.Texture.IsValid() ? source.Texture.Index + (source.Srgb ? 1 : 0) : 0;

			attributes.Set( LayerNames.Texture[layer], index );
			attributes.Set( LayerNames.Settings[layer], new global::Vector4(
				(int)source.Channels, (int)source.BlendMode, source.Blend.NextNumber( systemState ), (int)source.EffectMode ) );
			attributes.Set( LayerNames.Effects[layer], new global::Vector4(
				source.Distortion.NextNumber( systemState ), source.ZoomScale.NextNumber( systemState ), 0, 0 ) );

			if ( source.EffectMode == SpriteCardTextureType.SPRITECARD_TEXTURE_ANIMMOTIONVEC && source.Texture.IsValid() )
			{
				attributes.Set( "MotionVectorScale", new global::Vector2(
					source.MotionVectorsMaxDistance / source.Texture.Width,
					source.MotionVectorsMaxDistance / source.Texture.Height ) );
			}
		}
	}

	private static class LayerNames
	{
		public static readonly string[] Texture = ["Layer0Texture", "Layer1Texture", "Layer2Texture", "Layer3Texture", "Layer4Texture"];
		public static readonly string[] Settings = ["Layer0Settings", "Layer1Settings", "Layer2Settings", "Layer3Settings", "Layer4Settings"];
		public static readonly string[] Effects = ["Layer0Effects", "Layer1Effects", "Layer2Effects", "Layer3Effects", "Layer4Effects"];
	}

	/// <summary>
	/// The layers a renderer's <c>m_vecTexturesInput</c> describes, or one layer of the default texture.
	/// </summary>
	public static ParticleTextureLayer[] Build( ParticleDefinitionParser parse, DeadlockParticleSystem resource, string defaultTexture )
	{
		var parsed = new List<ParticleTextureLayer>();

		foreach ( var textureInput in parse.Array( "m_vecTexturesInput" ) )
		{
			if ( !textureInput.Boolean( "m_bEnabled", true ) )
				continue;

			// Normal maps aren't colour and would tint the card with a tangent space basis. Motion vector sheets stay
			// layers: the shader reads their flow and composites nothing.
			var textureType = textureInput.Enum( "m_nTextureType", SpriteCardTextureType.SPRITECARD_TEXTURE_DIFFUSE );
			var isMotionVectors = textureType == SpriteCardTextureType.SPRITECARD_TEXTURE_ANIMMOTIONVEC;

			if ( textureType == SpriteCardTextureType.SPRITECARD_TEXTURE_NORMALMAP )
				continue;

			if ( parsed.Count == MaxLayers )
				break;

			Texture texture;
			ParticleSpriteSheet sheet = null;
			var srgb = !isMotionVectors;

			// A gradient layer builds its ramp from m_Gradient rather than loading a texture.
			if ( textureInput.Boolean( "m_bReplaceTextureWithGradient", false ) )
			{
				texture = GradientTexture( textureInput );
				srgb = false;
			}
			else
			{
				var name = textureInput.Data.ContainsKey( "m_hTexture" ) ? textureInput.Data.GetStringProperty( "m_hTexture" ) : null;
				if ( string.IsNullOrEmpty( name ) )
					name = defaultTexture;

				texture = LoadTexture( name );
				sheet = resource?.GetSpriteSheet( NormalizePath( name ) );
				srgb &= !IsHighDynamicRange( texture );
			}

			var controls = textureInput.Data.GetSubCollection( "m_TextureControls" );
			var uv = controls == null ? textureInput : textureInput.Nested( controls );

			parsed.Add( new ParticleTextureLayer
			{
				Texture = texture,
				SpriteSheet = sheet,
				Srgb = srgb,
				Channels = textureInput.Enum( "m_nTextureChannels", SpriteCardTextureChannel.SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA ),
				BlendMode = textureInput.Enum( "m_nTextureBlendMode", ParticleTextureLayerBlendType.SPRITECARD_TEXTURE_BLEND_MULTIPLY ),
				Blend = textureInput.NumberProvider( "m_flTextureBlend", One ),
				EffectMode = textureType,
				ScaleU = controls == null ? One : uv.NumberProvider( "m_flFinalTextureScaleU", One ),
				ScaleV = controls == null ? One : uv.NumberProvider( "m_flFinalTextureScaleV", One ),
				OffsetU = controls == null ? Zero : uv.NumberProvider( "m_flFinalTextureOffsetU", Zero ),
				OffsetV = controls == null ? Zero : uv.NumberProvider( "m_flFinalTextureOffsetV", Zero ),
				Rotation = controls == null ? Zero : uv.NumberProvider( "m_flFinalTextureUVRotation", Zero ),
				Distortion = controls == null ? Zero : uv.NumberProvider( "m_flDistortion", Zero ),
				ZoomScale = controls == null ? Zero : uv.NumberProvider( "m_flZoomScale", Zero ),
				ClampUVs = controls != null && uv.Boolean( "m_bClampUVs", false ),
				MotionVectorsMaxDistance = sheet?.MotionVectorsMaxDistance ?? 0f,
			} );
		}

		if ( parsed.Count == 0 )
		{
			var texture = LoadTexture( defaultTexture );
			parsed.Add( new ParticleTextureLayer
			{
				Texture = texture,
				SpriteSheet = resource?.GetSpriteSheet( defaultTexture ),
				Srgb = !IsHighDynamicRange( texture ),
			} );
		}

		return [.. parsed];
	}

	private static string NormalizePath( string path )
	{
		path = path.Replace( '\\', '/' );
		return path.EndsWith( "_c" ) ? path[..^2] : path;
	}

	private static readonly Dictionary<string, Texture> Textures = new( StringComparer.OrdinalIgnoreCase );

	private static Texture LoadTexture( string path )
	{
		path = NormalizePath( path );

		lock ( Textures )
		{
			if ( Textures.TryGetValue( path, out var cached ) && cached.IsValid() )
				return cached;
		}

		var texture = Texture.Load( $"mount://deadlock/{path}", false );
		if ( texture is null || !texture.IsValid() || texture.IsError )
			texture = Texture.White;

		lock ( Textures )
			Textures[path] = texture;

		return texture;
	}

	private static bool IsHighDynamicRange( Texture texture ) => texture.IsValid() && texture.ImageFormat is ImageFormat.BC6H
		or ImageFormat.RGBA16161616F or ImageFormat.R16F or ImageFormat.RG1616F or ImageFormat.R32F or ImageFormat.RGBA32323232F
		or ImageFormat.RGBA16161616 or ImageFormat.R16 or ImageFormat.RG1616;

	/// <summary>
	/// A 256 texel ramp of <c>m_Gradient</c>'s stops, in gamma space as the gradient is authored.
	/// </summary>
	private static Texture GradientTexture( ParticleDefinitionParser textureInput )
	{
		var stops = new List<(float Position, Vector3 Color)>();
		var gradient = textureInput.Data.GetSubCollection( "m_Gradient" );

		if ( gradient != null )
		{
			foreach ( var stop in textureInput.Nested( gradient ).Array( "m_Stops" ) )
				stops.Add( (stop.Float( "m_flPosition", 0f ), stop.Color24( "m_Color", Vector3.One )) );
		}

		stops.Sort( ( a, b ) => a.Position.CompareTo( b.Position ) );

		const int Width = 256;
		var pixels = new byte[Width * 4];

		for ( var x = 0; x < Width; x++ )
		{
			var t = x / (float)(Width - 1);
			var color = Vector3.One;

			if ( stops.Count > 0 )
			{
				color = stops[0].Color;

				for ( var i = 0; i < stops.Count; i++ )
				{
					if ( t >= stops[i].Position )
						color = stops[i].Color;

					if ( i + 1 < stops.Count && t >= stops[i].Position && t < stops[i + 1].Position )
					{
						var span = stops[i + 1].Position - stops[i].Position;
						color = Vector3.Lerp( stops[i].Color, stops[i + 1].Color, span > 0f ? (t - stops[i].Position) / span : 0f );
						break;
					}
				}
			}

			pixels[x * 4 + 0] = (byte)Math.Clamp( color.X * 255f, 0f, 255f );
			pixels[x * 4 + 1] = (byte)Math.Clamp( color.Y * 255f, 0f, 255f );
			pixels[x * 4 + 2] = (byte)Math.Clamp( color.Z * 255f, 0f, 255f );
			pixels[x * 4 + 3] = 255;
		}

		return Texture.Create( Width, 1 ).WithData( pixels ).WithName( "deadlock particle gradient" ).Finish();
	}
}
