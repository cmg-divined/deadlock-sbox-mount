using System.Text.Json.Serialization;
using Deadlock.Particles;

namespace Deadlock;

/// <summary>
/// A Deadlock particle system (a <c>.vpcf</c>) as the Deadlock mount serves it, at
/// <c>mount://deadlock/particles/....vpcf</c>: its definition, upgraded to the newest format, and the sprite sheets
/// of the textures its renderers draw with. <see cref="DeadlockParticleEffect"/> plays it.
/// </summary>
public sealed class DeadlockParticleSystem : GameResource
{
	/// <summary>
	/// The particle system definition the simulation runs.
	/// </summary>
	[Hide, JsonIgnore]
	public Particles.ParticleSystem Definition { get; set; }

	/// <summary>
	/// Sprite sheets by texture path (e.g. <c>materials/particle/foo.vtex</c>), for the textures that have one.
	/// </summary>
	[Hide, JsonIgnore]
	public IReadOnlyDictionary<string, ParticleSpriteSheet> SpriteSheets { get; set; } = new Dictionary<string, ParticleSpriteSheet>();

	/// <summary>
	/// A mounted particle system by the path Deadlock refers to it by, e.g. <c>particles/foo.vpcf</c>, or null when
	/// the Deadlock mount has no such file.
	/// </summary>
	public static DeadlockParticleSystem Load( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		path = path.Replace( '\\', '/' );
		if ( path.EndsWith( "_c" ) )
			path = path[..^2];

		if ( !path.StartsWith( "mount://" ) )
			path = $"mount://deadlock/{path}";

		return GameResource.Load<DeadlockParticleSystem>( path ) is { Definition: not null } system ? system : null;
	}

	/// <summary>
	/// The sprite sheet of a texture this system draws with, or null when it has none.
	/// </summary>
	public ParticleSpriteSheet GetSpriteSheet( string texture )
		=> texture is not null && SpriteSheets.TryGetValue( texture, out var sheet ) ? sheet : null;
}
