using System;
using System.IO;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Loads a Deadlock map (maps/name.vpk) as a scene, as the game builds it: the world nodes' placed models and
/// aggregate geometry, the world collision split by collision attribute, every entity of the entity lumps, and the
/// baked and realtime lighting the Deadlock shaders light it with.
/// </summary>
internal sealed partial class MapLoader( string mapName ) : SceneLoader<DeadlockMount>
{
	/// <summary>
	/// Maps whose large textures load mips down, to take less video memory (see
	/// <see cref="DeadlockMount.LoadTexture(string, int)"/>). dl_midtown's material textures are about 4 GB at full
	/// detail and 1.3 GB a mip down. Its lighting (lightmaps, cubemaps, probes, fog) keeps its full detail.
	/// </summary>
	private static readonly Dictionary<string, int> TextureLevels = new( StringComparer.OrdinalIgnoreCase )
	{
		["dl_midtown"] = 1,
	};

	private int TextureLevel => TextureLevels.GetValueOrDefault( mapName );

	/// <summary>
	/// Models built for this map (aggregates, collision), kept alive for as long as the scene is.
	/// </summary>
	private readonly List<Model> _models = [];

	private readonly HashSet<string> _missingModels = new( StringComparer.OrdinalIgnoreCase );

	protected override void BuildScene()
	{
		DeadlockMount.EnsureGameTypes();

		var worldPath = $"maps/{mapName}/world.vwrld_c";
		if ( !Host.FileExists( worldPath ) )
		{
			Log.Warning( $"{mapName}: the map has no world." );
			return;
		}

		var world = new CompiledWorld( Host.ReadFile( worldPath ) );

		BuildWorld( world );
		BuildCollision();
		var entities = BuildEntities( world );
		BuildLighting( world, entities.Select( x => x.Entity ).ToList() );
		BuildFog( entities );
	}

	/// <summary>
	/// World collision: maps/name/world_physics.vmdl, one static collider per collision attribute, tagged with
	/// what the attribute interacts as (solid, playerclip, npcclip, sky...).
	/// </summary>
	private void BuildCollision()
	{
		var path = $"maps/{mapName}/world_physics.vmdl";
		if ( !Host.FileExists( path + "_c" ) )
			return;

		var root = new GameObject( true, "Collision" );
		var groups = new ModelImporter( Host, new CompiledModel( Host.ReadFile( path + "_c" ) ), $"maps/{mapName}/world_physics" ).ImportCollisionGroups();

		foreach ( var (tags, model) in groups )
		{
			_models.Add( model );

			var collision = new GameObject( true, tags.Length > 0 ? string.Join( ", ", tags ) : "default" );
			collision.SetParent( root, false );

			foreach ( var tag in tags )
				collision.Tags.Add( tag.ToLowerInvariant() );

			var collider = collision.Components.Create<ModelCollider>();
			collider.Model = model;
			collider.Static = true;

			// Cosmic veils only block sight: heroes, bullets and abilities pass through them.
			if ( tags.Contains( "Citadel_Obscured", StringComparer.OrdinalIgnoreCase ) )
				collider.IsTrigger = true;
		}
	}

	/// <summary>
	/// A mounted model by the path a map refers to it by, at the map's <see cref="TextureLevel"/>, or null if it
	/// isn't in the game's content.
	/// </summary>
	private Model LoadModel( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		return Host.LoadModel( CompiledWorld.NormalizePath( path ), TextureLevel );
	}

	/// <summary>
	/// A 3x4 matrix as a transform. Its columns are the scaled axes; a mirroring matrix gets a negative Y scale.
	/// </summary>
	private static Transform ToTransform( Matrix3x4 matrix )
	{
		var m = matrix.Values;
		var x = new Vector3( m[0], m[4], m[8] );
		var y = new Vector3( m[1], m[5], m[9] );
		var z = new Vector3( m[2], m[6], m[10] );
		var position = new Vector3( m[3], m[7], m[11] );

		if ( x.Length <= 0 || z.Length <= 0 )
			return new Transform( position );

		var scale = new Vector3( x.Length, y.Length, z.Length );
		if ( Vector3.Dot( Vector3.Cross( x, y ), z ) < 0 )
			scale.y = -scale.y;

		return new Transform( position, Rotation.LookAt( x.Normal, z.Normal ), scale );
	}
}
