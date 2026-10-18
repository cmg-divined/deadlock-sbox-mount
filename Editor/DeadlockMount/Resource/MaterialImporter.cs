using Deadlock;
using System;
using NVector3 = System.Numerics.Vector3;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Loads a compiled Deadlock material (vmat_c) as an s&amp;box <see cref="Material"/>.
/// </summary>
internal sealed class MaterialLoader( string filePath ) : ResourceLoader<DeadlockMount>
{
	protected override object Load()
	{
		var source = new CompiledMaterial( Host.ReadFile( filePath ) );

		// Created materials get a plain name like the Source 1 mount's, not the mount:// path the loader serves them from.
		return new MaterialImporter( Host, source, $"{Host.Ident}/{RelativePath}" ).Import();
	}
}

/// <summary>
/// Builds an s&amp;box material on <c>deadlock_pbr.shader</c> from a Deadlock material, translating its
/// parameters the way the game's shaders consume them. Its large textures are <paramref name="textureLevel"/> mips
/// down (see <see cref="DeadlockMount.LoadTexture(string, int)"/>).
/// </summary>
internal sealed class MaterialImporter( DeadlockMount mount, CompiledMaterial source, string name, int textureLevel = 0 )
{
	public const string Shader = "shaders/deadlock_pbr.shader";
	public const string EnvironmentShader = "shaders/deadlock_environment.shader";
	public const string ParallaxShader = "shaders/deadlock_parallax_layered.shader";
	public const string OverlayShader = "shaders/deadlock_overlay.shader";
	public const string GlassShader = "shaders/deadlock_glass.shader";
	public const string VeilShader = "shaders/deadlock_cosmic_veil.shader";

	/// <summary>
	/// How strongly the luminance preserving tint mode keeps saturation.
	/// </summary>
	private const float TintStrength = 0.85f;

	private static readonly float[] Red = [1, 0, 0, 0];
	private static readonly float[] Green = [0, 1, 0, 0];
	private static readonly float[] Alpha = [0, 0, 0, 1];

	/// <summary>
	/// Every texture of each shader and what it reads when the material has none: the shader's own default. s&amp;box
	/// only fills those in for compiled materials; a created one reads an unbound texture as the error checkerboard.
	/// </summary>
	private static readonly Dictionary<string, (string Texture, bool Srgb, Vector4 Value)[]> DefaultTextures = new()
	{
		[Shader] =
		[
			("g_tColor", true, new( 1, 1, 1, 1 )),
			("g_tNormalRoughness", false, new( 0.5f, 0.5f, 1, 1 )),
			("g_tMetalness", false, new( 0, 0, 0, 0 )),
			("g_tTintMask", false, new( 1, 1, 1, 1 )),
			("g_tSelfIllumMask", false, new( 1, 1, 1, 1 )),
			("g_tAmbientOcclusion", false, new( 1, 1, 1, 1 )),
			("g_tRimLightMask", false, new( 1, 1, 1, 1 )),
			("g_tNprTransmissiveColor", true, new( 0, 0, 0, 1 )),
			("g_tNprOutlineMask", false, new( 1, 0, 0, 0 )),
			("g_tJitterMask", false, new( 1, 0, 0, 0 )),
		],
		[EnvironmentShader] =
		[
			("g_tColor1", true, new( 1, 1, 1, 1 )),
			("g_tNormalRoughness1", false, new( 0.5f, 0.5f, 1, 1 )),
			("g_tPacked1", false, new( 0, 0, 0, 0 )),
			("g_tColor2", true, new( 1, 1, 1, 1 )),
			("g_tNormalRoughness2", false, new( 0.5f, 0.5f, 1, 1 )),
			("g_tPacked2", false, new( 0, 0, 0, 0 )),
			("g_tRevealMask2", false, new( 0.5f, 0.5f, 0.5f, 0.5f )),
			("g_tColor3", true, new( 1, 1, 1, 1 )),
			("g_tNormalRoughness3", false, new( 0.5f, 0.5f, 1, 1 )),
			("g_tPacked3", false, new( 0, 0, 0, 0 )),
			("g_tRevealMask3", false, new( 0.5f, 0.5f, 0.5f, 0.5f )),
			("g_tColorOverlay", true, new( 0.5f, 0.5f, 0.5f, 1 )),
			("g_tNormalRoughnessOverlay", false, new( 0.5f, 0.5f, 0.5f, 1 )),
			("g_tRevealOverlay", false, new( 0, 0, 0, 0 )),
			("g_tColorWorldOverlay", true, new( 0.5f, 0.5f, 0.5f, 1 )),
			("g_tColorWorldOverlay2", true, new( 0.5f, 0.5f, 0.5f, 1 )),
		],
		[ParallaxShader] =
		[
			("g_tColor", true, new( 1, 1, 1, 1 )),
			("g_tMask", false, new( 1, 1, 1, 1 )),
			("g_tNormalRoughness", false, new( 0.5f, 0.5f, 1, 1 )),
			("g_tColor2", true, new( 0, 0, 0, 0 )),
			("g_tMask2", false, new( 0, 0, 0, 0 )),
			("g_tNormalRoughness2", false, new( 0.5f, 0.5f, 1, 1 )),
			("g_tColor3", true, new( 0, 0, 0, 0 )),
			("g_tReflectionOcclusionMask", false, new( 0, 0, 0, 0 )),
		],
		[OverlayShader] =
		[
			("g_tColor", true, new( 1, 1, 1, 0 )),
			("g_tNormalRoughness", false, new( 0.5f, 0.5f, 1, 1 )),
			("g_tSelfIllumTintMask", false, new( 0, 1, 0, 0 )),
		],
		[GlassShader] =
		[
			("g_tColor", true, new( 1, 1, 1, 0 )),
			("g_tNormalRoughness", false, new( 0.5f, 0.5f, 1, 0 )),
			("g_tTintMask", false, new( 1, 1, 1, 1 )),
			("g_tGlass", false, new( 1, 1, 1, 1 )),
			("g_tSelfIllumMask", false, new( 1, 1, 1, 1 )),
		],
		[VeilShader] =
		[
			("g_tMasks", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tWarpMask", false, new( 0.5f, 0.5f, 0.5f, 0 )),
		],
	};

	private static readonly Dictionary<uint, Texture> SolidTextures = [];

	private Material _material;
	private readonly HashSet<string> _boundTextures = [];

	public Material Import()
	{
		if ( source.ShaderName == "sky.vfx" )
			return ImportSky();

		var environment = source.ShaderName is "environment_blend.vfx" or "environment_layer.vfx" or "environment_simple.vfx"
			&& !Combo( "F_TRANSLUCENT" ) && !Combo( "F_ALPHA_TEST" );
		var glass = source.ShaderName == "pbr.vfx" && Combo( "F_GLASS" );
		var shader = source.ShaderName switch
		{
			_ when environment => EnvironmentShader,
			_ when glass => GlassShader,
			"environment_parallax_layered.vfx" => ParallaxShader,
			"citadel_overlay.vfx" => OverlayShader,
			"cosmic_veil.vfx" => VeilShader,
			_ => Shader,
		};

		_material = Material.Create( name, shader, false )
			?? throw new InvalidOperationException( $"Couldn't create a material with {shader}." );

		switch ( source.ShaderName )
		{
			case var _ when environment:
				SetupEnvironment();
				break;

			case var _ when glass:
				SetupGlass();
				break;

			case "environment_parallax_layered.vfx":
				SetupParallax();
				break;

			case "citadel_overlay.vfx":
				SetupOverlay();
				break;

			case "cosmic_veil.vfx":
				SetupVeil();
				break;

			case "pbr.vfx":
				SetupPbr();
				break;

			case "generic.vfx":
				SetupGeneric();
				break;

			case "black.vfx" or "black_unlit.vfx":
				SetupBlack();
				break;

			default:
				SetupLayer();
				break;
		}

		// Deadlock's surface names mostly don't exist in s&box, so the closest s&box surface stands in.
		if ( source.StringAttributes.TryGetValue( "PhysicsSurfaceProperties", out var surface ) && mount.GetSurface( surface ) is { } mapped )
			_material.Attributes.Set( "PhysicsSurfaceProperties", mapped.ResourceName );

		BindDefaultTextures( shader );
		ApplyDynamicParams();

		return _material;
	}

	/// <summary>
	/// The material's dynamic expressions, for the parameters the shaders have a place for: a constant one replaces
	/// its parameter, a time() one runs every frame (scrolling water), and an $ent_random one waits for each object
	/// that uses the material (see MapLoader). Ones reading other attributes keep the material's own value.
	/// </summary>
	private void ApplyDynamicParams()
	{
		foreach ( var (parameter, code) in source.DynamicParams )
		{
			if ( DeadlockMaterialExpressions.UsesAttribute( code, DeadlockMaterialExpressions.EntRandom ) )
			{
				DeadlockMaterialExpressions.PerEntity( _material, parameter, code );
				continue;
			}

			if ( DeadlockMaterialExpressions.Evaluate( code, 0 ) is not { } value || !DeadlockMaterialExpressions.Apply( _material, parameter, value ) )
				continue;

			if ( DeadlockMaterialExpressions.UsesTime( code ) )
				DeadlockMaterialExpressions.Animate( _material, parameter, code );
		}
	}

	/// <summary>
	/// Binds the shader's default to each of its textures the material left unbound.
	/// </summary>
	private void BindDefaultTextures( string shader )
	{
		if ( !DefaultTextures.TryGetValue( shader, out var textures ) )
			return;

		foreach ( var (texture, srgb, value) in textures )
		{
			if ( !_boundTextures.Contains( texture ) )
				_material.Set( texture, SolidTexture( value, srgb ) );
		}
	}

	/// <summary>
	/// A 1x1 texture that reads as <paramref name="value"/>, sRGB encoded for a texture read as sRGB.
	/// </summary>
	private static Texture SolidTexture( Vector4 value, bool srgb )
	{
		byte Channel( float x, bool encode ) => (byte)MathF.Round( Math.Clamp( encode ? ColorMatrices.LinearToSrgb( x ) : x, 0f, 1f ) * 255f );

		var pixel = new[] { Channel( value.x, srgb ), Channel( value.y, srgb ), Channel( value.z, srgb ), Channel( value.w, false ) };
		var key = BitConverter.ToUInt32( pixel );

		lock ( SolidTextures )
		{
			if ( !SolidTextures.TryGetValue( key, out var texture ) || !texture.IsValid() )
				SolidTextures[key] = texture = Texture.Create( 1, 1 ).WithName( $"deadlock_solid_{key:x8}" ).WithData( pixel ).Finish();

			return texture;
		}
	}

	/// <summary>
	/// sky.vfx, skybox materials: an HDR cubemap shown at an exposure bias. s&amp;box's own sky shader draws
	/// the same thing, taking the bias as a tint.
	/// </summary>
	private Material ImportSky()
	{
		const string SkyShader = "shaders/sky.shader";

		var material = Material.Create( name, SkyShader, false )
			?? throw new InvalidOperationException( $"Couldn't create a material with {SkyShader}." );

		if ( mount.LoadTexture( source.GetTexture( "g_tSkyTexture" ), textureLevel ) is { } texture )
			material.Set( "SkyTexture", texture );

		// The render only bias brightens the sky as seen, not the light it gives; s&box's sky has one brightness.
		var exposure = MathF.Pow( 2, source.GetFloat( "g_flBrightnessExposureBias" ) + source.GetFloat( "g_flRenderOnlyExposureBias" ) );
		material.Set( "SkyTint", new Color( exposure, exposure, exposure ) );

		return material;
	}

	/// <summary>
	/// pbr.vfx, used by heroes (with NPR lighting) and props.
	/// </summary>
	private void SetupPbr()
	{
		var npr = Combo( "F_USE_NPR_LIGHTING" );
		var unlit = Combo( "F_UNLIT" );
		var translucent = Combo( "F_TRANSLUCENT" );
		var alphaTest = Combo( "F_ALPHA_TEST" ) && !translucent;

		SetFeature( "F_USE_NPR_LIGHTING", npr );
		SetFeature( "F_UNLIT", unlit );
		SetFeature( "F_TRANSLUCENT", translucent );
		SetFeature( "F_ALPHA_TEST", alphaTest );
		SetFeature( "F_ADDITIVE_BLEND", translucent && Combo( "F_ADDITIVE_BLEND" ) );
		SetFeature( "F_WRITE_DEPTH_BEFORE_ALPHA_BLENDING", translucent && Combo( "F_WRITE_DEPTH_BEFORE_ALPHA_BLENDING" ) );
		SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );
		SetFeature( "F_DO_NOT_CAST_SHADOWS", Combo( "F_DO_NOT_CAST_SHADOWS" ) );

		BindTexture( "g_tColor", "g_tColor" );
		BindTexture( "g_tNormalRoughness", "g_tNormalRoughness" );

		// The colour alpha is the opacity when the material is cut out or see-through, which moves
		// metalness to its own texture.
		if ( alphaTest || translucent )
			BindMask( "g_tMetalness", "g_tMetalness", Red, "g_vMetalnessChannel" );
		else
			BindMask( "g_tMetalness", "g_tColor", Alpha, "g_vMetalnessChannel" );

		// NPR materials pack the tint and rim light masks into one texture.
		if ( npr )
		{
			BindMask( "g_tTintMask", "g_tTintMaskRimLightMask", Red, "g_vTintMaskChannel" );
			BindMask( "g_tRimLightMask", "g_tTintMaskRimLightMask", Green, "g_vRimLightMaskChannel" );
			BindTexture( "g_tNprTransmissiveColor", "g_tNprTransmissiveColor" );
			SetupNprOutline( translucent || unlit );
		}
		else
		{
			BindMask( "g_tTintMask", "g_tTintMask", Red, "g_vTintMaskChannel" );
		}

		BindMask( "g_tAmbientOcclusion", "g_tAmbientOcclusion", Red, "g_vAmbientOcclusionChannel" );
		SetBool( "g_flAmbientOcclusionSecondaryUv", "g_bUseSecondaryUvForAmbientOcclusion" );

		SetAlbedoAdjustments( "1", "g_tColor" );
		SetTextureCoordinates( Combo( "F_ENABLE_TEXTURE_TRANSFORMS" ) );

		if ( Combo( "F_SELF_ILLUM" ) )
			SetupSelfIllum( "g_tSelfIllumMask", Red );

		_material.Set( "g_flAlphaTestReference", Float( "g_flAlphaTestReference1", 0.5f ) );
		_material.Set( "g_flOpacityScale", Float( "g_flOpacityScale1", 1f ) );
		_material.Set( "g_flAlphaAnglePower", Float( "g_flAlphaAnglePower1", 0f ) );
		_material.Set( "g_flAlphaBoostStrength", Float( "g_flAlphaBoostStrength", 3f ) );
		_material.Set( "g_flAlphaBoostDistance", MathF.Max( Float( "g_flAlphaBoostDistance", 20000f ), 1f ) );
		_material.Set( "g_flUseFrontFaceNormalsForBackFaces", Combo( "F_USE_FRONT_FACE_NORMALS_FOR_BACK_FACES" ) ? 1f : 0f );
		_material.Set( "g_flUnlitAdditiveScale", Float( "g_flUnlitAdditiveScale1", 1f ) );

		// Blooms by its colour times this and the self illumination mask, unthresholded (Deadlock Bloom Layer).
		_material.Set( "g_flBloomAmount", Combo( "F_OVERRIDE_BLOOM_AMOUNT" ) && !translucent ? Float( "g_flOverrideBloomAmount1", 1f ) : 0f );

		// The game spells the cubemap one "Cubmap".
		_material.Set( "g_flGradientFogAmount", Float( "g_flGradientFogAmount", 1f ) );
		_material.Set( "g_flCubemapFogAmount", Float( "g_flCubmapFogAmount", 1f ) );
		_material.Set( "g_flVolumeFogAmount", Float( "g_flVolumeFogAmount", 1f ) );

		SetupJitter();
	}

	/// <summary>
	/// F_JITTER_VERTICES: the vertices wobble by two waves through their world (1) or object (2) position. The
	/// fallbacks are the game shader's defaults.
	/// </summary>
	private void SetupJitter()
	{
		var mode = (int)Math.Clamp( source.GetInt( "F_JITTER_VERTICES" ), 0, 2 );
		_material.SetFeature( "F_JITTER_VERTICES", mode );

		if ( mode == 0 )
			return;

		BindTexture( "g_tJitterMask", "g_tJitterMask" );
		SetBool( "g_flJitterMaskSecondaryUv", "g_bUseSecondaryUvForJitterMask" );
		_material.Set( "g_flJitterSynchronize", source.GetInt( "g_bSynchronizeJitter1", 1 ) != 0 ? 1f : 0f );
		_material.Set( "g_flJitterQuantizeTime", Float( "g_flJitterQuantizeTime1", 0f ) );
		_material.Set( "g_flJitterQuantizeDisplacement", Float( "g_flJitterQuantizeDisplacement1", 0f ) );
		_material.Set( "g_vJitterExpression", GetVector3( "g_vJitterExpression", 0 ) );

		_material.Set( "g_vJitterFrequenciesA", GetVector3( "g_vJitterFrequenciesA1", 0.02f, 0.04f, 0.08f ) );
		_material.Set( "g_vJitterAmplitudesXA", GetVector3( "g_vJitterAmplitudesXA1", 4 ) );
		_material.Set( "g_vJitterAmplitudesYA", GetVector3( "g_vJitterAmplitudesYA1", 0 ) );
		_material.Set( "g_vJitterAmplitudesZA", GetVector3( "g_vJitterAmplitudesZA1", 0 ) );
		_material.Set( "g_flJitterSpeedA", Float( "g_flJitterSpeedA1", 10f ) );

		_material.Set( "g_vJitterFrequenciesB", GetVector3( "g_vJitterFrequenciesB1", 0.05f, 0.09f, 0.17f ) );
		_material.Set( "g_vJitterAmplitudesXB", GetVector3( "g_vJitterAmplitudesXB1", 0 ) );
		_material.Set( "g_vJitterAmplitudesYB", GetVector3( "g_vJitterAmplitudesYB1", 0 ) );
		_material.Set( "g_vJitterAmplitudesZB", GetVector3( "g_vJitterAmplitudesZB1", 0 ) );
		_material.Set( "g_flJitterSpeedB", Float( "g_flJitterSpeedB1", 7f ) );
	}

	/// <summary>
	/// The outline pass's settings (drawn by the Deadlock NPR Outline component): pbr.vfx draws a lit outline, a
	/// solid colour one, or none, as thick as the global setting (npr_dummy.vmat's 0.1) unless the material overrides it.
	/// </summary>
	private void SetupNprOutline( bool seeThrough )
	{
		var enabled = !seeThrough && !Combo( "F_DISABLE_NPR_OUTLINE" ) && !Combo( "F_CLOAK" ) && !Combo( "F_GLASS" );

		BindTexture( "g_tNprOutlineMask", "g_tNprOutlineMask" );
		_material.Set( "g_flNprOutlineEnabled", enabled ? 1f : 0f );
		_material.Set( "g_flNprOutlineThickness", Combo( "F_OVERRIDE_NPR_OUTLINE" ) ? Float( "g_flOverrideNprOutlineThickness", 0.2f ) : 0.1f );

		var solid = Combo( "F_SOLID_COLOR_OUTLINE" );
		_material.Set( "g_flNprSolidOutline", solid ? 1f : 0f );

		if ( solid )
		{
			var tint = GetVector3( "g_vSolidOutlineTint", 0.1f );
			_material.Set( "g_vNprSolidOutlineTint", new Vector3( ColorMatrices.SrgbToLinear( tint.x ), ColorMatrices.SrgbToLinear( tint.y ), ColorMatrices.SrgbToLinear( tint.z ) ) );
		}
	}

	/// <summary>
	/// The environment shaders read layered textures; until they're ported, their first layer is shown.
	/// Layers keep the tint mask in colour alpha, and self illumination and metalness in g_tPacked.
	/// </summary>
	private void SetupLayer()
	{
		var translucent = Combo( "F_TRANSLUCENT" ) || source.ShaderName is "citadel_overlay.vfx" or "projected_decals.vfx";
		var unlit = Combo( "F_UNLIT" ) || source.ShaderName.StartsWith( "tools_", StringComparison.Ordinal )
			|| source.ShaderName is "sky.vfx" or "unlit.vfx" or "spritecard.vfx";

		SetFeature( "F_TRANSLUCENT", translucent );
		SetFeature( "F_ALPHA_TEST", Combo( "F_ALPHA_TEST" ) && !translucent );
		SetFeature( "F_UNLIT", unlit );
		SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );

		// Skies bind a cubemap, which can't go in the 2D colour slot; they wait for map support.
		var color = FirstTexture( "g_tColor1", "g_tColor" );
		var normalRoughness = FirstTexture( "g_tNormalRoughness1", "g_tNormalRoughness" );
		var packed = FirstTexture( "g_tPacked1" );

		BindTexture( "g_tColor", color );
		BindTexture( "g_tNormalRoughness", normalRoughness );

		if ( packed is not null )
			BindMask( "g_tMetalness", packed, Green, "g_vMetalnessChannel" );

		if ( source.ShaderName.StartsWith( "environment_", StringComparison.Ordinal ) && !translucent )
			BindMask( "g_tTintMask", color, Alpha, "g_vTintMaskChannel" );

		SetAlbedoAdjustments( "1", color );
		_material.Set( "g_flAlphaTestReference", Float( "g_flAlphaTestReference1", Float( "g_flAlphaTestReference", 0.5f ) ) );
	}

	/// <summary>
	/// environment_blend, environment_layer and environment_simple, on deadlock_environment.shader. Its features are
	/// uniforms, set here from the material's combos.
	/// </summary>
	private void SetupEnvironment()
	{
		var simple = source.ShaderName == "environment_simple.vfx";
		var blend = source.ShaderName == "environment_blend.vfx";
		var layers = blend ? (Combo( "F_LAYER_COUNT" ) ? 3 : 2) : 1;

		SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );
		_material.Set( "g_flSimple", simple ? 1f : 0f );
		_material.Set( "g_flLayerCount", (float)layers );

		if ( simple )
		{
			BindTexture( "g_tColor1", "g_tColor" );
			return;
		}

		for ( var layer = 1; layer <= layers; layer++ )
		{
			var n = layer.ToString( System.Globalization.CultureInfo.InvariantCulture );

			BindTexture( $"g_tColor{n}", $"g_tColor{n}" );
			BindTexture( $"g_tNormalRoughness{n}", $"g_tNormalRoughness{n}" );
			BindTexture( $"g_tPacked{n}", $"g_tPacked{n}" );

			SetLayerColor( n, $"g_tColor{n}" );
			_material.Set( $"g_flMaskColorTint{n}", source.GetInt( $"g_bMaskColorTint{n}", 1 ) != 0 ? 1f : 0f );
			_material.Set( $"g_flMaskVertexColorTint{n}", source.GetInt( $"g_bMaskVertexColorTint{n}", 1 ) != 0 ? 1f : 0f );
			_material.Set( $"g_flModelTint{n}", source.GetInt( $"g_bModelTint{n}", 1 ) != 0 ? 1f : 0f );
			_material.Set( $"g_flVertexColorStrength{n}", Float( $"g_fVertexColorStrength{n}", 1f ) );
			_material.Set( $"g_flLayerNormalStrength{n}", Float( $"g_fLayerNormalStrength{n}", 1f ) );
			_material.Set( $"g_flLayerSelfIllumScale{n}", Float( $"g_fLayerSelfIllumScale{n}", 1f ) );
			_material.Set( $"g_vLayerSelfIllumTint{n}", GetVector3( $"g_vLayerSelfIllumTint{n}", 1 ) );
			_material.Set( $"g_vLayerRoughnessContrastBrightness{n}", GetVector2( $"g_vLayerRoughnessContrastBrightness{n}", 1, 0 ) );
			_material.Set( $"g_vLayerTintMaskContrastBrightness{n}", GetVector2( $"g_vLayerTintMaskContrastBrightness{n}", 1, 0 ) );
			_material.Set( $"g_vLayerMetalnessContrastBrightness{n}", GetVector2( $"g_vLayerMetalnessContrastBrightness{n}", 1, 0 ) );
			_material.Set( $"g_vUvTransform{n}", UvTransform( $"g_vScaleRotationTranslation{n}" ) );

			if ( Combo( $"F_LAYER{n}_BIPLANAR" ) )
			{
				_material.Set( $"g_flBiPlanar{n}", 1f );
				_material.Set( $"g_flBiPlanarTiling{n}", Float( $"g_flBiPlanarTiling{n}", Float( "g_flBiPlanarTiling", 196f ) ) );
			}

			if ( layer == 1 )
				continue;

			BindTexture( $"g_tRevealMask{n}", $"g_tRevealMask{n}" );
			_material.Set( $"g_flRevealOffset{n}", Float( $"g_flRevealOffset{n}", 0f ) );
			_material.Set( $"g_flRevealSoftness{n}", Float( $"g_flRevealSoftness{n}", 0.125f ) );
			SetBool( $"g_flRevealInvert{n}", $"g_bRevealInvert{n}" );
			SetBool( $"g_flRevealUseSecondaryUv{n}", $"g_bRevealUseSecondaryUv{n}" );
			_material.Set( $"g_vRevealUvTransform{n}", UvTransform( $"g_vRevealScaleRotationTranslation{n}" ) );

			SetBool( $"g_flBorderEffects{n}", $"g_bBorderEffects{n}" );
			_material.Set( $"g_vBorderWidth{n}", GetVector2( $"g_vBorderWidth{n}", 0.125f ) );
			_material.Set( $"g_vBorderSoftness{n}", GetVector2( $"g_vBorderSoftness{n}", 0.5f ) );
			_material.Set( $"g_vBorderLayerAmount{n}", GetVector3( $"g_vBorderLayerAmount{n}", 1 ) );
			_material.Set( $"g_flBorderBlendMode{n}", (float)source.GetInt( $"g_nBorderBlendMode{n}" ) );
			var borderTint = source.GetVector( $"g_vBorderTint{n}", [0.5f, 0.5f, 0.5f, 1f] );
			_material.Set( $"g_vBorderTint{n}", new Vector4( borderTint[0], borderTint[1], borderTint[2], borderTint[3] ) );
			_material.Set( $"g_flBorderRoughnessStrength{n}", Float( $"g_flBorderRoughnessStrength{n}", 0f ) );
			_material.Set( $"g_flBorderRoughness{n}", Float( $"g_flBorderRoughness{n}", 0.5f ) );
		}

		if ( !blend )
			return;

		if ( Combo( "F_UV_OVERLAY" ) )
		{
			_material.Set( "g_flUvOverlay", 1f );
			BindTexture( "g_tColorOverlay", "g_tColorOverlay" );
			BindTexture( "g_tNormalRoughnessOverlay", "g_tNormalRoughnessOverlay" );
			BindTexture( "g_tRevealOverlay", "g_tRevealOverlay" );

			var transform = source.GetVector( "g_vOverlayScaleRotationTranslation", [1, 0, 0, 0] );
			var secondary = source.GetInt( "g_bUseSecondaryUvForOverlay", 1 ) != 0;
			var identity = transform[0] == 1 && transform[1] == 0 && transform[2] == 0 && transform[3] == 0;

			_material.Set( "g_vOverlayUvTransform", UvTransform( "g_vOverlayScaleRotationTranslation" ) );
			_material.Set( "g_flUseSecondaryUvForOverlay", secondary ? 1f : 0f );
			_material.Set( "g_flOverlayDerivedFrame", secondary || !identity ? 1f : 0f );
			_material.Set( "g_flOverlayDarknessContrast", Float( "g_flOverlayDarknessContrast", 1f ) );
			_material.Set( "g_flOverlayBrightnessContrast", Float( "g_flOverlayBrightnessContrast", 1f ) );
			_material.Set( "g_vOverlayTintMask", PerLayer( "g_nOverlayTintMask", 0 ) );
			_material.Set( "g_vOverlayStrength", PerLayer( "g_flOverlayStrength", 1 ) );
			_material.Set( "g_vOverlayNormalStrength", PerLayer( "g_flOverlayNormalStrength", 1 ) );
			_material.Set( "g_vOverlayRoughnessStrength", PerLayer( "g_flOverlayRoughnessStrength", 0 ) );
			SetBool( "g_flRevealOverlayInvert2", "g_bRevealOverlayInvert2" );
			_material.Set( "g_flRevealOverlayStrength2", Float( "g_flRevealOverlayStrength2", 1f ) );
		}

		var worldOverlays = (int)source.GetInt( "F_WORLD_OVERLAY" );
		if ( worldOverlays > 0 )
		{
			_material.Set( "g_flWorldOverlay", (float)worldOverlays );
			_material.Set( "g_flWorldOverlayObjectSpace", Combo( "F_WORLD_OVERLAY_USE_OBJECT_SPACE" ) ? 1f : 0f );
			BindTexture( "g_tColorWorldOverlay", "g_tColorWorldOverlay" );
			_material.Set( "g_vWorldOverlayTintMask", PerLayer( "g_nWorldOverlayTintMask", 1 ) );

			if ( worldOverlays >= 2 )
			{
				BindTexture( "g_tColorWorldOverlay2", "g_tColorWorldOverlay2" );
				_material.Set( "g_vWorldOverlayTiling", GetVector2( "g_vWorldOverlayTiling", 512f ) );
				_material.Set( "g_vWorldOverlayBrightnessContrast", GetVector2( "g_vWorldOverlayBrightnessContrast", 1f ) );
				_material.Set( "g_vWorldOverlayDarknessContrast", GetVector2( "g_vWorldOverlayDarknessContrast", 1f ) );

				for ( var layer = 1; layer <= 3; layer++ )
					_material.Set( $"g_vWorldOverlayStrengths{layer}", GetVector2( $"g_vWorldOverlayStrengths{layer}", 1, 0 ) );
			}
			else
			{
				var tiling = Float( "g_flWorldOverlayTiling", 512f );
				_material.Set( "g_vWorldOverlayTiling", new Vector2( tiling, tiling ) );
				_material.Set( "g_vWorldOverlayBrightnessContrast", new Vector2( Float( "g_flWorldOverlayBrightnessContrast", 1f ), 1 ) );
				_material.Set( "g_vWorldOverlayDarknessContrast", new Vector2( Float( "g_flWorldOverlayDarknessContrast", 1f ), 1 ) );

				for ( var layer = 1; layer <= 3; layer++ )
					_material.Set( $"g_vWorldOverlayStrengths{layer}", new Vector2( Float( $"g_flWorldOverlayStrength{layer}", 1f ), 0 ) );
			}
		}
	}

	/// <summary>
	/// pbr.vfx with F_GLASS, on deadlock_glass.shader: g_tGlass says how much of what's behind shows through, blurred
	/// and refracted by the game's cloak parameters.
	/// </summary>
	private void SetupGlass()
	{
		SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );

		BindTexture( "g_tColor", "g_tColor" );
		BindTexture( "g_tNormalRoughness", "g_tNormalRoughness" );
		BindTexture( "g_tTintMask", "g_tTintMask" );
		BindTexture( "g_tGlass", "g_tGlass" );

		SetAlbedoAdjustments( "1", "g_tColor" );
		_material.Set( "g_flGlassReflectance", Float( "g_flGlassReflectance1", 0.04f ) );

		if ( Combo( "F_SELF_ILLUM" ) )
			SetupSelfIllum( "g_tSelfIllumMask", Red );

		_material.Set( "g_flGlassBlur", Float( "g_flCloakBlurAmount", 0.005f ) );
		_material.Set( "g_vGlassBlurRoughnessFactors", new Vector2( Float( "g_flCloakBlurFactorMinRoughness", 1f ), Float( "g_flCloakBlurFactorMaxRoughness", 1f ) ) );
		_material.Set( "g_flGlassBlurNoise", Float( "g_flCloakBlurNoiseAmount", 0.5f ) );
		_material.Set( "g_flGlassRefract", Float( "g_flCloakRefractAmount", 0.075f ) * Float( "g_flCloakFactor1", 1f ) * Float( "g_flFullyCloakedRefractFactor1", 1f ) );
	}

	/// <summary>
	/// environment_parallax_layered, windows: glass over two layers set back behind it, on deadlock_parallax_layered.shader.
	/// </summary>
	private void SetupParallax()
	{
		SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );

		foreach ( var texture in new[] { "g_tColor", "g_tMask", "g_tNormalRoughness", "g_tColor2", "g_tMask2", "g_tNormalRoughness2", "g_tColor3", "g_tReflectionOcclusionMask" } )
			BindTexture( texture, texture );

		SetLayerColor( "1", "g_tColor" );
		SetLayerColor( "2", "g_tColor2" );
		SetLayerColor( "3", "g_tColor3" );

		for ( var layer = 1; layer <= 3; layer++ )
			_material.Set( $"g_flVertexColorStrength{layer}", Float( $"g_fVertexColorStrength{layer}", 1f ) );

		// The shader takes these as its expressions compute them from the material: emissive tints as
		// pow( 2, level ) * SrgbGammaToLinear( tint ), layer depths in 1024ths.
		_material.Set( "g_vEmissiveTint2", EmissiveTint( "g_vEmissiveTint2", "g_flEmissiveLevel2" ) );
		_material.Set( "g_flEmissiveLevel2", Float( "g_flEmissiveLevel2", 0f ) );
		_material.Set( "g_vEmissiveTint3", EmissiveTint( "g_vEmissiveTint3", "g_flEmissiveLevel3" ) );
		_material.Set( "g_flEmissiveLevel3", Float( "g_flEmissiveLevel3", 0f ) );

		_material.Set( "g_flLayer2Offset", Float( "g_flLayer2Offset", 0f ) / 1024f );
		_material.Set( "g_flParallaxViewAngleLimit2", Float( "g_flParallaxViewAngleLimit2", 0.5f ) );
		SetBool( "g_flLayer2UseSecondaryUv", "g_bLayer2UseSecondaryUV" );
		_material.Set( "g_flLayer2ShadingBlendStrength", Float( "g_bLayer2ShadingBlendStrength", 1f ) );
		_material.Set( "g_flLayer3Offset", Float( "g_flLayer3Offset", 0f ) / 1024f );
		_material.Set( "g_flParallaxViewAngleLimit3", Float( "g_flParallaxViewAngleLimit3", 0.5f ) );
		SetBool( "g_flLayer3UseSecondaryUv", "g_bLayer3UseSecondaryUV" );
		_material.Set( "g_flReflectionOcclusionOffset", Float( "g_flReflectionOcclusionOffset", 0f ) / 1024f );
		_material.Set( "g_flReflectionOcclusionViewAngleLimit", Float( "g_flReflectionOcclusionViewAngleLimit", 0.5f ) );
		SetBool( "g_flReflectionOcclusionUseSecondaryUv", "g_bReflectionOcclusionUseSecondaryUV" );

		// The lit windows bloom by their colour times this and their glow, unthresholded (Deadlock Bloom Layer).
		_material.Set( "g_flBloomAmount", Combo( "F_OVERRIDE_BLOOM_AMOUNT" ) ? Float( "g_flOverrideBloomAmount1", 1f ) : 0f );
	}

	private Vector3 EmissiveTint( string tintParameter, string levelParameter )
	{
		var tint = GetVector3( tintParameter, 1 );
		var scale = MathF.Pow( 2, Float( levelParameter, 0f ) );
		return new Vector3( ColorMatrices.SrgbToLinear( tint.x ), ColorMatrices.SrgbToLinear( tint.y ), ColorMatrices.SrgbToLinear( tint.z ) ) * scale;
	}

	/// <summary>
	/// citadel_overlay, decals laid on the world as geometry, on deadlock_overlay.shader.
	/// </summary>
	private void SetupOverlay()
	{
		BindTexture( "g_tColor", "g_tColor" );
		BindTexture( "g_tNormalRoughness", "g_tNormalRoughness" );
		BindTexture( "g_tSelfIllumTintMask", "g_tSelfIllumTintMask" );

		_material.Set( "g_vColorTint", GetVector3( "g_vColorTint", 1 ) );
		_material.Set( "g_vAlbedoTexcoordUVOffset", GetVector2( "g_vAlbedoTexcoordUVOffset", 0 ) );
		_material.Set( "g_vAlbedoScrollSpeed", GetVector2( "g_vAlbedoScrollSpeed", 0 ) );
		_material.Set( "g_vNormalAndRoughnessTexcoordUVOffset", GetVector2( "g_vNormalAndRoughnessTexcoordUVOffset", 0 ) );
		_material.Set( "g_vNormalAndRoughnessScrollSpeed", GetVector2( "g_vNormalAndRoughnessScrollSpeed", 0 ) );
		_material.Set( "g_vSelfIllumTexcoordOffset", GetVector2( "g_vSelfIllumTexcoordOffset", 0 ) );
		_material.Set( "g_vSelfIllumScrollSpeed", GetVector2( "g_vSelfIllumScrollSpeed", 0 ) );
		_material.Set( "g_vColorTranslucencyInputRange", GetVector2( "g_vColorTranslucencyInputRange", 0, 1 ) );
		_material.Set( "g_vColorTranslucencyOutputRange", GetVector2( "g_vColorTranslucencyOutputRange", 0, 1 ) );
		_material.Set( "g_vSelfIllumTint", GetVector3( "g_vSelfIllumTint", 1 ) );
		_material.Set( "g_flSelfIllumScale", Float( "g_flSelfIllumScale", 0f ) );
		_material.Set( "g_flSelfIllumAlbedoFactor", Float( "g_flSelfIllumAlbedoFactor", 1f ) );
	}

	/// <summary>
	/// cosmic_veil, the curtains across doorways that hide what's behind them, on deadlock_cosmic_veil.shader. The
	/// defaults are the game shader's.
	/// </summary>
	private void SetupVeil()
	{
		BindTexture( "g_tMasks", "g_tMasks" );
		BindTexture( "g_tWarpMask", "g_tWarpMask" );

		_material.Set( "g_flRevealMax", Float( "g_flRevealMax", 1f ) );
		_material.Set( "g_vWarpMaskBrightnessAmounts", GetVector2( "g_vWarpMaskBrightnessAmounts", 1, 1 ) );
		SetBool( "g_flDoubleSidedVeil", "g_bDoubleSidedVeil" );
		_material.Set( "g_vHueShiftAmounts", GetVector2( "g_vHueShiftAmounts", -0.1f, 0.1f ) );
		_material.Set( "g_vRefractBlurAmounts", GetVector2( "g_vRefractBlurAmounts", 0.005f, 0.005f ) );
		_material.Set( "g_vRefractBlurAmountsVeiled", GetVector2( "g_vRefractBlurAmountsVeiled", 0.005f, 0.005f ) );
		_material.Set( "g_vRefractBrightnessAmounts", GetVector2( "g_vRefractBrightnessAmounts", 1, 0.2f ) );
		_material.Set( "g_vRefractBrightnessAmountsVeiled", GetVector2( "g_vRefractBrightnessAmountsVeiled", 1, 0.2f ) );
		_material.Set( "g_vRefractNoiseAmounts", GetVector2( "g_vRefractNoiseAmounts", 0.5f, 0.5f ) );
		_material.Set( "g_vRefractNoiseAmountsVeiled", GetVector2( "g_vRefractNoiseAmountsVeiled", 0.5f, 0.5f ) );
		_material.Set( "g_flSmokeRateVariance", Float( "g_flSmokeRateVariance", -0.8f ) );
		_material.Set( "g_flSmokeSpeed", Float( "g_flSmokeSpeed", 1f ) );
		_material.Set( "g_flSmokeStepRotation", Float( "g_flSmokeStepRotation", 0.01f ) );
		_material.Set( "g_flSmokeStepSize", Float( "g_flSmokeStepSize", 0.005f ) );
		_material.Set( "g_vSmokeSpatialFrequencies", GetVector2( "g_vSmokeSpatialFrequencies", 12, 17 ) );
	}

	/// <summary>
	/// A layer's colour correction and tint, as the rows of the matrices the game builds from its parameters.
	/// </summary>
	private void SetLayerColor( string layer, string colorTexture )
	{
		var csb = source.GetVector( $"g_vAlbedoContrastSaturationBrightness{layer}", [1, 1, 1, 0] );

		var pivot = NVector3.One;
		if ( csb[0] != 1f && mount.ReadTextureReflectivity( source.GetTexture( colorTexture ) ) is { } reflectivity )
			pivot = new NVector3( reflectivity[0], reflectivity[1], reflectivity[2] );

		SetRows( $"g_vAlbedoCorrect{layer}", ColorMatrices.Rows( ColorMatrices.ColorCorrect( new NVector3( csb[0], csb[1], csb[2] ), pivot ) ) );

		var tint = source.GetVector( $"g_vColorTint{layer}", [1, 1, 1, 0] );
		var linearTint = new NVector3( ColorMatrices.SrgbToLinear( tint[0] ), ColorMatrices.SrgbToLinear( tint[1] ), ColorMatrices.SrgbToLinear( tint[2] ) );
		SetRows( $"g_vColorTint{layer}", ColorMatrices.Rows( ColorMatrices.Tint( linearTint, TintStrength, (int)source.GetInt( $"g_nTextureColorTintMode{layer}" ) ) ) );
	}

	/// <summary>
	/// A (scale, rotation in degrees, translation) UV transform as the shader applies it: offset by the translation
	/// over the scale, then rotated and scaled, as ( scale * sin, scale * cos, offset ).
	/// </summary>
	private Vector4 UvTransform( string parameter )
	{
		var value = source.GetVector( parameter, [1, 0, 0, 0] );
		var scale = value[0] != 0 ? value[0] : 1;
		var angle = value[1] * MathF.PI / 180;
		return new Vector4( scale * MathF.Sin( angle ), scale * MathF.Cos( angle ), value[2] / scale, value[3] / scale );
	}

	/// <summary>
	/// A scalar parameter of each of the three layers, such as g_flOverlayStrength1 to 3.
	/// </summary>
	private Vector3 PerLayer( string prefix, float fallback )
	{
		float Get( int layer ) => source.IntParams.TryGetValue( $"{prefix}{layer}", out var number ) ? number : Float( $"{prefix}{layer}", fallback );
		return new Vector3( Get( 1 ), Get( 2 ), Get( 3 ) );
	}

	/// <summary>
	/// generic.vfx has an RGB normal map and a separate roughness texture, which the shader doesn't read
	/// yet, so it keeps a flat normal.
	/// </summary>
	private void SetupGeneric()
	{
		var translucent = Combo( "F_TRANSLUCENT" );

		SetFeature( "F_TRANSLUCENT", translucent );
		SetFeature( "F_ALPHA_TEST", Combo( "F_ALPHA_TEST" ) && !translucent );
		SetFeature( "F_UNLIT", Combo( "F_UNLIT" ) );
		SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );

		BindTexture( "g_tColor", "g_tColor" );

		if ( Combo( "F_SELF_ILLUM" ) )
			SetupSelfIllum( "g_tSelfIllumMask", Red );
	}

	/// <summary>
	/// black.vfx and black_unlit.vfx: plain black that nothing lights.
	/// </summary>
	private void SetupBlack()
	{
		SetFeature( "F_UNLIT", true );
		SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );

		_material.Set( "g_tColor", SolidTexture( new Vector4( 0, 0, 0, 1 ), true ) );
		_boundTextures.Add( "g_tColor" );
	}

	private void SetupSelfIllum( string maskParameter, float[] channel )
	{
		_material.Set( "g_flSelfIllum", 1f );
		BindMask( "g_tSelfIllumMask", maskParameter, channel, "g_vSelfIllumMaskChannel" );

		_material.Set( "g_vSelfIllumTint", GetVector3( "g_vSelfIllumTint1", 1 ) );
		_material.Set( "g_flSelfIllumScale", Float( "g_flSelfIllumScale1", 0f ) );
		_material.Set( "g_flSelfIllumAlbedoFactor", Float( "g_flSelfIllumAlbedoFactor1", 1f ) );
		_material.Set( "g_vSelfIllumFresnelTint", GetVector3( "g_vSelfIllumFresnelMaskTint1", 1 ) );
		_material.Set( "g_flSelfIllumFresnelExponent", Float( "g_flSelfIllumFresnelMaskExponent", 5f ) );
		_material.Set( "g_vSelfIllumFresnelRange", GetVector2( "g_vSelfIllumFresnelMaskRange1", 0 ) );
		SetBool( "g_flSelfIllumSecondaryUv", "g_bUseSecondaryUvForSelfIllum1" );
		_material.Set( "g_vSelfIllumScrollSpeed", GetVector2( "g_vSelfIllumScrollSpeed1", 0 ) );
		_material.Set( "g_flSelfIllumScrollQuantize", Float( "g_flSelfIllumScrollQuantize1", 0f ) );

		if ( Combo( "F_ENABLE_TEXTURE_TRANSFORMS" ) )
			SetTransform( "SelfIllum", "g_vSelfIllumTexcoordScale1", "g_vSelfIllumTexcoordOffset1", "g_flSelfIllumTexcoordRotation1" );
	}

	/// <summary>
	/// The colour correction and tint matrices, and how the tint and vertex colour are masked.
	/// </summary>
	private void SetAlbedoAdjustments( string layer, string colorTexture )
	{
		var csb = source.GetVector( $"g_vAlbedoContrastSaturationBrightness{layer}", [1, 1, 1, 0] );

		// Contrast pivots around the texture's average colour, which only matters when it isn't 1.
		var pivot = NVector3.One;
		if ( csb[0] != 1f && colorTexture is not null && mount.ReadTextureReflectivity( source.GetTexture( colorTexture ) ) is { } reflectivity )
			pivot = new NVector3( reflectivity[0], reflectivity[1], reflectivity[2] );

		var correct = ColorMatrices.Rows( ColorMatrices.ColorCorrect( new NVector3( csb[0], csb[1], csb[2] ), pivot ) );
		SetRows( "g_vAlbedoCorrect", correct );

		var tint = source.GetVector( $"g_vColorTint{layer}", [1, 1, 1, 0] );
		var linearTint = new NVector3( ColorMatrices.SrgbToLinear( tint[0] ), ColorMatrices.SrgbToLinear( tint[1] ), ColorMatrices.SrgbToLinear( tint[2] ) );
		var mode = (int)source.GetInt( $"g_nTextureColorTintMode{layer}" );
		SetRows( "g_vColorTint", ColorMatrices.Rows( ColorMatrices.Tint( linearTint, TintStrength, mode ) ) );

		_material.Set( "g_flMaskColorTint", source.GetInt( $"g_bMaskColorTint{layer}", 1 ) != 0 ? 1f : 0f );
		_material.Set( "g_flMaskVertexColorTint", source.GetInt( $"g_bMaskVertexColorTint{layer}", 1 ) != 0 ? 1f : 0f );
		_material.Set( "g_flVertexColorStrength", Float( $"g_fVertexColorStrength{layer}", 1f ) );
		SetBool( "g_flApplyTintToVertexColors", "g_bApplyTintToVertexColors" );
	}

	private void SetTextureCoordinates( bool transforms )
	{
		_material.Set( "g_vAlbedoScrollSpeed", GetVector2( "g_vAlbedoScrollSpeed1", 0 ) );
		_material.Set( "g_flAlbedoScrollQuantize", Float( "g_flAlbedoScrollQuantize1", 0f ) );
		_material.Set( "g_vNormalScrollSpeed", GetVector2( "g_vNormalAndRoughnessScrollSpeed1", 0 ) );
		_material.Set( "g_flNormalScrollQuantize", Float( "g_flNormalAndRoughnessScrollQuantize1", 0f ) );

		if ( !transforms )
			return;

		SetTransform( "Albedo", "g_vAlbedoTexcoordScale1", "g_vAlbedoTexcoordOffset1", "g_flAlbedoTexcoordRotation1" );
		SetTransform( "Normal", "g_vNormalTexcoordScale1", "g_vNormalTexcoordOffset1", "g_flNormalTexcoordRotation1" );
	}

	private void SetTransform( string target, string scale, string offset, string rotation )
	{
		var s = source.GetVector( scale, [1, 1, 0, 0] );
		var o = source.GetVector( offset, [0, 0, 0, 0] );
		_material.Set( $"g_v{target}Transform", new Vector4( s[0], s[1], o[0], o[1] ) );
		_material.Set( $"g_fl{target}Rotation", Float( rotation, 0f ) );
	}

	private void SetRows( string prefix, float[][] rows )
	{
		_material.Set( $"{prefix}R", new Vector4( rows[0][0], rows[0][1], rows[0][2], rows[0][3] ) );
		_material.Set( $"{prefix}G", new Vector4( rows[1][0], rows[1][1], rows[1][2], rows[1][3] ) );
		_material.Set( $"{prefix}B", new Vector4( rows[2][0], rows[2][1], rows[2][2], rows[2][3] ) );
	}

	private string FirstTexture( params string[] parameters )
	{
		foreach ( var parameter in parameters )
		{
			if ( source.GetTexture( parameter ) is { Length: > 0 } )
				return parameter;
		}

		return null;
	}

	/// <summary>
	/// Binds the texture a Deadlock parameter names to a shader texture. Returns false if there isn't one.
	/// </summary>
	private bool BindTexture( string shaderTexture, string parameter )
	{
		if ( parameter is null || mount.LoadTexture( source.GetTexture( parameter ), textureLevel ) is not { } texture )
			return false;

		_material.Set( shaderTexture, texture );
		_boundTextures.Add( shaderTexture );
		return true;
	}

	/// <summary>
	/// Binds a mask texture and the channel it's read from. Without a texture the shader's fallback is used.
	/// </summary>
	private void BindMask( string shaderTexture, string parameter, float[] channel, string channelParameter )
	{
		if ( BindTexture( shaderTexture, parameter ) )
			_material.Set( channelParameter, new Vector4( channel[0], channel[1], channel[2], channel[3] ) );
	}

	private bool Combo( string feature ) => source.GetInt( feature ) != 0;

	private void SetFeature( string feature, bool enabled ) => _material.SetFeature( feature, enabled ? 1 : 0 );

	private void SetBool( string shaderParameter, string parameter ) => _material.Set( shaderParameter, source.GetInt( parameter ) != 0 ? 1f : 0f );

	private float Float( string parameter, float fallback ) => source.FloatParams.TryGetValue( parameter, out var value ) ? value : fallback;

	private Vector2 GetVector2( string parameter, float fallback ) => GetVector2( parameter, fallback, fallback );

	private Vector2 GetVector2( string parameter, float x, float y )
	{
		var value = source.GetVector( parameter, [x, y, 0, 0] );
		return new Vector2( value[0], value[1] );
	}

	private Vector3 GetVector3( string parameter, float fallback ) => GetVector3( parameter, fallback, fallback, fallback );

	private Vector3 GetVector3( string parameter, float x, float y, float z )
	{
		var value = source.GetVector( parameter, [x, y, z, 0] );
		return new Vector3( value[0], value[1], value[2] );
	}
}
