using System;
using Deadlock.Animation;
using Deadlock.Animation.ModelAnimation;
using GraphRuntime = Deadlock.Animation.ModelAnimation2.AnimationGraph;

namespace Deadlock;

/// <summary>
/// Animates a mounted Deadlock model with its AnimGraph 2 graph, the way the game does: a port of
/// ValveResourceFormat's AnimGraph 2 runtime (itself Esoterica's) runs the graph each frame and its pose goes onto the
/// renderer's bones. Set the graph's parameters (speeds, states, actions) to drive it.
/// </summary>
/// <remarks>
/// The graph animates the bones of its skeleton; the model's other bones (twist and helper bones) keep their bind pose
/// under their parents.
/// </remarks>
[Title( "Deadlock Animator" ), Category( "Deadlock" ), Icon( "directions_run" )]
public sealed class DeadlockAnimator : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// The renderer animated. Its own animation graph is turned off while this plays.
	/// </summary>
	[Property] public SkinnedModelRenderer Renderer { get; set; }

	/// <summary>
	/// The graph played. Unset, the model's own graph is (the first of its <see cref="DeadlockAnimGraphs"/>).
	/// </summary>
	[Property] public DeadlockAnimationGraph Graph { get; set; }

	/// <summary>
	/// Whether the graph steps by itself every frame. Something that sets its parameters each frame (like
	/// <see cref="DeadlockHero"/>) turns this off and calls <see cref="Step"/> after setting them.
	/// </summary>
	[Property] public bool UpdateAutomatically { get; set; } = true;

	/// <summary>
	/// Whether it animates in the editor's scene view too, not only in play mode.
	/// </summary>
	[Property] public bool PlayInEditor { get; set; } = true;

	[Property, Range( 0, 4 )] public float TimeScale { get; set; } = 1f;

	/// <summary>
	/// How the graph's root moved during the last step, in the model's space: the movement and turning the clips
	/// played (turns in place, mantles) carry.
	/// </summary>
	public Transform RootMotion { get; private set; } = global::Transform.Zero;

	/// <summary>
	/// Whether a graph is loaded and playing.
	/// </summary>
	public bool IsPlaying => graph is not null;

	private GraphRuntime graph;
	private Model boundModel;
	private DeadlockAnimationGraph boundGraph;
	private bool failed;

	// Per model bone: the graph bone that animates it or -1, its parent and its bind pose relative to it.
	private int[] graphBone = [];
	private int[] parentBone = [];
	private Transform[] bindLocal = [];
	private Transform[] modelSpace = [];

	private readonly Dictionary<string, float> floats = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, bool> bools = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, string> ids = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, System.Numerics.Vector4> vectors = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Sets a float parameter, such as <c>forward_speed</c>. Parameters the graph doesn't have are ignored.
	/// </summary>
	public void SetFloat( string name, float value )
	{
		floats[name] = value;
		if ( graph is not null && graph.FloatParameters.ContainsKey( name ) )
			graph.FloatParameters[name] = value;
	}

	public void SetBool( string name, bool value )
	{
		bools[name] = value;
		if ( graph is not null && graph.BoolParameters.ContainsKey( name ) )
			graph.BoolParameters[name] = value;
	}

	/// <summary>
	/// Sets an ID parameter, such as <c>base_action</c> to <c>ground_jump</c>; empty clears it.
	/// </summary>
	public void SetId( string name, string value )
	{
		value ??= string.Empty;
		ids[name] = value;
		if ( graph is not null && graph.IdParameters.ContainsKey( name ) )
			graph.IdParameters[name] = value;
	}

	public void SetVector( string name, Vector3 value )
	{
		var v = new System.Numerics.Vector4( value.x, value.y, value.z, 0f );
		vectors[name] = v;
		if ( graph is not null && graph.VectorParameters.ContainsKey( name ) )
			graph.VectorParameters[name] = v;
	}

	public float GetFloat( string name ) => graph is not null && graph.FloatParameters.TryGetValue( name, out var value ) ? value : floats.GetValueOrDefault( name );

	public string GetId( string name ) => graph is not null && graph.IdParameters.TryGetValue( name, out var value ) ? value : ids.GetValueOrDefault( name ) ?? "";

	/// <summary>
	/// The graph's parameter names, once it's loaded.
	/// </summary>
	public IReadOnlyList<string> ParameterNames => graph?.ParameterNames ?? [];

	protected override void OnEnabled()
	{
		AnimationLog.Handler ??= message => Log.Warning( message );
	}

	protected override void OnDisabled()
	{
		Release();
	}

	protected override void OnUpdate()
	{
		if ( UpdateAutomatically )
			Step( Time.Delta );
	}

	/// <summary>
	/// Advances the graph and poses the renderer's bones.
	/// </summary>
	public void Step( float timeStep )
	{
		if ( !Game.IsPlaying && !PlayInEditor )
		{
			Release();
			return;
		}

		var renderer = Renderer;
		if ( !renderer.IsValid() || renderer.Model is not { } model || renderer.SceneModel is not { } sceneModel )
			return;

		if ( !Bind( model ) )
			return;

		if ( renderer.UseAnimGraph )
			renderer.UseAnimGraph = false;

		var world = renderer.WorldTransform;
		var worldBone = new FrameBone( world.Position, world.UniformScale, ToQuaternion( world.Rotation ) );

		FrameBone[] pose;

		try
		{
			pose = graph.Update( MathF.Max( 0f, timeStep * TimeScale ), worldBone );
		}
		catch ( Exception e )
		{
			Log.Warning( $"{GameObject.Name}: animation graph failed: {e.Message}" );
			Release();
			failed = true;
			return;
		}

		RootMotion = ToTransform( graph.RootMotionDelta );

		for ( var i = 0; i < graphBone.Length; i++ )
		{
			var local = graphBone[i] >= 0 && graphBone[i] < pose.Length ? ToTransform( pose[graphBone[i]] ) : bindLocal[i];
			modelSpace[i] = parentBone[i] >= 0 ? modelSpace[parentBone[i]].ToWorld( local ) : local;
			sceneModel.SetBoneOverride( i, modelSpace[i] );
		}
	}

	/// <summary>
	/// Loads the graph for a model, and works out which graph bone animates which model bone.
	/// </summary>
	private bool Bind( Model model )
	{
		var source = Graph ?? FindModelGraph( model );

		if ( graph is not null && boundModel == model && boundGraph == source )
			return true;

		if ( failed && boundModel == model && boundGraph == source )
			return false;

		Release();
		boundModel = model;
		boundGraph = source;
		failed = true;

		if ( source?.Root is null || source.Load( source.Root ) is not { } definition )
			return false;

		try
		{
			graph = new GraphRuntime( source.Root, definition, source );
		}
		catch ( Exception e )
		{
			Log.Warning( $"{GameObject.Name}: animation graph '{source.Root}' failed to load: {e.Message}" );
			graph = null;
			return false;
		}

		var graphBones = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		for ( var i = 0; i < graph.Skeleton.Bones.Length; i++ )
			graphBones.TryAdd( graph.Skeleton.Bones[i].Name, i );

		var bones = model.Bones.AllBones;
		graphBone = new int[bones.Count];
		parentBone = new int[bones.Count];
		bindLocal = new Transform[bones.Count];
		modelSpace = new Transform[bones.Count];

		for ( var i = 0; i < bones.Count; i++ )
		{
			var bone = bones[i];
			graphBone[i] = graphBones.GetValueOrDefault( bone.Name, -1 );
			parentBone[i] = bone.Parent?.Index ?? -1;
			bindLocal[i] = bone.Parent is { } parent ? parent.LocalTransform.ToLocal( bone.LocalTransform ) : bone.LocalTransform;
		}

		ApplyParameters();
		failed = false;
		return true;
	}

	private void ApplyParameters()
	{
		foreach ( var (name, value) in floats )
			if ( graph.FloatParameters.ContainsKey( name ) ) graph.FloatParameters[name] = value;

		foreach ( var (name, value) in bools )
			if ( graph.BoolParameters.ContainsKey( name ) ) graph.BoolParameters[name] = value;

		foreach ( var (name, value) in ids )
			if ( graph.IdParameters.ContainsKey( name ) ) graph.IdParameters[name] = value;

		foreach ( var (name, value) in vectors )
			if ( graph.VectorParameters.ContainsKey( name ) ) graph.VectorParameters[name] = value;
	}

	private static DeadlockAnimationGraph FindModelGraph( Model model )
	{
		if ( !model.TryGetData<DeadlockAnimGraphs>( out var graphs ) || graphs?.Graphs is not { Count: > 0 } references )
			return null;

		return DeadlockAnimationGraph.Find( references[0].Graph );
	}

	private void Release()
	{
		if ( graph is not null && Renderer.IsValid() )
		{
			Renderer.ClearPhysicsBones();
			Renderer.UseAnimGraph = true;
		}

		graph = null;
		boundModel = null;
		boundGraph = null;
		failed = false;
		RootMotion = global::Transform.Zero;
	}

	private static Transform ToTransform( FrameBone bone )
		=> new( bone.Position, new Rotation( bone.Angle.X, bone.Angle.Y, bone.Angle.Z, bone.Angle.W ), bone.Scale );

	private static System.Numerics.Quaternion ToQuaternion( Rotation rotation )
		=> new( rotation.x, rotation.y, rotation.z, rotation.w );
}
