using System;
using System.Reflection;

namespace Sandbox.Mounting.Deadlock;

public sealed partial class DeadlockMount
{
	/// <summary>
	/// Makes the Deadlock components (map lighting, particle effects, entities, the animator...) known to the running
	/// game's type library. In this project they come with the game's own code. Installed on its own in s&amp;box's
	/// mount folder, the mount carries them in its own assembly, which games know nothing of, and scenes would drop
	/// them when saved or loaded. Done on mount and again before building a map, as loading a game rebuilds its type
	/// library.
	/// </summary>
	internal static void EnsureGameTypes()
	{
		try
		{
			var library = Game.TypeLibrary;
			if ( library is null || library.GetType( typeof( global::Deadlock.DeadlockEntity ) ) is not null )
				return;

			var addAssembly = library.GetType().GetMethod( "AddAssembly", BindingFlags.Instance | BindingFlags.NonPublic, [typeof( Assembly ), typeof( bool )] );
			addAssembly?.Invoke( library, [typeof( global::Deadlock.DeadlockEntity ).Assembly, false] );
		}
		catch ( Exception e )
		{
			global::Sandbox.Internal.GlobalGameNamespace.Log.Warning( $"Deadlock: couldn't make the Deadlock components known to the game: {e.Message}" );
		}
	}
}
