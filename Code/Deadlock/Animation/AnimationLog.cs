using System;

namespace Deadlock.Animation;

/// <summary>
/// Where the animation code's warnings go: <see cref="Handler"/>, which the game code points at its log. They went to
/// the console in ValveResourceFormat, which game code can't write to.
/// </summary>
public static class AnimationLog
{
	public static Action<string> Handler { get; set; }

	public static void Warning( string message ) => Handler?.Invoke( message );
}
