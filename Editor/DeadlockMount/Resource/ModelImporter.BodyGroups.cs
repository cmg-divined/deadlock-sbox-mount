using Deadlock;
using System;

namespace Sandbox.Mounting.Deadlock;

internal sealed partial class ModelImporter
{
	/// <summary>
	/// s&amp;box tracks body group choices in a 64 bit mask.
	/// </summary>
	private const int MaxBodyGroupChoices = 64;

	/// <summary>
	/// A body group and its choice names, by choice index.
	/// </summary>
	private sealed record BodyGroup( string Name, string[] Choices );

	/// <summary>
	/// The model's body groups, or empty when it has none or more choices than s&amp;box can hold.
	/// </summary>
	private List<BodyGroup> _bodyGroups = [];

	/// <summary>
	/// The body group for meshes outside any body group, once a model has body groups. Meshes added to every
	/// group would claim every group's choice bits and break choosing, so like Deadlock's own "autodefault" mesh
	/// group they get a body group with a single choice.
	/// </summary>
	private string _alwaysVisibleGroup;

	private BodyGroupChoice?[] _meshGroupChoices = [];

	private void ReadBodyGroups()
	{
		_meshGroupChoices = model.BodyGroupChoices;

		var groups = _meshGroupChoices.Where( x => x is not null ).Select( x => x.Value ).GroupBy( x => x.Group, StringComparer.Ordinal ).ToList();
		if ( groups.Count == 0 )
			return;

		var bodyGroups = groups.Select( group =>
		{
			var choices = new string[group.Max( x => x.Choice ) + 1];
			for ( var i = 0; i < choices.Length; i++ )
				choices[i] = group.FirstOrDefault( x => x.Choice == i ).ChoiceName ?? i.ToString( System.Globalization.CultureInfo.InvariantCulture );

			return new BodyGroup( group.Key, choices );
		} ).ToList();

		// One more choice for the always visible group.
		if ( bodyGroups.Sum( x => x.Choices.Length ) + 1 > MaxBodyGroupChoices )
		{
			Log.Warning( $"{name}: {bodyGroups.Sum( x => x.Choices.Length )} body group choices is more than s&box supports; only the default choices are imported." );
			return;
		}

		_bodyGroups = bodyGroups;

		_alwaysVisibleGroup = "autodefault";
		for ( var suffix = 1; _bodyGroups.Any( x => x.Name == _alwaysVisibleGroup ); suffix++ )
			_alwaysVisibleGroup = $"autodefault_{suffix}";
	}

	/// <summary>
	/// Whether a mesh is imported at all: with body groups every mesh is, without only the ones shown by default.
	/// </summary>
	private bool IsImported( int meshIndex ) => _bodyGroups.Count > 0 || model.IsVisibleByDefault( meshIndex );

	/// <summary>
	/// The body group choices a mesh shows in, or null if it shows whatever the choices.
	/// </summary>
	private (string Group, int Choice)[] GetMeshChoices( int meshIndex )
	{
		if ( _bodyGroups.Count == 0 )
			return null;

		var mask = model.GetMeshGroupMask( meshIndex );
		var choices = new List<(string, int)>();

		// Meshes without a mask entry are in every group.
		if ( mask != ulong.MaxValue )
		{
			for ( var i = 0; i < _meshGroupChoices.Length && i < 64; i++ )
			{
				if ( ((mask >> i) & 1) != 0 && _meshGroupChoices[i] is { } choice )
					choices.Add( (choice.Group, choice.Choice) );
			}
		}

		return choices.Count > 0 ? [.. choices] : [(_alwaysVisibleGroup, 0)];
	}

	/// <summary>
	/// Whether a mesh shows with the default choices, which is what the model's bounds and trace mesh cover.
	/// </summary>
	private bool IsDefaultMesh( int meshIndex )
	{
		var mask = model.GetMeshGroupMask( meshIndex );
		return mask == ulong.MaxValue || model.IsVisibleByDefault( meshIndex ) || GetMeshChoices( meshIndex ) is [(var group, _)] && group == _alwaysVisibleGroup;
	}

	/// <summary>
	/// Registers every choice up front, in order, so choice indices match Deadlock's, empty choices exist, and
	/// choice 0 (Deadlock's default in every model) is the default.
	/// </summary>
	private void AddBodyGroups( ModelBuilder builder, bool hasAlwaysVisible )
	{
		if ( _bodyGroups.Count == 0 )
			return;

		foreach ( var group in _bodyGroups )
		{
			for ( var choice = 0; choice < group.Choices.Length; choice++ )
				builder.AddMesh( null, group.Name, choice );
		}

		if ( hasAlwaysVisible )
			builder.AddMesh( null, _alwaysVisibleGroup, 0 );

		builder.WithData( new DeadlockBodyGroups
		{
			Groups = _bodyGroups.Select( x => new DeadlockBodyGroup { Name = x.Name, Choices = [.. x.Choices] } ).ToList(),
		} );
	}
}
