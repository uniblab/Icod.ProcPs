// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.Uptime;

using System.Globalization;
using System.Text;
using Icod.ProcPs.Shared;

/// <summary>Implements procps-ng 4.0.6 <c>uptime</c>.</summary>
public static class Command {
	private static readonly Encoding Utf8 = new UTF8Encoding( false );
	private const string VersionText = "uptime from procps-ng 4.0.6";
	private const string HelpText = """

Usage:
 uptime [options]

Options:
 -c, --container show container uptime
 -p, --pretty   show uptime in pretty format
 -r, --raw      show uptime values in raw format
 -s, --since    system up since

 -h, --help     display this help and exit
 -V, --version  output version information and exit

For more details see uptime(1).
""" + "\n";
	/// <summary>Runs <c>uptime</c> synchronously.</summary>
	public static int Run( string[] args, TextWriter? stdout = null, TextWriter? stderr = null ) {
		ArgumentNullException.ThrowIfNull( args );
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		var status = RunAsync( args, output, error ).GetAwaiter().GetResult();
		( stdout ?? Console.Out ).Write( Utf8.GetString( output.ToArray() ) );
		( stderr ?? Console.Error ).Write( Utf8.GetString( error.ToArray() ) );
		return status;
	}
	/// <summary>Runs procps-ng <c>uptime</c> asynchronously.</summary>
	public static async Task<int> RunAsync(
		string[] args,
		Stream? stdout = null,
		Stream? stderr = null,
		IProcSystemMetricsProvider? metricsProvider = null,
		TimeProvider? timeProvider = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		var metrics = metricsProvider ?? SystemProcSystemMetricsProvider.Instance;
		var clock = timeProvider ?? TimeProvider.System;
		var parsed = ParseArguments( args );
		if ( null != parsed.Error ) {
			if ( 0 < parsed.Error.Length ) await WriteDiagnosticAsync( stderr, parsed.Error, cancellationToken ).ConfigureAwait( false );
			await WriteErrorAsync( stderr, NormalizeLineEndings( HelpText ), cancellationToken ).ConfigureAwait( false );
			return 1;
		}
		if ( UptimeAction.Help == parsed.Action ) {
			await WriteAsync( stdout, NormalizeLineEndings( HelpText ), cancellationToken ).ConfigureAwait( false );
			return 0;
		}
		if ( UptimeAction.Version == parsed.Action ) {
			await WriteLineAsync( stdout, VersionText, cancellationToken ).ConfigureAwait( false );
			return 0;
		}
		try {
			if ( UptimeAction.Raw == parsed.Action ) return await PrintRawAsync( stdout, stderr, metrics, clock, cancellationToken ).ConfigureAwait( false );
			if ( UptimeAction.Since == parsed.Action ) return await PrintSinceAsync( stdout, stderr, metrics, clock, parsed.ContainerMode, cancellationToken ).ConfigureAwait( false );
			var uptime = await metrics.GetUptimeAsync( parsed.ContainerMode, cancellationToken ).ConfigureAwait( false );
			if ( !uptime.HasValue ) return await ReportUptimeFailureAsync( stderr, parsed.ContainerMode, uptime, cancellationToken ).ConfigureAwait( false );
			if ( parsed.Pretty ) {
				await WriteLineAsync( stdout, FormatPretty( uptime.Value.Uptime.TotalSeconds ), cancellationToken ).ConfigureAwait( false );
				return 0;
			}
			var snapshot = await metrics.GetSnapshotAsync( cancellationToken ).ConfigureAwait( false );
			var load = ResolveLoadAverages( snapshot );
			if ( !load.HasValue ) {
				await WriteDiagnosticAsync( stderr, "uptime: Cannot get load average", cancellationToken ).ConfigureAwait( false );
				return 1;
			}
			await WriteLineAsync( stdout, FormatStandard( clock.GetLocalNow(), uptime.Value.Uptime.TotalSeconds, snapshot.UserSessions, load.Value ), cancellationToken ).ConfigureAwait( false );
			return 0;
		} catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested ) { return 130; }
	}
	private static async Task<int> PrintRawAsync( Stream? stdout, Stream? stderr, IProcSystemMetricsProvider metrics, TimeProvider clock, CancellationToken cancellationToken ) {
		var snapshot = await metrics.GetSnapshotAsync( cancellationToken ).ConfigureAwait( false );
		if ( !snapshot.Uptime.HasValue ) { await WriteDiagnosticAsync( stderr, "uptime: procps_uptime_secs", cancellationToken ).ConfigureAwait( false ); return 1; }
		if ( !snapshot.UserSessions.HasValue ) { await WriteDiagnosticAsync( stderr, "uptime: procps_users", cancellationToken ).ConfigureAwait( false ); return 1; }
		var loadObservation = ResolveLoadAverages( snapshot );
		if ( !loadObservation.HasValue ) { await WriteDiagnosticAsync( stderr, "uptime: procps_loadavg", cancellationToken ).ConfigureAwait( false ); return 1; }
		var load = loadObservation.Value;
		var text = string.Format(
			CultureInfo.InvariantCulture,
			"{0} {1:F6} {2} {3:F2} {4:F2} {5:F2}",
			clock.GetUtcNow().ToUnixTimeSeconds(), snapshot.Uptime.Value.Uptime.TotalSeconds, snapshot.UserSessions.Value.Count,
			load.OneMinute, load.FiveMinutes, load.FifteenMinutes
		);
		await WriteLineAsync( stdout, text, cancellationToken ).ConfigureAwait( false );
		return 0;
	}
	private static async Task<int> PrintSinceAsync( Stream? stdout, Stream? stderr, IProcSystemMetricsProvider metrics, TimeProvider clock, bool containerMode, CancellationToken cancellationToken ) {
		var uptime = await metrics.GetUptimeAsync( containerMode, cancellationToken ).ConfigureAwait( false );
		if ( !uptime.HasValue ) return await ReportUptimeFailureAsync( stderr, containerMode, uptime, cancellationToken ).ConfigureAwait( false );
		var sinceUtc = clock.GetUtcNow() - uptime.Value.Uptime;
		var since = TimeZoneInfo.ConvertTime( sinceUtc, clock.LocalTimeZone );
		await WriteLineAsync( stdout, since.ToString( "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture ), cancellationToken ).ConfigureAwait( false );
		return 0;
	}
	private static async Task<int> ReportUptimeFailureAsync( Stream? stderr, bool containerMode, ProcObservedValue<ProcUptimeInfo> observation, CancellationToken cancellationToken ) {
		var text = containerMode ? "uptime: Cannot get container uptime" : "uptime: Cannot get system uptime";
		if ( !string.IsNullOrWhiteSpace( observation.Diagnostic ) ) text = string.Concat( text, ": ", observation.Diagnostic );
		await WriteDiagnosticAsync( stderr, text, cancellationToken ).ConfigureAwait( false );
		return 1;
	}
	/// <summary>Formats the procps-ng pretty uptime component.</summary>
	public static string FormatPretty( double uptimeSeconds ) => string.Concat( "up ", FormatUptimeOnly( uptimeSeconds, pretty: true ) );
	/// <summary>Formats the procps-ng standard uptime display using Linux-specific load details.</summary>
	public static string FormatStandard( DateTimeOffset now, double uptimeSeconds, ProcObservedValue<ProcUserSessionInfo> users, ProcLoadAverage load )
		=> FormatStandard( now, uptimeSeconds, users, new ProcLoadAverages( load.OneMinute, load.FiveMinutes, load.FifteenMinutes ) );
	/// <summary>Formats the procps-ng standard uptime display using cross-platform load averages.</summary>
	public static string FormatStandard( DateTimeOffset now, double uptimeSeconds, ProcObservedValue<ProcUserSessionInfo> users, ProcLoadAverages load ) {
		var userText = users.HasValue
			? string.Format( CultureInfo.InvariantCulture, ", {0,2} {1},  ", users.Value.Count, 1 == users.Value.Count ? "user" : "users" )
			: ", ? users,  ";
		return string.Format(
			CultureInfo.InvariantCulture,
			" {0:HH:mm:ss} up {1}{2}load average: {3:F2}, {4:F2}, {5:F2}",
			now, FormatUptimeOnly( uptimeSeconds, pretty: false ), userText, load.OneMinute, load.FiveMinutes, load.FifteenMinutes
		);
	}
	private static ProcObservedValue<ProcLoadAverages> ResolveLoadAverages( ProcSystemSnapshot snapshot ) {
		if ( snapshot.LoadAverages.HasValue ) return snapshot.LoadAverages;
		if ( snapshot.LoadAverage.HasValue ) {
			var load = snapshot.LoadAverage.Value;
			return ProcObservedValue<ProcLoadAverages>.Available(
				new ProcLoadAverages( load.OneMinute, load.FiveMinutes, load.FifteenMinutes ),
				snapshot.LoadAverage.Source,
				snapshot.LoadAverage.Fidelity
			);
		}
		return ProcObservedValue<ProcLoadAverages>.Missing( snapshot.LoadAverage.Availability, snapshot.LoadAverage.Diagnostic ?? snapshot.LoadAverages.Diagnostic );
	}
	private static string FormatUptimeOnly( double uptimeSeconds, bool pretty ) {
		const int decade = 60 * 60 * 24 * 365 * 10;
		const int year = 60 * 60 * 24 * 365;
		const int week = 60 * 60 * 24 * 7;
		const int day = 60 * 60 * 24;
		var seconds = Math.Max( 0d, uptimeSeconds );
		var decades = 0; var years = 0; var weeks = 0; var days = 0; var hours = 0; var minutes = 0;
		if ( pretty && seconds > decade ) { decades = (int)seconds / decade; seconds -= decades * decade; }
		if ( pretty && seconds > year ) { years = (int)seconds / year; seconds -= years * year; }
		if ( pretty && seconds > week ) { weeks = (int)seconds / week; seconds -= weeks * week; }
		if ( seconds > day ) { days = (int)seconds / day; seconds -= days * day; }
		if ( seconds > 3600 ) { hours = (int)seconds / 3600; seconds -= hours * 3600; }
		if ( seconds > 60 ) { minutes = (int)seconds / 60; seconds -= minutes * 60; }
		var pieces = new List<string>();
		if ( pretty ) {
			if ( 0 != decades ) pieces.Add( string.Format( CultureInfo.InvariantCulture, "{0} {1}", decades, decades > 1 ? "decades" : "decade" ) );
			if ( 0 != years ) pieces.Add( string.Format( CultureInfo.InvariantCulture, "{0} {1}", years, years > 1 ? "years" : "year" ) );
			if ( 0 != weeks ) pieces.Add( string.Format( CultureInfo.InvariantCulture, "{0} {1}", weeks, weeks > 1 ? "weeks" : "week" ) );
			if ( 0 != days ) pieces.Add( string.Format( CultureInfo.InvariantCulture, "{0} {1}", days, 1 == days ? "day" : "days" ) );
			if ( 0 != hours ) pieces.Add( string.Format( CultureInfo.InvariantCulture, "{0} {1}", hours, hours > 1 ? "hours" : "hour" ) );
			if ( 0 != minutes || seconds <= 60 ) pieces.Add( string.Format( CultureInfo.InvariantCulture, "{0} {1}", minutes, minutes > 1 ? "minutes" : "minute" ) );
			return string.Join( ", ", pieces );
		}
		if ( 0 != days ) pieces.Add( string.Format( CultureInfo.InvariantCulture, "{0} {1}", days, 1 == days ? "day" : "days" ) );
		if ( 0 != hours ) pieces.Add( string.Format( CultureInfo.InvariantCulture, "{0,2}:{1:00}", hours, minutes ) );
		else pieces.Add( string.Format( CultureInfo.InvariantCulture, "{0} min", minutes ) );
		return string.Join( ", ", pieces );
	}
	private static UptimeArguments ParseArguments( string[] args ) {
		var container = null != Environment.GetEnvironmentVariable( "PROCPS_CONTAINER" );
		var pretty = false;
		var operandSeen = false;
		var endOfOptions = false;
		for ( var index = 0; index < args.Length; index++ ) {
			var token = args[ index ];
			if ( endOfOptions ) { operandSeen = true; continue; }
			if ( "--" == token ) { endOfOptions = true; continue; }
			if ( !token.StartsWith( '-' ) || "-" == token ) { operandSeen = true; continue; }
			if ( token.StartsWith( "--", StringComparison.Ordinal ) ) {
				var equal = token.IndexOf( '=' );
				var name = 0 > equal ? token[ 2.. ] : token[ 2..equal ];
				var option = ResolveLongOption( name );
				if ( null == option ) return new( container, pretty, UptimeAction.Standard, $"uptime: unrecognized option '{token}'" );
				if ( 0 <= equal ) return new( container, pretty, UptimeAction.Standard, $"uptime: option '--{option}' doesn't allow an argument" );
				switch ( option ) { case "container": container = true; break; case "pretty": pretty = true; break; case "help": return new( container, pretty, UptimeAction.Help, null ); case "raw": return new( container, pretty, UptimeAction.Raw, null ); case "since": return new( container, pretty, UptimeAction.Since, null ); case "version": return new( container, pretty, UptimeAction.Version, null ); }
				continue;
			}
			for ( var position = 1; position < token.Length; position++ ) {
				switch ( token[ position ] ) { case 'c': container = true; break; case 'p': pretty = true; break; case 'h': return new( container, pretty, UptimeAction.Help, null ); case 'r': return new( container, pretty, UptimeAction.Raw, null ); case 's': return new( container, pretty, UptimeAction.Since, null ); case 'V': return new( container, pretty, UptimeAction.Version, null ); default: return new( container, pretty, UptimeAction.Standard, $"uptime: invalid option -- '{token[ position ]}'" ); }
			}
		}
		return operandSeen ? Failure() : new( container, pretty, UptimeAction.Standard, null );
		UptimeArguments Failure() => new( container, pretty, UptimeAction.Standard, string.Empty );
	}
	private static string? ResolveLongOption( string name ) {
		string[] options = [ "container", "pretty", "help", "raw", "since", "version" ];
		var matches = options.Where( option => option.StartsWith( name, StringComparison.Ordinal ) ).ToArray();
		return 1 == matches.Length ? matches[ 0 ] : null;
	}
	private static async Task WriteAsync( Stream? stream, string text, CancellationToken cancellationToken ) { if ( null == stream ) { await Console.Out.WriteAsync( text.AsMemory(), cancellationToken ).ConfigureAwait( false ); return; } var bytes = Utf8.GetBytes( text ); await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false ); await stream.FlushAsync( cancellationToken ).ConfigureAwait( false ); }
	private static async Task WriteErrorAsync( Stream? stream, string text, CancellationToken cancellationToken ) { if ( null == stream ) { await Console.Error.WriteAsync( text.AsMemory(), cancellationToken ).ConfigureAwait( false ); return; } var bytes = Utf8.GetBytes( text ); await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false ); await stream.FlushAsync( cancellationToken ).ConfigureAwait( false ); }
	private static Task WriteLineAsync( Stream? stream, string text, CancellationToken cancellationToken ) => WriteAsync( stream, string.Concat( text, Environment.NewLine ), cancellationToken );
	private static async Task WriteDiagnosticAsync( Stream? stream, string text, CancellationToken cancellationToken ) { if ( null == stream ) { await Console.Error.WriteLineAsync( text.AsMemory(), cancellationToken ).ConfigureAwait( false ); return; } var bytes = Utf8.GetBytes( string.Concat( text, Environment.NewLine ) ); await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false ); await stream.FlushAsync( cancellationToken ).ConfigureAwait( false ); }
	private static string NormalizeLineEndings( string value ) {
		var normalized = value
			.Replace( "\r\n", "\n", StringComparison.Ordinal )
			.Replace( "\r", "\n", StringComparison.Ordinal );

		return "\n" == Environment.NewLine
			? normalized
			: normalized.Replace( "\n", Environment.NewLine, StringComparison.Ordinal );
	}
	private enum UptimeAction { Standard, Help, Version, Raw, Since }
	private sealed record UptimeArguments( bool ContainerMode, bool Pretty, UptimeAction Action, string? Error );
}
