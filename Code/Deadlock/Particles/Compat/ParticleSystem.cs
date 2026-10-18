using System;

namespace Deadlock.Particles;

/// <summary>
/// A particle system definition (a <c>.vpcf</c>'s data), upgraded to the newest format the simulation reads.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CParticleSystemDefinition">CParticleSystemDefinition</seealso>
public class ParticleSystem
{
	/// <summary>
	/// The file the definition came from, e.g. <c>particles/foo.vpcf</c>.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// The definition, in the newest format.
	/// </summary>
	public KVObject Data { get; }

	/// <param name="name">The file the definition came from.</param>
	/// <param name="data">The definition as stored.</param>
	/// <param name="format">The KV3 format it's stored in, e.g. <c>vpcf61</c>; it is upgraded from there.</param>
	public ParticleSystem( string name, KVObject data, string format )
	{
		Name = name;
		Data = Upgrade.ParticleFormatUpgrader.UpgradeToLatest( data, format );
	}

	/// <summary>
	/// The definition in the newest format, as <see cref="Data"/>.
	/// </summary>
	public KVObject GetUpgradedData() => Data;

	private IEnumerable<KVObject> Functions( string key ) => Data.GetArray( key ) ?? (IEnumerable<KVObject>)[];

	public IEnumerable<KVObject> GetRenderers() => Functions( "m_Renderers" );
	public IEnumerable<KVObject> GetOperators() => Functions( "m_Operators" );
	public IEnumerable<KVObject> GetInitializers() => Functions( "m_Initializers" );
	public IEnumerable<KVObject> GetEmitters() => Functions( "m_Emitters" );
	public IEnumerable<KVObject> GetPreEmissionOperators() => Functions( "m_PreEmissionOperators" );
	public IEnumerable<KVObject> GetForceGenerators() => Functions( "m_ForceGenerators" );
	public IEnumerable<KVObject> GetConstraints() => Functions( "m_Constraints" );
	public IEnumerable<KVObject> GetChildren() => Functions( "m_Children" );

	/// <summary>
	/// Gets whether a child entry runs. <c>m_bDisableChild</c> only removes a child from behavior version 5 on;
	/// older definitions keep disabled children.
	/// </summary>
	public bool IsChildEnabled( KVObject childEntry )
		=> Data.GetInt32Property( "m_nBehaviorVersion" ) < 5 || !childEntry.GetBooleanProperty( "m_bDisableChild" );

	/// <summary>
	/// Gets the names of child particles.
	/// </summary>
	public IEnumerable<string> GetChildParticleNames( bool enabledOnly = false )
	{
		var children = GetChildren();

		if ( enabledOnly )
			children = children.Where( IsChildEnabled );

		return children.Select( c => c.GetStringProperty( "m_ChildRef" ) ).ToList();
	}
}

/// <summary>
/// Loads the other files a particle system refers to: child systems and snapshots.
/// </summary>
public interface IFileLoader
{
	/// <summary>
	/// A particle system by its path, e.g. <c>particles/foo.vpcf</c>, or null if there isn't one.
	/// </summary>
	ParticleSystem LoadParticleSystem( string path );

	/// <summary>
	/// A snapshot by its path, e.g. <c>particles/foo.vsnap</c>, or null if there isn't one.
	/// </summary>
	ParticleSnapshot LoadSnapshot( string path );
}
