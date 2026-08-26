/*
	Icod.ProcPs.HugeTop.Tests
	Tests for the hugetop command implementation.
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

namespace Icod.ProcPs.HugeTop.Tests;

using System.Text;
using Icod.Processes;
using Icod.ProcPs.Shared;
using Icod.Timing;
using Xunit;

/// <summary>Exercises the DCurses-backed procps-ng compatible <c>hugetop</c> migration.</summary>
public sealed class HugeTopCommandTests {
	private static readonly DateTimeOffset ObservationTime = new(
		2026,
		8,
		26,
		12,
		34,
		56,
		TimeSpan.Zero
	);

	[Fact]
	public async Task OnceReportAggregatesNodesAndRendersProcesses() {
		CommandResult result = await RunCoreAsync(
			[ "--once" ],
			new FakeTerminal( new FakeClock() )
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Contains( "node(s): 2.0Mi - 3/7", result.Stdout, StringComparison.Ordinal );
		Assert.Contains( "42", result.Stdout, StringComparison.Ordinal );
		Assert.Contains( "server", result.Stdout, StringComparison.Ordinal );
		Assert.Contains( "worker", result.Stdout, StringComparison.Ordinal );
		Assert.True(
			result.Stdout.IndexOf( "server", StringComparison.Ordinal )
				< result.Stdout.IndexOf( "worker", StringComparison.Ordinal )
		);
		Assert.Equal( string.Empty, result.Stderr );
	}

	[Fact]
	public async Task NumaAndHumanModesRenderPerNodePools() {
		FakeClock clock = new();
		CommandResult result = await RunCoreAsync(
			[ "--once", "--numa", "--human" ],
			new FakeTerminal( clock ),
			clock
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Contains( "node0: 2.0Mi - 2/4", result.Stdout, StringComparison.Ordinal );
		Assert.Contains( "node1: 2.0Mi - 1/3", result.Stdout, StringComparison.Ordinal );
		Assert.Contains( "1.0Mi", result.Stdout, StringComparison.Ordinal );
	}

	[Fact]
	public async Task OnceModeDoesNotOpenInteractiveTerminal() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );
		FakeHugePageProvider provider = new( AvailableSnapshot() );

		CommandResult result = await RunCoreAsync(
			[ "--once" ],
			terminal,
			clock,
			provider
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 1, provider.CaptureCount );
		Assert.Equal( 0, terminal.OpenCount );
		Assert.False( terminal.Disposed );
	}

	[Fact]
	public async Task InteractiveModeUsesDefaultCadenceAndDisposesOnInterrupt() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );
		terminal.Events.Enqueue( new ScheduledTerminalEvent( HugeTopTerminalEventKind.Interrupt ) );
		FakeHugePageProvider provider = new( AvailableSnapshot() );

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Equal( TimeSpan.FromSeconds( 3 ), Assert.Single( terminal.Waits ) );
		Assert.Single( terminal.Frames );
		Assert.Contains( "node(s):", terminal.Frames[ 0 ].Lines[ 1 ], StringComparison.Ordinal );
		Assert.Equal( 1, provider.CaptureCount );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task DelayOptionControlsRefreshCadence() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );
		terminal.Events.Enqueue( new ScheduledTerminalEvent( HugeTopTerminalEventKind.Interrupt ) );

		CommandResult result = await RunCoreAsync(
			[ "--delay", "7" ],
			terminal,
			clock
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Equal( TimeSpan.FromSeconds( 7 ), Assert.Single( terminal.Waits ) );
	}

	[Fact]
	public async Task TimeoutBeginsNextRefreshCycle() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );
		terminal.Events.Enqueue( new ScheduledTerminalEvent( HugeTopTerminalEventKind.Timeout ) );
		terminal.Events.Enqueue( new ScheduledTerminalEvent( HugeTopTerminalEventKind.Interrupt ) );
		FakeHugePageProvider provider = new( AvailableSnapshot(), AvailableSnapshot() );

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Equal( 2, provider.CaptureCount );
		Assert.Equal( 2, terminal.Frames.Count );
		Assert.Equal( 2, terminal.Waits.Count );
	}

	[Fact]
	public async Task CaptureTimeCountsAgainstRefreshInterval() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );
		terminal.Events.Enqueue( new ScheduledTerminalEvent( HugeTopTerminalEventKind.Interrupt ) );
		FakeHugePageProvider provider = new( AvailableSnapshot() ) {
			CaptureAction = () => clock.Advance( TimeSpan.FromSeconds( 1 ) )
		};

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Equal( TimeSpan.FromSeconds( 2 ), Assert.Single( terminal.Waits ) );
	}

	[Fact]
	public async Task ResizeRerendersCurrentSnapshotWithoutResampling() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new HugeTopTerminalDimensions( 60, 10 )
		);
		terminal.Events.Enqueue(
			new ScheduledTerminalEvent(
				HugeTopTerminalEventKind.Resize,
				new HugeTopTerminalDimensions( 70, 11 )
			)
		);
		terminal.Events.Enqueue( new ScheduledTerminalEvent( HugeTopTerminalEventKind.Interrupt ) );
		FakeHugePageProvider provider = new( AvailableSnapshot() );

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Equal( 1, provider.CaptureCount );
		Assert.Equal( 2, terminal.Frames.Count );
		Assert.Equal( 60, terminal.Frames[ 0 ].Columns );
		Assert.Equal( 10, terminal.Frames[ 0 ].Rows );
		Assert.Equal( 70, terminal.Frames[ 1 ].Columns );
		Assert.Equal( 11, terminal.Frames[ 1 ].Rows );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task ResumeRequestsPhysicalRepaintWithoutResampling() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );
		terminal.Events.Enqueue( new ScheduledTerminalEvent( HugeTopTerminalEventKind.Repaint ) );
		terminal.Events.Enqueue( new ScheduledTerminalEvent( HugeTopTerminalEventKind.Interrupt ) );
		FakeHugePageProvider provider = new( AvailableSnapshot() );

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Equal( 1, provider.CaptureCount );
		Assert.Equal( 1, terminal.RepaintCount );
		Assert.Single( terminal.Frames );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task CallerCancellationDuringWaitReturnsCanceledAndDisposes() {
		using CancellationTokenSource cancellation = new();
		FakeClock clock = new();
		FakeTerminal terminal = new( clock ) {
			ReadAction = cancellation.Cancel
		};
		FakeHugePageProvider provider = new( AvailableSnapshot() );

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider,
			cancellation.Token
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Equal( 1, provider.CaptureCount );
		Assert.Single( terminal.Frames );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task RedirectedInteractiveOutputIsRejectedBeforeSampling() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock, isInteractive: false );
		FakeHugePageProvider provider = new( AvailableSnapshot() );

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider
		);

		Assert.Equal( 1, result.ExitCode );
		Assert.Contains( "use --once", result.Stderr, StringComparison.Ordinal );
		Assert.Equal( 0, provider.CaptureCount );
		Assert.Empty( terminal.Frames );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task TooSmallTerminalFailsBeforeSamplingAndDisposes() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new HugeTopTerminalDimensions( 19, 5 )
		);
		FakeHugePageProvider provider = new( AvailableSnapshot() );

		CommandResult result = await RunCoreAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider
		);

		Assert.Equal( 1, result.ExitCode );
		Assert.Contains( "at least 20 columns by 5 rows", result.Stderr, StringComparison.Ordinal );
		Assert.Equal( 0, provider.CaptureCount );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task UnsupportedProviderReturnsControlledFailure() {
		FakeClock clock = new();
		ProcObservedValue<ProcHugePageSnapshot> unavailable =
			ProcObservedValue<ProcHugePageSnapshot>.Missing(
				ProcObservationAvailability.Unsupported,
				"huge pages are unavailable"
			);

		CommandResult result = await RunCoreAsync(
			[ "--once" ],
			new FakeTerminal( clock ),
			clock,
			new FakeHugePageProvider( unavailable )
		);

		Assert.Equal( 1, result.ExitCode );
		Assert.Contains( "huge pages are unavailable", result.Stderr, StringComparison.Ordinal );
	}

	[Fact]
	public async Task SystemProviderIsExplicitlyUnsupportedOutsideLinux() {
		if ( OperatingSystem.IsLinux() ) {
			return;
		}

		ProcObservedValue<ProcHugePageSnapshot> observed =
			await SystemProcHugePageProvider.Instance.GetSnapshotAsync();
		Assert.False( observed.HasValue );
		Assert.Equal( ProcObservationAvailability.Unsupported, observed.Availability );
	}

	[Fact]
	public async Task CommandLineValidationHelpAndVersionDoNotOpenTerminal() {
		FakeClock clock = new();
		FakeTerminal terminal = new( clock );
		FakeHugePageProvider provider = new( AvailableSnapshot() );

		CommandResult help = await RunCoreAsync(
			[ "--help" ],
			terminal,
			clock,
			provider
		);
		CommandResult version = await RunCoreAsync(
			[ "--version" ],
			terminal,
			clock,
			provider
		);
		CommandResult delay = await RunCoreAsync(
			[ "--delay=0" ],
			terminal,
			clock,
			provider
		);
		CommandResult operand = await RunCoreAsync(
			[ "unexpected" ],
			terminal,
			clock,
			provider
		);

		Assert.Equal( 0, help.ExitCode );
		Assert.Contains( "Usage:", help.Stdout, StringComparison.Ordinal );
		Assert.Equal( 0, version.ExitCode );
		Assert.Contains( "Icod.ProcPs.HugeTop (0.9.0-Alpha-7) inspired by procps-ng 4.0.6", version.Stdout, StringComparison.Ordinal );
		Assert.Equal( 1, delay.ExitCode );
		Assert.Contains( "delay must be positive", delay.Stderr, StringComparison.Ordinal );
		Assert.Equal( 1, operand.ExitCode );
		Assert.Contains( "unexpected operand", operand.Stderr, StringComparison.Ordinal );
		Assert.Equal( 0, terminal.OpenCount );
		Assert.Equal( 0, provider.CaptureCount );
	}

	[Fact]
	public async Task LinuxProviderReadsSysfsPoolsAndSmapsHugetlbMetrics() {
		string root = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			$".icod-hugetop-{Guid.NewGuid():N}"
		);
		try {
			string poolRoot = System.IO.Path.Combine(
				root,
				"node0",
				"hugepages",
				"hugepages-2048kB"
			);
			Directory.CreateDirectory( poolRoot );
			await File.WriteAllTextAsync(
				System.IO.Path.Combine( poolRoot, "nr_hugepages" ),
				"4\n"
			);
			await File.WriteAllTextAsync(
				System.IO.Path.Combine( poolRoot, "free_hugepages" ),
				"2\n"
			);

			ProcProcessSnapshot process = new(
				new ProcessIdentity(
					42,
					new ProcessReuseToken( "test", "start-42" )
				)
			) {
				CommandName = Exact( "server" )
			};
			ProcMemoryMapSet maps = new(
				[
					new ProcMemoryMapRegion(
						new ProcMemoryMapEntry(
							0x1000,
							0x2000,
							"rw-s",
							0,
							"00:00",
							0,
							"[huge]"
						),
						[
							new ProcMemoryMapMetric( "Shared_Hugetlb", 512, "kB" ),
							new ProcMemoryMapMetric( "Private_Hugetlb", 1024, "kB" )
						]
					)
				],
				isDetailed: true
			);
			LinuxProcHugePageProvider provider = new(
				new FakeProcessProvider( new ProcProcessCollection( [ process ] ) ),
				new FakeMemoryMapProvider( Exact( maps ) ),
				root
			);

			ProcObservedValue<ProcHugePageSnapshot> observed =
				await provider.GetSnapshotAsync();

			Assert.True( observed.HasValue );
			Assert.Equal( ProcObservationSource.LinuxSysfs, observed.Source );
			ProcHugePageNode node = Assert.Single( observed.Value.Nodes );
			Assert.Equal( 0, node.NodeId );
			ProcHugePagePool pool = Assert.Single( node.Pools );
			Assert.Equal( 2UL * 1024 * 1024, pool.PageSizeBytes );
			Assert.Equal( 4UL, pool.TotalPages );
			Assert.Equal( 2UL, pool.FreePages );
			ProcHugePageProcess usage = Assert.Single( observed.Value.Processes );
			Assert.Equal( 42, usage.ProcessId );
			Assert.Equal( "server", usage.CommandName );
			Assert.Equal( 512UL * 1024, usage.SharedBytes );
			Assert.Equal( 1024UL * 1024, usage.PrivateBytes );
		} finally {
			if ( Directory.Exists( root ) ) {
				Directory.Delete( root, recursive: true );
			}
		}
	}

	private static async Task<CommandResult> RunCoreAsync(
		IReadOnlyList<string> args,
		FakeTerminal terminal,
		FakeClock? clock = null,
		FakeHugePageProvider? provider = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( terminal );
		clock ??= terminal.Clock;
		provider ??= new FakeHugePageProvider( AvailableSnapshot() );
		using MemoryStream stdout = new();
		using MemoryStream stderr = new();
		int exitCode = await Command.RunAsyncCore(
			args,
			stdout,
			stderr,
			provider,
			terminal,
			clock,
			() => ObservationTime,
			cancellationToken
		);
		return new CommandResult(
			exitCode,
			Encoding.UTF8.GetString( stdout.ToArray() ),
			Encoding.UTF8.GetString( stderr.ToArray() )
		);
	}

	private static ProcObservedValue<ProcHugePageSnapshot> AvailableSnapshot() => Exact(
		new ProcHugePageSnapshot(
			[
				new ProcHugePageNode(
					0,
					[ new ProcHugePagePool( 2UL * 1024 * 1024, 4, 2 ) ]
				),
				new ProcHugePageNode(
					1,
					[ new ProcHugePagePool( 2UL * 1024 * 1024, 3, 1 ) ]
				)
			],
			[
				new ProcHugePageProcess(
					7,
					"worker",
					512UL * 1024,
					0
				),
				new ProcHugePageProcess(
					42,
					"server",
					1024UL * 1024,
					2UL * 1024 * 1024
				)
			]
		)
	);

	private static ProcObservedValue<T> Exact<T>( T value ) =>
		ProcObservedValue<T>.Available(
			value,
			ProcObservationSource.LinuxProcfs,
			ProcObservationFidelity.Exact
		);

	private sealed record CommandResult(
		int ExitCode,
		string Stdout,
		string Stderr
	);

	private sealed record ScheduledTerminalEvent(
		HugeTopTerminalEventKind Kind,
		HugeTopTerminalDimensions? Dimensions = null
	);

	private sealed class FakeHugePageProvider : IProcHugePageProvider {
		private readonly Queue<ProcObservedValue<ProcHugePageSnapshot>> snapshots;
		private ProcObservedValue<ProcHugePageSnapshot> current;

		internal FakeHugePageProvider(
			params ProcObservedValue<ProcHugePageSnapshot>[] snapshots
		) {
			ArgumentNullException.ThrowIfNull( snapshots );
			if ( 0 == snapshots.Length ) {
				throw new ArgumentException(
					"At least one huge-page snapshot is required.",
					nameof( snapshots )
				);
			}
			this.snapshots = new Queue<ProcObservedValue<ProcHugePageSnapshot>>( snapshots );
			this.current = snapshots[ 0 ];
		}

		internal int CaptureCount { get; private set; }
		internal Action? CaptureAction { get; init; }

		public Task<ProcObservedValue<ProcHugePageSnapshot>> GetSnapshotAsync(
			CancellationToken cancellationToken = default
		) {
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
	}

	private sealed class FakeClock : IMonotonicClock {
		private long timestamp;
		internal List<TimeSpan> Delays { get; } = [];

		public long GetTimestamp() => this.timestamp;

		public TimeSpan GetElapsedTime(
			long startingTimestamp,
			long endingTimestamp
		) => TimeSpan.FromTicks( endingTimestamp - startingTimestamp );

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

	private sealed class FakeTerminal : IHugeTopTerminalSessionFactory, IHugeTopTerminalSession {
		private HugeTopTerminalDimensions dimensions;

		internal FakeTerminal(
			FakeClock clock,
			HugeTopTerminalDimensions? dimensions = null,
			bool isInteractive = true
		) {
			ArgumentNullException.ThrowIfNull( clock );
			this.Clock = clock;
			this.dimensions = dimensions ?? new HugeTopTerminalDimensions( 80, 12 );
			this.IsInteractive = isInteractive;
		}

		internal FakeClock Clock { get; }
		public bool IsInteractive { get; }
		public CancellationToken TerminationToken => CancellationToken.None;
		internal Queue<ScheduledTerminalEvent> Events { get; } = new();
		internal List<TimeSpan> Waits { get; } = [];
		internal List<HugeTopRenderFrame> Frames { get; } = [];
		internal int OpenCount { get; private set; }
		internal int RepaintCount { get; private set; }
		internal Action? ReadAction { get; init; }
		internal bool Disposed { get; private set; }

		public ValueTask<IHugeTopTerminalSession> OpenAsync(
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			this.OpenCount++;
			return ValueTask.FromResult<IHugeTopTerminalSession>( this );
		}

		public HugeTopTerminalDimensions GetDimensions() => this.dimensions;

		public ValueTask<HugeTopTerminalEvent> ReadEventAsync(
			TimeSpan timeout,
			CancellationToken cancellationToken = default
		) {
			if ( TimeSpan.Zero > timeout ) {
				throw new ArgumentOutOfRangeException( nameof( timeout ) );
			}
			cancellationToken.ThrowIfCancellationRequested();
			this.Waits.Add( timeout );
			this.ReadAction?.Invoke();
			cancellationToken.ThrowIfCancellationRequested();
			ScheduledTerminalEvent scheduled = (0 < this.Events.Count)
				? this.Events.Dequeue()
				: new ScheduledTerminalEvent( HugeTopTerminalEventKind.Interrupt )
			;
			if ( scheduled.Dimensions.HasValue ) {
				this.dimensions = scheduled.Dimensions.Value;
			}
			if ( HugeTopTerminalEventKind.Timeout == scheduled.Kind ) {
				this.Clock.Advance( timeout );
			}
			return ValueTask.FromResult( new HugeTopTerminalEvent( scheduled.Kind ) );
		}

		public ValueTask RenderAsync(
			HugeTopRenderFrame frame,
			CancellationToken cancellationToken = default
		) {
			ArgumentNullException.ThrowIfNull( frame );
			cancellationToken.ThrowIfCancellationRequested();
			this.Frames.Add( frame );
			return ValueTask.CompletedTask;
		}

		public ValueTask RepaintAsync(
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			this.RepaintCount++;
			return ValueTask.CompletedTask;
		}

		public ValueTask DisposeAsync() {
			this.Disposed = true;
			return ValueTask.CompletedTask;
		}
	}

	private sealed class FakeProcessProvider : IProcProcessProvider {
		private readonly ProcProcessCollection processes;

		internal FakeProcessProvider( ProcProcessCollection processes ) {
			ArgumentNullException.ThrowIfNull( processes );
			this.processes = processes;
		}

		public ProcProcessCapabilities Capabilities => ProcProcessCapabilities.Enumeration;

		public Task<ProcProcessCollection> GetProcessesAsync(
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult( this.processes );
		}

		public Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync(
			int processId,
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			ProcProcessSnapshot? process = this.processes.Processes.FirstOrDefault(
				candidate => candidate.ProcessId == processId
			);
			return Task.FromResult(
				(process is null)
					? ProcObservedValue<ProcProcessSnapshot>.Missing(
						ProcObservationAvailability.Vanished
					)
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

	private sealed class FakeMemoryMapProvider : IProcMemoryMapProvider {
		private readonly ProcObservedValue<ProcMemoryMapSet> maps;

		internal FakeMemoryMapProvider( ProcObservedValue<ProcMemoryMapSet> maps ) {
			ArgumentNullException.ThrowIfNull( maps );
			this.maps = maps;
		}

		public Task<ProcObservedValue<ProcMemoryMapSet>> ObserveAsync(
			ProcProcessSnapshot process,
			bool detailed = false,
			CancellationToken cancellationToken = default
		) {
			ArgumentNullException.ThrowIfNull( process );
			cancellationToken.ThrowIfCancellationRequested();
			Assert.True( detailed );
			return Task.FromResult( this.maps );
		}
	}
}
