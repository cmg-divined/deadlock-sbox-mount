using System;

namespace Deadlock;

/// <summary>
/// The body groups of a mounted Deadlock model with their choice names, which s&amp;box doesn't keep for
/// models built at runtime: the renderer lists every choice as "mesh". Choices are in the same order as the
/// model's body part choices. Read it with <c>Model.GetData&lt;DeadlockBodyGroups&gt;()</c>.
/// </summary>
[Sandbox.ModelEditor.GameData( "deadlock_body_groups" )]
public sealed class DeadlockBodyGroups
{
	/// <summary>
	/// Hidden from ModelDoc, which can't edit lists.
	/// </summary>
	[Hide]
	public List<DeadlockBodyGroup> Groups { get; set; } = [];

	public DeadlockBodyGroup Get( string group ) => Groups.FirstOrDefault( x => string.Equals( x.Name, group, StringComparison.OrdinalIgnoreCase ) );
}

public sealed class DeadlockBodyGroup
{
	public string Name { get; set; }

	/// <summary>
	/// Choice names by choice index, such as <c>base</c>, <c>smug</c> and <c>cocky</c> for a hero's head.
	/// </summary>
	public List<string> Choices { get; set; } = [];
}

public static class DeadlockBodyGroupExtensions
{
	/// <summary>
	/// Selects a body group choice by its Deadlock name. Returns false if the model has no such group or choice.
	/// </summary>
	public static bool SetDeadlockBodyGroup( this ModelRenderer renderer, string group, string choice )
	{
		if ( !renderer.IsValid() || renderer.Model is not { } model || !model.TryGetData<DeadlockBodyGroups>( out var groups ) )
			return false;

		var index = groups?.Get( group )?.Choices.FindIndex( x => string.Equals( x, choice, StringComparison.OrdinalIgnoreCase ) ) ?? -1;
		if ( index < 0 )
			return false;

		renderer.SetBodyGroup( group, index );
		return true;
	}

	/// <summary>
	/// The Deadlock name of a body group's current choice, or null.
	/// </summary>
	public static string GetDeadlockBodyGroup( this ModelRenderer renderer, string group )
	{
		if ( !renderer.IsValid() || renderer.Model is not { } model || !model.TryGetData<DeadlockBodyGroups>( out var groups ) )
			return null;

		if ( groups?.Get( group )?.Choices is not { } choices )
			return null;

		var index = renderer.GetBodyGroup( group );
		return index >= 0 && index < choices.Count ? choices[index] : null;
	}
}
