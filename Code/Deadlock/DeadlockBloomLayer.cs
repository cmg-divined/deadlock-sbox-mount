namespace Deadlock;

/// <summary>
/// pbr.vfx's bloom amount override (F_OVERRIDE_BLOOM_AMOUNT): lamps, candles and neon bloom by their colour times the
/// amount and their self illumination mask, with no threshold. The model is drawn again, only into s&amp;box's bloom
/// layer, which Deadlock Post Processing's bloom takes in. Goes on the GameObject with the model's renderer.
/// </summary>
[Title( "Deadlock Bloom Layer" ), Category( "Deadlock" ), Icon( "flare" )]
public sealed class DeadlockBloomLayer : DeadlockModelPass
{
	private static readonly Dictionary<string, Material> BloomMaterials = new();
	private static Material _nothing;

	protected override string PassName => "Bloom Layer";

	protected override Material CopyOf( Material material ) => BloomOf( material );

	protected override void Configure( ModelRenderer copy )
	{
		copy.RenderOptions.Game = false;
		copy.RenderOptions.Bloom = true;
	}

	/// <summary>
	/// Whether any of the model's materials blooms by an amount.
	/// </summary>
	public static bool HasBloom( Model model ) => model.Materials.Any( Blooms );

	private static bool Blooms( Material material ) => material is not null
		&& material.Shader?.ResourcePath is ShaderPath or "shaders/deadlock_parallax_layered.shader"
		&& material.GetVector4( "g_flBloomAmount" ).x > 0;

	/// <summary>
	/// A blooming material's bloom layer copy: the same material, writing what it blooms. Null for any other.
	/// </summary>
	public static Material BloomOf( Material material )
	{
		if ( !Blooms( material ) )
			return null;

		return CachedCopy( BloomMaterials, material, "bloom_layer", copy => copy.SetFeature( "F_BLOOM_LAYER", 1 ) );
	}

	// A bloom layer material with no amount discards everything.
	protected override Material Nothing()
	{
		if ( _nothing.IsValid() )
			return _nothing;

		_nothing = Material.Create( "deadlock/bloom_layer_nothing", ShaderPath );
		_nothing.SetFeature( "F_BLOOM_LAYER", 1 );
		_nothing.Set( "g_flBloomAmount", 0f );
		return _nothing;
	}
}
