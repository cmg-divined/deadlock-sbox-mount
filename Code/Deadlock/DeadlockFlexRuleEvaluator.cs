using System;

namespace Deadlock;

/// <summary>
/// Runs a <see cref="DeadlockFlexSet"/>'s rules, turning controller values into flex weights.
/// </summary>
public sealed class DeadlockFlexRuleEvaluator
{
	private const int Const = 1;
	private const int Fetch1 = 2;
	private const int Fetch2 = 3;
	private const int Add = 4;
	private const int Sub = 5;
	private const int Mul = 6;
	private const int Div = 7;
	private const int Neg = 8;
	private const int Max = 13;
	private const int Min = 14;
	private const int TwoWay0 = 15;
	private const int TwoWay1 = 16;
	private const int NWay = 17;
	private const int Combo = 18;
	private const int Dominate = 19;
	private const int DmeLowerEyelid = 20;
	private const int DmeUpperEyelid = 21;
	private const int Sqrt = 22;
	private const int RemapValClamped = 23;
	private const int Sin = 24;
	private const int Cos = 25;
	private const int Abs = 26;

	private readonly DeadlockFlexSet _set;
	private readonly float[] _stack = new float[64];
	private int _count;

	/// <summary>
	/// Flex weights from the last <see cref="Evaluate"/>, by flex index.
	/// </summary>
	public float[] Weights { get; }

	/// <summary>
	/// Whether a rule writes each flex. Flexes without a rule keep their weight from elsewhere.
	/// </summary>
	public bool[] Driven { get; }

	public DeadlockFlexRuleEvaluator( DeadlockFlexSet set )
	{
		_set = set;
		Weights = new float[set.Flexes.Count];
		Driven = new bool[set.Flexes.Count];

		foreach ( var rule in set.Rules )
		{
			if ( rule.Flex >= 0 && rule.Flex < Driven.Length )
				Driven[rule.Flex] = true;
		}
	}

	/// <summary>
	/// Runs every rule in order; later rules can read earlier flexes.
	/// </summary>
	/// <param name="controllers">Controller values, by the set's controller index.</param>
	public void Evaluate( ReadOnlySpan<float> controllers )
	{
		Array.Clear( Weights );

		foreach ( var rule in _set.Rules )
		{
			if ( rule.Flex < 0 || rule.Flex >= Weights.Length )
				continue;

			_count = 0;

			for ( var i = 0; i < rule.OpCodes.Count; i++ )
				Run( rule.OpCodes[i], i < rule.OpData.Count ? rule.OpData[i] : 0, controllers );

			var weight = _count > 0 ? _stack[_count - 1] : 0f;
			Weights[rule.Flex] = float.IsFinite( weight ) ? weight : 0f;
		}
	}

	private void Run( int opCode, int data, ReadOnlySpan<float> controllers )
	{
		switch ( opCode )
		{
			case Const: Push( BitConverter.Int32BitsToSingle( data ) ); break;
			case Fetch1: Push( Controller( controllers, data ) ); break;
			case Fetch2: Push( data >= 0 && data < Weights.Length ? Weights[data] : 0f ); break;
			case Add: Push( Pop() + Pop() ); break;
			case Sub: { var b = Pop(); Push( Pop() - b ); break; }
			case Mul: Push( Pop() * Pop() ); break;
			case Div:
			{
				// A divisor at or near zero gives zero rather than infinity.
				var divisor = Pop();
				var dividend = Pop();
				Push( MathF.Abs( divisor ) <= 0.0001f ? 0f : dividend / divisor );
				break;
			}
			case Neg: Push( -Pop() ); break;
			case Max: Push( MathF.Max( Pop(), Pop() ) ); break;
			case Min: Push( MathF.Min( Pop(), Pop() ) ); break;
			case TwoWay0: Push( Remap( Controller( controllers, data ), -1f, 0f, 1f, 0f, clamp: true ) ); break;
			case TwoWay1: Push( Remap( Controller( controllers, data ), 0f, 1f, 0f, 1f, clamp: true ) ); break;
			case NWay:
			{
				// Ramps up between t1 and t2, holds until t3, ramps down by t4, along another controller.
				var along = Controller( controllers, PopIndex() );
				var value = Controller( controllers, data );
				var t4 = Pop();
				var t3 = Pop();
				var t2 = Pop();
				var t1 = Pop();

				Push( along < t1 ? 0f
					: along < t2 ? Remap( along, t1, t2, 0f, value, clamp: false )
					: along < t3 ? value
					: along < t4 ? Remap( along, t3, t4, value, 0f, clamp: false )
					: 0f );
				break;
			}
			case Combo:
			{
				var product = 1f;
				for ( var i = 0; i < data; i++ )
					product *= Pop();

				Push( product );
				break;
			}
			case Dominate:
			{
				var dominators = 1f;
				for ( var i = 0; i < data; i++ )
					dominators *= Pop();

				Push( Pop() * (1f - dominators) );
				break;
			}
			case DmeLowerEyelid:
			case DmeUpperEyelid:
			{
				var closeLidV = Normalized( controllers, data, 0f, 1f );
				var closeLid = Normalized( controllers, PopIndex(), 0f, 1f );
				var blinkIndex = PopIndex();
				var eyeUpDownIndex = PopIndex();
				var blink = blinkIndex >= 0 ? Normalized( controllers, blinkIndex, 0f, 1f ) : 0f;
				var eyeUpDown = eyeUpDownIndex >= 0 ? Normalized( controllers, eyeUpDownIndex, -1f, 1f ) : 0f;
				var closed = MathF.Max( blink, closeLid );

				Push( opCode == DmeLowerEyelid
					? closed * (1f - closeLidV) * (eyeUpDown > 0f ? 1f - eyeUpDown : 1f)
					: closed * closeLidV * (eyeUpDown < 0f ? 1f + eyeUpDown : 1f) );
				break;
			}
			case Sqrt: Push( MathF.Sqrt( MathF.Max( Pop(), 0f ) ) ); break;
			case RemapValClamped:
			{
				var d = Pop();
				var c = Pop();
				var b = Pop();
				var a = Pop();
				Push( Remap( Pop(), a, b, c, d, clamp: true ) );
				break;
			}
			case Sin: Push( MathF.Sin( Pop() ) ); break;
			case Cos: Push( MathF.Cos( Pop() ) ); break;
			case Abs: Push( MathF.Abs( Pop() ) ); break;

			// Parenthesis and comma tokens never reach a compiled rule.
			default: break;
		}
	}

	private void Push( float value )
	{
		if ( _count < _stack.Length )
			_stack[_count++] = value;
	}

	private float Pop() => _count > 0 ? _stack[--_count] : 0f;

	/// <summary>
	/// Controller indices are pushed as constants holding the index's bits.
	/// </summary>
	private int PopIndex() => BitConverter.SingleToInt32Bits( Pop() );

	private static float Controller( ReadOnlySpan<float> controllers, int index )
	{
		return index >= 0 && index < controllers.Length ? controllers[index] : 0f;
	}

	/// <summary>
	/// A controller's value remapped from its own range onto [min, max].
	/// </summary>
	private float Normalized( ReadOnlySpan<float> controllers, int index, float min, float max )
	{
		var value = Controller( controllers, index );
		if ( index < 0 || index >= _set.Controllers.Count )
			return value;

		var controller = _set.Controllers[index];
		return Remap( value, controller.Min, controller.Max, min, max, clamp: true );
	}

	private static float Remap( float value, float inMin, float inMax, float outMin, float outMax, bool clamp )
	{
		// A degenerate input range is a threshold.
		if ( inMin == inMax )
			return value >= inMax ? outMax : outMin;

		var t = (value - inMin) / (inMax - inMin);
		if ( clamp )
			t = Math.Clamp( t, 0f, 1f );

		return outMin + (outMax - outMin) * t;
	}
}
