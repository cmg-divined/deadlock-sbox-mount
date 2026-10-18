using System;
using System.IO;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Content with its large textures some mips down, for maps whose full detail takes more video memory than is
/// comfortable. These are copies built beside the mounted resources, so everything else keeps its full detail.
/// </summary>
public sealed partial class DeadlockMount
{
	/// <summary>
	/// Textures don't go smaller than this: only larger ones lose mips.
	/// </summary>
	private const int ReducedTextureSize = 1024;

	private readonly Dictionary<string, Texture> _reducedTextures = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, Material> _reducedMaterials = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, Model> _reducedModels = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Loads a mounted texture without up to <paramref name="level"/> of its largest mips, as long as it stays at
	/// least <see cref="ReducedTextureSize"/>. Textures that don't shrink (small ones, cubemaps, arrays, volumes) are
	/// the mounted texture itself.
	/// </summary>
	internal Texture LoadTexture( string path, int level )
	{
		if ( level <= 0 || string.IsNullOrWhiteSpace( path ) )
			return LoadTexture( path );

		path = path.Replace( '\\', '/' );
		if ( !FileExists( path + "_c" ) )
			return null;

		var key = ReducedName( path, level );
		if ( _reducedTextures.TryGetValue( key, out var cached ) )
			return cached;

		var skip = MipsToSkip( ReadTextureHeader( path ), level );
		Texture texture;

		try
		{
			texture = skip == 0
				? LoadTexture( path )
				: TextureLoader.Create( new CompiledTexture( ReadFile( path + "_c" ) ), ReducedName( path, skip ), skip );
		}
		catch ( Exception e ) when ( e is InvalidDataException or NotSupportedException )
		{
			Log.Warning( $"Couldn't load '{path}' with fewer mips: {e.Message}" );
			texture = LoadTexture( path );
		}

		_reducedTextures[key] = texture;
		return texture;
	}

	/// <summary>
	/// Loads a mounted material with its large textures <paramref name="level"/> mips down, see
	/// <see cref="LoadTexture(string, int)"/>.
	/// </summary>
	internal Material LoadMaterial( string path, int level )
	{
		if ( level <= 0 || string.IsNullOrWhiteSpace( path ) )
			return LoadMaterial( path );

		path = path.Replace( '\\', '/' );
		if ( !FileExists( path + "_c" ) )
			return LoadMaterial( path );

		var key = ReducedName( path, level );
		if ( _reducedMaterials.TryGetValue( key, out var material ) )
			return material;

		try
		{
			material = new MaterialImporter( this, new CompiledMaterial( ReadFile( path + "_c" ) ), $"{Ident}/{key}", level ).Import();
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't load '{path}' with fewer mips: {e.Message}" );
			material = LoadMaterial( path );
		}

		_reducedMaterials[key] = material;
		return material;
	}

	/// <summary>
	/// Loads a mounted model by its path, e.g. <c>models/foo.vmdl</c>, with its materials' large textures
	/// <paramref name="level"/> mips down (see <see cref="LoadTexture(string, int)"/>). Returns null when it isn't in
	/// the game's content or doesn't load.
	/// </summary>
	internal Model LoadModel( string path, int level )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		path = path.Replace( '\\', '/' );
		if ( !FileExists( path + "_c" ) )
			return null;

		if ( level <= 0 )
			return Model.Load( $"mount://{Ident}/{path}" ) is { IsError: false } mounted ? mounted : null;

		var key = ReducedName( path, level );
		if ( _reducedModels.TryGetValue( key, out var model ) )
			return model;

		try
		{
			model = new ModelImporter( this, new CompiledModel( ReadFile( path + "_c" ) ), key, level ).Import();
		}
		catch ( Exception e )
		{
			// The mounted model fails the same way, and the resource system turns that into an error model.
			Log.Warning( $"Couldn't load '{path}': {e.Message}" );
			model = null;
		}

		model = model is { IsError: false } ? model : null;
		_reducedModels[key] = model;
		return model;
	}

	/// <summary>
	/// How many of its largest mips a texture can do without, up to <paramref name="level"/>.
	/// </summary>
	private static int MipsToSkip( CompiledTexture texture, int level )
	{
		if ( texture.IsRawImage || texture.IsCube || texture.IsVolume || texture.IsArray || texture.Depth > 1 )
			return 0;

		var size = Math.Max( texture.Width, texture.Height );
		var skip = 0;

		while ( skip < level && skip + 1 < texture.MipCount && size >> (skip + 1) >= ReducedTextureSize )
			skip++;

		return skip;
	}

	/// <summary>
	/// The name a reduced copy goes by: <c>materials/foo.vmat</c> becomes <c>materials/foo_mip1.vmat</c>.
	/// </summary>
	private static string ReducedName( string path, int level )
	{
		var extension = System.IO.Path.GetExtension( path );
		return $"{path[..^extension.Length]}_mip{level}{extension}";
	}

	private void ClearReduced()
	{
		_reducedTextures.Clear();
		_reducedMaterials.Clear();
		_reducedModels.Clear();
	}
}
