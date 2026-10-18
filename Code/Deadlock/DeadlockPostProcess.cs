using Sandbox.Rendering;
using System;

namespace Deadlock;

/// <summary>
/// Deadlock's post processing, as a map's post_processing_volume sets it up (a .vpost): the camera's exposure, bloom,
/// the vignette and the game's filmic tonemapping curve. It takes the place of s&amp;box's Tone Mapping, so a camera
/// should have one or the other.
/// </summary>
[Title( "Deadlock Post Processing" ), Category( "Post Processing" ), Icon( "camera" )]
public sealed class DeadlockPostProcess : BasePostProcess<DeadlockPostProcess>
{
	/// <summary>
	/// The camera's exposure, in stops. Deadlock maps fix it with their post processing volume's exposure range.
	/// </summary>
	[Property, Group( "Exposure" )] public float Exposure { get; set; } = 0;

	/// <summary>
	/// Stops added before the curve.
	/// </summary>
	[Property, Group( "Tonemapping" )] public float ExposureBias { get; set; } = 0.8992f;
	[Property, Group( "Tonemapping" )] public float ShoulderStrength { get; set; } = 0.3538f;
	[Property, Group( "Tonemapping" )] public float LinearStrength { get; set; } = 0.3258f;
	[Property, Group( "Tonemapping" )] public float LinearAngle { get; set; } = 0.2528f;
	[Property, Group( "Tonemapping" )] public float ToeStrength { get; set; } = 0.6966f;
	[Property, Group( "Tonemapping" )] public float ToeNumerator { get; set; } = 0;
	[Property, Group( "Tonemapping" )] public float ToeDenominator { get; set; } = 0.7819f;

	/// <summary>
	/// The brightest input the curve maps to white, in stops.
	/// </summary>
	[Property, Group( "Tonemapping" )] public float WhitePoint { get; set; } = 3.9996f;

	/// <summary>
	/// How the bloom goes on: added to the image before the curve, or screened over it after.
	/// </summary>
	[Property, Group( "Bloom" )] public BloomBlend BloomMode { get; set; } = BloomBlend.Add;
	[Property, Group( "Bloom" )] public float BloomStrength { get; set; } = 0.5f;

	/// <summary>
	/// Where the bloom starts and how far over it a pixel blooms fully, in exposed luminance.
	/// </summary>
	[Property, Group( "Bloom" )] public float BloomThreshold { get; set; } = 1f;
	[Property, Group( "Bloom" )] public float BloomThresholdWidth { get; set; } = 0.999f;

	/// <summary>
	/// How much of each blur level the bloom takes, from a quarter of the screen down to a 64th.
	/// </summary>
	[Property, Group( "Bloom" )] public List<float> BloomWeights { get; set; } = [0, 0.070259f, 0.181076f, 0.300013f, 0.448652f];

	/// <summary>
	/// Negative darkens the edges of the screen, positive brightens them.
	/// </summary>
	[Property, Group( "Vignette" )] public float VignetteStrength { get; set; } = -0.5982f;

	/// <summary>
	/// Offset from the middle of the screen, -1 to 1 across it.
	/// </summary>
	[Property, Group( "Vignette" )] public Vector2 VignetteCenter { get; set; }
	[Property, Group( "Vignette" )] public float VignetteRadius { get; set; } = 0.4386f;
	[Property, Group( "Vignette" )] public float VignetteRoundness { get; set; } = 0.2134f;
	[Property, Group( "Vignette" )] public float VignetteFeather { get; set; } = 0.6098f;
	[Property, Group( "Vignette" )] public Color VignetteTint { get; set; } = Color.White;

	public enum BloomBlend
	{
		Add,
		Screen,
	}

	private static readonly Material Shader = Material.FromShader( "shaders/deadlock_postprocess.shader" );
	private static readonly Material BloomShader = Material.FromShader( "shaders/deadlock_bloom.shader" );

	// A quarter of the screen down to a 64th.
	private const int BloomLevels = 5;
	private static readonly string[] BloomLevelNames = ["DeadlockBloom0", "DeadlockBloom1", "DeadlockBloom2", "DeadlockBloom3", "DeadlockBloom4"];
	private static readonly string[] BloomBlurNames = ["DeadlockBloomBlur0", "DeadlockBloomBlur1", "DeadlockBloomBlur2", "DeadlockBloomBlur3", "DeadlockBloomBlur4"];

	/// <summary>
	/// The bloom layer (Deadlock Bloom Layer) is 10 bit: materials write their bloom as a fraction of this.
	/// </summary>
	public const float BloomLayerRange = 64;

	// Made on first use: a hot reload can leave an existing component without it.
	private CommandList _commands;
	private bool _warned;

	public override void Render()
	{
		// The game's exposure is fixed by the map, not adapted to the image.
		Camera.AutoExposure.Enabled = false;

		if ( !_warned && Camera.GetComponent<Tonemapping>() is { Enabled: true } )
		{
			_warned = true;
			Log.Warning( $"{Camera.GameObject.Name} has both Tone Mapping and Deadlock Post Processing; the image gets tonemapped twice. Disable Tone Mapping." );
		}

		_commands ??= new CommandList( "Deadlock Post Processing" );
		_commands.Reset();

		var color = _commands.Attributes.GrabFrameTexture( "ColorBuffer" );
		var bloomStrength = GetWeighted( x => x.BloomStrength, 0 );

		if ( bloomStrength > 0 )
			RenderBloom( color, bloomStrength );

		var exposure = MathF.Pow( 2, GetWeighted( x => x.Exposure, 0 ) + GetWeighted( x => x.ExposureBias, 0 ) );
		_commands.Attributes.Set( "Exposure", exposure );

		// Heroes' NPR bounce light is held to fixed brightnesses after this exposure.
		Scene.RenderAttributes.Set( "DeadlockExposure", exposure );

		// The bloom texture already has the strength in it; it goes on added or screened.
		var bloom = bloomStrength > 0 ? 1f : 0f;
		var screen = GetWeighted( x => x.BloomMode == BloomBlend.Screen ? 1f : 0f, 0 );
		_commands.Attributes.Set( "BloomAdd", bloom * (1 - screen) );
		_commands.Attributes.Set( "BloomScreen", bloom * screen );

		var shoulder = GetWeighted( x => x.ShoulderStrength, 0.15f );
		var linear = GetWeighted( x => x.LinearStrength, 0.5f );
		var angle = GetWeighted( x => x.LinearAngle, 0.1f );
		var toe = GetWeighted( x => x.ToeStrength, 0.2f );
		var toeNumerator = GetWeighted( x => x.ToeNumerator, 0.02f );
		var toeDenominator = GetWeighted( x => x.ToeDenominator, 0.3f );
		var white = MathF.Pow( 2, GetWeighted( x => x.WhitePoint, 4 ) );

		float Curve( float x ) => (x * (x * shoulder + angle * linear) + toe * toeNumerator) / (x * (x * shoulder + linear) + toe * toeDenominator) - toeNumerator / toeDenominator;

		_commands.Attributes.Set( "TonemapCurve", new Vector4( shoulder, linear, angle, toe ) );
		_commands.Attributes.Set( "TonemapToe", new Vector4( toeNumerator, toeDenominator, white, 1 / Curve( white ) ) );

		// Packed as the game packs them: negative strengths darken the edges by up to all of it, positive ones add
		// 3 s^2; the feather is twice as wide; roundness 0 to 1 takes the superellipse from exponent 8 to 2.
		var strength = GetWeighted( x => x.VignetteStrength, 0 );
		var darken = Math.Clamp( -strength, 0f, 1f );
		var brighten = Math.Clamp( strength, 0f, 1f );
		var exponent = 2 / (0.25f + 0.75f * Math.Clamp( GetWeighted( x => x.VignetteRoundness, 1 ), 0f, 1f ));

		_commands.Attributes.Set( "Vignette", strength != 0 ? 1f : 0f );
		var center = GetWeighted( x => x.VignetteCenter, Vector2.Zero );
		_commands.Attributes.Set( "VignetteCenter", new Vector4( center.x, -center.y, 1 - darken, 3 * brighten * brighten ) );
		_commands.Attributes.Set( "VignetteTint", GetWeighted( x => x.VignetteTint, Color.White ) );
		_commands.Attributes.Set( "VignetteShape", new Vector4( MathF.Max( GetWeighted( x => x.VignetteRadius, 0.5f ), 0.01f ), MathF.Max( 2 * GetWeighted( x => x.VignetteFeather, 0.5f ), 0.001f ), exponent, -1 / exponent ) );

		_commands.Blit( Shader );
		InsertCommandList( _commands, Stage.Tonemapping, 0, "Deadlock Post Processing" );
	}

	/// <summary>
	/// The game's bloom: the screen thresholded at a quarter of its size (four taps, each by how far its exposed
	/// luminance passes the threshold), plus the bloom layer; halved four times; each level blurred across and down;
	/// then the levels summed back up by their weights, into the first.
	/// </summary>
	private void RenderBloom( RenderTargetHandle color, float strength )
	{
		var threshold = GetWeighted( x => x.BloomThreshold, 1 );
		var width = MathF.Max( GetWeighted( x => x.BloomThresholdWidth, 0.999f ), 0.001f );
		_commands.Attributes.Set( "BloomThreshold", new Vector2( 1 / width, -threshold / width ) );
		_commands.Attributes.Set( "BloomExposure", MathF.Pow( 2, GetWeighted( x => x.Exposure, 0 ) ) );
		_commands.Attributes.Set( "BloomScale", strength );
		_commands.Attributes.Set( "BloomEffectsRange", BloomLayerRange );

		var bloom = BloomLevel( 0, color );

		_commands.ClearRenderTarget();
		_commands.Attributes.Set( "BloomTexture", bloom.ColorTexture );
	}

	/// <summary>
	/// One level: made from the one above it (the screen, for the first), blurred, then summed with the levels below
	/// it, into its blur target, which comes back. The smallest comes back blurred only, for its weight to go on above.
	/// </summary>
	private RenderTargetHandle BloomLevel( int level, RenderTargetHandle above )
	{
		// Temporary targets are sized from the viewport bound when they're made, which the level above left at its own
		// size: back to the view's first.
		_commands.ClearRenderTarget();

		var target = _commands.GetRenderTarget( BloomLevelNames[level], 4 << level, ImageFormat.RGBA16161616F, ImageFormat.None );
		var blur = _commands.GetRenderTarget( BloomBlurNames[level], 4 << level, ImageFormat.RGBA16161616F, ImageFormat.None );

		BloomPass( level == 0 ? 0 : 1, above, target );
		BloomPass( 2, target, blur );
		BloomPass( 3, blur, target );

		if ( level == BloomLevels - 1 )
			return target;

		var below = BloomLevel( level + 1, target );

		_commands.Attributes.Set( "BloomLevel", target.ColorTexture );
		_commands.Attributes.Set( "BloomBelowWeight", level + 1 == BloomLevels - 1 ? BloomWeight( level + 1 ) : 1f );
		_commands.Attributes.Set( "BloomLevelWeight", BloomWeight( level ) );
		BloomPass( 4, below, blur );

		return blur;
	}

	private float BloomWeight( int level ) => GetWeighted( x => x.BloomWeights is { } weights && level < weights.Count ? weights[level] : 0f, 0 );

	private void BloomPass( int pass, RenderTargetHandle source, RenderTargetHandle target )
	{
		_commands.SetRenderTarget( target );
		_commands.Attributes.SetCombo( "D_PASS", pass );
		_commands.Attributes.Set( "BloomSource", source.ColorTexture );
		_commands.Attributes.Set( "BloomSourceInvSize", source.Size, true );
		_commands.Attributes.Set( "BloomTargetInvSize", target.Size, true );
		_commands.Blit( BloomShader );
		_commands.ResourceBarrierTransition( target, ResourceState.PixelShaderResource );
	}
}
