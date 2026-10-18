using System;

namespace Deadlock;

/// <summary>
/// Drives a mounted Deadlock model's face morphs from its flex controllers, running the model's flex rules
/// like the game does. Set controller values in the inspector or with <see cref="Set"/>.
/// </summary>
[Title( "Deadlock Flexes" ), Category( "Deadlock" ), Icon( "face" )]
public sealed class DeadlockFlexes : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// The renderer whose morphs are driven. Uses the one on this object when unset.
	/// </summary>
	[Property]
	public SkinnedModelRenderer Renderer { get; set; }

	/// <summary>
	/// Controller values by name. Filled in with the model's controllers when the model changes.
	/// </summary>
	[Property]
	public Dictionary<string, float> Controllers { get; set; } = new( StringComparer.OrdinalIgnoreCase );

	private Model _model;
	private Binding[] _bindings = [];

	/// <summary>
	/// One flex set, with its controllers and flexes resolved against the current model.
	/// </summary>
	private sealed record Binding( DeadlockFlexSet Set, DeadlockFlexRuleEvaluator Evaluator, float[] ControllerValues, int[] MorphIndices );

	public void Set( string controller, float value ) => Controllers[controller] = value;

	public float Get( string controller ) => Controllers.GetValueOrDefault( controller );

	protected override void OnUpdate()
	{
		var renderer = Renderer.IsValid() ? Renderer : GetComponent<SkinnedModelRenderer>();
		if ( !renderer.IsValid() || renderer.Model is not { } model )
			return;

		if ( model != _model )
			Bind( model );

		if ( renderer.SceneModel is not { } sceneModel )
			return;

		foreach ( var binding in _bindings )
		{
			var controllers = binding.Set.Controllers;
			for ( var i = 0; i < controllers.Count; i++ )
				binding.ControllerValues[i] = Controllers.GetValueOrDefault( controllers[i].Name );

			binding.Evaluator.Evaluate( binding.ControllerValues );

			for ( var flex = 0; flex < binding.MorphIndices.Length; flex++ )
			{
				// The weights are already smoothed by whatever sets the controllers, so apply them without a fade.
				if ( binding.MorphIndices[flex] >= 0 && binding.Evaluator.Driven[flex] )
					sceneModel.Morphs.Set( binding.MorphIndices[flex], binding.Evaluator.Weights[flex], 0f );
			}
		}
	}

	private void Bind( Model model )
	{
		_model = model;
		_bindings = [];

		if ( !model.TryGetData<DeadlockFlexRig>( out var rig ) || rig?.Sets is not { Count: > 0 } )
			return;

		// Each mesh morph becomes a model morph of the same name.
		var morphIndices = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		for ( var i = 0; i < model.Morphs.Count; i++ )
			morphIndices.TryAdd( model.Morphs.GetName( i ), i );

		_bindings = rig.Sets.Select( set => new Binding(
			set,
			new DeadlockFlexRuleEvaluator( set ),
			new float[set.Controllers.Count],
			set.Flexes.Select( name => morphIndices.GetValueOrDefault( name, -1 ) ).ToArray() ) ).ToArray();

		foreach ( var controller in rig.Sets.SelectMany( x => x.Controllers ) )
			Controllers.TryAdd( controller.Name, 0f );
	}
}
