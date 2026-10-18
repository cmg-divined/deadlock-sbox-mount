using System;
using NMatrix = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

namespace Sandbox.Mounting.Deadlock;

/// <summary>
/// The colour correction and tint matrices Deadlock's shaders apply to albedo. They are computed from
/// material parameters on the CPU; a shader applies one as <c>dot( row, float4( rgb, 1 ) )</c> per channel.
/// Ported from ValveResourceFormat.
/// </summary>
internal static class ColorMatrices
{
	private static readonly NVector3 Luminance = NVector3.Normalize( new NVector3( 0.2126f, 0.7152f, 0.0722f ) );

	/// <summary>
	/// Rows of a matrix, red first, each as (x, y, z, w).
	/// </summary>
	public static float[][] Rows( NMatrix m ) =>
	[
		[m.M11, m.M12, m.M13, m.M14],
		[m.M21, m.M22, m.M23, m.M24],
		[m.M31, m.M32, m.M33, m.M34],
	];

	/// <summary>
	/// Contrast, saturation and brightness, with contrast pivoting around <paramref name="pivot"/>
	/// (the texture's average colour).
	/// </summary>
	public static NMatrix ColorCorrect( NVector3 contrastSaturationBrightness, NVector3 pivot )
	{
		var rotation = LuminanceRotation();

		var result = NMatrix.CreateTranslation( -pivot ) * NMatrix.CreateScale( contrastSaturationBrightness.X ) * NMatrix.CreateTranslation( pivot );
		result *= NMatrix.CreateScale( contrastSaturationBrightness.Z );
		result *= NMatrix.CreateScale( Luminance );
		result *= rotation;
		result *= NMatrix.CreateScale( contrastSaturationBrightness.Y, contrastSaturationBrightness.Y, 1f );
		result *= NMatrix.Transpose( rotation );
		result *= NMatrix.CreateScale( NVector3.One / Luminance );

		return NMatrix.Transpose( result );
	}

	/// <summary>
	/// A tint in one of the shader's modes: 0 multiplies, 1 preserves luminance, 2 is mod2x.
	/// </summary>
	/// <param name="linearTint">The tint colour, in linear space.</param>
	public static NMatrix Tint( NVector3 linearTint, float strength, int mode ) => mode switch
	{
		1 => LuminancePreservingTint( linearTint, strength ),
		2 => NMatrix.CreateScale( linearTint * 2f ),
		_ => NMatrix.CreateScale( linearTint ),
	};

	private static NMatrix LuminancePreservingTint( NVector3 rgb, float strength )
	{
		var max = MathF.Max( rgb.X, MathF.Max( rgb.Y, rgb.Z ) );
		var min = MathF.Min( rgb.X, MathF.Min( rgb.Y, rgb.Z ) );
		var saturation = max == 0f ? 0f : (max - min) / max;

		var rotation = LuminanceRotation();
		var luminanceScale = NMatrix.CreateScale( Luminance );
		var gray = NVector3.Transform( rgb, luminanceScale * rotation );

		var desaturate = 1f - saturation;
		var saturationFactor = 1f - saturation * saturation * strength;

		var result = luminanceScale * rotation;
		result *= NMatrix.CreateTranslation( -gray.X, -gray.Y, 0f );
		result *= NMatrix.CreateScale( desaturate, desaturate, saturationFactor );
		result *= NMatrix.CreateTranslation( gray.X, gray.Y, (1f - saturationFactor) * gray.Z );
		result *= NMatrix.Transpose( rotation );
		result *= NMatrix.CreateScale( NVector3.One / Luminance );

		return NMatrix.Transpose( result );
	}

	/// <summary>
	/// Rotates the luminance axis onto +Z, so saturation can be scaled in the XY plane.
	/// </summary>
	private static NMatrix LuminanceRotation()
	{
		var cross = NVector3.Cross( Luminance, NVector3.UnitZ );
		var angle = MathF.Atan2( cross.Length(), NVector3.Dot( Luminance, NVector3.UnitZ ) );
		return NMatrix.CreateFromAxisAngle( NVector3.Normalize( cross ), angle );
	}

	public static float SrgbToLinear( float value ) =>
		value <= 0.04045f ? value / 12.92f : MathF.Pow( (value + 0.055f) / 1.055f, 2.4f );
}
