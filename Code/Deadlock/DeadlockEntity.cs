using System;

namespace Deadlock;

/// <summary>
/// A Deadlock map entity as the map defines it: its class, name, every key value and its outputs. Mounted maps put
/// one on each entity's GameObject, so game code can read what the map says (spawn teams, trigger kinds, targets).
/// </summary>
[Title( "Deadlock Entity" ), Category( "Deadlock" ), Icon( "data_object" )]
public sealed class DeadlockEntity : Component
{
	[Property, ReadOnly]
	public string Classname { get; set; }

	[Property, ReadOnly]
	public string TargetName { get; set; }

	/// <summary>
	/// Key values as Hammer writes them: numbers in invariant culture, vectors space separated.
	/// </summary>
	[Property]
	public Dictionary<string, string> KeyValues { get; set; } = new( StringComparer.OrdinalIgnoreCase );

	[Property]
	public List<DeadlockEntityOutput> Outputs { get; set; } = [];

	public string Get( string key, string fallback = null ) => KeyValues.TryGetValue( key, out var value ) ? value : fallback;
}

/// <summary>
/// When the entity fires <see cref="Output"/>, call <see cref="Input"/> on the entities named <see cref="Target"/>.
/// </summary>
public sealed class DeadlockEntityOutput
{
	public string Output { get; set; }
	public string Target { get; set; }
	public string Input { get; set; }
	public string Parameter { get; set; }
	public float Delay { get; set; }

	/// <summary>
	/// How many times it fires, -1 for always.
	/// </summary>
	public int TimesToFire { get; set; } = -1;
}
