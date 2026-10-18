namespace Deadlock;

/// <summary>
/// Deadlock's NPR outline, as pbr.vfx's outline pass draws heroes and some props: the model a second time, pushed out
/// along its normals by each material's outline mask, only its back faces showing, lit with darkened colours or
/// multiplying what's behind by a solid tint. Goes on the GameObject with the model's renderer.
/// </summary>
[Title( "Deadlock NPR Outline" ), Category( "Deadlock" ), Icon( "border_outer" )]
public sealed class DeadlockNprOutline : DeadlockModelPass
{
	private static readonly Dictionary<string, Material> OutlineMaterials = new();
	private static Material _nothing;

	protected override string PassName => "NPR Outline";

	protected override Material CopyOf( Material material ) => OutlineOf( material );

	/// <summary>
	/// Whether any of the model's materials draws an outline.
	/// </summary>
	public static bool HasOutline( Model model ) => model.Materials.Any( IsOutlined );

	private static bool IsOutlined( Material material ) => material is not null && material.Shader?.ResourcePath == ShaderPath
		&& material.GetFeature( "F_USE_NPR_LIGHTING" ) != 0 && material.GetVector4( "g_flNprOutlineEnabled" ).x > 0.5f;

	/// <summary>
	/// An outlined NPR material's outline copy: the same material with the outline pass's combo, both faces drawn (the
	/// pixel shader keeps the back ones), solid if the material's outline is. Null for any other material.
	/// </summary>
	public static Material OutlineOf( Material material )
	{
		if ( !IsOutlined( material ) )
			return null;

		return CachedCopy( OutlineMaterials, material, "npr_outline", outline =>
		{
			outline.SetFeature( "F_NPR_OUTLINE", 1 );
			outline.SetFeature( "F_RENDER_BACKFACES", 1 );
			outline.SetFeature( "F_NPR_SOLID_OUTLINE", material.GetVector4( "g_flNprSolidOutline" ).x > 0.5f ? 1 : 0 );
		} );
	}

	// An outline material with the outline turned off discards everything.
	protected override Material Nothing()
	{
		if ( _nothing.IsValid() )
			return _nothing;

		_nothing = Material.Create( "deadlock/npr_outline_nothing", ShaderPath );
		_nothing.SetFeature( "F_USE_NPR_LIGHTING", 1 );
		_nothing.SetFeature( "F_NPR_OUTLINE", 1 );
		_nothing.SetFeature( "F_RENDER_BACKFACES", 1 );
		_nothing.Set( "g_flNprOutlineEnabled", 0f );
		return _nothing;
	}
}
