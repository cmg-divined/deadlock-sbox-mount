using System;
using System.Numerics;
namespace Deadlock.Animation;


using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
using AnimationGraph = Deadlock.Animation.ModelAnimation2.AnimationGraph;
[KV3Transfer] public partial class ContactAudioActionVData { }

/// <summary>The sounds a contact point plays.</summary>
[KV3Transfer]
public partial class ContactAudioInfo
{
    /// <summary>The audio action ID.</summary>
    public StringToken AudioActionID { get; }
    /// <summary>The audio type ID.</summary>
    public StringToken AudioTypeID { get; }
    /// <summary>The sound event to play instead of the default.</summary>
    public StringToken SoundeventOverrideID { get; }
}

[KV3Transfer] public partial class ContactAudioTypeVData { }
