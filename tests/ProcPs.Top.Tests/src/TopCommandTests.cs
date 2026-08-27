/*
	Icod.ProcPs.Top.Tests
	Tests for the top command implementation.
	Copyright (C) 2026  Timothy J. Bruce <uniblab@hotmail.com>
*/

/*
	This program is free software: you can redistribute it and/or modify
	it under the terms of the GNU General Public License as published by
	the Free Software Foundation, either version 3 of the License, or
	(at your option) any later version.

	This program is distributed in the hope that it will be useful,
	but WITHOUT ANY WARRANTY; without even the implied warranty of
	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
	GNU General Public License for more details.

	You should have received a copy of the GNU General Public License
	along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

namespace Icod.ProcPs.Top.Tests;

using System.Text;
using Icod.Host;
using Icod.Processes;
using Icod.ProcPs.Shared;
using Icod.Timing;
using Xunit;

/// <summary>Exercises the DCurses-backed procps-ng compatible <c>top</c> command.</summary>
public sealed class TopCommandTests {
	private static readonly DateTimeOffset ObservationTime = new(
		2026,
		8,
		26,
		1,
		30,
		0,
		TimeSpan.Zero
	);

	[Fact]
	public async Task HelpVersionAndFieldListDoNotOpenTerminalOrSampleProviders() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );
		FakeProcessProvider processes = new( CreateProcesses() );
		FakeMetricsProvider metrics = new( CreateSystemSnapshot() );

		CommandResult help = await RunCoreAsync(
			[ "--help" ],
			terminal,
			clock,
			processes,
			metrics
		);
		CommandResult version = await RunCoreAsync(
			[ "--version" ],
			terminal,
			clock,
			processes,
			metrics
		);
		CommandResult fields = await RunCoreAsync(
			[ "--list-fields" ],
			terminal,
			clock,
			processes,
			metrics
		);

		Assert.Equal( 0, help.ExitCode );
		Assert.Contains( "top [options]", help.Stdout, StringComparison.Ordinal );
		Assert.Equal( 0, version.ExitCode );
		Assert.Contains( "Icod.ProcPs.Top (1.0.1) inspired by procps-ng 4.0.6", version.Stdout, StringComparison.Ordinal );
		Assert.Equal( 0, fields.ExitCode );
		Assert.Contains( "PID", fields.Stdout, StringComparison.Ordinal );
		Assert.Contains( "SHR", fields.Stdout, StringComparison.Ordinal );
		Assert.Equal( 0, terminal.OpenCount );
		Assert.Equal( 0, processes.CaptureCount );
		Assert.Equal( 0, metrics.CaptureCount );
	}

	[Fact]
	public async Task UnsupportedCumulativeChildCpuModeFailsBeforeTerminalOpen() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );

		CommandResult result = await RunCoreAsync(
			[ "-S" ],
			terminal,
			clock
		);

		Assert.Equal( 1, result.ExitCode );
		Assert.Contains( "dead-child CPU counters", result.Stderr, StringComparison.Ordinal );
		Assert.Equal( 0, terminal.OpenCount );
	}

	[Fact]
	public async Task BatchModeRendersSummaryAndTasksWithoutOpeningTerminal() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );

		CommandResult result = await RunCoreAsync(
			[ "-b", "-n", "1", "-o", "PID" ],
			terminal,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Contains( "load average: 1.00, 2.00, 3.00", result.Stdout, StringComparison.Ordinal );
		Assert.Contains( "%Cpu(s)", result.Stdout, StringComparison.Ordinal );
		Assert.Contains( "PID USER", result.Stdout, StringComparison.Ordinal );
		Assert.Contains( "alpha", result.Stdout, StringComparison.Ordinal );
		Assert.Contains( "beta", result.Stdout, StringComparison.Ordinal );
		Assert.Equal( string.Empty, result.Stderr );
		Assert.Equal( 0, terminal.OpenCount );
	}

	[Fact]
	public async Task BatchPidAndUserFiltersRestrictRenderedTasks() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );

		CommandResult pidFiltered = await RunCoreAsync(
			[ "-b", "-n", "1", "-p", "202" ],
			terminal,
			clock
		);
		CommandResult userFiltered = await RunCoreAsync(
			[ "-b", "-n", "1", "-u", "alice" ],
			terminal,
			clock
		);

		Assert.Equal( 0, pidFiltered.ExitCode );
		Assert.DoesNotContain( "alpha", pidFiltered.Stdout, StringComparison.Ordinal );
		Assert.Contains( "beta", pidFiltered.Stdout, StringComparison.Ordinal );
		Assert.Equal( 0, userFiltered.ExitCode );
		Assert.Contains( "alpha", userFiltered.Stdout, StringComparison.Ordinal );
		Assert.DoesNotContain( "beta", userFiltered.Stdout, StringComparison.Ordinal );
	}

	[Fact]
	public async Task BatchWidthLimitsEveryOutputLine() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );

		CommandResult result = await RunCoreAsync(
			[ "-b", "-n", "1", "--width=48", "-c" ],
			terminal,
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		foreach ( string line in SplitLines( result.Stdout ) ) {
			Assert.True( 48 >= CountRunes( line ) );
		}
	}

	[Fact]
	public async Task BatchRefreshUsesFixedRateCadence() {
		FakeClock clock = new();
		FakeProcessProvider processes = new(
			CreateProcesses(),
			CreateProcesses()
		) {
			CaptureAction = () => clock.Advance( TimeSpan.FromSeconds( 1 ) )
		};
		FakeTerminal terminal = new( clock );

		CommandResult result = await RunCoreAsync(
			[ "-b", "-n", "2", "-d", "3" ],
			terminal,
			clock,
			processes
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 2, processes.CaptureCount );
		Assert.Single( clock.Delays );
		Assert.Equal( TimeSpan.FromSeconds( 2 ), clock.Delays[ 0 ] );
	}

	[Fact]
	public async Task SamplerComputesIntervalCpuFromProcessAndSystemDeltas() {
		FakeClock clock = new();
		ProcProcessSnapshot firstProcess = CreateProcess(
			101,
			"alpha",
			1000,
			10,
			10,
			64UL * 1024 * 1024
		);
		ProcProcessSnapshot secondProcess = CreateProcess(
			101,
			"alpha",
			1000,
			30,
			10,
			64UL * 1024 * 1024
		);
		FakeProcessProvider processes = new(
			new ProcProcessCollection( [ firstProcess ] ),
			new ProcProcessCollection( [ secondProcess ] )
		);
		FakeMetricsProvider metrics = new(
			CreateSystemSnapshot( cpuTotalOffset: 0 ),
			CreateSystemSnapshot( cpuTotalOffset: 200 )
		);
		TopSampler sampler = new(
			processes,
			metrics,
			new FakeSupplementProvider(),
			new FakeAccountResolver(),
			new FakeProcessorProvider( 4 ),
			clock,
			() => ObservationTime
		);

		TopSample first = await sampler.CaptureAsync( false, CancellationToken.None );
		clock.Advance( TimeSpan.FromSeconds( 1 ) );
		TopSample second = await sampler.CaptureAsync( false, CancellationToken.None );

		Assert.Equal( 0.0, Assert.Single( first.Tasks ).CpuPercentIrix, 3 );
		Assert.Equal( 40.0, Assert.Single( second.Tasks ).CpuPercentIrix, 3 );
	}

	[Fact]
	public async Task InteractiveModeUsesDefaultCadenceAndDisposesOnInterrupt() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );
		terminal.Events.Enqueue( new ScheduledTerminalEvent( TopTerminalEventKind.Interrupt ) );
		FakeProcessProvider processes = new( CreateProcesses() );

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			processes
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Equal( 1, terminal.OpenCount );
		Assert.Single( terminal.Waits );
		Assert.Equal( TimeSpan.FromSeconds( 3 ), terminal.Waits[ 0 ] );
		Assert.Single( terminal.Frames );
		Assert.Equal( 1, processes.CaptureCount );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task ResizeRerendersCurrentSampleWithoutResampling() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, new TopTerminalDimensions( 80, 14 ) );
		terminal.Events.Enqueue(
			new ScheduledTerminalEvent(
				TopTerminalEventKind.Resize,
				new TopTerminalDimensions( 100, 18 )
			)
		);
		terminal.Events.Enqueue( new ScheduledTerminalEvent( TopTerminalEventKind.Interrupt ) );
		FakeProcessProvider processes = new( CreateProcesses() );

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			processes
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Equal( 1, processes.CaptureCount );
		Assert.Equal( 2, terminal.Frames.Count );
		Assert.Equal( 80, terminal.Frames[ 0 ].Columns );
		Assert.Equal( 100, terminal.Frames[ 1 ].Columns );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task RepaintRequestsPhysicalRefreshWithoutResampling() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );
		terminal.Events.Enqueue( new ScheduledTerminalEvent( TopTerminalEventKind.Repaint ) );
		terminal.Events.Enqueue( new ScheduledTerminalEvent( TopTerminalEventKind.Interrupt ) );
		FakeProcessProvider processes = new( CreateProcesses() );

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			processes
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Equal( 1, processes.CaptureCount );
		Assert.Equal( 1, terminal.RepaintCount );
		Assert.Single( terminal.Frames );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task InteractiveMemorySortRerendersWithoutResampling() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );
		terminal.Events.Enqueue( CharacterEvent( 'M' ) );
		terminal.Events.Enqueue( CharacterEvent( 'q' ) );
		FakeProcessProvider processes = new( CreateProcesses() );

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			processes
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 1, processes.CaptureCount );
		Assert.Equal( 2, terminal.Frames.Count );
		Assert.Contains( "101", terminal.Frames[ 0 ].Lines[ 6 ].Text, StringComparison.Ordinal );
		Assert.Contains( "202", terminal.Frames[ 1 ].Lines[ 6 ].Text, StringComparison.Ordinal );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task ImmediateRefreshInputResamplesWithoutWaitingForTimeout() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );
		terminal.Events.Enqueue( CharacterEvent( ' ' ) );
		terminal.Events.Enqueue( new ScheduledTerminalEvent( TopTerminalEventKind.Interrupt ) );
		FakeProcessProvider processes = new( CreateProcesses(), CreateProcesses() );

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			processes
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Equal( 2, processes.CaptureCount );
		Assert.Equal( 2, terminal.Frames.Count );
		Assert.Equal( 2, terminal.Waits.Count );
	}

	[Fact]
	public async Task SecureModeBlocksProcessControlPrompts() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );
		terminal.Events.Enqueue( CharacterEvent( 'k' ) );
		terminal.Events.Enqueue( CharacterEvent( 'r' ) );
		terminal.Events.Enqueue( CharacterEvent( 'd' ) );
		terminal.Events.Enqueue( CharacterEvent( 'q' ) );
		FakeProcessControl control = new();

		CommandResult result = await RunCoreAsync(
			[ "--secure-mode" ],
			terminal,
			clock,
			processControl: control
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 0, control.SignalCount );
		Assert.Equal( 0, control.PriorityCount );
		Assert.Equal( 0, control.ParseSignalCount );
		Assert.All(
			terminal.Frames.Skip( 1 ),
			frame => Assert.DoesNotContain( frame.Lines, line => TopLineStyle.Prompt == line.Style )
		);
	}

	[Fact]
	public async Task SignalPromptUsesReuseProtectedObservedProcess() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );
		terminal.Events.Enqueue( CharacterEvent( 'k' ) );
		terminal.Events.Enqueue( CharacterEvent( '1' ) );
		terminal.Events.Enqueue( CharacterEvent( '0' ) );
		terminal.Events.Enqueue( CharacterEvent( '1' ) );
		terminal.Events.Enqueue( KeyEvent( TopInputKey.Enter ) );
		terminal.Events.Enqueue( KeyEvent( TopInputKey.Enter ) );
		terminal.Events.Enqueue( CharacterEvent( 'q' ) );
		FakeProcessControl control = new();
		FakeProcessProvider processes = new( CreateProcesses(), CreateProcesses() );

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			processes,
			processControl: control
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 1, control.ParseSignalCount );
		Assert.Equal( 1, control.SignalCount );
		Assert.Equal( 101, control.LastSignaledProcessId );
		Assert.NotNull( control.LastSignaledIdentity );
		Assert.NotNull( control.LastSignaledIdentity!.ReuseToken );
	}

	[Fact]
	public async Task RenicePromptUsesReuseProtectedObservedProcess() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );
		terminal.Events.Enqueue( CharacterEvent( 'r' ) );
		terminal.Events.Enqueue( CharacterEvent( '1' ) );
		terminal.Events.Enqueue( CharacterEvent( '0' ) );
		terminal.Events.Enqueue( CharacterEvent( '1' ) );
		terminal.Events.Enqueue( KeyEvent( TopInputKey.Enter ) );
		terminal.Events.Enqueue( CharacterEvent( '-' ) );
		terminal.Events.Enqueue( CharacterEvent( '5' ) );
		terminal.Events.Enqueue( KeyEvent( TopInputKey.Enter ) );
		terminal.Events.Enqueue( CharacterEvent( 'q' ) );
		FakeProcessControl control = new();
		FakeProcessProvider processes = new( CreateProcesses(), CreateProcesses() );

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			processes,
			processControl: control
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 1, control.PriorityCount );
		Assert.Equal( 101, control.LastPriorityProcessId );
		Assert.Equal( -5, control.LastNiceValue );
		Assert.NotNull( control.LastPriorityIdentity );
		Assert.NotNull( control.LastPriorityIdentity!.ReuseToken );
	}

	[Fact]
	public async Task NonInteractiveTerminalFailsBeforeSamplingAndIsDisposed() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, isInteractive: false );
		FakeProcessProvider processes = new( CreateProcesses() );

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			processes
		);

		Assert.Equal( 1, result.ExitCode );
		Assert.Contains( "use -b", result.Stderr, StringComparison.Ordinal );
		Assert.Equal( 0, processes.CaptureCount );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task InitialTooSmallTerminalFailsBeforeSamplingAndIsDisposed() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, new TopTerminalDimensions( 39, 7 ) );
		FakeProcessProvider processes = new( CreateProcesses() );

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			processes
		);

		Assert.Equal( 1, result.ExitCode );
		Assert.Contains( "at least 40 columns by 7 rows", result.Stderr, StringComparison.Ordinal );
		Assert.Equal( 0, processes.CaptureCount );
		Assert.True( terminal.Disposed );
	}

	private static async Task<CommandResult> RunCoreAsync(
		IReadOnlyList<string> args,
		FakeTerminal terminal,
		FakeClock clock,
		FakeProcessProvider? processProvider = null,
		FakeMetricsProvider? metricsProvider = null,
		FakeSupplementProvider? supplementProvider = null,
		FakeAccountResolver? accountResolver = null,
		FakeProcessorProvider? processorProvider = null,
		ITopProcessControl? processControl = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( terminal );
		ArgumentNullException.ThrowIfNull( clock );
		using MemoryStream stdout = new();
		using MemoryStream stderr = new();
		int exitCode = await Command.RunAsyncCore(
			args,
			stdout,
			stderr,
			processProvider ?? new FakeProcessProvider( CreateProcesses() ),
			metricsProvider ?? new FakeMetricsProvider( CreateSystemSnapshot() ),
			supplementProvider ?? new FakeSupplementProvider(),
			accountResolver ?? new FakeAccountResolver(),
			processorProvider ?? new FakeProcessorProvider( 4 ),
			clock,
			() => ObservationTime,
			_ => null,
			() => 9999,
			terminal,
			processControl ?? new FakeProcessControl(),
			cancellationToken
		);
		return new CommandResult(
			exitCode,
			Encoding.UTF8.GetString( stdout.ToArray() ),
			Encoding.UTF8.GetString( stderr.ToArray() )
		);
	}

	private static ProcProcessCollection CreateProcesses() => new(
		[
			CreateProcess( 101, "alpha", 1000, 10, 10, 64UL * 1024 * 1024 ),
			CreateProcess( 202, "beta", 1001, 20, 10, 256UL * 1024 * 1024 )
		]
	);

	private static ProcProcessSnapshot CreateProcess(
		int processId,
		string command,
		uint userId,
		ulong userCpu,
		ulong systemCpu,
		ulong residentBytes
	) {
		ProcessIdentity identity = new(
			processId,
			new ProcessReuseToken( "test", $"start-{processId}" )
		);
		return new ProcProcessSnapshot( identity ) {
			CommandName = Exact( command ),
			CommandLineArguments = Exact<IReadOnlyList<string>>(
				[ command, "--worker", processId.ToString( System.Globalization.CultureInfo.InvariantCulture ) ]
			),
			State = Exact( ProcProcessState.Sleeping ),
			ParentProcessId = Exact( 1 ),
			RealUserId = Exact( userId ),
			EffectiveUserId = Exact( userId ),
			UserCpuTicks = Exact( userCpu ),
			SystemCpuTicks = Exact( systemCpu ),
			StartTimeTicks = Exact( (ulong)processId ),
			VirtualMemoryBytes = Exact( residentBytes * 2 ),
			ResidentMemoryBytes = Exact( residentBytes ),
			NiceValue = Exact( 0 ),
			ThreadCount = Exact( 1 ),
			LifetimeStable = Exact( true )
		};
	}

	private static ProcSystemSnapshot CreateSystemSnapshot( ulong cpuTotalOffset = 0 ) => new() {
		Cpu = Exact(
			new ProcCpuTimes(
				100 + cpuTotalOffset,
				10,
				50,
				800,
				20,
				5,
				5,
				10,
				0,
				0
			)
		),
		Memory = Exact(
			new ProcMemoryInfo(
				8UL * 1024 * 1024 * 1024,
				2UL * 1024 * 1024 * 1024,
				3UL * 1024 * 1024 * 1024,
				128UL * 1024 * 1024,
				512UL * 1024 * 1024,
				null,
				2UL * 1024 * 1024 * 1024,
				1UL * 1024 * 1024 * 1024
			)
		),
		LoadAverage = Exact( new ProcLoadAverage( 1.0, 2.0, 3.0, 1, 2, 202 ) ),
		LoadAverages = Exact( new ProcLoadAverages( 1.0, 2.0, 3.0 ) ),
		Uptime = Exact( new ProcUptimeInfo( TimeSpan.FromHours( 5 ), TimeSpan.FromHours( 10 ) ) ),
		UserSessions = Exact( new ProcUserSessionInfo( 1 ) )
	};

	private static ProcObservedValue<T> Exact<T>( T value ) => ProcObservedValue<T>.Available(
		value,
		ProcObservationSource.LinuxProcfs,
		ProcObservationFidelity.Exact
	);

	private static ScheduledTerminalEvent CharacterEvent( char value ) => new(
		TopTerminalEventKind.Input,
		Input: new TopInputEvent( TopInputKey.Character, new Rune( value ) )
	);

	private static ScheduledTerminalEvent KeyEvent( TopInputKey key ) => new(
		TopTerminalEventKind.Input,
		Input: new TopInputEvent( key, null )
	);

	private static string[] SplitLines( string text ) => text
		.Replace( "\r\n", "\n", StringComparison.Ordinal )
		.Split( '\n', StringSplitOptions.RemoveEmptyEntries );

	private static int CountRunes( string text ) {
		ArgumentNullException.ThrowIfNull( text );
		int count = 0;
		foreach ( Rune rune in text.EnumerateRunes() ) {
			_ = rune;
			count++;
		}
		return count;
	}

	private sealed record CommandResult( int ExitCode, string Stdout, string Stderr );

	private sealed class FakeProcessProvider : IProcProcessProvider {
		private readonly Queue<ProcProcessCollection> snapshots;
		private ProcProcessCollection current;

		internal FakeProcessProvider( params ProcProcessCollection[] snapshots ) {
			ArgumentNullException.ThrowIfNull( snapshots );
			if ( 0 == snapshots.Length ) {
				throw new ArgumentException( "At least one process snapshot is required.", nameof( snapshots ) );
			}
			this.snapshots = new Queue<ProcProcessCollection>( snapshots );
			this.current = snapshots[ 0 ];
		}

		public ProcProcessCapabilities Capabilities => ProcProcessCapabilities.Enumeration
			| ProcProcessCapabilities.Identity
			| ProcProcessCapabilities.Users
			| ProcProcessCapabilities.CommandLine
			| ProcProcessCapabilities.CpuTimes
			| ProcProcessCapabilities.Memory
			| ProcProcessCapabilities.Priority
			| ProcProcessCapabilities.Threads;
		internal int CaptureCount { get; private set; }
		internal Action? CaptureAction { get; init; }

		public Task<ProcProcessCollection> GetProcessesAsync( CancellationToken cancellationToken = default ) {
			cancellationToken.ThrowIfCancellationRequested();
			this.CaptureCount++;
			this.CaptureAction?.Invoke();
			if ( 1 < this.snapshots.Count ) {
				this.current = this.snapshots.Dequeue();
			} else if ( 1 == this.snapshots.Count ) {
				this.current = this.snapshots.Peek();
			}
			return Task.FromResult( this.current );
		}

		public Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync(
			int processId,
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			ProcProcessSnapshot? process = this.current.Processes.FirstOrDefault(
				item => item.ProcessId == processId
			);
			return Task.FromResult(
				process is null
					? ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Vanished )
					: Exact( process )
			);
		}

		public Task<ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>> GetMemoryMapsAsync(
			int processId,
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(
				ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing(
					ProcObservationAvailability.Unsupported
				)
			);
		}
	}

	private sealed class FakeMetricsProvider : IProcSystemMetricsProvider {
		private readonly Queue<ProcSystemSnapshot> snapshots;
		private ProcSystemSnapshot current;

		internal FakeMetricsProvider( params ProcSystemSnapshot[] snapshots ) {
			ArgumentNullException.ThrowIfNull( snapshots );
			if ( 0 == snapshots.Length ) {
				throw new ArgumentException( "At least one system snapshot is required.", nameof( snapshots ) );
			}
			this.snapshots = new Queue<ProcSystemSnapshot>( snapshots );
			this.current = snapshots[ 0 ];
		}

		public ProcSystemCapabilities Capabilities => ProcSystemCapabilities.CpuActivity
			| ProcSystemCapabilities.Memory
			| ProcSystemCapabilities.LoadAverage
			| ProcSystemCapabilities.Uptime
			| ProcSystemCapabilities.UserSessions;
		internal int CaptureCount { get; private set; }

		public Task<ProcSystemSnapshot> GetSnapshotAsync( CancellationToken cancellationToken = default ) {
			cancellationToken.ThrowIfCancellationRequested();
			this.CaptureCount++;
			if ( 1 < this.snapshots.Count ) {
				this.current = this.snapshots.Dequeue();
			} else if ( 1 == this.snapshots.Count ) {
				this.current = this.snapshots.Peek();
			}
			return Task.FromResult( this.current );
		}
	}

	private sealed class FakeSupplementProvider : IProcMatchSupplementProvider {
		public Task<IReadOnlyList<ProcMatchCandidate>> GetCandidatesAsync(
			IReadOnlyList<ProcProcessSnapshot> processes,
			bool includeLightweightTasks,
			CancellationToken cancellationToken = default
		) {
			ArgumentNullException.ThrowIfNull( processes );
			cancellationToken.ThrowIfCancellationRequested();
			IReadOnlyList<ProcMatchCandidate> result = processes
				.Select(
					process => new ProcMatchCandidate(
						process,
						new ProcMatchSupplement { ThreadGroupId = process.ProcessId }
					)
				)
				.ToArray();
			return Task.FromResult( result );
		}
	}

	private sealed class FakeAccountResolver : IProcAccountDisplayResolver {
		public bool TryResolveUser( string text, out uint id ) {
			ArgumentNullException.ThrowIfNull( text );
			if ( "alice" == text ) {
				id = 1000;
				return true;
			}
			if ( "bob" == text ) {
				id = 1001;
				return true;
			}
			return uint.TryParse( text, out id );
		}

		public bool TryResolveGroup( string text, out uint id ) {
			ArgumentNullException.ThrowIfNull( text );
			return uint.TryParse( text, out id );
		}

		public bool TryGetUserName( uint id, out string name ) {
			name = id switch {
				1000 => "alice",
				1001 => "bob",
				_ => string.Empty
			};
			return 0 < name.Length;
		}

		public bool TryGetGroupName( uint id, out string name ) {
			name = string.Empty;
			return false;
		}
	}

	private sealed class FakeProcessorProvider : IProcessorResourceProvider {
		internal FakeProcessorProvider( int processorCount ) {
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero( processorCount );
			this.ProcessorCount = processorCount;
		}

		internal int ProcessorCount { get; }

		public ValueTask<ProcessorResourceSnapshot> GetProcessorResourcesAsync(
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			return ValueTask.FromResult(
				new ProcessorResourceSnapshot(
					HostResourceValue<int>.Available(
						this.ProcessorCount,
						HostResourceProvenance.Derived
					),
					HostResourceValue<int>.Available(
						this.ProcessorCount,
						HostResourceProvenance.Derived
					),
					HostResourceValue<int>.Available(
						this.ProcessorCount,
						HostResourceProvenance.Derived
					),
					HostResourceValue<int>.Available(
						this.ProcessorCount,
						HostResourceProvenance.Derived
					),
					HostResourceValue<ProcessorAffinityDescriptor>.Unsupported(),
					HostResourceValue<ProcessorQuotaDescriptor>.Unsupported(),
					HostResourceValue<ProcessorTopologyDescriptor>.Unsupported()
				)
			);
		}
	}

	private sealed class FakeClock : IMonotonicClock {
		private long timestamp;
		internal List<TimeSpan> Delays { get; } = [];

		public long GetTimestamp() => this.timestamp;

		public TimeSpan GetElapsedTime( long startingTimestamp, long endingTimestamp ) =>
			TimeSpan.FromTicks( endingTimestamp - startingTimestamp );

		public ValueTask DelayAsync(
			TimeSpan delay,
			CancellationToken cancellationToken = default
		) {
			if ( TimeSpan.Zero > delay ) {
				throw new ArgumentOutOfRangeException( nameof( delay ) );
			}
			cancellationToken.ThrowIfCancellationRequested();
			this.Delays.Add( delay );
			this.Advance( delay );
			return ValueTask.CompletedTask;
		}

		internal void Advance( TimeSpan duration ) {
			if ( TimeSpan.Zero > duration ) {
				throw new ArgumentOutOfRangeException( nameof( duration ) );
			}
			this.timestamp = checked( this.timestamp + duration.Ticks );
		}
	}

	private sealed record ScheduledTerminalEvent(
		TopTerminalEventKind Kind,
		TopTerminalDimensions? Dimensions = null,
		TopInputEvent? Input = null
	);

	private sealed class FakeTerminal : ITopTerminalSessionFactory, ITopTerminalSession {
		private readonly FakeClock clock;
		private TopTerminalDimensions dimensions;

		internal FakeTerminal(
			FakeClock clock,
			TopTerminalDimensions? dimensions = null,
			bool isInteractive = true
		) {
			ArgumentNullException.ThrowIfNull( clock );
			this.clock = clock;
			this.dimensions = dimensions ?? new TopTerminalDimensions( 100, 20 );
			this.IsInteractive = isInteractive;
		}

		public bool IsInteractive { get; }
		public CancellationToken TerminationToken => CancellationToken.None;
		internal Queue<ScheduledTerminalEvent> Events { get; } = new();
		internal List<TimeSpan> Waits { get; } = [];
		internal List<TopRenderFrame> Frames { get; } = [];
		internal int OpenCount { get; private set; }
		internal int RepaintCount { get; private set; }
		internal int AlertCount { get; private set; }
		internal bool Disposed { get; private set; }

		public ValueTask<ITopTerminalSession> OpenAsync(
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			this.OpenCount++;
			return ValueTask.FromResult<ITopTerminalSession>( this );
		}

		public TopTerminalDimensions GetDimensions() => this.dimensions;

		public ValueTask<TopTerminalEvent> ReadEventAsync(
			TimeSpan timeout,
			CancellationToken cancellationToken = default
		) {
			if ( TimeSpan.Zero > timeout ) {
				throw new ArgumentOutOfRangeException( nameof( timeout ) );
			}
			cancellationToken.ThrowIfCancellationRequested();
			this.Waits.Add( timeout );
			ScheduledTerminalEvent scheduled = 0 < this.Events.Count
				? this.Events.Dequeue()
				: new ScheduledTerminalEvent( TopTerminalEventKind.Interrupt );
			if ( scheduled.Dimensions.HasValue ) {
				this.dimensions = scheduled.Dimensions.Value;
			}
			if ( TopTerminalEventKind.Timeout == scheduled.Kind ) {
				this.clock.Advance( timeout );
			}
			return ValueTask.FromResult(
				new TopTerminalEvent( scheduled.Kind, scheduled.Input )
			);
		}

		public ValueTask RenderAsync(
			TopRenderFrame frame,
			CancellationToken cancellationToken = default
		) {
			ArgumentNullException.ThrowIfNull( frame );
			cancellationToken.ThrowIfCancellationRequested();
			this.Frames.Add( frame );
			return ValueTask.CompletedTask;
		}

		public ValueTask RepaintAsync( CancellationToken cancellationToken = default ) {
			cancellationToken.ThrowIfCancellationRequested();
			this.RepaintCount++;
			return ValueTask.CompletedTask;
		}

		public ValueTask AlertAsync( CancellationToken cancellationToken = default ) {
			cancellationToken.ThrowIfCancellationRequested();
			this.AlertCount++;
			return ValueTask.CompletedTask;
		}

		public ValueTask DisposeAsync() {
			this.Disposed = true;
			return ValueTask.CompletedTask;
		}
	}

	private sealed class FakeProcessControl : ITopProcessControl {
		internal int ParseSignalCount { get; private set; }
		internal int SignalCount { get; private set; }
		internal int PriorityCount { get; private set; }
		internal int? LastSignaledProcessId { get; private set; }
		internal ProcessIdentity? LastSignaledIdentity { get; private set; }
		internal int? LastPriorityProcessId { get; private set; }
		internal ProcessIdentity? LastPriorityIdentity { get; private set; }
		internal int? LastNiceValue { get; private set; }

		public ProcessOperationResult<ProcessSignal> ParseSignal( string text ) {
			ArgumentNullException.ThrowIfNull( text );
			this.ParseSignalCount++;
			return ProcessOperationResult<ProcessSignal>.Success(
				new ProcessSignal( 15, "TERM" )
			);
		}

		public Task<ProcessOperationResult> SignalAsync(
			ProcProcessSnapshot process,
			ProcessSignal signal,
			CancellationToken cancellationToken
		) {
			ArgumentNullException.ThrowIfNull( process );
			ArgumentNullException.ThrowIfNull( signal );
			cancellationToken.ThrowIfCancellationRequested();
			this.SignalCount++;
			this.LastSignaledProcessId = process.ProcessId;
			this.LastSignaledIdentity = process.Identity;
			return Task.FromResult( ProcessOperationResult.Success() );
		}

		public ProcessOperationResult SetPriority( ProcProcessSnapshot process, int niceValue ) {
			ArgumentNullException.ThrowIfNull( process );
			this.PriorityCount++;
			this.LastPriorityProcessId = process.ProcessId;
			this.LastPriorityIdentity = process.Identity;
			this.LastNiceValue = niceValue;
			return ProcessOperationResult.Success();
		}
	}
}
