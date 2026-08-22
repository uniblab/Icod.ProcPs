// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.W;

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Icod.ProcPs.Shared;

/// <summary>Implements the procps-ng 4.0.6 <c>w</c> logged-in user and activity report.</summary>
public static class Command {
	private const int Success = 0;
	private const int Failure = 1;
	private const int Cancelled = 130;
	private const int DefaultUserWidth = 8;
	private const int DefaultFromWidth = 16;
	private static readonly Encoding Utf8 = new UTF8Encoding( false );
	private const string VersionText = "w from procps-ng 4.0.6";
	private const string HelpText = """

Usage:
 w [options] [user]

Options:
 -c, --container     show container uptime
 -h, --no-header     do not print the header
 -u, --no-current    ignore the current process user when selecting WHAT
 -s, --short         use the short format
 -t, --terminal      include observable terminal sessions not present in login accounting
 -f, --from          toggle the FROM field
 -o, --old-style     use the old idle-time format
 -i, --ip-addr       display the accounting provider's numeric/address origin when available
 -p, --pids          show login/current process identifiers

     --help          display this help and exit
 -V, --version       output version information and exit

For more details see w(1).
""" + "\n";

	/// <summary>Runs <c>w</c> synchronously.</summary>
	/// <param name="args">Command-line arguments.</param>
	/// <param name="stdout">Optional output writer.</param>
	/// <param name="stderr">Optional diagnostic writer.</param>
	/// <returns>The process exit status.</returns>
	public static int Run( string[] args, TextWriter? stdout = null, TextWriter? stderr = null ) {
		ArgumentNullException.ThrowIfNull( args );
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		var status = RunAsync( args, output, error ).GetAwaiter().GetResult();
		( stdout ?? Console.Out ).Write( Utf8.GetString( output.ToArray() ) );
		( stderr ?? Console.Error ).Write( Utf8.GetString( error.ToArray() ) );
		return status;
	}

	/// <summary>Runs procps-ng <c>w</c> asynchronously with injectable ProcPs providers.</summary>
	public static async Task<int> RunAsync(
		string[] args,
		Stream? stdout = null,
		Stream? stderr = null,
		IProcLoginSessionProvider? sessionProvider = null,
		IProcProcessProvider? processProvider = null,
		IProcSystemMetricsProvider? metricsProvider = null,
		IProcAccountResolver? accountResolver = null,
		TimeProvider? timeProvider = null,
		Func<double>? cpuUnitsPerSecondProvider = null,
		Func<string, string?>? environmentVariableProvider = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		var sessions = sessionProvider ?? SystemProcLoginSessionProvider.Instance;
		var processes = processProvider ?? SystemProcProcessProvider.Instance;
		var metrics = metricsProvider ?? SystemProcSystemMetricsProvider.Instance;
		var accounts = accountResolver ?? SystemProcAccountResolver.Instance;
		var clock = timeProvider ?? TimeProvider.System;
		var cpuUnits = cpuUnitsPerSecondProvider ?? ResolveCpuUnitsPerSecond;
		var environment = environmentVariableProvider ?? Environment.GetEnvironmentVariable;
		var parsed = ParseArguments( args, environment );
		if ( null != parsed.Error ) {
			await WriteDiagnosticAsync( stderr, parsed.Error, cancellationToken ).ConfigureAwait( false );
			return Failure;
		}
		if ( parsed.ShowHelp ) {
			await WriteAsync( stdout, NormalizeLineEndings( HelpText ), cancellationToken ).ConfigureAwait( false );
			return Success;
		}
		if ( parsed.ShowVersion ) {
			await WriteLineAsync( stdout, VersionText, cancellationToken ).ConfigureAwait( false );
			return Success;
		}
		foreach ( var warning in parsed.Warnings ) {
			await WriteDiagnosticAsync( stderr, warning, cancellationToken ).ConfigureAwait( false );
		}
		try {
			var observedSessions = await sessions.GetSessionsAsync( cancellationToken ).ConfigureAwait( false );
			if ( !observedSessions.HasValue ) {
				var diagnostic = observedSessions.Diagnostic;
				if ( string.IsNullOrWhiteSpace( diagnostic ) ) {
					diagnostic = "the host does not expose a supported login-session accounting source";
				}
				await WriteDiagnosticAsync( stderr, $"w: cannot read login sessions: {diagnostic}", cancellationToken ).ConfigureAwait( false );
				return Failure;
			}
			var collection = await processes.GetProcessesAsync( cancellationToken ).ConfigureAwait( false );
			var loginSessions = observedSessions.Value.ToList();
			if ( parsed.TerminalMode ) {
				AppendUnaccountedTerminalSessions( loginSessions, collection.Processes );
			}
			if ( null != parsed.UserName ) {
				loginSessions = loginSessions
					.Where( session => string.Equals( session.UserName, parsed.UserName, StringComparison.Ordinal ) )
					.ToList();
			}
			var now = clock.GetUtcNow();
			if ( parsed.ShowHeader ) {
				var headerStatus = await WriteHeaderAsync(
					stdout,
					stderr,
					metrics,
					parsed.ContainerMode,
					loginSessions.Count,
					now,
					clock.LocalTimeZone,
					cancellationToken
				).ConfigureAwait( false );
				if ( Success != headerStatus ) {
					return headerStatus;
				}
				await WriteColumnHeaderAsync( stdout, parsed, cancellationToken ).ConfigureAwait( false );
			}
			var unitsPerSecond = cpuUnits();
			if ( 0d >= unitsPerSecond || double.IsNaN( unitsPerSecond ) || double.IsInfinity( unitsPerSecond ) ) {
				unitsPerSecond = TimeSpan.TicksPerSecond;
			}
			foreach ( var session in loginSessions ) {
				cancellationToken.ThrowIfCancellationRequested();
				if ( IsStaleAccountingSession( session, collection.Processes ) ) {
					continue;
				}
				var activity = BuildActivity( session, collection.Processes, accounts, unitsPerSecond, parsed.NoCurrentUserFilter );
				await WriteLineAsync(
					stdout,
					FormatRow( session, activity, parsed, now, clock.LocalTimeZone ),
					cancellationToken
				).ConfigureAwait( false );
			}
			return Success;
		} catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested ) {
			return Cancelled;
		} catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or InvalidOperationException ) {
			await WriteDiagnosticAsync( stderr, $"w: {exception.Message}", cancellationToken ).ConfigureAwait( false );
			return Failure;
		}
	}

	private static async Task<int> WriteHeaderAsync(
		Stream? stdout,
		Stream? stderr,
		IProcSystemMetricsProvider metrics,
		bool containerMode,
		int userCount,
		DateTimeOffset nowUtc,
		TimeZoneInfo localTimeZone,
		CancellationToken cancellationToken
	) {
		var uptime = await metrics.GetUptimeAsync( containerMode, cancellationToken ).ConfigureAwait( false );
		if ( !uptime.HasValue ) {
			var subject = "system";
			if ( containerMode ) {
				subject = "container";
			}
			var diagnostic = uptime.Diagnostic;
			if ( string.IsNullOrWhiteSpace( diagnostic ) ) {
				diagnostic = "uptime is unavailable";
			}
			await WriteDiagnosticAsync( stderr, $"w: cannot get {subject} uptime: {diagnostic}", cancellationToken ).ConfigureAwait( false );
			return Failure;
		}
		var snapshot = await metrics.GetSnapshotAsync( cancellationToken ).ConfigureAwait( false );
		var load = ResolveLoad( snapshot );
		var localNow = TimeZoneInfo.ConvertTime( nowUtc, localTimeZone );
		var userWord = "users";
		if ( 1 == userCount ) {
			userWord = "user";
		}
		var loadText = "n/a, n/a, n/a";
		if ( load.HasValue ) {
			loadText = string.Format(
				CultureInfo.InvariantCulture,
				"{0:F2}, {1:F2}, {2:F2}",
				load.Value.OneMinute,
				load.Value.FiveMinutes,
				load.Value.FifteenMinutes
			);
		}
		var line = string.Format(
			CultureInfo.InvariantCulture,
			" {0:HH:mm:ss} up {1},  {2} {3},  load average: {4}",
			localNow,
			FormatUptime( uptime.Value.Uptime ),
			userCount,
			userWord,
			loadText
		);
		await WriteLineAsync( stdout, line, cancellationToken ).ConfigureAwait( false );
		return Success;
	}

	private static ProcObservedValue<ProcLoadAverages> ResolveLoad( ProcSystemSnapshot snapshot ) {
		ArgumentNullException.ThrowIfNull( snapshot );
		if ( snapshot.LoadAverages.HasValue ) {
			return snapshot.LoadAverages;
		}
		if ( snapshot.LoadAverage.HasValue ) {
			var linux = snapshot.LoadAverage.Value;
			return ProcObservedValue<ProcLoadAverages>.Available(
				new ProcLoadAverages( linux.OneMinute, linux.FiveMinutes, linux.FifteenMinutes ),
				snapshot.LoadAverage.Source,
				snapshot.LoadAverage.Fidelity
			);
		}
		return ProcObservedValue<ProcLoadAverages>.Missing(
			ProcObservationAvailability.Unavailable,
			snapshot.LoadAverages.Diagnostic ?? snapshot.LoadAverage.Diagnostic
		);
	}

	private static async Task WriteColumnHeaderAsync( Stream? stdout, ParsedArguments options, CancellationToken cancellationToken ) {
		ArgumentNullException.ThrowIfNull( options );
		var builder = new StringBuilder();
		builder.Append( "USER".PadRight( options.UserWidth ) );
		builder.Append( ' ' );
		builder.Append( "TTY".PadRight( 12 ) );
		if ( options.ShowFrom ) {
			builder.Append( ' ' );
			builder.Append( "FROM".PadRight( options.FromWidth ) );
		}
		if ( options.ShortFormat ) {
			builder.Append( "   IDLE WHAT" );
		} else {
			builder.Append( " LOGIN@   IDLE   JCPU   PCPU WHAT" );
		}
		await WriteLineAsync( stdout, builder.ToString(), cancellationToken ).ConfigureAwait( false );
	}

	private static string FormatRow(
		ProcLoginSession session,
		SessionActivity activity,
		ParsedArguments options,
		DateTimeOffset nowUtc,
		TimeZoneInfo localTimeZone
	) {
		ArgumentNullException.ThrowIfNull( session );
		ArgumentNullException.ThrowIfNull( activity );
		ArgumentNullException.ThrowIfNull( options );
		ArgumentNullException.ThrowIfNull( localTimeZone );
		var builder = new StringBuilder();
		builder.Append( Fit( session.UserName, options.UserWidth ).PadRight( options.UserWidth ) );
		builder.Append( ' ' );
		builder.Append( Fit( NormalizeTerminal( session.TerminalName ), 12 ).PadRight( 12 ) );
		if ( options.ShowFrom ) {
			builder.Append( ' ' );
			builder.Append( Fit( FormatOrigin( session, options.IpAddress ), options.FromWidth ).PadRight( options.FromWidth ) );
		}
		if ( !options.ShortFormat ) {
			builder.Append( ' ' );
			builder.Append( FormatLoginTime( session.LoginTimeUtc, nowUtc, localTimeZone ).PadLeft( 7 ) );
		}
		builder.Append( ' ' );
		builder.Append( FormatIdle( session.LastActivityTimeUtc, nowUtc, options.OldStyle ).PadLeft( 7 ) );
		if ( !options.ShortFormat ) {
			builder.Append( ' ' );
			builder.Append( FormatCpuDuration( activity.JobCpu, options.OldStyle ).PadLeft( 7 ) );
			builder.Append( ' ' );
			builder.Append( FormatCpuDuration( activity.ProcessCpu, options.OldStyle ).PadLeft( 7 ) );
		}
		builder.Append( ' ' );
		var commandWidth = options.CommandWidth;
		if ( options.ShowPids ) {
			var pids = string.Concat( FormatPid( session.LoginProcessId ), "/", FormatPid( activity.CurrentProcessId ) );
			builder.Append( pids );
			builder.Append( ' ' );
			commandWidth = Math.Max( 0, commandWidth - pids.Length - 1 );
		}
		builder.Append( Fit( activity.What, commandWidth ) );
		return builder.ToString().TrimEnd();
	}

	private static SessionActivity BuildActivity(
		ProcLoginSession session,
		IReadOnlyList<ProcProcessSnapshot> processes,
		IProcAccountResolver accounts,
		double cpuUnitsPerSecond,
		bool noCurrentUserFilter
	) {
		ArgumentNullException.ThrowIfNull( session );
		ArgumentNullException.ThrowIfNull( processes );
		ArgumentNullException.ThrowIfNull( accounts );
		var associated = processes.Where( process => IsAssociated( session, process, processes ) ).ToList();
		var jobCpuUnits = 0UL;
		foreach ( var process in associated ) {
			jobCpuUnits = SaturatingAdd( jobCpuUnits, CpuUnits( process ) );
		}
		IEnumerable<ProcProcessSnapshot> currentCandidates = associated;
		var foregroundProcessGroupId = ResolveForegroundProcessGroupId( associated );
		if ( foregroundProcessGroupId.HasValue ) {
			currentCandidates = currentCandidates.Where( process =>
				process.ProcessGroupId.HasValue && foregroundProcessGroupId.Value == process.ProcessGroupId.Value
			);
		}
		if ( !noCurrentUserFilter && accounts.TryResolveUser( session.UserName, out var userId ) ) {
			currentCandidates = currentCandidates.Where( process => IsOwnedByUser( process, userId ) );
		}
		ProcProcessSnapshot? current = null;
		foreach ( var candidate in currentCandidates ) {
			if ( null == current || IsNewer( candidate, current ) ) {
				current = candidate;
			}
		}
		TimeSpan? processCpu = null;
		var what = "-";
		int? processId = null;
		if ( null != current ) {
			processCpu = TimeSpan.FromSeconds( CpuUnits( current ) / cpuUnitsPerSecond );
			what = FormatCommand( current );
			processId = current.ProcessId;
		}
		return new SessionActivity(
			TimeSpan.FromSeconds( jobCpuUnits / cpuUnitsPerSecond ),
			processCpu,
			processId,
			what
		);
	}

	private static int? ResolveForegroundProcessGroupId( IReadOnlyList<ProcProcessSnapshot> processes ) {
		ArgumentNullException.ThrowIfNull( processes );
		foreach ( var process in processes ) {
			if ( process.ForegroundProcessGroupId.HasValue && 0 < process.ForegroundProcessGroupId.Value ) {
				return process.ForegroundProcessGroupId.Value;
			}
		}
		return null;
	}

	private static bool IsStaleAccountingSession( ProcLoginSession session, IReadOnlyList<ProcProcessSnapshot> processes ) {
		ArgumentNullException.ThrowIfNull( session );
		ArgumentNullException.ThrowIfNull( processes );
		if ( !session.LoginProcessId.HasValue ) {
			return false;
		}
		return !processes.Any( process => session.LoginProcessId.Value == process.ProcessId );
	}

	private static bool IsOwnedByUser( ProcProcessSnapshot process, uint userId ) {
		ArgumentNullException.ThrowIfNull( process );
		if ( process.EffectiveUserId.HasValue && userId == process.EffectiveUserId.Value ) {
			return true;
		}
		if ( process.RealUserId.HasValue && userId == process.RealUserId.Value ) {
			return true;
		}
		return false;
	}

	private static bool IsAssociated(
		ProcLoginSession session,
		ProcProcessSnapshot process,
		IReadOnlyList<ProcProcessSnapshot> processes
	) {
		ArgumentNullException.ThrowIfNull( session );
		ArgumentNullException.ThrowIfNull( process );
		ArgumentNullException.ThrowIfNull( processes );
		if ( session.PlatformSessionId.HasValue && process.PlatformSessionId.HasValue ) {
			return session.PlatformSessionId.Value == process.PlatformSessionId.Value;
		}
		if ( !string.IsNullOrWhiteSpace( session.TerminalName ) && process.Terminal.HasValue ) {
			var processTerminal = process.Terminal.Value.Name;
			if ( !string.IsNullOrWhiteSpace( processTerminal )
				&& string.Equals( NormalizeTerminal( session.TerminalName ), NormalizeTerminal( processTerminal ), StringComparison.Ordinal ) ) {
				return true;
			}
		}
		if ( session.LoginProcessId.HasValue ) {
			var login = processes.FirstOrDefault( candidate => candidate.ProcessId == session.LoginProcessId.Value );
			if ( null != login && login.SessionId.HasValue && process.SessionId.HasValue ) {
				return login.SessionId.Value == process.SessionId.Value;
			}
		}
		return false;
	}

	private static void AppendUnaccountedTerminalSessions(
		ICollection<ProcLoginSession> sessions,
		IReadOnlyList<ProcProcessSnapshot> processes
	) {
		ArgumentNullException.ThrowIfNull( sessions );
		ArgumentNullException.ThrowIfNull( processes );
		var known = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var session in sessions ) {
			if ( !string.IsNullOrWhiteSpace( session.TerminalName ) ) {
				known.Add( NormalizeTerminal( session.TerminalName ) );
			}
		}
		foreach ( var group in processes
			.Where( process => IsTerminalModeCandidate( process ) )
			.GroupBy( process => NormalizeTerminal( process.Terminal.Value.Name ), StringComparer.Ordinal ) ) {
			if ( known.Contains( group.Key ) ) {
				continue;
			}
			var representative = group.OrderByDescending( process => StartSortKey( process ) ).ThenByDescending( process => process.ProcessId ).First();
			var user = "?";
			if ( representative.EffectiveUserId.HasValue ) {
				user = representative.EffectiveUserId.Value.ToString( CultureInfo.InvariantCulture );
			}
			sessions.Add( new ProcLoginSession( user, group.Key, null, null, null, null, null ) );
			known.Add( group.Key );
		}
	}

	private static bool IsTerminalModeCandidate( ProcProcessSnapshot process ) {
		ArgumentNullException.ThrowIfNull( process );
		if ( !process.Terminal.HasValue || string.IsNullOrWhiteSpace( process.Terminal.Value.Name ) ) {
			return false;
		}
		if ( process.ParentProcessId.HasValue
			&& ( 0 == process.ParentProcessId.Value || 1 == process.ParentProcessId.Value ) ) {
			return false;
		}
		return true;
	}

	private static bool IsNewer( ProcProcessSnapshot candidate, ProcProcessSnapshot current ) {
		ArgumentNullException.ThrowIfNull( candidate );
		ArgumentNullException.ThrowIfNull( current );
		var candidateStart = StartSortKey( candidate );
		var currentStart = StartSortKey( current );
		if ( candidateStart != currentStart ) {
			return candidateStart > currentStart;
		}
		return candidate.ProcessId > current.ProcessId;
	}

	private static ulong StartSortKey( ProcProcessSnapshot process ) {
		ArgumentNullException.ThrowIfNull( process );
		if ( process.StartTimeTicks.HasValue ) {
			return process.StartTimeTicks.Value;
		}
		return 0UL;
	}

	private static ulong CpuUnits( ProcProcessSnapshot process ) {
		ArgumentNullException.ThrowIfNull( process );
		var user = 0UL;
		var system = 0UL;
		if ( process.UserCpuTicks.HasValue ) {
			user = process.UserCpuTicks.Value;
		}
		if ( process.SystemCpuTicks.HasValue ) {
			system = process.SystemCpuTicks.Value;
		}
		return SaturatingAdd( user, system );
	}

	private static ulong SaturatingAdd( ulong left, ulong right ) {
		if ( ulong.MaxValue - left < right ) {
			return ulong.MaxValue;
		}
		return left + right;
	}

	private static string FormatCommand( ProcProcessSnapshot process ) {
		ArgumentNullException.ThrowIfNull( process );
		if ( process.CommandLineArguments.HasValue && 0 < process.CommandLineArguments.Value.Count ) {
			return string.Join( " ", process.CommandLineArguments.Value );
		}
		if ( process.CommandName.HasValue && !string.IsNullOrWhiteSpace( process.CommandName.Value ) ) {
			return process.CommandName.Value;
		}
		return "-";
	}

	private static string FormatOrigin( ProcLoginSession session, bool ipAddress ) {
		ArgumentNullException.ThrowIfNull( session );
		if ( ipAddress && !string.IsNullOrWhiteSpace( session.RemoteAddress ) ) {
			return session.RemoteAddress;
		}
		if ( string.IsNullOrWhiteSpace( session.RemoteHost ) ) {
			return "-";
		}
		return session.RemoteHost;
	}

	private static string FormatLoginTime( DateTimeOffset? loginTimeUtc, DateTimeOffset nowUtc, TimeZoneInfo localTimeZone ) {
		ArgumentNullException.ThrowIfNull( localTimeZone );
		if ( !loginTimeUtc.HasValue ) {
			return "?";
		}
		var local = TimeZoneInfo.ConvertTime( loginTimeUtc.Value, localTimeZone );
		var nowLocal = TimeZoneInfo.ConvertTime( nowUtc, localTimeZone );
		var age = nowUtc - loginTimeUtc.Value;
		if ( TimeSpan.Zero > age ) {
			age = TimeSpan.Zero;
		}
		if ( TimeSpan.FromHours( 12 ) < age && local.Date != nowLocal.Date ) {
			if ( TimeSpan.FromDays( 6 ) < age ) {
				return local.ToString( "ddMMMyy", CultureInfo.InvariantCulture );
			}
			return local.ToString( "dddHH", CultureInfo.InvariantCulture );
		}
		return local.ToString( "HH:mm", CultureInfo.InvariantCulture );
	}

	private static string FormatIdle( DateTimeOffset? lastActivityUtc, DateTimeOffset nowUtc, bool oldStyle ) {
		if ( !lastActivityUtc.HasValue ) {
			return "?";
		}
		var idle = nowUtc - lastActivityUtc.Value;
		if ( TimeSpan.Zero > idle ) {
			idle = TimeSpan.Zero;
		}
		return FormatInterval( idle, oldStyle );
	}

	private static string FormatCpuDuration( TimeSpan duration, bool oldStyle ) {
		if ( TimeSpan.Zero > duration ) {
			duration = TimeSpan.Zero;
		}
		return FormatInterval( duration, oldStyle );
	}

	private static string FormatCpuDuration( TimeSpan? duration, bool oldStyle ) {
		if ( !duration.HasValue ) {
			return "?";
		}
		return FormatCpuDuration( duration.Value, oldStyle );
	}

	private static string FormatInterval( TimeSpan duration, bool oldStyle ) {
		if ( TimeSpan.FromHours( 48 ) <= duration ) {
			return string.Format( CultureInfo.InvariantCulture, "{0}days", (int)duration.TotalDays );
		}
		if ( TimeSpan.FromHours( 1 ) <= duration ) {
			var suffix = "m";
			if ( oldStyle ) {
				suffix = string.Empty;
			}
			return string.Format( CultureInfo.InvariantCulture, "{0}:{1:00}{2}", (int)duration.TotalHours, duration.Minutes, suffix );
		}
		if ( TimeSpan.FromMinutes( 1 ) < duration ) {
			if ( oldStyle ) {
				return string.Format( CultureInfo.InvariantCulture, "{0}:{1:00}m", (int)duration.TotalMinutes, duration.Seconds );
			}
			return string.Format( CultureInfo.InvariantCulture, "{0}:{1:00}", (int)duration.TotalMinutes, duration.Seconds );
		}
		if ( oldStyle ) {
			return string.Empty;
		}
		var hundredths = duration.Milliseconds / 10;
		return string.Format( CultureInfo.InvariantCulture, "{0}.{1:00}s", duration.Seconds, hundredths );
	}

	private static string FormatUptime( TimeSpan uptime ) {
		if ( TimeSpan.Zero > uptime ) {
			uptime = TimeSpan.Zero;
		}
		var pieces = new List<string>();
		if ( 0 < uptime.Days ) {
			var word = "days";
			if ( 1 == uptime.Days ) {
				word = "day";
			}
			pieces.Add( string.Format( CultureInfo.InvariantCulture, "{0} {1}", uptime.Days, word ) );
		}
		if ( 0 < uptime.Hours ) {
			pieces.Add( string.Format( CultureInfo.InvariantCulture, "{0,2}:{1:00}", uptime.Hours, uptime.Minutes ) );
		} else {
			pieces.Add( string.Format( CultureInfo.InvariantCulture, "{0} min", uptime.Minutes ) );
		}
		return string.Join( ", ", pieces );
	}

	private static string FormatPid( int? processId ) {
		if ( processId.HasValue ) {
			return processId.Value.ToString( CultureInfo.InvariantCulture );
		}
		return "-";
	}

	private static string Fit( string value, int width ) {
		ArgumentNullException.ThrowIfNull( value );
		if ( 0 >= width ) {
			return string.Empty;
		}
		if ( width >= value.Length ) {
			return value;
		}
		return value[ ..width ];
	}

	private static string NormalizeTerminal( string? terminal ) {
		if ( string.IsNullOrWhiteSpace( terminal ) ) {
			return "-";
		}
		var result = terminal.Trim();
		if ( result.StartsWith( "/dev/", StringComparison.Ordinal ) ) {
			result = result[ 5.. ];
		}
		return result;
	}

	private static ParsedArguments ParseArguments( string[] args, Func<string, string?> environment ) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( environment );
		var options = new ParsedArguments {
			ContainerMode = null != environment( "PROCPS_CONTAINER" )
		};
		options.UserWidth = ReadWidth(
			environment( "PROCPS_USERLEN" ),
			DefaultUserWidth,
			8,
			32,
			$"w: User length environment PROCPS_USERLEN must be between 8 and 32, ignoring.",
			options
		);
		options.FromWidth = ReadWidth(
			environment( "PROCPS_FROMLEN" ),
			DefaultFromWidth,
			8,
			256,
			$"w: from length environment PROCPS_FROMLEN must be between 8 and 256, ignoring",
			options
		);
		var endOfOptions = false;
		for ( var index = 0; index < args.Length; index++ ) {
			var token = args[ index ];
			if ( endOfOptions || !token.StartsWith( '-' ) || "-" == token ) {
				if ( null != options.UserName ) {
					options.Error = "w: too many arguments";
					return options;
				}
				options.UserName = token;
				continue;
			}
			if ( "--" == token ) {
				endOfOptions = true;
				continue;
			}
			if ( token.StartsWith( "--", StringComparison.Ordinal ) ) {
				if ( !ApplyLongOption( token, options ) ) {
					return options;
				}
				continue;
			}
			for ( var position = 1; position < token.Length; position++ ) {
				if ( !ApplyShortOption( token[ position ], options ) ) {
					return options;
				}
			}
		}
		options.CommandWidth = ResolveCommandWidth( environment( "COLUMNS" ), options );
		return options;
	}

	private static bool ApplyLongOption( string token, ParsedArguments options ) {
		ArgumentNullException.ThrowIfNull( token );
		ArgumentNullException.ThrowIfNull( options );
		if ( token.Contains( '=' ) ) {
			options.Error = $"w: option '{token}' doesn't allow an argument";
			return false;
		}
		var name = token[ 2.. ];
		switch ( name ) {
			case "container": options.ContainerMode = true; return true;
			case "no-header": options.ShowHeader = false; return true;
			case "no-current": options.NoCurrentUserFilter = true; return true;
			case "short": options.ShortFormat = true; return true;
			case "terminal": options.TerminalMode = true; return true;
			case "from": options.ShowFrom = !options.ShowFrom; return true;
			case "old-style": options.OldStyle = true; return true;
			case "ip-addr": options.IpAddress = true; options.ShowFrom = true; return true;
			case "pids": options.ShowPids = true; return true;
			case "help": options.ShowHelp = true; return true;
			case "version": options.ShowVersion = true; return true;
			default:
				options.Error = $"w: unrecognized option '{token}'";
				return false;
		}
	}

	private static bool ApplyShortOption( char option, ParsedArguments options ) {
		ArgumentNullException.ThrowIfNull( options );
		switch ( option ) {
			case 'c': options.ContainerMode = true; return true;
			case 'h': options.ShowHeader = false; return true;
			case 'u': options.NoCurrentUserFilter = true; return true;
			case 's': options.ShortFormat = true; return true;
			case 't': options.TerminalMode = true; return true;
			case 'f': options.ShowFrom = !options.ShowFrom; return true;
			case 'o': options.OldStyle = true; return true;
			case 'i': options.IpAddress = true; options.ShowFrom = true; return true;
			case 'p': options.ShowPids = true; return true;
			case 'V': options.ShowVersion = true; return true;
			default:
				options.Error = $"w: invalid option -- '{option}'";
				return false;
		}
	}

	private static int ReadWidth(
		string? value,
		int defaultValue,
		int minimum,
		int maximum,
		string warning,
		ParsedArguments options
	) {
		ArgumentNullException.ThrowIfNull( warning );
		ArgumentNullException.ThrowIfNull( options );
		if ( string.IsNullOrWhiteSpace( value ) ) {
			return defaultValue;
		}
		if ( !int.TryParse( value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed )
			|| minimum > parsed || maximum < parsed ) {
			options.Warnings.Add( warning );
			return defaultValue;
		}
		return parsed;
	}

	private static int ResolveCommandWidth( string? columnsText, ParsedArguments options ) {
		ArgumentNullException.ThrowIfNull( options );
		var columns = 512;
		if ( !string.IsNullOrWhiteSpace( columnsText )
			&& int.TryParse( columnsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedColumns ) ) {
			columns = Math.Clamp( parsedColumns, 7, 512 );
		}
		var used = 21 + options.UserWidth;
		if ( options.ShowFrom ) {
			used += options.FromWidth;
		}
		if ( !options.ShortFormat ) {
			used += 20;
		}
		return Math.Clamp( columns - used, 7, 512 );
	}

	private static double ResolveCpuUnitsPerSecond() {
		if ( OperatingSystem.IsLinux() ) {
			try {
				var ticks = Native.SysConf( Native.ClockTicksPerSecondLinux );
				if ( 0 < ticks ) {
					return ticks;
				}
			} catch ( DllNotFoundException ) {
			} catch ( EntryPointNotFoundException ) {
			}
		}
		if ( OperatingSystem.IsMacOS() ) {
			return 1_000_000_000d;
		}
		return TimeSpan.TicksPerSecond;
	}

	private static async Task WriteAsync( Stream? stream, string text, CancellationToken cancellationToken ) {
		if ( null == stream ) {
			await Console.Out.WriteAsync( text.AsMemory(), cancellationToken ).ConfigureAwait( false );
			return;
		}
		var bytes = Utf8.GetBytes( text );
		await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false );
		await stream.FlushAsync( cancellationToken ).ConfigureAwait( false );
	}

	private static async Task WriteDiagnosticAsync( Stream? stream, string text, CancellationToken cancellationToken ) {
		if ( null == stream ) {
			await Console.Error.WriteLineAsync( text.AsMemory(), cancellationToken ).ConfigureAwait( false );
			return;
		}
		var bytes = Utf8.GetBytes( string.Concat( text, Environment.NewLine ) );
		await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false );
		await stream.FlushAsync( cancellationToken ).ConfigureAwait( false );
	}

	private static Task WriteLineAsync( Stream? stream, string text, CancellationToken cancellationToken ) {
		return WriteAsync( stream, string.Concat( text, Environment.NewLine ), cancellationToken );
	}

	private static string NormalizeLineEndings( string value ) {
		ArgumentNullException.ThrowIfNull( value );
		var normalized = value
			.Replace( "\r\n", "\n", StringComparison.Ordinal )
			.Replace( "\r", "\n", StringComparison.Ordinal );
		if ( "\n" == Environment.NewLine ) {
			return normalized;
		}
		return normalized.Replace( "\n", Environment.NewLine, StringComparison.Ordinal );
	}

	private sealed class ParsedArguments {
		public bool ContainerMode { get; set; }
		public bool ShowHeader { get; set; } = true;
		public bool NoCurrentUserFilter { get; set; }
		public bool ShortFormat { get; set; }
		public bool TerminalMode { get; set; }
		public bool ShowFrom { get; set; } = true;
		public bool OldStyle { get; set; }
		public bool IpAddress { get; set; }
		public bool ShowPids { get; set; }
		public bool ShowHelp { get; set; }
		public bool ShowVersion { get; set; }
		public int UserWidth { get; set; } = DefaultUserWidth;
		public int FromWidth { get; set; } = DefaultFromWidth;
		public int CommandWidth { get; set; } = 512;
		public List<string> Warnings { get; } = [];
		public string? UserName { get; set; }
		public string? Error { get; set; }
	}

	private sealed class SessionActivity {
		public TimeSpan JobCpu { get; }
		public TimeSpan? ProcessCpu { get; }
		public int? CurrentProcessId { get; }
		public string What { get; }

		public SessionActivity( TimeSpan jobCpu, TimeSpan? processCpu, int? currentProcessId, string what ) {
			ArgumentNullException.ThrowIfNull( what );
			this.JobCpu = jobCpu;
			this.ProcessCpu = processCpu;
			this.CurrentProcessId = currentProcessId;
			this.What = what;
		}
	}

	private static class Native {
		public const int ClockTicksPerSecondLinux = 2;

		[DllImport( "libc", EntryPoint = "sysconf" )]
		public static extern long SysConf( int name );
	}
}
