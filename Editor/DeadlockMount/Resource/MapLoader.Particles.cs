using System;
using Deadlock;

namespace Sandbox.Mounting.Deadlock;

internal sealed partial class MapLoader
{
	/// <summary>
	/// An info_particle_system: its effect_name played by a <see cref="DeadlockParticleEffect"/>, from the start if
	/// start_active, with its literal control point values. Its cpoint bindings are made by
	/// <see cref="BindParticleControlPoints"/> once every entity exists.
	/// </summary>
	private void AddParticleSystem( MapEntity entity, GameObject gameObject )
	{
		var effectName = entity.GetString( "effect_name" );
		if ( string.IsNullOrWhiteSpace( effectName ) )
			return;

		var particle = DeadlockParticleSystem.Load( effectName );
		if ( particle is null )
		{
			Log.Warning( $"{mapName}: info_particle_system '{entity.TargetName}' effect '{effectName}' is missing." );
			return;
		}

		var effect = gameObject.Components.Create<DeadlockParticleEffect>();
		effect.Particle = particle;
		effect.StartActive = entity.GetBool( "start_active", true );
		effect.ControlPointValues = ReadControlPointValues( entity );
	}

	/// <summary>
	/// data_cp takes a vector, tint_cp a colour (0 to 255), and up to four cpointN_value keys a position each.
	/// </summary>
	private static List<DeadlockControlPointValue> ReadControlPointValues( MapEntity entity )
	{
		var values = new List<DeadlockControlPointValue>();

		void Add( int controlPoint, float[] value )
		{
			if ( controlPoint is >= 0 and < 64 && value is { Length: 3 } )
				values.Add( new DeadlockControlPointValue { ControlPoint = controlPoint, Value = new Vector3( value[0], value[1], value[2] ) } );
		}

		Add( entity.GetInt( "data_cp", -1 ), entity.GetVector( "data_cp_value", 3, [0, 0, 0] ) );
		Add( entity.GetInt( "tint_cp", -1 ), entity.GetVector( "tint_cp_color", 3, [255, 255, 255] ) );

		for ( var index = 0; index < 64 && values.Count < 6; index++ )
		{
			if ( entity.GetString( $"cpoint{index}_value" ) is { Length: > 0 } )
				Add( index, entity.GetVector( $"cpoint{index}_value", 3 ) );
		}

		return values;
	}

	/// <summary>
	/// Binds each particle effect's control points to the entities its cpointN keys name. Names starting with '!' only
	/// exist at runtime, apart from !self.
	/// </summary>
	private void BindParticleControlPoints( List<(MapEntity Entity, GameObject GameObject)> created, Dictionary<string, GameObject> byName )
	{
		foreach ( var (entity, gameObject) in created )
		{
			if ( gameObject.Components.Get<DeadlockParticleEffect>() is not { } effect )
				continue;

			var points = new List<GameObject>();

			for ( var index = 0; index < 64; index++ )
			{
				var name = entity.GetString( $"cpoint{index}" );
				GameObject target = null;

				if ( string.IsNullOrEmpty( name ) )
				{
					// Unset: the system decides.
				}
				else if ( name.Equals( "!self", StringComparison.OrdinalIgnoreCase ) )
				{
					target = gameObject;
				}
				else if ( name[0] != '!' && !byName.TryGetValue( name, out target ) )
				{
					Log.Warning( $"{mapName}: info_particle_system '{entity.TargetName}' cpoint{index} names '{name}', which doesn't exist." );
				}

				points.Add( target );
			}

			var count = points.FindLastIndex( x => x is not null ) + 1;
			effect.ControlPoints = points.GetRange( 0, count );
		}
	}
}
