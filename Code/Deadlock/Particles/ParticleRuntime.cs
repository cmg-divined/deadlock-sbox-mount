using System;

namespace Deadlock.Particles;

/// <summary>
/// Loads the particle systems and snapshots a playing system refers to, from the Deadlock mount, and remembers which
/// resource each loaded system came from so its renderers find their sprite sheets.
/// </summary>
internal sealed class ParticleResourceFiles : IFileLoader
{
	private readonly Dictionary<string, DeadlockParticleSystem> resources = new( StringComparer.OrdinalIgnoreCase );

	public ParticleSystem LoadParticleSystem( string path )
	{
		var resource = DeadlockParticleSystem.Load( path );
		if ( resource?.Definition is not { } definition )
			return null;

		resources[definition.Name] = resource;
		return definition;
	}

	// Snapshots aren't mounted yet; the functions reading them do without.
	public ParticleSnapshot LoadSnapshot( string path ) => null;

	/// <summary>
	/// The resource a system loaded through this came from, by the system's name.
	/// </summary>
	public DeadlockParticleSystem Resource( string name )
		=> name is not null && resources.TryGetValue( name, out var resource ) ? resource : null;
}

/// <summary>
/// Particle collision against the scene's physics, for the collision constraints and the place on ground initializer.
/// </summary>
internal sealed class ParticleSceneCollision( Scene scene ) : IParticleCollision
{
	public bool HasGeometry => scene.IsValid();

	public bool TraceRay( System.Numerics.Vector3 rayStart, System.Numerics.Vector3 rayEnd, out System.Numerics.Vector3 position,
		out System.Numerics.Vector3 normal, out float fraction )
	{
		position = rayEnd;
		normal = System.Numerics.Vector3.Zero;
		fraction = 1f;

		if ( !scene.IsValid() )
			return false;

		var trace = scene.Trace.Ray( rayStart, rayEnd ).WithoutTags( "trigger", "player", "debris" ).Run();
		if ( !trace.Hit )
			return false;

		position = trace.HitPosition;
		normal = trace.Normal;
		fraction = trace.Fraction;
		return true;
	}
}

/// <summary>
/// Where particle code's warnings go: s&amp;box's log, each once.
/// </summary>
internal sealed class ParticleLog : ILogger
{
	public static ParticleLog Instance { get; } = new();

	public void Log( ParticleLogLevel level, string message )
	{
		if ( level >= ParticleLogLevel.Warning )
			global::Sandbox.Internal.GlobalGameNamespace.Log.Warning( $"Particles: {message}" );
	}
}
