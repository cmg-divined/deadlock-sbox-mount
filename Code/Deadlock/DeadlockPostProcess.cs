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
	[Property, Group( "Bloom" )] public float BloomThreshold { get; set; } = 1f;

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
	private static readonly ComputeShader BloomShader = new( "postprocess/postprocess_bloom_cs" );

	// Half resolution down to a 64th.
	private const int BloomMips = 6;
	private static readonly string[] BloomMipNames = ["DeadlockBloom0", "DeadlockBloom1", "DeadlockBloom2", "DeadlockBloom3", "DeadlockBloom4", "DeadlockBloom5"];

	private readonly CommandList _commands = new( "Deadlock Post Processing" );
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

		_commands.Reset();

		var color = _commands.Attributes.GrabFrameTexture( "ColorBuffer" );
		var bloomStrength = GetWeighted( x => x.BloomStrength, 0 );

		if ( bloomStrength > 0 )
			RenderBloom( color );

		var exposure = MathF.Pow( 2, GetWeighted( x => x.Exposure, 0 ) + GetWeighted( x => x.ExposureBias, 0 ) );
		_commands.Attributes.Set( "Exposure", exposure );

		// The bloom accumulates every level into the top one; the game's strength is for their average.
		var bloom = bloomStrength / BloomMips;
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

		var strength = GetWeighted( x => x.VignetteStrength, 0 );
		var roundness = MathF.Max( GetWeighted( x => x.VignetteRoundness, 1 ), 0.01f );
		var exponent = 2 / roundness;

		_commands.Attributes.Set( "Vignette", strength != 0 ? 1f : 0f );
		var center = GetWeighted( x => x.VignetteCenter, Vector2.Zero );
		_commands.Attributes.Set( "VignetteCenter", new Vector4( center.x, center.y, strength < 0 ? 1 + strength : 1 - strength, MathF.Max( strength, 0 ) ) );
		_commands.Attributes.Set( "VignetteTint", GetWeighted( x => x.VignetteTint, Color.White ) );
		_commands.Attributes.Set( "VignetteShape", new Vector4( GetWeighted( x => x.VignetteRadius, 0.5f ), GetWeighted( x => x.VignetteFeather, 0.5f ), exponent, -1 / exponent ) );

		_commands.Blit( Shader );
		InsertCommandList( _commands, Stage.Tonemapping, 0, "Deadlock Post Processing" );
	}

	/// <summary>
	/// Thresholds the image and blurs it down and back up, into the first level.
	/// </summary>
	private void RenderBloom( RenderTargetHandle color )
	{
		var mip0 = _commands.GetRenderTarget( BloomMipNames[0], 2, ImageFormat.RGBA16161616F, ImageFormat.None );
		var mip1 = _commands.GetRenderTarget( BloomMipNames[1], 4, ImageFormat.RGBA16161616F, ImageFormat.None );
		var mip2 = _commands.GetRenderTarget( BloomMipNames[2], 8, ImageFormat.RGBA16161616F, ImageFormat.None );
		var mip3 = _commands.GetRenderTarget( BloomMipNames[3], 16, ImageFormat.RGBA16161616F, ImageFormat.None );
		var mip4 = _commands.GetRenderTarget( BloomMipNames[4], 32, ImageFormat.RGBA16161616F, ImageFormat.None );
		var mip5 = _commands.GetRenderTarget( BloomMipNames[5], 64, ImageFormat.RGBA16161616F, ImageFormat.None );

		_commands.Attributes.Set( "Threshold", GetWeighted( x => x.BloomThreshold, 1 ) );

		// Threshold, full resolution to half.
		_commands.Attributes.SetCombo( "D_STAGE", 0 );
		BloomPass( color, mip0, ResourceState.NonPixelShaderResource );

		// Down.
		_commands.Attributes.SetCombo( "D_STAGE", 1 );
		BloomPass( mip0, mip1, ResourceState.NonPixelShaderResource );
		BloomPass( mip1, mip2, ResourceState.NonPixelShaderResource );
		BloomPass( mip2, mip3, ResourceState.NonPixelShaderResource );
		BloomPass( mip3, mip4, ResourceState.NonPixelShaderResource );
		BloomPass( mip4, mip5, ResourceState.NonPixelShaderResource );

		// Up, each level adding the one below.
		_commands.Attributes.SetCombo( "D_STAGE", 2 );
		BloomPass( mip5, mip4, ResourceState.NonPixelShaderResource );
		BloomPass( mip4, mip3, ResourceState.NonPixelShaderResource );
		BloomPass( mip3, mip2, ResourceState.NonPixelShaderResource );
		BloomPass( mip2, mip1, ResourceState.NonPixelShaderResource );
		BloomPass( mip1, mip0, ResourceState.PixelShaderResource );

		_commands.Attributes.Set( "BloomTexture", mip0.ColorTexture );
	}

	private void BloomPass( RenderTargetHandle input, RenderTargetHandle output, ResourceState after )
	{
		_commands.Attributes.Set( "InputTexture", input.ColorTexture );
		_commands.Attributes.Set( "BloomOut", output.ColorTexture );
		_commands.DispatchCompute( BloomShader, output.Size );
		_commands.ResourceBarrierTransition( output, after );
	}
}
