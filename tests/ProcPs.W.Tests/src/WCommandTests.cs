namespace Icod.ProcPs.W.Tests;

using System.Text;
using Icod.CommandFramework.Host;
using Icod.CommandFramework.Processes;
using Icod.ProcPs.Shared;
using Xunit;
using Tool = Icod.ProcPs.W.Command;

/// <summary>Exercises the Batch 63 procps-ng 4.0.6 <c>w</c> compatibility engine.</summary>
public sealed class WCommandTests {
	private static readonly DateTimeOffset Now = new( 2026, 8, 8, 20, 0, 0, TimeSpan.Zero );

	/// <summary>Verifies the default report includes heading, session identity, activity, and JCPU/PCPU data.</summary>
	[Fact]
	public async Task DefaultReportCombinesSessionsProcessesAndSystemMetrics() {
		using var output = new MemoryStream();
		var status = await RunAsync( [], output );
		Assert.Equal( 0, status );
		var text = Text( output );
		Assert.Contains( "20:00:00 up 2 days,  3:04", text, StringComparison.Ordinal );
		Assert.Contains( "1 user", text, StringComparison.Ordinal );
		Assert.Contains( "load average: 0.25, 0.50, 0.75", text, StringComparison.Ordinal );
		Assert.Contains( "USER", text, StringComparison.Ordinal );
		Assert.Contains( "alice", text, StringComparison.Ordinal );
		Assert.Contains( "pts/1", text, StringComparison.Ordinal );
		Assert.Contains( "host.example", text, StringComparison.Ordinal );
		Assert.Contains( "4.50s", text, StringComparison.Ordinal );
		Assert.Contains( "3.00s", text, StringComparison.Ordinal );
		Assert.Contains( "vim notes.txt", text, StringComparison.Ordinal );
	}

	/// <summary>Verifies short mode and header suppression omit long-only fields and the uptime heading.</summary>
	[Fact]
	public async Task ShortModeAndNoHeaderSuppressLongFieldsAndHeading() {
		using var output = new MemoryStream();
		var status = await RunAsync( [ "-hs" ], output );
		Assert.Equal( 0, status );
		var text = Text( output );
		Assert.DoesNotContain( "load average", text, StringComparison.Ordinal );
		Assert.DoesNotContain( "JCPU", text, StringComparison.Ordinal );
		Assert.DoesNotContain( "PCPU", text, StringComparison.Ordinal );
		Assert.Contains( "vim notes.txt", text, StringComparison.Ordinal );
	}

	/// <summary>Verifies pid mode prefixes WHAT with the login/current process pair.</summary>
	[Fact]
	public async Task PidModeShowsLoginAndCurrentProcessIdentifiers() {
		using var output = new MemoryStream();
		var status = await RunAsync( [ "-hp" ], output );
		Assert.Equal( 0, status );
		var text = Text( output );
		Assert.DoesNotContain( "USER", text, StringComparison.Ordinal );
		Assert.Contains( "100/101 vim notes.txt", text, StringComparison.Ordinal );
	}

	/// <summary>Verifies the optional user operand restricts displayed login sessions.</summary>
	[Fact]
	public async Task UserOperandFiltersSessions() {
		using var output = new MemoryStream();
		var sessions = new FakeSessionProvider(
			[
				LoginSession( "alice", "pts/1", 100 ),
				new ProcLoginSession( "bob", "pts/9", null, Now.AddHours( -2 ), Now.AddMinutes( -5 ), 900, null )
			]
		);
		var status = await RunAsync( [ "-h", "alice" ], output, sessionProvider: sessions );
		Assert.Equal( 0, status );
		var text = Text( output );
		Assert.Contains( "alice", text, StringComparison.Ordinal );
		Assert.DoesNotContain( "bob", text, StringComparison.Ordinal );
	}

	/// <summary>Verifies <c>-f</c> toggles the origin column off, matching procps-ng compatibility behavior.</summary>
	[Fact]
	public async Task FromOptionTogglesOriginColumn() {
		using var output = new MemoryStream();
		var status = await RunAsync( [ "-hf" ], output );
		Assert.Equal( 0, status );
		var text = Text( output );
		Assert.DoesNotContain( "FROM", text, StringComparison.Ordinal );
		Assert.DoesNotContain( "host.example", text, StringComparison.Ordinal );
	}

	/// <summary>Verifies Linux-style foreground process-group data governs PCPU and WHAT selection when available.</summary>
	[Fact]
	public async Task ForegroundProcessGroupWinsOverNewerBackgroundProcess() {
		var processes = new FakeProcessProvider(
			[
				Process( 100, 77, 1000, 10, 100, 50, "login", [ "login" ], "/dev/pts/1", 100, 200 ),
				Process( 101, 77, 1000, 20, 200, 100, "vim", [ "vim", "notes.txt" ], "/dev/pts/1", 200, 200 ),
				Process( 102, 77, 1000, 30, 10, 10, "background", [ "background" ], "/dev/pts/1", 300, 200 )
			]
		);
		using var output = new MemoryStream();
		var status = await RunAsync( [ "-h" ], output, processProvider: processes );
		Assert.Equal( 0, status );
		var text = Text( output );
		Assert.Contains( "vim notes.txt", text, StringComparison.Ordinal );
		Assert.DoesNotContain( "background", text, StringComparison.Ordinal );
	}

	/// <summary>Verifies <c>-u</c> permits the newest associated process even when it belongs to another effective user.</summary>
	[Fact]
	public async Task NoCurrentOptionDisablesCurrentUserFilter() {
		var processes = new FakeProcessProvider(
			[
				Process( 100, 77, 1000, 10, 100, 50, "login", [ "login" ] ),
				Process( 101, 77, 1000, 20, 200, 100, "vim", [ "vim", "notes.txt" ] ),
				Process( 102, 77, 2000, 30, 10, 10, "other", [ "other-task" ] )
			]
		);
		using var ordinary = new MemoryStream();
		var ordinaryStatus = await RunAsync( [ "-h" ], ordinary, processProvider: processes );
		Assert.Equal( 0, ordinaryStatus );
		Assert.Contains( "vim notes.txt", Text( ordinary ), StringComparison.Ordinal );
		Assert.DoesNotContain( "other-task", Text( ordinary ), StringComparison.Ordinal );

		using var noCurrent = new MemoryStream();
		var noCurrentStatus = await RunAsync( [ "-hu" ], noCurrent, processProvider: processes );
		Assert.Equal( 0, noCurrentStatus );
		Assert.Contains( "other-task", Text( noCurrent ), StringComparison.Ordinal );
	}

	/// <summary>Verifies container mode delegates uptime semantics to the shared metrics provider.</summary>
	[Fact]
	public async Task ContainerModeRequestsContainerUptime() {
		using var output = new MemoryStream();
		var metrics = new FakeMetricsProvider();
		var status = await RunAsync( [ "--container" ], output, metricsProvider: metrics );
		Assert.Equal( 0, status );
		Assert.True( metrics.ContainerModeRequested );
	}

	/// <summary>Verifies terminal mode can include an observed terminal absent from login accounting.</summary>
	[Fact]
	public async Task TerminalModeAddsUnaccountedObservableTerminal() {
		using var output = new MemoryStream();
		var sessions = new FakeSessionProvider( [] );
		var processes = new FakeProcessProvider(
			[
				Process( 333, 333, 3000, 1, 10, 10, "shell", [ "shell" ], "/dev/pts/7" )
			]
		);
		var status = await RunAsync( [ "-ht" ], output, sessionProvider: sessions, processProvider: processes );
		Assert.Equal( 0, status );
		var text = Text( output );
		Assert.Contains( "3000", text, StringComparison.Ordinal );
		Assert.Contains( "pts/7", text, StringComparison.Ordinal );
	}

	/// <summary>Verifies the documented ProcPs environment width controls are honored within their valid ranges.</summary>
	[Fact]
	public async Task EnvironmentControlsUserAndFromWidths() {
		using var output = new MemoryStream();
		var sessions = new FakeSessionProvider(
			[ new ProcLoginSession( "abcdefghijkl", "pts/1", "host.example", Now.AddHours( -1 ), Now.AddSeconds( -30 ), 100, null ) ]
		);
		string? EnvironmentProvider( string name ) {
			ArgumentNullException.ThrowIfNull( name );
			if ( "PROCPS_USERLEN" == name ) {
				return "8";
			}
			if ( "PROCPS_FROMLEN" == name ) {
				return "8";
			}
			return null;
		}
		var status = await RunAsync( [ "-h" ], output, sessionProvider: sessions, environmentProvider: EnvironmentProvider );
		Assert.Equal( 0, status );
		var text = Text( output );
		Assert.Contains( "abcdefgh", text, StringComparison.Ordinal );
		Assert.Contains( "host.exa", text, StringComparison.Ordinal );
	}

	/// <summary>Verifies an explicitly injected terminal width still limits the WHAT field.</summary>
	[Fact]
	public async Task ColumnsEnvironmentLimitsWhatWidth() {
		using var output = new MemoryStream();
		string? EnvironmentProvider( string name ) {
			ArgumentNullException.ThrowIfNull( name );
			if ( "COLUMNS" == name ) {
				return "50";
			}
			return null;
		}
		var status = await RunAsync( [ "-h" ], output, environmentProvider: EnvironmentProvider );
		Assert.Equal( 0, status );
		var text = Text( output );
		Assert.Contains( "vim not", text, StringComparison.Ordinal );
		Assert.DoesNotContain( "vim notes.txt", text, StringComparison.Ordinal );
	}

	/// <summary>Verifies invalid ProcPs width environment values warn and fall back without touching the test console.</summary>
	[Fact]
	public async Task InvalidEnvironmentWidthsReportWarningsAndUseDefaults() {
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		string? EnvironmentProvider( string name ) {
			ArgumentNullException.ThrowIfNull( name );
			if ( "PROCPS_USERLEN" == name ) {
				return "4";
			}
			if ( "PROCPS_FROMLEN" == name ) {
				return "5";
			}
			return null;
		}
		var status = await Tool.RunAsync(
			[ "-h" ],
			stdout: output,
			stderr: error,
			sessionProvider: new FakeSessionProvider( [ LoginSession( "alice", "pts/1", 100 ) ] ),
			processProvider: new FakeProcessProvider( DefaultProcesses() ),
			metricsProvider: new FakeMetricsProvider(),
			accountResolver: new FakeAccountResolver(),
			timeProvider: new FixedTimeProvider( Now ),
			cpuUnitsPerSecondProvider: static () => 100d,
			environmentVariableProvider: EnvironmentProvider
		);
		Assert.Equal( 0, status );
		var diagnostics = Text( error );
		Assert.Contains( "PROCPS_USERLEN", diagnostics, StringComparison.Ordinal );
		Assert.Contains( "PROCPS_FROMLEN", diagnostics, StringComparison.Ordinal );
		Assert.Contains( "alice", Text( output ), StringComparison.Ordinal );
	}

	/// <summary>Verifies IP-address mode prefers a numeric accounting origin and forces the FROM column on.</summary>
	[Fact]
	public async Task IpAddressModeUsesNumericOriginAndForcesFromColumn() {
		using var output = new MemoryStream();
		var sessions = new FakeSessionProvider(
			[ new ProcLoginSession( "alice", "pts/1", "host.example", Now.AddHours( -1 ), Now.AddSeconds( -30 ), 100, null, "203.0.113.8" ) ]
		);
		var status = await RunAsync( [ "-hfi" ], output, sessionProvider: sessions );
		Assert.Equal( 0, status );
		var text = Text( output );
		Assert.Contains( "203.0.113.8", text, StringComparison.Ordinal );
		Assert.DoesNotContain( "host.example", text, StringComparison.Ordinal );
	}

	/// <summary>Verifies unavailable native login accounting produces a controlled diagnostic rather than guessed rows.</summary>
	[Fact]
	public async Task MissingLoginAccountingReportsControlledFailure() {
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		var sessions = new MissingSessionProvider();
		var status = await Tool.RunAsync(
			[],
			stdout: output,
			stderr: error,
			sessionProvider: sessions,
			processProvider: new FakeProcessProvider( [] ),
			metricsProvider: new FakeMetricsProvider(),
			accountResolver: new FakeAccountResolver(),
			timeProvider: new FixedTimeProvider( Now ),
			cpuUnitsPerSecondProvider: static () => 100d,
			environmentVariableProvider: static _ => null
		);
		Assert.Equal( 1, status );
		Assert.Equal( $"w: cannot read login sessions: fixture unavailable{Environment.NewLine}", Text( error ) );
		Assert.Equal( string.Empty, Text( output ) );
	}

	/// <summary>Verifies help, version, and invalid-option diagnostics without touching process providers.</summary>
	[Fact]
	public async Task HelpVersionAndInvalidOptionAreSupported() {
		using var help = new MemoryStream();
		using var helpError = new MemoryStream();
		Assert.Equal( 0, await Tool.RunAsync( [ "--help" ], stdout: help, stderr: helpError ) );
		Assert.Contains( "Usage:", Text( help ), StringComparison.Ordinal );
		Assert.Equal( string.Empty, Text( helpError ) );

		using var version = new MemoryStream();
		using var versionError = new MemoryStream();
		Assert.Equal( 0, await Tool.RunAsync( [ "--version" ], stdout: version, stderr: versionError ) );
		Assert.Equal( $"w from procps-ng 4.0.6{Environment.NewLine}", Text( version ) );
		Assert.Equal( string.Empty, Text( versionError ) );

		using var invalid = new MemoryStream();
		using var invalidError = new MemoryStream();
		Assert.Equal( 1, await Tool.RunAsync( [ "--not-a-w-option" ], stdout: invalid, stderr: invalidError ) );
		Assert.Equal( $"w: unrecognized option '--not-a-w-option'{Environment.NewLine}", Text( invalidError ) );
		Assert.Equal( string.Empty, Text( invalid ) );
	}

	/// <summary>Verifies cancellation is reported using the suite's controlled cancellation status.</summary>
	[Fact]
	public async Task CancellationReturnsControlledStatus() {
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var status = await Tool.RunAsync(
			[],
			stdout: output,
			stderr: error,
			sessionProvider: new FakeSessionProvider( [ LoginSession( "alice", "pts/1", 100 ) ] ),
			processProvider: new FakeProcessProvider( DefaultProcesses() ),
			metricsProvider: new FakeMetricsProvider(),
			accountResolver: new FakeAccountResolver(),
			timeProvider: new FixedTimeProvider( Now ),
			cpuUnitsPerSecondProvider: static () => 100d,
			environmentVariableProvider: static _ => null,
			cancellationToken: cancellation.Token
		);
		Assert.Equal( 130, status );
	}

	private static Task<int> RunAsync(
		string[] args,
		Stream stdout,
		IProcLoginSessionProvider? sessionProvider = null,
		IProcProcessProvider? processProvider = null,
		IProcSystemMetricsProvider? metricsProvider = null,
		Func<string, string?>? environmentProvider = null
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( stdout );
		return Tool.RunAsync(
			args,
			stdout: stdout,
			stderr: Stream.Null,
			sessionProvider: sessionProvider ?? new FakeSessionProvider( [ LoginSession( "alice", "pts/1", 100 ) ] ),
			processProvider: processProvider ?? new FakeProcessProvider( DefaultProcesses() ),
			metricsProvider: metricsProvider ?? new FakeMetricsProvider(),
			accountResolver: new FakeAccountResolver(),
			timeProvider: new FixedTimeProvider( Now ),
			cpuUnitsPerSecondProvider: static () => 100d,
			environmentVariableProvider: environmentProvider ?? EmptyEnvironment
		);
	}

	private static string? EmptyEnvironment( string name ) {
		ArgumentNullException.ThrowIfNull( name );
		return null;
	}

	private static IReadOnlyList<ProcProcessSnapshot> DefaultProcesses() {
		return [
			Process( 100, 77, 1000, 10, 100, 50, "login", [ "login" ] ),
			Process( 101, 77, 1000, 20, 200, 100, "vim", [ "vim", "notes.txt" ] )
		];
	}

	private static ProcLoginSession LoginSession( string user, string terminal, int loginProcessId ) {
		ArgumentNullException.ThrowIfNull( user );
		ArgumentNullException.ThrowIfNull( terminal );
		if ( 0 >= loginProcessId ) {
			throw new ArgumentOutOfRangeException( nameof( loginProcessId ) );
		}
		return new ProcLoginSession(
			user,
			terminal,
			"host.example",
			Now.AddHours( -1 ),
			Now.AddSeconds( -30 ),
			loginProcessId,
			null
		);
	}

	private static ProcProcessSnapshot Process(
		int processId,
		int sessionId,
		uint userId,
		ulong start,
		ulong userCpu,
		ulong systemCpu,
		string commandName,
		params string[] commandLine
	) {
		return Process( processId, sessionId, userId, start, userCpu, systemCpu, commandName, commandLine, "/dev/pts/1" );
	}

	private static ProcProcessSnapshot Process(
		int processId,
		int sessionId,
		uint userId,
		ulong start,
		ulong userCpu,
		ulong systemCpu,
		string commandName,
		string[] commandLine,
		string terminal,
		int? processGroupId = null,
		int? foregroundProcessGroupId = null
	) {
		if ( 0 >= processId ) {
			throw new ArgumentOutOfRangeException( nameof( processId ) );
		}
		if ( 0 >= sessionId ) {
			throw new ArgumentOutOfRangeException( nameof( sessionId ) );
		}
		ArgumentNullException.ThrowIfNull( commandName );
		ArgumentNullException.ThrowIfNull( commandLine );
		ArgumentNullException.ThrowIfNull( terminal );
		var processGroup = ProcObservedValue<int>.Missing( ProcObservationAvailability.Unavailable );
		if ( processGroupId.HasValue ) {
			processGroup = Exact( processGroupId.Value );
		}
		var foregroundProcessGroup = ProcObservedValue<int>.Missing( ProcObservationAvailability.Unavailable );
		if ( foregroundProcessGroupId.HasValue ) {
			foregroundProcessGroup = Exact( foregroundProcessGroupId.Value );
		}
		return new ProcProcessSnapshot( new ProcessIdentity( processId ) ) {
			CommandName = Exact( commandName ),
			CommandLineArguments = Exact<IReadOnlyList<string>>( commandLine ),
			ProcessGroupId = processGroup,
			ForegroundProcessGroupId = foregroundProcessGroup,
			SessionId = Exact( sessionId ),
			RealUserId = Exact( userId ),
			EffectiveUserId = Exact( userId ),
			Terminal = Exact( new ProcTerminalInfo( 1, terminal ) ),
			StartTimeTicks = Exact( start ),
			UserCpuTicks = Exact( userCpu ),
			SystemCpuTicks = Exact( systemCpu )
		};
	}


	private static ProcObservedValue<T> Exact<T>( T value ) {
		return ProcObservedValue<T>.Available( value, ProcObservationSource.Derived, ObservationFidelity.Exact );
	}

	private static string Text( MemoryStream stream ) {
		ArgumentNullException.ThrowIfNull( stream );
		return Encoding.UTF8.GetString( stream.ToArray() );
	}

	private sealed class FakeSessionProvider : IProcLoginSessionProvider {
		private readonly IReadOnlyList<ProcLoginSession> sessions;

		public FakeSessionProvider( IReadOnlyList<ProcLoginSession> sessions ) {
			ArgumentNullException.ThrowIfNull( sessions );
			this.sessions = sessions;
		}

		public Task<ProcObservedValue<IReadOnlyList<ProcLoginSession>>> GetSessionsAsync( CancellationToken cancellationToken = default ) {
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult( Exact( this.sessions ) );
		}
	}

	private sealed class MissingSessionProvider : IProcLoginSessionProvider {
		public Task<ProcObservedValue<IReadOnlyList<ProcLoginSession>>> GetSessionsAsync( CancellationToken cancellationToken = default ) {
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(
				ProcObservedValue<IReadOnlyList<ProcLoginSession>>.Missing( ProcObservationAvailability.Unsupported, "fixture unavailable" )
			);
		}
	}

	private sealed class FakeProcessProvider : IProcProcessProvider {
		private readonly IReadOnlyList<ProcProcessSnapshot> processes;
		public ProcProcessCapabilities Capabilities => ProcProcessCapabilities.Enumeration | ProcProcessCapabilities.Identity;

		public FakeProcessProvider( IReadOnlyList<ProcProcessSnapshot> processes ) {
			ArgumentNullException.ThrowIfNull( processes );
			this.processes = processes;
		}

		public Task<ProcProcessCollection> GetProcessesAsync( CancellationToken cancellationToken = default ) {
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult( new ProcProcessCollection( this.processes ) );
		}

		public Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync( int processId, CancellationToken cancellationToken = default ) {
			if ( 0 >= processId ) {
				throw new ArgumentOutOfRangeException( nameof( processId ) );
			}
			cancellationToken.ThrowIfCancellationRequested();
			var process = this.processes.FirstOrDefault( candidate => processId == candidate.ProcessId );
			if ( null == process ) {
				return Task.FromResult( ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Vanished ) );
			}
			return Task.FromResult( Exact( process ) );
		}

		public Task<ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>> GetMemoryMapsAsync( int processId, CancellationToken cancellationToken = default ) {
			if ( 0 >= processId ) {
				throw new ArgumentOutOfRangeException( nameof( processId ) );
			}
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.Unsupported ) );
		}
	}

	private sealed class FakeMetricsProvider : IProcSystemMetricsProvider {
		public bool ContainerModeRequested { get; private set; }
		public ProcSystemCapabilities Capabilities => ProcSystemCapabilities.Uptime | ProcSystemCapabilities.LoadAverage;

		public Task<ProcSystemSnapshot> GetSnapshotAsync( CancellationToken cancellationToken = default ) {
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult( new ProcSystemSnapshot {
				Uptime = Exact( new ProcUptimeInfo( new TimeSpan( 2, 3, 4, 0 ), null ) ),
				LoadAverages = Exact( new ProcLoadAverages( 0.25, 0.50, 0.75 ) )
			} );
		}

		public Task<ProcObservedValue<ProcUptimeInfo>> GetUptimeAsync( bool containerMode, CancellationToken cancellationToken = default ) {
			cancellationToken.ThrowIfCancellationRequested();
			this.ContainerModeRequested = containerMode;
			return Task.FromResult( Exact( new ProcUptimeInfo( new TimeSpan( 2, 3, 4, 0 ), null ) ) );
		}
	}

	private sealed class FakeAccountResolver : IProcAccountResolver {
		public bool TryResolveUser( string text, out uint id ) {
			ArgumentNullException.ThrowIfNull( text );
			if ( "alice" == text ) {
				id = 1000;
				return true;
			}
			if ( "bob" == text ) {
				id = 2000;
				return true;
			}
			id = 0;
			return false;
		}

		public bool TryResolveGroup( string text, out uint id ) {
			ArgumentNullException.ThrowIfNull( text );
			id = 0;
			return false;
		}
	}

	private sealed class FixedTimeProvider : TimeProvider {
		private readonly DateTimeOffset now;

		public FixedTimeProvider( DateTimeOffset now ) {
			this.now = now;
		}

		public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

		public override DateTimeOffset GetUtcNow() {
			return this.now;
		}
	}
}
