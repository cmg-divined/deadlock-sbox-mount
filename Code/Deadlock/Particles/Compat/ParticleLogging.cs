using System;
using System.Text;

namespace Deadlock.Particles;

/// <summary>
/// Severity of a particle message.
/// </summary>
public enum ParticleLogLevel
{
	Trace,
	Debug,
	Information,
	Warning,
	Error,
}

/// <summary>
/// Where particle code reports what it can't do. Stands in for Microsoft.Extensions.Logging's ILogger, which the
/// ported ValveResourceFormat code was written against.
/// </summary>
public interface ILogger
{
	void Log( ParticleLogLevel level, string message );
}

/// <summary>
/// The structured logging calls the ported code makes, with their <c>{Name}</c> placeholders filled in order.
/// </summary>
public static class LoggerExtensions
{
	public static void LogTrace( this ILogger logger, string template, params object[] args ) => Write( logger, ParticleLogLevel.Trace, template, args );
	public static void LogDebug( this ILogger logger, string template, params object[] args ) => Write( logger, ParticleLogLevel.Debug, template, args );
	public static void LogInformation( this ILogger logger, string template, params object[] args ) => Write( logger, ParticleLogLevel.Information, template, args );
	public static void LogWarning( this ILogger logger, string template, params object[] args ) => Write( logger, ParticleLogLevel.Warning, template, args );
	public static void LogError( this ILogger logger, string template, params object[] args ) => Write( logger, ParticleLogLevel.Error, template, args );

	private static readonly HashSet<string> LoggedWarnings = [];

	/// <summary>
	/// Logs a warning the first time it occurs with these arguments, for warnings raised from per-frame code (an
	/// unsupported class, an unknown name) that would otherwise repeat forever.
	/// </summary>
	public static void LogUniqueWarning( this ILogger logger, string template, params object[] args )
	{
		if ( FirstOccurrence( template, args ) )
			Write( logger, ParticleLogLevel.Warning, template, args );
	}

	/// <summary>
	/// Logs a warning the first time it occurs for <paramref name="identity"/>, ignoring the rest of the message's
	/// arguments, such as which file hit it.
	/// </summary>
	public static void LogUniqueWarningFor( this ILogger logger, object[] identity, string template, params object[] args )
	{
		if ( FirstOccurrence( template, identity ) )
			Write( logger, ParticleLogLevel.Warning, template, args );
	}

	private static bool FirstOccurrence( string template, object[] identity )
	{
		var key = template + "\u0001" + string.Join( "\u0001", identity ?? [] );

		lock ( LoggedWarnings )
			return LoggedWarnings.Add( key );
	}

	private static void Write( ILogger logger, ParticleLogLevel level, string template, object[] args )
	{
		if ( logger is null or NullLogger )
			return;

		logger.Log( level, Format( template, args ) );
	}

	/// <summary>
	/// Replaces each <c>{...}</c> placeholder with the next argument.
	/// </summary>
	public static string Format( string template, object[] args )
	{
		if ( args is null || args.Length == 0 )
			return template;

		var builder = new StringBuilder( template.Length + 32 );
		var next = 0;

		for ( var i = 0; i < template.Length; i++ )
		{
			var c = template[i];
			var close = c == '{' ? template.IndexOf( '}', i + 1 ) : -1;

			if ( close < 0 )
			{
				builder.Append( c );
				continue;
			}

			builder.Append( next < args.Length ? args[next++] : template.Substring( i, close - i + 1 ) );
			i = close;
		}

		return builder.ToString();
	}
}

/// <summary>
/// A logger that drops everything.
/// </summary>
public sealed class NullLogger : ILogger
{
	public static NullLogger Instance { get; } = new();

	public void Log( ParticleLogLevel level, string message ) { }
}
