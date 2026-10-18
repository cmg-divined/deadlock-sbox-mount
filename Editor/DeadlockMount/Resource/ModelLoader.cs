namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Loads a compiled Deadlock model (vmdl_c) as an s&amp;box <see cref="Model"/>.
/// </summary>
internal sealed class ModelLoader( string filePath ) : ResourceLoader<DeadlockMount>
{
	protected override object Load()
	{
		var model = new CompiledModel( Host.ReadFile( filePath ) );
		return new ModelImporter( Host, model, Path ).Import();
	}
}
