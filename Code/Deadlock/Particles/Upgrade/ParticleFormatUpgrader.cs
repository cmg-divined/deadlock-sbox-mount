using System;

namespace Deadlock.Particles.Upgrade;

/// <summary>
/// Replays the vpcf KV3 format-conversion chain, upgrading particle system documents from their stored format to
/// the newest implemented one. Deadlock's particles are stored as vpcf61 to vpcf66, so only those steps of
/// ValveResourceFormat's chain are here.
/// </summary>
public static class ParticleFormatUpgrader
{
	/// <summary>
	/// The newest <c>m_nBehaviorVersion</c> the chain produces.
	/// </summary>
	public const int LatestBehaviorVersion = 12;

	internal static IReadOnlyList<ParticleUpgradeStep> Steps { get; } =
	[
		new Vpcf61ToVpcf62(),
		new Vpcf62ToVpcf63(),
		new Vpcf63ToVpcf64(),
		new Vpcf64ToVpcf65(),
		new Vpcf65ToVpcf66(),
	];

	/// <summary>
	/// The newest format the chain produces.
	/// </summary>
	public static string LatestFormat => Steps[^1].ToFormat;

	/// <summary>
	/// Deep-clones the document root and applies every step past the stored format, returning the upgraded clone.
	/// Formats older than the chain start at its first step; the newest format and unknown newer ones are left as
	/// they are.
	/// </summary>
	public static KVObject UpgradeToLatest( KVObject root, string storedFormat )
	{
		ArgumentNullException.ThrowIfNull( root );

		var start = StartIndex( storedFormat );
		if ( start >= Steps.Count )
			return root;

		var upgraded = KVObjectDeepClone.Clone( root );

		for ( var i = start; i < Steps.Count; i++ )
			Steps[i].Apply( upgraded );

		return upgraded;
	}

	private static int StartIndex( string format )
	{
		for ( var i = 0; i < Steps.Count; i++ )
		{
			if ( Steps[i].FromFormat == format )
				return i;
		}

		if ( format == LatestFormat )
			return Steps.Count;

		// "vpcfNN": older than the chain replays all of it, anything else is taken as current.
		return format is { Length: > 4 } && format.StartsWith( "vpcf", StringComparison.Ordinal )
			&& int.TryParse( format.AsSpan( 4 ), out var version ) && version < 61
			? 0
			: Steps.Count;
	}
}
