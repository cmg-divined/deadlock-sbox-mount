using Editor;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// Picks a Deadlock hero for a string property marked <c>[Editor( "deadlock_hero" )]</c> (the Deadlock Hero and
/// Deadlock Map components), from the heroes the mounted game has, by the names the game shows.
/// </summary>
[CustomEditor( typeof( string ), NamedEditor = "deadlock_hero" )]
internal sealed class DeadlockHeroControlWidget : ControlWidget
{
	public override bool IsControlButton => true;

	public DeadlockHeroControlWidget( SerializedProperty property ) : base( property )
	{
		Cursor = CursorShape.Finger;
	}

	protected override void PaintControl()
	{
		// Painting doesn't read the heroes (opening the menu does), so before that this shows the VData name.
		var value = SerializedProperty.GetValue<string>( null );
		var title = string.IsNullOrEmpty( value ) ? "None" : DeadlockMount.Current?.CachedHeroes?.FirstOrDefault( x => x.Name == value )?.Title ?? value;

		var color = IsControlHovered ? Theme.Blue : Theme.TextControl;
		if ( IsControlDisabled )
			color = color.WithAlpha( 0.5f );

		var rect = LocalRect.Shrink( 8, 0 );
		Paint.SetPen( color );
		Paint.DrawText( rect, SerializedProperty.IsMultipleDifferentValues ? "Multiple Values" : title, TextFlag.LeftCenter );
		Paint.DrawIcon( rect, "Arrow_Drop_Down", 17, TextFlag.RightCenter );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( IsControlDisabled || !e.LeftMouseButton )
			return;

		var menu = new Editor.Menu( this );
		menu.AddOption( "None", "block", () => Choose( "" ) );

		foreach ( var hero in Heroes() )
			menu.AddOption( hero.Title, "person", () => Choose( hero.Name ) );

		if ( DeadlockMount.Current is null )
			menu.AddOption( "(mount Deadlock to list its heroes)", "warning" );

		menu.OpenAtCursor();
	}

	private void Choose( string hero )
	{
		SerializedProperty.SetValue( hero );
		SignalValuesChanged();
		Update();
	}

	private static IReadOnlyList<DeadlockHeroChoice> Heroes() => DeadlockMount.Current?.GetHeroes() ?? [];
}
