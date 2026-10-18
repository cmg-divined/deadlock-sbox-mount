using System;
using System.Threading.Tasks;

namespace Deadlock;

/// <summary>
/// Puts a mounted Deadlock map into this scene, the way <see cref="MapInstance"/> does for Hammer maps. The map's
/// objects become unsaved children of this GameObject: they show in the editor without being written into the
/// scene file, and load again when the game starts. Put your player, camera and game logic in the same scene.
/// </summary>
/// <remarks>
/// Mounted map scenes are read only, and pressing play on one plays the map as mounted, without anything added to
/// it in the editor. This component is how a scene of your own gets the map.
/// </remarks>
[Title( "Deadlock Map" ), Category( "Deadlock" ), Icon( "map" )]
public class DeadlockMap : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// The map's name, as in game/citadel/maps: dl_midtown, dl_hideout, new_player_basics...
	/// </summary>
	[Property]
	public string MapName { get; set; } = "dl_midtown";

	/// <summary>
	/// The hero the player spawns as when the game starts, at one of the map's spawn points, unless the scene has a
	/// <see cref="DeadlockHero"/> of its own. None spawns nobody.
	/// </summary>
	[Property, Editor( "deadlock_hero" )]
	public string SpawnHero { get; set; } = "hero_drifter";

	/// <summary>
	/// The mounted scene the map loads from.
	/// </summary>
	public string ScenePath => $"mount://deadlock/maps/{MapName}.scene";

	private string _loadedMap;
	private string _missingMap;

	protected override Task OnLoad( LoadingContext context )
	{
		// In game, load while the scene loads, so the map is there before anything spawns into it.
		if ( Active )
			LoadMap();

		return Task.CompletedTask;
	}

	protected override void OnStart()
	{
		if ( !Game.IsPlaying || string.IsNullOrWhiteSpace( SpawnHero ) || Scene.GetAllComponents<DeadlockHero>().Any() )
			return;

		var player = new GameObject( true, "Player" );
		player.WorldPosition = WorldPosition;
		player.Components.Create<DeadlockHero>().Hero = SpawnHero;
	}

	protected override void OnUpdate()
	{
		// Loads the map once the component is enabled, and again when its name changes in the inspector to another
		// map. Not in OnEnabled: adding the component in the editor enables it inside its own creation, and building
		// the map's objects there left their renderers half set up and crashed s&box.
		if ( _loadedMap != MapName && _missingMap != MapName )
			LoadMap();
	}

	protected override void OnDisabled() => UnloadMap();

	protected override void OnDestroy() => UnloadMap();

	public void LoadMap()
	{
		if ( string.IsNullOrWhiteSpace( MapName ) )
		{
			UnloadMap();
			_loadedMap = MapName;
			return;
		}

		// A name that isn't a map (one being typed in the inspector, a letter at a time) keeps the map that's loaded.
		var sceneFile = SceneFile.Load( ScenePath );
		if ( sceneFile?.GameObjects is null )
		{
			_missingMap = MapName;
			Log.Warning( $"{GameObject.Name}: couldn't load the Deadlock map '{MapName}' ({ScenePath}). Is the Deadlock mount enabled?" );
			return;
		}

		UnloadMap();
		_loadedMap = MapName;

		// The map's colliders would join that body and the whole map would move with it.
		if ( Components.GetInAncestorsOrSelf<Rigidbody>() is { } rigidbody )
			Log.Warning( $"{GameObject.Name}: Deadlock Map is under '{rigidbody.GameObject.Name}', which has a Rigidbody, so the map will move with it. Put it on an empty GameObject." );

		foreach ( var json in sceneFile.GameObjects )
		{
			var gameObject = new GameObject( false );
			gameObject.Flags |= GameObjectFlags.NotSaved;
			gameObject.SetParent( GameObject, false );
			gameObject.Deserialize( json );
		}
	}

	public void UnloadMap()
	{
		_loadedMap = null;

		if ( !GameObject.IsValid() )
			return;

		// Immediately, so a reload doesn't meet the old objects' ids.
		foreach ( var child in GameObject.Children.Where( x => x.Flags.Contains( GameObjectFlags.NotSaved ) ).ToArray() )
			child.DestroyImmediate();
	}
}
