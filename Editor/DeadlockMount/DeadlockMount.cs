using System;
using System.IO;
using System.Threading.Tasks;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Mounts Deadlock's compiled Source 2 content from its VPKs.
/// </summary>
public sealed partial class DeadlockMount : BaseGameMount
{
	private const long AppId = 1422450;

	/// <summary>
	/// Content folders under <c>game/</c>, highest priority first, as listed in Deadlock's gameinfo.
	/// </summary>
	private static readonly string[] SearchPaths = ["citadel", "core"];

	private readonly List<VpkArchive> _archives = [];
	private readonly Dictionary<string, VpkArchive> _files = new( StringComparer.OrdinalIgnoreCase );
	private string _gameDirectory;

	public override string Ident => "deadlock";
	public override string Title => "Deadlock";
	public override long? SteamAppId => AppId;

	protected override void Initialize( InitializeContext context )
	{
		IsInstalled = false;
		IsMounted = false;

		if ( !context.IsAppInstalled( AppId ) )
			return;

		var installDirectory = context.GetAppDirectory( AppId );
		if ( string.IsNullOrWhiteSpace( installDirectory ) )
			return;

		_gameDirectory = System.IO.Path.Combine( installDirectory, "game" );
		IsInstalled = System.IO.File.Exists( System.IO.Path.Combine( _gameDirectory, "citadel", "pak01_dir.vpk" ) );
	}

	protected override Task Mount( MountContext context )
	{
		CloseArchives();

		foreach ( var searchPath in SearchPaths )
		{
			var path = System.IO.Path.Combine( _gameDirectory, searchPath, "pak01_dir.vpk" );
			if ( !System.IO.File.Exists( path ) )
				continue;

			try
			{
				OpenArchive( path );
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or UnauthorizedAccessException )
			{
				Log.Warning( $"Couldn't open '{path}': {e.Message}" );
				context.AddError( $"Couldn't open '{path}': {e.Message}" );
			}
		}

		// Map VPKs come after the base content, which wins where a map bundles a file under the same path.
		var maps = new List<string>();
		var mapsDirectory = System.IO.Path.Combine( _gameDirectory, "citadel", "maps" );

		if ( System.IO.Directory.Exists( mapsDirectory ) )
		{
			foreach ( var path in System.IO.Directory.GetFiles( mapsDirectory, "*.vpk" ).Order( StringComparer.OrdinalIgnoreCase ) )
			{
				var mapName = System.IO.Path.GetFileNameWithoutExtension( path );

				try
				{
					var archive = OpenArchive( path );
					if ( archive.Contains( $"maps/{mapName}/world.vwrld_c" ) )
						maps.Add( mapName );
				}
				catch ( Exception e ) when ( e is IOException or InvalidDataException or UnauthorizedAccessException )
				{
					Log.Warning( $"Couldn't open '{path}': {e.Message}" );
					context.AddError( $"Couldn't open '{path}': {e.Message}" );
				}
			}
		}

		foreach ( var mapName in maps )
			context.Add( ResourceType.Scene, $"maps/{mapName}", new MapLoader( mapName ) );

		int models = 0, materials = 0, textures = 0, particles = 0, graphs = 0;

		foreach ( var file in _files.Keys )
		{
			// Resources are registered under their source name, without the compiled "_c" suffix.
			var extension = System.IO.Path.GetExtension( file );

			if ( extension.Equals( ".vmdl_c", StringComparison.OrdinalIgnoreCase ) )
			{
				context.Add( ResourceType.Model, file[..^2], new ModelLoader( file ) );
				models++;
			}
			else if ( extension.Equals( ".vmat_c", StringComparison.OrdinalIgnoreCase ) )
			{
				context.Add( ResourceType.Material, file[..^2], new MaterialLoader( file ) );
				materials++;
			}
			else if ( extension.Equals( ".vtex_c", StringComparison.OrdinalIgnoreCase ) )
			{
				context.Add( ResourceType.Texture, file[..^2], new TextureLoader( file ) );
				textures++;
			}
			else if ( extension.Equals( ".vpcf_c", StringComparison.OrdinalIgnoreCase ) )
			{
				// Particle systems are game resources the Deadlock Particle Effect component plays.
				context.Add( ResourceType.GameResource, file[..^2], new ParticleLoader( file ) );
				particles++;
			}
			else if ( extension.Equals( ".vnmgraph_c", StringComparison.OrdinalIgnoreCase ) )
			{
				// Animation graphs, with the clips they play, for the Deadlock Animator.
				context.Add( ResourceType.GameResource, file[..^2], new AnimationGraphLoader( file ) );
				graphs++;
			}
			else if ( extension.Equals( ".vdata_c", StringComparison.OrdinalIgnoreCase ) )
			{
				// VData (heroes, abilities...), for game code that plays by the game's numbers.
				context.Add( ResourceType.GameResource, file[..^2], new VDataLoader( file ) );
			}
		}

		Log.Info( $"Mounted {_files.Count} files from {_archives.Count} VPKs: {maps.Count} maps, {models} models, {materials} materials, {textures} textures, {particles} particle systems, {graphs} animation graphs." );

		IsMounted = _files.Count > 0;
		Current = this;
		EnsureGameTypes();
		return Task.CompletedTask;
	}

	protected override void Shutdown()
	{
		if ( Current == this )
			Current = null;

		CloseArchives();
	}

	private VpkArchive OpenArchive( string path )
	{
		var archive = new VpkArchive( path );
		_archives.Add( archive );

		// Earlier search paths win, like the game's own filesystem.
		foreach ( var file in archive.Files )
			_files.TryAdd( file, archive );

		return archive;
	}

	private void CloseArchives()
	{
		lock ( _files )
		{
			foreach ( var archive in _archives )
				archive.Dispose();

			_archives.Clear();
			_files.Clear();
			ClearSurfaces();
			ClearVData();
			ClearReduced();
			ClearParticles();
			_heroes = null;
		}
	}

	internal bool FileExists( string path ) => _files.ContainsKey( path );

	/// <summary>
	/// Reads a file from the mounted VPKs, by its path inside the game's content, e.g. <c>models/foo.vmdl_c</c>.
	/// </summary>
	internal byte[] ReadFile( string path, int maxLength = int.MaxValue )
	{
		VpkArchive archive;
		path = path.Replace( '\\', '/' );

		lock ( _files )
		{
			if ( !_files.TryGetValue( path, out archive ) )
				throw new FileNotFoundException( $"'{path}' is not in {Title}'s content.", path );
		}

		return archive.ReadFile( path, maxLength );
	}

	/// <summary>
	/// Loads a mounted material by the path a model or map refers to it by, e.g. <c>materials/foo.vmat</c>.
	/// Falls back to the default material when it isn't in the game's content.
	/// </summary>
	internal Material LoadMaterial( string path )
	{
		if ( !string.IsNullOrWhiteSpace( path ) )
		{
			path = path.Replace( '\\', '/' );

			if ( FileExists( path + "_c" ) && Material.Load( $"mount://{Ident}/{path}" ) is { IsValid: true } material )
				return material;
		}

		return Material.Load( "materials/default.vmat" );
	}

	/// <summary>
	/// Loads a mounted texture by the path a material refers to it by, e.g. <c>materials/foo_png_1234.vtex</c>.
	/// Returns null when it isn't in the game's content.
	/// </summary>
	internal Texture LoadTexture( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		path = path.Replace( '\\', '/' );

		if ( !FileExists( path + "_c" ) )
			return null;

		var texture = Texture.Load( $"mount://{Ident}/{path}", false );
		return texture is { IsValid: true, IsError: false } ? texture : null;
	}

	/// <summary>
	/// A texture's average colour, from its header alone.
	/// </summary>
	internal float[] ReadTextureReflectivity( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) || !FileExists( path + "_c" ) )
			return null;

		return ReadTextureHeader( path ).Reflectivity;
	}

	/// <summary>
	/// A texture's header, without necessarily reading its pixel data.
	/// </summary>
	internal CompiledTexture ReadTextureHeader( string path )
	{
		// The header and block table come before the pixel data, so a small prefix is usually enough.
		try
		{
			return new CompiledTexture( ReadFile( path + "_c", 64 * 1024 ) );
		}
		catch ( Exception e ) when ( e is InvalidDataException or ArgumentOutOfRangeException or IndexOutOfRangeException )
		{
			return new CompiledTexture( ReadFile( path + "_c" ) );
		}
	}
}
