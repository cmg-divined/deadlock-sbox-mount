namespace Deadlock;

/// <summary>
/// The face rig of a mounted Deadlock model: the controllers animators work with and the rules that turn
/// them into morph weights. The Deadlock mount stores it on the model, read it with
/// <c>Model.GetData&lt;DeadlockFlexRig&gt;()</c>.
/// </summary>
[Sandbox.ModelEditor.GameData( "deadlock_flex_rig" )]
public sealed class DeadlockFlexRig
{
	/// <summary>
	/// One set per distinct mesh morph set. Rules address their own set's controllers and flexes by index.
	/// Hidden from ModelDoc, which can't edit lists.
	/// </summary>
	[Hide]
	public List<DeadlockFlexSet> Sets { get; set; } = [];
}

/// <summary>
/// The flexes (morph targets) of a mesh, and the controllers and rules that drive them.
/// </summary>
public sealed class DeadlockFlexSet
{
	/// <summary>
	/// Flex names by index. These are the names of the model's morphs.
	/// </summary>
	public List<string> Flexes { get; set; } = [];

	public List<DeadlockFlexControllerInfo> Controllers { get; set; } = [];

	public List<DeadlockFlexRule> Rules { get; set; } = [];
}

public sealed class DeadlockFlexControllerInfo
{
	public string Name { get; set; }
	public string Type { get; set; }
	public float Min { get; set; }
	public float Max { get; set; } = 1;
}

/// <summary>
/// A stack program computing one flex's weight. Opcodes follow Source 2's FLEX_OP values.
/// </summary>
public sealed class DeadlockFlexRule
{
	public int Flex { get; set; }
	public List<int> OpCodes { get; set; } = [];
	public List<int> OpData { get; set; } = [];
}
