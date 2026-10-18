using System;

namespace Deadlock;

/// <summary>
/// Marks a type ValveResourceFormat's KV3 transfer generator wrote a KeyValues reading constructor for. The generated
/// readers are kept as source (Animation/Generated), so this only documents where they came from.
/// </summary>
[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct )]
internal sealed class KV3TransferAttribute : Attribute
{
}

/// <summary>
/// The KeyValues key a property is read from, where it isn't the generator's default for the property's name.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
internal sealed class KVPropertyAttribute( string name ) : Attribute
{
	public string Name { get; } = name;
}

/// <summary>
/// A property the KV3 transfer generator skips.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
internal sealed class KVIgnoreAttribute : Attribute
{
}
