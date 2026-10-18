using System;

namespace Deadlock;

/// <summary>
/// Draws the GameObject's model a second time with other materials: a hidden child renderer (bone merged to a skinned
/// one), made only when one of the model's materials has a copy for the pass. Every other slot draws nothing.
/// </summary>
public abstract class DeadlockModelPass : Component, Component.ExecuteInEditor
{
	protected const string ShaderPath = "shaders/deadlock_pbr.shader";

	private ModelRenderer _copy;
	private Model _model;

	/// <summary>
	/// The child's name.
	/// </summary>
	protected abstract string PassName { get; }

	/// <summary>
	/// The pass's copy of a material, or null where the model draws nothing in this pass.
	/// </summary>
	protected abstract Material CopyOf( Material material );

	/// <summary>
	/// A material that draws nothing in this pass.
	/// </summary>
	protected abstract Material Nothing();

	/// <summary>
	/// Sets the child renderer up for the pass, once it has the model.
	/// </summary>
	protected virtual void Configure( ModelRenderer copy )
	{
	}

	protected override void OnDisabled()
	{
		DestroyCopy();
		_model = null;
	}

	protected override void OnUpdate()
	{
		var body = Components.Get<ModelRenderer>();
		var model = body.IsValid() ? body.Model : null;

		if ( model != _model || (_copy is not null && !_copy.IsValid()) )
		{
			_model = model;
			DestroyCopy();

			if ( model is not null && model.Materials.Any( x => CopyOf( x ) is not null ) )
				CreateCopy( body );
		}

		if ( !_copy.IsValid() )
			return;

		_copy.Enabled = body.Enabled;

		if ( _copy.MaterialGroup != body.MaterialGroup )
		{
			_copy.MaterialGroup = body.MaterialGroup;
			OverrideMaterials( body );
		}
	}

	private void CreateCopy( ModelRenderer body )
	{
		var copyObject = new GameObject( true, PassName );
		copyObject.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.Hidden;
		copyObject.SetParent( GameObject, false );

		_copy = body is SkinnedModelRenderer ? copyObject.Components.Create<SkinnedModelRenderer>() : copyObject.Components.Create<ModelRenderer>();
		_copy.Model = _model;
		_copy.MaterialGroup = body.MaterialGroup;

		// Merged before anything else updates it: set up as a skinned renderer of its own first, under the body, it
		// leaves the body undrawn.
		if ( body is SkinnedModelRenderer skinned && _copy is SkinnedModelRenderer copy )
			copy.BoneMergeTarget = skinned;

		_copy.RenderType = ModelRenderer.ShadowRenderType.Off;
		Configure( _copy );
		OverrideMaterials( body );
	}

	private void DestroyCopy()
	{
		if ( _copy.IsValid() )
			_copy.GameObject.Destroy();

		_copy = null;
	}

	/// <summary>
	/// Every slot gets the pass's copy of the material the body draws it with, or a material that draws nothing.
	/// </summary>
	private void OverrideMaterials( ModelRenderer body )
	{
		for ( var slot = 0; slot < _copy.Materials.Count; slot++ )
		{
			var material = body.MaterialOverride ?? body.Materials.GetOverride( slot ) ?? body.Materials.GetOriginal( slot );
			_copy.Materials.SetOverride( slot, CopyOf( material ) ?? Nothing() );
		}
	}

	/// <summary>
	/// A copy of a material with a feature turned on, cached by name.
	/// </summary>
	protected static Material CachedCopy( Dictionary<string, Material> cache, Material material, string suffix, Action<Material> setup )
	{
		var key = material.Name ?? material.ResourcePath;
		if ( key is null )
			return null;

		if ( cache.TryGetValue( key, out var copy ) && copy.IsValid() )
			return copy;

		copy = material.CreateCopy( $"{key}_{suffix}" );
		setup( copy );

		cache[key] = copy;
		return copy;
	}
}
