namespace Deadlock;

/// <summary>
/// A <see cref="DeadlockMap"/> that starts on the hideout (dl_hideout) rather than midtown.
/// </summary>
[Title( "Deadlock Hideout" ), Category( "Deadlock" ), Icon( "home" )]
public sealed class DeadlockHideout : DeadlockMap
{
	public DeadlockHideout() => MapName = "dl_hideout";
}
