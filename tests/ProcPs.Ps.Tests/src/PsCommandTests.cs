namespace Icod.ProcPs.Ps.Tests;

using System.Text;
using Icod.CommandFramework.Host;
using Icod.CommandFramework.Processes;
using Icod.ProcPs.Shared;
using Xunit;
using Tool = Icod.ProcPs.Ps.Command;

/// <summary>Exercises the Batch 62 procps-ng 4.0.6 <c>ps</c> compatibility engine.</summary>
public sealed class PsCommandTests {
	/// <summary>Verifies the default selection remains limited to the caller's user and terminal.</summary>
	[Fact]
	public async Task DefaultSelectionUsesCurrentUserAndTerminal() {
		using var output = new MemoryStream();
		var status = await RunAsync( [], output, currentProcessId: 101 );
		Assert.Equal( 0, status );
		var text = Text( output );
		Assert.Contains( "101", text, StringComparison.Ordinal );
		Assert.DoesNotContain( "202", text, StringComparison.Ordinal );
	}

	/// <summary>Verifies all-process selection and custom format aliases and headings.</summary>
	[Fact]
	public async Task AllAndCustomFormatAreSupported() {
		using var output = new MemoryStream();
		var status = await RunAsync( [ "-A", "-o", "pid=PROCESS,ppid,user,args" ], output );
		Assert.Equal( 0, status );
		var text = Text( output );
		Assert.Contains( "PROCESS", text, StringComparison.Ordinal );
		Assert.Contains( "alice", text, StringComparison.Ordinal );
		Assert.Contains( "bob", text, StringComparison.Ordinal );
		Assert.Contains( "worker --jobs", text, StringComparison.Ordinal );
	}

	/// <summary>Verifies BSD <c>aux</c> output and descending sort syntax.</summary>
	[Fact]
	public async Task BsdAuxAndSortingAreSupported() {
		using var output = new MemoryStream();
		var status = await RunAsync( [ "aux", "--sort=-pid" ], output );
		Assert.Equal( 0, status );
		var lines = Lines( output );
		Assert.Contains( "USER", lines[ 0 ], StringComparison.Ordinal );
		Assert.Contains( "202", lines[ 1 ], StringComparison.Ordinal );
	}

	/// <summary>Verifies quick PID selection preserves operand order and empty custom headings suppress headers.</summary>
	[Fact]
	public async Task QuickPidPreservesRequestedOrder() {
		using var output = new MemoryStream();
		var status = await RunAsync( [ "-q", "202,101", "-o", "pid=" ], output );
		Assert.Equal( 0, status );
		var lines = Lines( output );
		Assert.Equal( "202", lines[ 0 ].Trim() );
		Assert.Equal( "101", lines[ 1 ].Trim() );
	}

	/// <summary>Verifies command-name and account-name selectors.</summary>
	[Fact]
	public async Task SelectionByCommandAndUserWorks() {
		using var commandOutput = new MemoryStream();
		Assert.Equal( 0, await RunAsync( [ "-C", "worker", "-o", "pid=" ], commandOutput ) );
		Assert.Equal( string.Concat( "202", Environment.NewLine ), Text( commandOutput ) );

		using var userOutput = new MemoryStream();
		Assert.Equal( 0, await RunAsync( [ "-u", "alice", "-o", "pid=" ], userOutput ) );
		Assert.Equal( string.Concat( "101", Environment.NewLine ), Text( userOutput ) );
	}

	/// <summary>Verifies procps list-selection compatibility for numeric PID forms and effective groups.</summary>
	[Fact]
	public async Task NumericPidAndEffectiveGroupSelectionAreSupported() {
		using var numericOutput = new MemoryStream();
		Assert.Equal( 0, await RunAsync( [ "202", "-o", "pid=" ], numericOutput ) );
		Assert.Equal( string.Concat( "202", Environment.NewLine ), Text( numericOutput ) );

		using var groupOutput = new MemoryStream();
		Assert.Equal( 0, await RunAsync( [ "--group", "bob", "-o", "pid=" ], groupOutput ) );
		Assert.Equal( string.Concat( "202", Environment.NewLine ), Text( groupOutput ) );

		using var compatibilityGroupOutput = new MemoryStream();
		Assert.Equal( 0, await RunAsync( [ "-g", "bob", "-o", "pid=" ], compatibilityGroupOutput ) );
		Assert.Equal( string.Concat( "202", Environment.NewLine ), Text( compatibilityGroupOutput ) );
	}

	/// <summary>Verifies explicit per-field widths and help sections are accepted.</summary>
	[Fact]
	public async Task FieldWidthsAndHelpSectionsAreSupported() {
		using var output = new MemoryStream();
		Assert.Equal( 0, await RunAsync( [ "-p", "101", "-o", "pid:10=PROCESS" ], output ) );
		Assert.Contains( "   PROCESS", Text( output ), StringComparison.Ordinal );

		using var help = new MemoryStream();
		Assert.Equal( 0, await RunAsync( [ "--help=output" ], help ) );
		Assert.Contains( "Output:", Text( help ), StringComparison.Ordinal );
	}

	/// <summary>Verifies calculated fields and reusable namespace/container/environment observations.</summary>
	[Fact]
	public async Task MetricsAndExtendedProcPsFieldsAreRendered() {
		using var output = new MemoryStream();
		var status = await RunAsync(
			[ "-p", "202", "--cols", "200", "-o", "pid=,pcpu=,pmem=,etime=,cgroup=,container=,pidns=,environ=" ],
			output
		);
		Assert.Equal( 0, status );
		var text = Text( output );
		Assert.Contains( "202", text, StringComparison.Ordinal );
		Assert.Contains( "0.1", text, StringComparison.Ordinal );
		Assert.Contains( "00:30:00", text, StringComparison.Ordinal );
		Assert.Contains( "/system.slice/worker.service", text, StringComparison.Ordinal );
		Assert.Contains( "container-202", text, StringComparison.Ordinal );
		Assert.Contains( "4026532202", text, StringComparison.Ordinal );
		Assert.Contains( "WORKER_COUNT=4", text, StringComparison.Ordinal );
	}

	/// <summary>Verifies forest ordering and command indentation.</summary>
	[Fact]
	public async Task ForestOrdersChildrenAfterParents() {
		using var output = new MemoryStream();
		var status = await RunAsync( [ "-A", "--forest", "-o", "pid=,ppid=,args=" ], output );
		Assert.Equal( 0, status );
		var lines = Lines( output );
		Assert.Contains( "101", lines[ 0 ], StringComparison.Ordinal );
		Assert.Contains( "202", lines[ 1 ], StringComparison.Ordinal );
		Assert.Contains( "\\_ worker --jobs", lines[ 1 ], StringComparison.Ordinal );
	}

	/// <summary>Verifies thread mode consumes the shared lightweight-task supplement provider.</summary>
	[Fact]
	public async Task ThreadModeReportsLightweightTasks() {
		using var output = new MemoryStream();
		var status = await RunAsync( [ "-A", "-L", "-o", "pid=,lwp=,thgrpid=" ], output );
		Assert.Equal( 0, status );
		var text = Text( output );
		Assert.Contains( "203", text, StringComparison.Ordinal );
		Assert.Contains( "202", text, StringComparison.Ordinal );
	}


	/// <summary>Verifies thread mode selects a thread-oriented default field set when no custom format is supplied.</summary>
	[Fact]
	public async Task ThreadModeUsesThreadDefaultFields() {
		using var output = new MemoryStream();
		var status = await RunAsync( [ "-A", "-L" ], output );
		Assert.Equal( 0, status );
		var lines = Lines( output );
		Assert.Contains( "LWP", lines[ 0 ], StringComparison.Ordinal );
		Assert.Contains( "TGID", lines[ 0 ], StringComparison.Ordinal );
		Assert.Contains( "203", Text( output ), StringComparison.Ordinal );
	}

	/// <summary>Verifies environment personality selection affects the default presentation.</summary>
	[Fact]
	public async Task PersonalityEnvironmentSelectsBsdPresentation() {
		using var output = new MemoryStream();
		var environment = new Dictionary<string, string?>( StringComparer.Ordinal ) {
			[ "PS_PERSONALITY" ] = "bsd"
		};
		var status = await Tool.RunAsync(
			[ "-A" ],
			stdout: output,
			stderr: Stream.Null,
			processProvider: new FakeProvider(),
			metricsProvider: new FakeMetricsProvider(),
			supplementProvider: new FakeSupplementProvider(),
			accountResolver: new FakeAccountResolver(),
			currentProcessIdProvider: static () => 101,
			environment: environment
		);
		Assert.Equal( 0, status );
		Assert.Contains( "STAT", Lines( output )[ 0 ], StringComparison.Ordinal );
	}

	/// <summary>Verifies width and explicit header controls.</summary>
	[Fact]
	public async Task WidthAndHeadersAreHonored() {
		using var output = new MemoryStream();
		var status = await RunAsync( [ "-A", "--no-headers", "--cols", "16", "-o", "pid,args" ], output );
		Assert.Equal( 0, status );
		foreach ( var line in Lines( output ) ) {
			Assert.True( 16 >= line.Length );
			Assert.DoesNotContain( "PID", line, StringComparison.Ordinal );
		}
	}

	/// <summary>Verifies Linux security, signal, and capability observations are rendered through shared supplements.</summary>
	[Fact]
	public async Task SecuritySignalAndCapabilityFieldsUseSupplementObservations() {
		using var output = new MemoryStream();
		var status = await RunAsync( [ "-p", "101", "--cols", "200", "-o", "label=,blocked=,caught=,ignored=,pending=,capeff=" ], output );
		Assert.Equal( 0, status );
		Assert.Equal( "system_u:system_r:worker_t:s0 0000000000000004 0000000000000008 0000000000000001 0000000000000002 0000000000000025", Text( output ).Trim() );
	}

	/// <summary>Verifies large enumerations are streamed without dependence on console output.</summary>
	[Fact]
	public async Task LargeProcessSetIsWrittenWithoutBufferingFailures() {
		using var output = new MemoryStream();
		var status = await Tool.RunAsync(
			[ "-A", "-o", "pid=" ],
			stdout: output,
			stderr: Stream.Null,
			processProvider: new LargeProvider( 3000 ),
			metricsProvider: new FakeMetricsProvider(),
			supplementProvider: new FakeSupplementProvider(),
			accountResolver: new FakeAccountResolver(),
			currentProcessIdProvider: static () => 1
		);
		Assert.Equal( 0, status );
		Assert.Equal( 3000, Lines( output ).Length );
	}

	/// <summary>Verifies common help and version paths write only to injected streams.</summary>
	[Fact]
	public async Task HelpAndVersionWork() {
		using var help = new MemoryStream();
		Assert.Equal( 0, await RunAsync( [ "--help" ], help ) );
		Assert.Contains( "Usage:", Text( help ), StringComparison.Ordinal );
		using var version = new MemoryStream();
		Assert.Equal( 0, await RunAsync( [ "--version" ], version ) );
		Assert.Contains( "procps-ng 4.0.6", Text( version ), StringComparison.Ordinal );
		using var fields = new MemoryStream();
		Assert.Equal( 0, await RunAsync( [ "L" ], fields ) );
		Assert.Contains( "pid", Text( fields ), StringComparison.Ordinal );
		Assert.Contains( "capeff", Text( fields ), StringComparison.Ordinal );
	}

	/// <summary>Verifies cancellation returns the conventional shell code without leaking diagnostics.</summary>
	[Fact]
	public async Task CancellationReturnsConventionalCode() {
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		using var output = new MemoryStream();
		var status = await Tool.RunAsync(
			[],
			stdout: output,
			stderr: Stream.Null,
			processProvider: new FakeProvider(),
			metricsProvider: new FakeMetricsProvider(),
			supplementProvider: new FakeSupplementProvider(),
			accountResolver: new FakeAccountResolver(),
			currentProcessIdProvider: static () => 101,
			cancellationToken: cancellation.Token
		);
		Assert.Equal( 130, status );
	}

	private static Task<int> RunAsync( string[] args, Stream output, int currentProcessId = 101 ) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( output );
		return Tool.RunAsync(
			args,
			stdout: output,
			stderr: Stream.Null,
			processProvider: new FakeProvider(),
			metricsProvider: new FakeMetricsProvider(),
			supplementProvider: new FakeSupplementProvider(),
			accountResolver: new FakeAccountResolver(),
			currentProcessIdProvider: () => currentProcessId,
			nowProvider: static () => new DateTimeOffset( 2026, 7, 27, 12, 0, 0, TimeSpan.Zero )
		);
	}

	private static string Text( MemoryStream stream ) {
		ArgumentNullException.ThrowIfNull( stream );
		return Encoding.UTF8.GetString( stream.ToArray() );
	}

	private static string[] Lines( MemoryStream stream ) {
		ArgumentNullException.ThrowIfNull( stream );
		return Text( stream ).Split( Environment.NewLine, StringSplitOptions.RemoveEmptyEntries );
	}

	private static ProcObservedValue<T> Exact<T>( T value ) => ProcObservedValue<T>.Available(
		value,
		ProcObservationSource.DotNetProcessApi,
		ObservationFidelity.Equivalent
	);

	private sealed class FakeProvider : IProcProcessProvider {
		public ProcProcessCapabilities Capabilities => ProcProcessCapabilities.Enumeration
			| ProcProcessCapabilities.Identity
			| ProcProcessCapabilities.Parentage
			| ProcProcessCapabilities.ProcessGroups
			| ProcProcessCapabilities.Sessions
			| ProcProcessCapabilities.Users
			| ProcProcessCapabilities.Terminals
			| ProcProcessCapabilities.Namespaces
			| ProcProcessCapabilities.Containers
			| ProcProcessCapabilities.CommandLine
			| ProcProcessCapabilities.CpuTimes
			| ProcProcessCapabilities.Memory
			| ProcProcessCapabilities.Priority
			| ProcProcessCapabilities.Threads;

		public Task<ProcProcessCollection> GetProcessesAsync( CancellationToken cancellationToken = default ) {
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult( new ProcProcessCollection( [ CreateProcess( 101 ), CreateProcess( 202 ) ] ) );
		}

		public Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync( int processId, CancellationToken cancellationToken = default ) {
			if ( 0 >= processId ) {
				throw new ArgumentOutOfRangeException( nameof( processId ) );
			}
			cancellationToken.ThrowIfCancellationRequested();
			var process = processId switch {
				101 => CreateProcess( 101 ),
				202 => CreateProcess( 202 ),
				_ => null
			};
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

	private sealed class LargeProvider : IProcProcessProvider {
		private readonly int count;
		public ProcProcessCapabilities Capabilities => ProcProcessCapabilities.Enumeration | ProcProcessCapabilities.Identity;

		public LargeProvider( int count ) {
			if ( 0 > count ) {
				throw new ArgumentOutOfRangeException( nameof( count ) );
			}
			this.count = count;
		}

		public Task<ProcProcessCollection> GetProcessesAsync( CancellationToken cancellationToken = default ) {
			cancellationToken.ThrowIfCancellationRequested();
			var processes = Enumerable.Range( 1, this.count )
				.Select( processId => new ProcProcessSnapshot( new ProcessIdentity( processId ) ) {
					CommandName = Exact( "worker" ),
					CommandLineArguments = Exact<IReadOnlyList<string>>( [ "worker" ] )
				} )
				.ToArray();
			return Task.FromResult( new ProcProcessCollection( processes ) );
		}

		public Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync( int processId, CancellationToken cancellationToken = default ) {
			if ( 0 >= processId ) {
				throw new ArgumentOutOfRangeException( nameof( processId ) );
			}
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult( ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Unsupported ) );
		}

		public Task<ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>> GetMemoryMapsAsync( int processId, CancellationToken cancellationToken = default ) {
			if ( 0 >= processId ) {
				throw new ArgumentOutOfRangeException( nameof( processId ) );
			}
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.Unsupported ) );
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
			var result = new List<ProcMatchCandidate>();
			foreach ( var process in processes ) {
				result.Add( new ProcMatchCandidate( process, Supplement( process.ProcessId ) ) );
				if ( includeLightweightTasks && 202 == process.ProcessId ) {
					var task = CreateProcess( 203 );
					result.Add( new ProcMatchCandidate( task, Supplement( 202 ) ) );
				}
			}
			return Task.FromResult<IReadOnlyList<ProcMatchCandidate>>( result );
		}

		private static ProcMatchSupplement Supplement( int threadGroupId ) {
			var elapsed = ( 101 == threadGroupId )
				? TimeSpan.FromHours( 2 )
				: TimeSpan.FromMinutes( 30 )
			;
			var environment = ( 202 == threadGroupId )
				? new[] { "WORKER_COUNT=4", "MODE=batch" }
				: new[] { "SHELL=/bin/sh" }
			;
			var status = new Dictionary<string, string>( StringComparer.Ordinal ) {
				[ "SigPnd" ] = "0000000000000002",
				[ "SigBlk" ] = "0000000000000004",
				[ "SigIgn" ] = "0000000000000001",
				[ "SigCgt" ] = "0000000000000008",
				[ "CapInh" ] = "0000000000000000",
				[ "CapPrm" ] = "0000000000000025",
				[ "CapEff" ] = "0000000000000025",
				[ "CapBnd" ] = "000001ffffffffff",
				[ "CapAmb" ] = "0000000000000000"
			};
			return new ProcMatchSupplement {
				ThreadGroupId = threadGroupId,
				Elapsed = Exact( elapsed ),
				Environment = Exact<IReadOnlyList<string>>( environment ),
				LinuxStatusFields = Exact<IReadOnlyDictionary<string, string>>( status ),
				SecurityLabel = Exact( "system_u:system_r:worker_t:s0" )
			};
		}
	}

	private sealed class FakeMetricsProvider : IProcSystemMetricsProvider {
		public ProcSystemCapabilities Capabilities => ProcSystemCapabilities.Memory | ProcSystemCapabilities.Uptime;

		public Task<ProcSystemSnapshot> GetSnapshotAsync( CancellationToken cancellationToken = default ) {
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult( new ProcSystemSnapshot {
				Memory = Exact( new ProcMemoryInfo( 8UL * 1024UL * 1024UL * 1024UL, 2UL * 1024UL * 1024UL * 1024UL, 4UL * 1024UL * 1024UL * 1024UL ) ),
				Uptime = Exact( new ProcUptimeInfo( TimeSpan.FromDays( 10 ), null ) )
			} );
		}
	}

	private sealed class FakeAccountResolver : IProcPsAccountResolver {
		public bool TryResolveUser( string text, out uint id ) {
			ArgumentNullException.ThrowIfNull( text );
			if ( "alice" == text || "1000" == text ) {
				id = 1000;
				return true;
			}
			if ( "bob" == text || "1001" == text ) {
				id = 1001;
				return true;
			}
			id = 0;
			return false;
		}

		public bool TryResolveGroup( string text, out uint id ) {
			ArgumentNullException.ThrowIfNull( text );
			return TryResolveUser( text, out id );
		}

		public bool TryGetUserName( uint id, out string name ) {
			if ( 1000 == id ) {
				name = "alice";
				return true;
			}
			if ( 1001 == id ) {
				name = "bob";
				return true;
			}
			name = string.Empty;
			return false;
		}

		public bool TryGetGroupName( uint id, out string name ) => this.TryGetUserName( id, out name );
	}

	private static ProcProcessSnapshot CreateProcess( int processId ) {
		if ( 101 == processId ) {
			return new ProcProcessSnapshot( new ProcessIdentity( 101 ) ) {
				CommandName = Exact( "shell" ),
				CommandLineArguments = Exact<IReadOnlyList<string>>( [ "shell", "-l" ] ),
				State = Exact( ProcProcessState.Sleeping ),
				ParentProcessId = Exact( 1 ),
				ProcessGroupId = Exact( 101 ),
				SessionId = Exact( 101 ),
				RealUserId = Exact<uint>( 1000 ),
				EffectiveUserId = Exact<uint>( 1000 ),
				RealGroupId = Exact<uint>( 1000 ),
				EffectiveGroupId = Exact<uint>( 1000 ),
				Terminal = Exact( new ProcTerminalInfo( 1, "pts/1" ) ),
				UserCpuTicks = Exact( (ulong)TimeSpan.FromSeconds( 5 ).Ticks ),
				SystemCpuTicks = Exact( (ulong)TimeSpan.FromSeconds( 2 ).Ticks ),
				ResidentMemoryBytes = Exact<ulong>( 64UL * 1024UL * 1024UL ),
				VirtualMemoryBytes = Exact<ulong>( 512UL * 1024UL * 1024UL ),
				NiceValue = Exact( 0 ),
				ThreadCount = Exact( 1 ),
				NamespaceProcessIds = Exact<IReadOnlyList<int>>( [ 101 ] ),
				Namespaces = Exact<IReadOnlyDictionary<string, ProcNamespaceInfo>>( Namespaces( 101 ) ),
				Container = Exact( new ProcContainerInfo( "/user.slice" ) )
			};
		}
		if ( 202 == processId || 203 == processId ) {
			return new ProcProcessSnapshot( new ProcessIdentity( processId ) ) {
				CommandName = Exact( "worker" ),
				CommandLineArguments = Exact<IReadOnlyList<string>>( [ "worker", "--jobs" ] ),
				State = Exact( ProcProcessState.Running ),
				ParentProcessId = Exact( 101 ),
				ProcessGroupId = Exact( 101 ),
				SessionId = Exact( 101 ),
				RealUserId = Exact<uint>( 1001 ),
				EffectiveUserId = Exact<uint>( 1001 ),
				RealGroupId = Exact<uint>( 1001 ),
				EffectiveGroupId = Exact<uint>( 1001 ),
				Terminal = ProcObservedValue<ProcTerminalInfo>.Missing( ProcObservationAvailability.Unavailable ),
				UserCpuTicks = Exact( (ulong)TimeSpan.FromSeconds( 1 ).Ticks ),
				SystemCpuTicks = Exact( (ulong)TimeSpan.FromSeconds( 1 ).Ticks ),
				ResidentMemoryBytes = Exact<ulong>( 8UL * 1024UL * 1024UL ),
				VirtualMemoryBytes = Exact<ulong>( 128UL * 1024UL * 1024UL ),
				NiceValue = Exact( 5 ),
				ThreadCount = Exact( 2 ),
				NamespaceProcessIds = Exact<IReadOnlyList<int>>( [ processId, 2 ] ),
				Namespaces = Exact<IReadOnlyDictionary<string, ProcNamespaceInfo>>( Namespaces( 202 ) ),
				Container = Exact( new ProcContainerInfo( "/system.slice/worker.service", "container-202", "systemd" ) )
			};
		}
		throw new ArgumentOutOfRangeException( nameof( processId ) );
	}

	private static IReadOnlyDictionary<string, ProcNamespaceInfo> Namespaces( int seed ) => new Dictionary<string, ProcNamespaceInfo>( StringComparer.Ordinal ) {
		[ "ipc" ] = new( "ipc", $"ipc:[4026532{seed:D3}]", (ulong)( 4026532000 + seed ) ),
		[ "mnt" ] = new( "mnt", $"mnt:[4026532{seed:D3}]", (ulong)( 4026532100 + seed ) ),
		[ "net" ] = new( "net", $"net:[4026532{seed:D3}]", (ulong)( 4026532150 + seed ) ),
		[ "pid" ] = new( "pid", $"pid:[4026532{seed:D3}]", (ulong)( 4026532000 + seed ) ),
		[ "user" ] = new( "user", $"user:[4026532{seed:D3}]", (ulong)( 4026532300 + seed ) ),
		[ "uts" ] = new( "uts", $"uts:[4026532{seed:D3}]", (ulong)( 4026532400 + seed ) )
	};
}
