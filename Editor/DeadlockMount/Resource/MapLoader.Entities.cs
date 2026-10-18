using Deadlock;
using System;

namespace Sandbox.Mounting.Deadlock;

internal sealed partial class MapLoader
{
	/// <summary>
	/// Brush entities that block movement. Other brush entities are touch volumes (triggers) or markup.
	/// </summary>
	private static readonly HashSet<string> SolidBrushClasses = new( StringComparer.OrdinalIgnoreCase )
	{
		"func_brush", "func_tracktrain", "func_door", "func_door_rotating", "func_movelinear", "func_rotating",
		"func_physbox", "func_breakable", "func_wall", "func_wall_toggle", "func_button", "func_rot_button",
	};

	/// <summary>
	/// Entities where a player can spawn.
	/// </summary>
	private static readonly HashSet<string> SpawnClasses = new( StringComparer.OrdinalIgnoreCase )
	{
		"info_player_start", "info_team_spawn", "info_player_teamspawn", "info_player_deathmatch",
	};

	/// <summary>
	/// Every entity of the map's entity lumps, as a GameObject carrying its key values, with the s&amp;box
	/// components for what it shows or does: models, collision, realtime lights, the sky, spawn points.
	/// </summary>
	/// <returns>The entities and the GameObjects made for them, in lump order.</returns>
	private List<(MapEntity Entity, GameObject GameObject)> BuildEntities( CompiledWorld world )
	{
		var root = new GameObject( true, "Entities" );
		var created = new List<(MapEntity Entity, GameObject GameObject)>();
		var visited = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		void Load( string lumpPath )
		{
			if ( !visited.Add( lumpPath ) )
				return;

			if ( !Host.FileExists( lumpPath + "_c" ) )
			{
				Log.Warning( $"{mapName}: entity lump '{lumpPath}' is missing." );
				return;
			}

			var lump = new EntityLump( Host.ReadFile( lumpPath + "_c" ) );
			if ( lump.LegacyEntities > 0 )
				Log.Warning( $"{mapName}: {lump.LegacyEntities} entities in '{lump.Name}' use the old binary key value format, which isn't read." );

			foreach ( var entity in lump.Entities )
				created.Add( (entity, CreateEntity( entity, root )) );

			foreach ( var child in lump.ChildLumps )
				Load( child );
		}

		foreach ( var lump in world.EntityLumps )
			Load( lump );

		// Parents are found by name once every entity exists; compiled origins are already in world space.
		var byName = created.Where( x => x.Entity.TargetName.Length > 0 ).GroupBy( x => x.Entity.TargetName, StringComparer.OrdinalIgnoreCase )
			.ToDictionary( x => x.Key, x => x.First().GameObject, StringComparer.OrdinalIgnoreCase );

		foreach ( var (entity, gameObject) in created )
		{
			if ( entity.GetString( "parentname" ) is { Length: > 0 } parentName && byName.TryGetValue( parentName, out var parent ) && parent != gameObject )
				gameObject.SetParent( parent, true );
		}

		BindParticleControlPoints( created, byName );

		return created;
	}

	private GameObject CreateEntity( MapEntity entity, GameObject root )
	{
		var gameObject = new GameObject( true, entity.TargetName.Length > 0 ? entity.TargetName : entity.Classname );
		gameObject.SetParent( root, false );
		gameObject.WorldTransform = ReadTransform( entity );

		var data = gameObject.Components.Create<DeadlockEntity>();
		data.Classname = entity.Classname;
		data.TargetName = entity.TargetName;

		foreach ( var (key, value) in entity.Values )
			data.KeyValues[key] = MapEntity.FormatValue( value );

		data.Outputs = entity.Connections.Select( x => new DeadlockEntityOutput
		{
			Output = x.Output,
			Target = x.Target,
			Input = x.Input,
			Parameter = x.Parameter,
			Delay = x.Delay,
			TimesToFire = x.TimesToFire,
		} ).ToList();

		switch ( entity.Classname.ToLowerInvariant() )
		{
			case "env_sky":
				AddSky( entity, gameObject );
				break;

			case "light_environment":
				AddSun( entity, gameObject );
				break;

			case "post_processing_volume":
				AddPostProcessing( entity, gameObject );
				break;

			case "info_particle_system":
				AddParticleSystem( entity, gameObject );
				break;

			case "light_omni2":
			case "light_omni":
			case "light_barn":
			case "light_spot":
			case "light_rect":
				AddLight( entity, gameObject );
				break;

			case var spawn when SpawnClasses.Contains( spawn ):
				AddSpawnPoint( entity, gameObject );
				break;
		}

		// Lights can name a model for their shape, which the game doesn't draw.
		if ( !entity.Classname.StartsWith( "light_", StringComparison.OrdinalIgnoreCase ) )
			AddEntityModel( entity, gameObject );

		if ( entity.GetBool( "StartDisabled" ) )
			gameObject.Enabled = false;

		return gameObject;
	}

	private static Transform ReadTransform( MapEntity entity )
	{
		var origin = entity.GetVector( "origin", 3, [0, 0, 0] );
		var angles = entity.GetVector( "angles", 3, [0, 0, 0] );
		var scales = entity.GetVector( "scales", 3, [1, 1, 1] );

		// Older entities leave scales unset, which reads as zero.
		var scale = new Vector3(
			scales[0] != 0 ? scales[0] : 1,
			scales[1] != 0 ? scales[1] : 1,
			scales[2] != 0 ? scales[2] : 1 );

		return new Transform( new Vector3( origin[0], origin[1], origin[2] ), Rotation.From( angles[0], angles[1], angles[2] ), scale );
	}

	/// <summary>
	/// The entity's model: its "model" key, or for VData subclassed entities (breakable props) the subclass's.
	/// </summary>
	private (string Path, float Scale) GetEntityModel( MapEntity entity )
	{
		if ( entity.GetString( "model" ) is { Length: > 0 } model )
			return (model, 1);

		if ( Host.GetVData( entity.GetString( "subclass_name" ) ) is { } subclass && subclass.GetString( "m_hModel" ) is { Length: > 0 } subclassModel )
			return (subclassModel, subclass.GetFloat( "m_flModelScale", 1 ));

		return (null, 1);
	}

	/// <summary>
	/// An entity's model, drawn and collided with the way its class does: props render and are solid,
	/// physics props simulate, solid brushes block, other brush models are touch volumes.
	/// </summary>
	private void AddEntityModel( MapEntity entity, GameObject gameObject )
	{
		var (path, scale) = GetEntityModel( entity );
		if ( path is null || IsPreMatchBlocker( entity ) )
			return;

		if ( LoadModel( path ) is not { } model )
		{
			Log.Warning( $"{mapName}: {entity.Classname} '{entity.TargetName}' model '{path}' is missing." );
			return;
		}

		if ( scale != 1 )
			gameObject.WorldScale *= scale;

		var classname = entity.Classname;
		var tint = ReadRenderColor( entity );
		var skin = entity.GetString( "skin" ) is { Length: > 0 } s ? s : null;

		if ( classname.StartsWith( "prop_physics", StringComparison.OrdinalIgnoreCase ) )
		{
			var prop = gameObject.Components.Create<Prop>();
			prop.Model = model;
			prop.IsStatic = false;
			prop.StartAsleep = (entity.GetInt( "spawnflags" ) & 1) != 0;
			prop.Tint = tint;
			if ( skin is not null )
				prop.MaterialGroup = skin;

			return;
		}

		var renders = model.MeshCount > 0 && !string.Equals( entity.GetString( "rendermode" ), "kRenderNone", StringComparison.OrdinalIgnoreCase );

		if ( renders )
		{
			ModelRenderer renderer = model.BoneCount > 0
				? gameObject.Components.Create<SkinnedModelRenderer>()
				: gameObject.Components.Create<ModelRenderer>();

			renderer.Model = model;
			renderer.Tint = tint;

			if ( skin is not null )
				renderer.MaterialGroup = skin;

			if ( entity.GetBool( "disableshadows" ) )
				renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		}

		if ( model.Physics is not { Parts.Count: > 0 } )
			return;

		bool solid;
		bool trigger;

		if ( SolidBrushClasses.Contains( classname ) )
		{
			// func_brush Solidity: 0 toggles (starts solid), 1 is never solid, 2 is always solid.
			solid = entity.GetInt( "Solidity" ) != 1;
			trigger = false;
		}
		else if ( model.MeshCount == 0 )
		{
			// Invisible brush models of other classes are volumes things touch: triggers, nav markup, fog volumes.
			solid = true;
			trigger = true;
		}
		else
		{
			// Props: solid 0 is not solid, 6 (the default) collides with their physics model.
			solid = entity.GetInt( "solid", 6 ) != 0;
			trigger = false;
		}

		if ( !solid )
			return;

		var collider = gameObject.Components.Create<ModelCollider>();
		collider.Model = model;
		collider.Static = true;
		collider.IsTrigger = trigger;
	}

	/// <summary>
	/// The walls that keep heroes in their base until the match starts, which the game removes then. Maps load as
	/// they are during a match.
	/// </summary>
	private static bool IsPreMatchBlocker( MapEntity entity ) =>
		entity.Classname.Equals( "func_brush", StringComparison.OrdinalIgnoreCase )
		&& entity.TargetName.EndsWith( "spawn_block_brush", StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// rendercolor (sRGB 0-255) and renderamt (0-255 alpha).
	/// </summary>
	private static Color ReadRenderColor( MapEntity entity )
	{
		var color = entity.GetVector( "rendercolor", 3, [255, 255, 255] );
		var alpha = entity.GetFloat( "renderamt", 255 );
		return new Color( color[0] / 255f, color[1] / 255f, color[2] / 255f, alpha / 255f );
	}

	/// <summary>
	/// A colour key (0-255). s&amp;box takes light and sky colours as authored, gamma space, like its own Source 2
	/// map loading does.
	/// </summary>
	private static Color ReadColor( MapEntity entity, string key, float[] fallback )
	{
		var color = entity.GetVector( key, 3, fallback );
		return new Color( color[0] / 255f, color[1] / 255f, color[2] / 255f );
	}

	private void AddSky( MapEntity entity, GameObject gameObject )
	{
		var sky = gameObject.Components.Create<SkyBox2D>();

		if ( entity.GetString( "skyname" ) is { Length: > 0 } material )
			sky.SkyMaterial = Host.LoadMaterial( material );

		var tint = entity.GetVector( "tint_color", 3, [255, 255, 255] );
		var brightness = entity.GetFloat( "brightnessscale", 1 );
		sky.Tint = new Color( tint[0] / 255f * brightness, tint[1] / 255f * brightness, tint[2] / 255f * brightness );
		sky.SkyIndirectLighting = true;
	}

	/// <summary>
	/// light_environment: the sun. Its brightness is a plain multiplier, unlike the light2 entities.
	/// </summary>
	private static void AddSun( MapEntity entity, GameObject gameObject )
	{
		if ( !IsRealtime( entity ) )
			return;

		var light = gameObject.Components.Create<DirectionalLight>();
		var brightness = entity.GetFloat( "brightness", 1 ) * entity.GetFloat( "brightnessscale", 1 );

		light.LightColor = ReadColor( entity, "color", [255, 255, 255] ) * brightness;
		light.SkyColor = ReadColor( entity, "skycolor", [0, 0, 0] ) * entity.GetFloat( "skyintensity", 1 );
		light.Shadows = entity.GetInt( "castshadows", 1 ) != 0;

		if ( entity.GetInt( "numcascades" ) is > 0 and var cascades )
			light.ShadowCascadeCount = cascades;

		ApplyLightSettings( entity, light );
	}

	/// <summary>
	/// light_omni2, light_barn and light_rect (and the older omni and spot lights) that light the world in realtime.
	/// Static lights exist in the game only through the baked lightmaps, so they get no realtime light here either.
	/// </summary>
	private static void AddLight( MapEntity entity, GameObject gameObject )
	{
		if ( !IsRealtime( entity ) )
			return;

		var classname = entity.Classname.ToLowerInvariant();
		var light2 = classname is "light_omni2" or "light_barn" or "light_rect";

		// light2 entities store brightness as an exposure value, 2^EV being the legacy brightness s&box's lights use
		// with their default inverse square attenuation.
		var brightness = (light2 ? MathF.Pow( 2, entity.GetFloat( "brightness" ) ) : entity.GetFloat( "brightness", 1 )) * entity.GetFloat( "brightnessscale", 1 );

		var color = light2 && entity.GetInt( "colormode" ) != 0
			? ColorTemperature( entity.GetFloat( "colortemperature", 6600 ) )
			: ReadColor( entity, "color", [255, 255, 255] );

		var outerAngle = classname switch
		{
			"light_omni2" => entity.GetFloat( "outer_angle", 180 ),
			"light_rect" => 90,
			_ => entity.GetFloat( "outerconeangle", entity.GetFloat( "outer_angle", 45 ) ),
		};

		var innerAngle = classname switch
		{
			"light_omni2" => entity.GetFloat( "inner_angle" ),
			"light_rect" => 0,
			_ => entity.GetFloat( "innerconeangle", entity.GetFloat( "inner_angle" ) ),
		};

		Light light;

		// An omni2 with a cone narrower than a full sphere lights only in front of it, like a spot light.
		if ( classname is "light_omni2" or "light_omni" && outerAngle >= 180 )
		{
			var point = gameObject.Components.Create<PointLight>();
			point.Radius = entity.GetFloat( "range", 512 );
			light = point;
		}
		else
		{
			var spot = gameObject.Components.Create<SpotLight>();
			spot.Radius = entity.GetFloat( "range", 512 );
			spot.ConeOuter = Math.Clamp( outerAngle, 0, 90 );
			spot.ConeInner = Math.Clamp( innerAngle, 0, spot.ConeOuter );
			light = spot;
		}

		light.LightColor = color * brightness;

		// A stationary light's shadows are baked: Deadlock's shaders take them from the lightmaps' shadow channels, the
		// light probes and the precomputed shadow atlas, as the game does. A realtime shadow map would re-render the
		// world for it every frame (dl_midtown has 27, reaching up to 8000 units) for nothing they read.
		light.Shadows = entity.GetInt( "castshadows", 1 ) != 0 && !HasBakedShadows( entity );

		ApplyLightSettings( entity, light );
	}

	/// <summary>
	/// Whether the game lights with this entity at runtime: dynamic and stationary lights (stationary ones bake
	/// only their shadows), not static ones, which only exist in the lightmaps.
	/// </summary>
	private static bool HasBakedShadows( MapEntity entity ) => entity.GetInt( "bakedshadowindex", entity.GetInt( "bakelightindex", -1 ) ) >= 0;

	private static bool IsRealtime( MapEntity entity )
	{
		var light2 = entity.Classname is "light_omni2" or "light_barn" or "light_rect";
		var directLight = entity.GetInt( "directlight", light2 ? 2 : 1 );
		var bakedShadowIndex = entity.GetInt( "bakedshadowindex", entity.GetInt( "bakelightindex", -1 ) );

		return directLight switch
		{
			2 or 3 => true,
			1 => bakedShadowIndex >= 0,
			_ => false,
		};
	}

	private static void ApplyLightSettings( MapEntity entity, Light light )
	{
		var contribution = (Light.LightContribution)0;
		if ( entity.GetInt( "renderdiffuse", 1 ) != 0 )
			contribution |= Light.LightContribution.Diffuse;
		if ( entity.GetInt( "renderspecular", 1 ) == 1 )
			contribution |= Light.LightContribution.Specular;
		if ( entity.GetInt( "rendertransmissive", 1 ) != 0 )
			contribution |= Light.LightContribution.Transmissive;

		light.Contribution = contribution;

		if ( entity.GetInt( "fog" ) != 0 )
		{
			light.FogMode = entity.GetInt( "fogshadows", -1 ) == 0 ? Light.FogInfluence.WithoutShadows : Light.FogInfluence.Enabled;
			light.FogStrength = entity.GetFloat( "fogstrength", 1 );
		}
		else
		{
			light.FogMode = Light.FogInfluence.Disabled;
		}

		light.Enabled = entity.GetBool( "enabled", true );
	}

	private static void AddSpawnPoint( MapEntity entity, GameObject gameObject )
	{
		var spawn = gameObject.Components.Create<SpawnPoint>();

		// Deadlock's teams: 2 is the Amber Hand, 3 the Sapphire Flame.
		spawn.Color = entity.GetInt( "teamnumber" ) switch
		{
			2 => new Color( 1f, 0.6f, 0.1f ),
			3 => new Color( 0.2f, 0.45f, 1f ),
			_ => spawn.Color,
		};
	}

	/// <summary>
	/// A blackbody colour, ported from ValveResourceFormat's approximation.
	/// </summary>
	private static Color ColorTemperature( float kelvin )
	{
		var t = kelvin / 100f;
		var red = t > 66f ? 329.69873f * MathF.Pow( t - 60f, -0.13320476f ) : 255f;
		var green = t > 66f ? 288.12216f * MathF.Pow( t - 60f, -0.075514846f ) : 99.4708f * MathF.Log( t ) - 161.11957f;
		var blue = t < 66f ? 138.51773f * MathF.Log( t - 10f ) - 305.0448f : 255f;

		static float Channel( float value ) => float.IsNaN( value ) ? 0 : Math.Clamp( value, 0, 255 ) / 255f;

		return new Color( Channel( red ), Channel( green ), Channel( blue ) );
	}
}
