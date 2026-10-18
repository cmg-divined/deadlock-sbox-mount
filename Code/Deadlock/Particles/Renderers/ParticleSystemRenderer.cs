using System;

namespace Deadlock.Particles.Renderers;

/// <summary>
/// Draws a <see cref="ParticleSystemSimulation"/>. One of these mirrors each system in the simulation's child tree
/// and owns the renderers that system draws through; particles, control points and timing belong to the simulation.
/// </summary>
internal sealed class ParticleSystemRenderer : IParticleSystemObserver, IDisposable
{
	private const string UnsupportedClassWarning = "Unsupported {ComponentType} class '{ClassName}' {File}";

	private readonly List<ParticleFunctionRenderer> renderers = [];
	private readonly List<ParticleSystemRenderer> childRenderers = [];

	public ParticleSystemSimulation Simulation { get; }

	public ParticleSystemRenderer( ParticleSystemSimulation simulation, DeadlockParticleSystem resource, IFileLoader files, ILogger logger )
	{
		Simulation = simulation;
		simulation.Observer = this;

		foreach ( var rendererInfo in simulation.Definition.GetArray( "m_Renderers" ) ?? [] )
		{
			var parse = new ParticleDefinitionParser( rendererInfo, logger, simulation.BehaviorVersion );
			if ( parse.Boolean( "m_bDisableOperator", default ) )
				continue;

			var rendererClass = rendererInfo.GetStringProperty( "_class" );
			ParticleFunctionRenderer renderer;

			try
			{
				renderer = Create( rendererClass, parse, resource );
			}
			catch ( Exception e )
			{
				logger.LogUniqueWarning( "Renderer {ClassName} of {File} failed to load: {Message}", rendererClass, simulation.Name, e.Message );
				continue;
			}

			if ( renderer is null )
			{
				logger.LogUniqueWarningFor( ["renderer", rendererClass], UnsupportedClassWarning, "renderer", rendererClass, simulation.Name );
				continue;
			}

			renderers.Add( renderer );
		}

		simulation.RenderState.SequenceDurations = CollectSequenceDurations();

		// Children are loaded by the simulation; each gets the sprite sheets of its own resource.
		foreach ( var childSimulation in simulation.Children )
		{
			var childResource = files is ParticleResourceFiles resources ? resources.Resource( childSimulation.Name ) : null;
			childRenderers.Add( new ParticleSystemRenderer( childSimulation, childResource, files, logger ) );
		}
	}

	private static ParticleFunctionRenderer Create( string className, ParticleDefinitionParser parse, DeadlockParticleSystem resource ) => className switch
	{
		"C_OP_RenderSprites" => new RenderSprites( parse, resource ),
		"C_OP_RenderRopes" => new RenderRopes( parse, resource ),
		"C_OP_RenderTrails" => new RenderTrails( parse, resource ),
		"C_OP_RenderStandardLight" => new RenderStandardLight( parse ),
		"C_OP_RenderOmni2Light" => new RenderOmni2Light( parse ),
		"C_OP_RenderLightBeam" => new RenderLightBeam( parse ),
		_ => null,
	};

	/// <summary>
	/// Adds the lights this system's light renderers placed at its last step, and its running children's.
	/// </summary>
	public void CollectLights( List<ParticleLight> output )
	{
		foreach ( var renderer in renderers )
		{
			if ( renderer is ParticleLightRenderer lightRenderer )
				output.AddRange( lightRenderer.Lights );
		}

		foreach ( var childRenderer in childRenderers )
		{
			if ( childRenderer.Simulation.ShouldRunAsChildOf( Simulation.RenderState ) )
				childRenderer.CollectLights( output );
		}
	}

	/// <summary>
	/// How long each sequence of the first sheet this system's renderers draw with runs, or null without one.
	/// </summary>
	private float[] CollectSequenceDurations()
	{
		foreach ( var renderer in renderers )
		{
			if ( renderer.SpriteSheet is not { } sheet )
				continue;

			var durations = new float[sheet.Sequences.Length];
			for ( var i = 0; i < durations.Length; i++ )
				durations[i] = sheet.Sequences[i].TotalTime;

			return durations;
		}

		return null;
	}

	public void OnFrameSimulated( ParticleCollection particles, ParticleSystemState state )
	{
		foreach ( var renderer in renderers )
			renderer.Simulate( particles, state );
	}

	/// <summary>
	/// Finishes the frame for this system's renderers and its children's. Renderers out of draw distance or faded
	/// out are hidden; the rest act when there was a step.
	/// </summary>
	public void Act( System.Numerics.Vector3 cameraPosition, bool stepped )
	{
		var withinDrawDistance = Simulation.IsWithinDrawDistance( cameraPosition );

		foreach ( var renderer in renderers )
		{
			if ( !withinDrawDistance || renderer.GetOperatorRunStrength( Simulation.RenderState ) <= 0f )
				renderer.Hide();
			else if ( stepped )
				renderer.Act( Simulation.Particles, Simulation.RenderState );
		}

		foreach ( var childRenderer in childRenderers )
			childRenderer.Act( cameraPosition, stepped );
	}

	/// <summary>
	/// Draws this system's renderers that draw in <paramref name="pass"/>, and its running children's.
	/// </summary>
	public void Draw( in ParticleView view, ParticleRenderPass pass )
	{
		foreach ( var childRenderer in childRenderers )
		{
			if ( childRenderer.Simulation.ShouldRunAsChildOf( Simulation.RenderState ) )
				childRenderer.Draw( view, pass );
		}

		if ( Simulation.Particles.Count == 0 || !Simulation.IsWithinDrawDistance( view.Location ) )
			return;

		foreach ( var renderer in renderers )
		{
			// The water and bloom only passes have no s&box equivalent.
			if ( renderer.Pass != pass || renderer.OnlyRenderInEffectsWaterPass || renderer.OnlyRenderInEffectsBloomPass )
				continue;

			if ( renderer.GetOperatorRunStrength( Simulation.RenderState ) <= 0f )
				continue;

			renderer.Draw( Simulation.Particles, Simulation.RenderState, view );
		}
	}

	public void Hide()
	{
		foreach ( var renderer in renderers )
			renderer.Hide();

		foreach ( var childRenderer in childRenderers )
			childRenderer.Hide();
	}

	public void Dispose()
	{
		foreach ( var renderer in renderers )
			renderer.Dispose();

		foreach ( var childRenderer in childRenderers )
			childRenderer.Dispose();

		renderers.Clear();
		childRenderers.Clear();
	}
}
