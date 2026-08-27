/*
	Icod.ProcPs.SlabTop.Tests
	Tests for the slabtop command implementation.
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

namespace Icod.ProcPs.SlabTop.Tests;

using System.Text;
using Icod.ProcPs.Shared;
using Icod.Timing;
using Xunit;

/// <summary>Exercises the DCurses-backed procps-ng compatible <c>slabtop</c> migration.</summary>
public sealed class SlabTopCommandTests {
	[Fact]
	public async Task OnceReportRendersSummaryAndDefaultSort() {
		CommandResult result = await RunOnceAsync(
			[ "--once" ],
			AvailableSlabs()
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Contains( "Active / Total Objects", result.Stdout, StringComparison.Ordinal );
		Assert.Contains( "CACHE SIZE NAME", result.Stdout, StringComparison.Ordinal );
		Assert.True(
			result.Stdout.IndexOf( "z_large_cache", StringComparison.Ordinal )
				< result.Stdout.IndexOf( "a_small_cache", StringComparison.Ordinal )
		);
		Assert.Equal( string.Empty, result.Stderr );
	}

	[Fact]
	public async Task SortAndHumanOptionsAreApplied() {
		CommandResult result = await RunOnceAsync(
			[ "--once", "--sort=n", "--human" ],
			AvailableSlabs()
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.True(
			result.Stdout.IndexOf( "a_small_cache", StringComparison.Ordinal )
				< result.Stdout.IndexOf( "z_large_cache", StringComparison.Ordinal )
		);
		Assert.Contains( "1.0Ki", result.Stdout, StringComparison.Ordinal );
	}

	[Theory]
	[InlineData( "a" )]
	[InlineData( "b" )]
	[InlineData( "c" )]
	[InlineData( "l" )]
	[InlineData( "v" )]
	[InlineData( "n" )]
	[InlineData( "o" )]
	[InlineData( "p" )]
	[InlineData( "s" )]
	[InlineData( "u" )]
	public async Task DocumentedSortCriteriaAreAccepted( string criterion ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( criterion );
		CommandResult result = await RunOnceAsync(
			[ "--once", $"--sort={criterion}" ],
			AvailableSlabs()
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Contains( "z_large_cache", result.Stdout, StringComparison.Ordinal );
	}

	[Fact]
	public void SlabInfoParserUsesKernelSlabDataCounts() {
		string text = string.Join(
			Environment.NewLine,
			"slabinfo - version: 2.1",
			"cache_a 10 20 64 8 1 : tunables 0 0 0 : slabdata 2 4 0",
			string.Empty
		);

		IReadOnlyList<ProcSlabCacheEntry> entries = ProcKernelMemoryParsers.ParseSlabInfo( text );
		ProcSlabCacheEntry entry = Assert.Single( entries );
		Assert.Equal( 2UL, entry.ActiveSlabs );
		Assert.Equal( 4UL, entry.TotalSlabs );
	}

	[Fact]
	public void SlabInfoParserRejectsMissingSlabData() {
		const string text = "cache_a 10 20 64 8 1";
		Assert.Throws<FormatException>(
			() => ProcKernelMemoryParsers.ParseSlabInfo( text )
		);
	}

	[Fact]
	public async Task InteractiveModeUsesDefaultCadenceAndDisposesOnInterrupt() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new SlabTopTerminalDimensions( 90, 14 )
		);
		terminal.Events.Enqueue( new ScheduledTerminalEvent( SlabTopTerminalEventKind.Timeout ) );
		terminal.Events.Enqueue( new ScheduledTerminalEvent( SlabTopTerminalEventKind.Interrupt ) );
		FakeSlabProvider provider = new( AvailableSlabs(), AvailableSlabs() );

		CommandResult result = await RunInteractiveAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Equal( TimeSpan.FromSeconds( 3 ), terminal.Waits[ 0 ] );
		Assert.Equal( 2, terminal.Frames.Count );
		Assert.Contains(
			"Active / Total Objects",
			terminal.Frames[ 0 ].Lines[ 0 ],
			StringComparison.Ordinal
		);
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task DelayOptionControlsRefreshCadence() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new SlabTopTerminalDimensions( 90, 14 )
		);
		terminal.Events.Enqueue( new ScheduledTerminalEvent( SlabTopTerminalEventKind.Interrupt ) );

		CommandResult result = await RunInteractiveAsync(
			[ "--delay=5" ],
			terminal,
			clock,
			new FakeSlabProvider( AvailableSlabs() )
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Equal( TimeSpan.FromSeconds( 5 ), terminal.Waits[ 0 ] );
	}

	[Fact]
	public async Task ResizeRedrawsCurrentSnapshotWithoutResampling() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new SlabTopTerminalDimensions( 80, 12 )
		);
		terminal.Events.Enqueue(
			new ScheduledTerminalEvent(
				SlabTopTerminalEventKind.Resize,
				new SlabTopTerminalDimensions( 100, 15 )
			)
		);
		terminal.Events.Enqueue( new ScheduledTerminalEvent( SlabTopTerminalEventKind.Interrupt ) );
		FakeSlabProvider provider = new( AvailableSlabs() );

		CommandResult result = await RunInteractiveAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Equal( 1, provider.CaptureCount );
		Assert.Equal( 2, terminal.Frames.Count );
		Assert.Equal( 80, terminal.Frames[ 0 ].Columns );
		Assert.Equal( 12, terminal.Frames[ 0 ].Rows );
		Assert.Equal( 100, terminal.Frames[ 1 ].Columns );
		Assert.Equal( 15, terminal.Frames[ 1 ].Rows );
	}

	[Fact]
	public async Task ResumeRequestsPhysicalRepaintWithoutResampling() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new SlabTopTerminalDimensions( 90, 14 )
		);
		terminal.Events.Enqueue( new ScheduledTerminalEvent( SlabTopTerminalEventKind.Repaint ) );
		terminal.Events.Enqueue( new ScheduledTerminalEvent( SlabTopTerminalEventKind.Interrupt ) );
		FakeSlabProvider provider = new( AvailableSlabs() );

		CommandResult result = await RunInteractiveAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Equal( 1, provider.CaptureCount );
		Assert.Equal( 1, terminal.RepaintCount );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task RedirectedInteractiveOutputIsRejectedButOnceIsSupported() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new SlabTopTerminalDimensions( 80, 25 ),
			isInteractive: false
		);
		FakeSlabProvider provider = new( AvailableSlabs() );

		CommandResult interactive = await RunInteractiveAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider
		);
		Assert.Equal( 1, interactive.ExitCode );
		Assert.Contains( "use --once", interactive.Stderr, StringComparison.Ordinal );
		Assert.Empty( terminal.Frames );
		Assert.True( terminal.Disposed );

		CommandResult once = await RunOnceAsync( [ "--once" ], AvailableSlabs() );
		Assert.Equal( 0, once.ExitCode );
	}

	[Fact]
	public async Task UnsupportedProviderReturnsControlledFailure() {
		ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>> unavailable =
			ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>.Missing(
				ProcObservationAvailability.Unsupported,
				"slabinfo is unavailable"
			);

		CommandResult result = await RunOnceAsync( [ "--once" ], unavailable );
		Assert.Equal( 1, result.ExitCode );
		Assert.Contains( "slabinfo is unavailable", result.Stderr, StringComparison.Ordinal );
	}

	[Fact]
	public async Task SystemProviderIsExplicitlyUnsupportedOutsideLinux() {
		if ( OperatingSystem.IsLinux() ) {
			return;
		}

		ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>> observed =
			await SystemProcSlabProvider.Instance.GetSlabsAsync();
		Assert.False( observed.HasValue );
		Assert.Equal( ProcObservationAvailability.Unsupported, observed.Availability );
	}

	[Fact]
	public async Task CommandLineValidationHelpAndVersionDoNotOpenTerminal() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new SlabTopTerminalDimensions( 90, 14 )
		);
		FakeSlabProvider provider = new( AvailableSlabs() );

		CommandResult conflict = await RunCoreAsync(
			[ "--delay", "2", "--once" ],
			terminal,
			clock,
			provider
		);
		Assert.Equal( 1, conflict.ExitCode );
		Assert.Contains( "Cannot combine -d and -o", conflict.Stderr, StringComparison.Ordinal );

		CommandResult help = await RunCoreAsync(
			[ "--help" ],
			terminal,
			clock,
			provider
		);
		Assert.Equal( 0, help.ExitCode );
		Assert.Contains( "Valid sort criteria", help.Stdout, StringComparison.Ordinal );

		CommandResult version = await RunCoreAsync(
			[ "--version" ],
			terminal,
			clock,
			provider
		);
		Assert.Equal( 0, version.ExitCode );
		Assert.Contains( "Icod.ProcPs.SlabTop (1.0.1) inspired by procps-ng 4.0.6", version.Stdout, StringComparison.Ordinal );
		Assert.Equal( 0, terminal.OpenCount );
	}

	[Fact]
	public async Task OnceModeDoesNotOpenInteractiveTerminal() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new SlabTopTerminalDimensions( 80, 25 )
		);
		FakeSlabProvider provider = new( AvailableSlabs() );

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
	public async Task InitialUnusableGeometryFailsBeforeSamplingAndDisposes() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new SlabTopTerminalDimensions( 39, 9 )
		);
		FakeSlabProvider provider = new( AvailableSlabs() );

		CommandResult result = await RunInteractiveAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider
		);

		Assert.Equal( 1, result.ExitCode );
		Assert.Contains( "too small", result.Stderr, StringComparison.Ordinal );
		Assert.Equal( 1, terminal.OpenCount );
		Assert.Equal( 0, provider.CaptureCount );
		Assert.Empty( terminal.Frames );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task ResizeToUnusableGeometryFailsWithoutResamplingAndDisposes() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new SlabTopTerminalDimensions( 80, 12 )
		);
		terminal.Events.Enqueue(
			new ScheduledTerminalEvent(
				SlabTopTerminalEventKind.Resize,
				new SlabTopTerminalDimensions( 39, 8 )
			)
		);
		FakeSlabProvider provider = new( AvailableSlabs() );

		CommandResult result = await RunInteractiveAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider
		);

		Assert.Equal( 1, result.ExitCode );
		Assert.Contains( "too small", result.Stderr, StringComparison.Ordinal );
		Assert.Equal( 1, provider.CaptureCount );
		Assert.Single( terminal.Frames );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task InteractiveProviderFailureIsControlledAndDisposes() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new SlabTopTerminalDimensions( 90, 14 )
		);
		ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>> unavailable =
			ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>.Missing(
				ProcObservationAvailability.AccessDenied,
				"slabinfo permission denied"
			);
		FakeSlabProvider provider = new( unavailable );

		CommandResult result = await RunInteractiveAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider
		);

		Assert.Equal( 1, result.ExitCode );
		Assert.Contains(
			"slabinfo permission denied",
			result.Stderr,
			StringComparison.Ordinal
		);
		Assert.Equal( 1, provider.CaptureCount );
		Assert.Empty( terminal.Frames );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task CaptureTimeCountsAgainstRefreshInterval() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new SlabTopTerminalDimensions( 90, 14 )
		);
		terminal.Events.Enqueue( new ScheduledTerminalEvent( SlabTopTerminalEventKind.Interrupt ) );
		FakeSlabProvider provider = new( AvailableSlabs() ) {
			CaptureAction = () => clock.Advance( TimeSpan.FromSeconds( 1 ) )
		};

		CommandResult result = await RunInteractiveAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Single( terminal.Waits );
		Assert.Equal( TimeSpan.FromSeconds( 2 ), terminal.Waits[ 0 ] );
		Assert.Equal( 1, provider.CaptureCount );
		Assert.True( terminal.Disposed );
	}

	[Fact]
	public async Task CallerCancellationDuringWaitReturnsCanceledAndDisposes() {
		using CancellationTokenSource cancellation = new();
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new SlabTopTerminalDimensions( 90, 14 )
		) {
			BeforeRead = cancellation.Cancel
		};
		FakeSlabProvider provider = new( AvailableSlabs() );

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
	public async Task InputEventsDoNotForceResampleOrExit() {
		FakeClock clock = new();
		FakeTerminal terminal = new(
			clock,
			new SlabTopTerminalDimensions( 90, 14 )
		);
		terminal.Events.Enqueue( new ScheduledTerminalEvent( SlabTopTerminalEventKind.Input ) );
		terminal.Events.Enqueue( new ScheduledTerminalEvent( SlabTopTerminalEventKind.Interrupt ) );
		FakeSlabProvider provider = new( AvailableSlabs() );

		CommandResult result = await RunInteractiveAsync(
			Array.Empty<string>(),
			terminal,
			clock,
			provider
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Equal( 1, provider.CaptureCount );
		Assert.Single( terminal.Frames );
		Assert.Equal( 2, terminal.Waits.Count );
		Assert.Equal( 0, terminal.RepaintCount );
		Assert.True( terminal.Disposed );
	}

	private static ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>> AvailableSlabs() {
		IReadOnlyList<ProcSlabCacheEntry> entries = [
			new ProcSlabCacheEntry(
				"a_small_cache", 12UL, 20UL, 64UL, 8UL, 1UL, 2UL, 3UL
			),
			new ProcSlabCacheEntry(
				"z_large_cache", 40UL, 80UL, 1024UL, 4UL, 2UL, 15UL, 20UL
			)
		];
		return ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>.Available(
			entries,
			ProcObservationSource.LinuxProcfs,
			ProcObservationFidelity.Exact
		);
	}

	private static Task<CommandResult> RunOnceAsync(
		IReadOnlyList<string> args,
		ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>> slabs
	) {
		FakeClock clock = new();
		return RunCoreAsync(
			args,
			new FakeTerminal( clock, new SlabTopTerminalDimensions( 80, 25 ) ),
			clock,
			new FakeSlabProvider( slabs )
		);
	}

	private static Task<CommandResult> RunInteractiveAsync(
		IReadOnlyList<string> args,
		FakeTerminal terminal,
		FakeClock clock,
		FakeSlabProvider provider
	) {
		return RunCoreAsync( args, terminal, clock, provider );
	}

	private static async Task<CommandResult> RunCoreAsync(
		IReadOnlyList<string> args,
		FakeTerminal terminal,
		FakeClock clock,
		FakeSlabProvider provider,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( terminal );
		ArgumentNullException.ThrowIfNull( clock );
		ArgumentNullException.ThrowIfNull( provider );
		using MemoryStream stdout = new();
		using MemoryStream stderr = new();
		int exitCode = await Command.RunAsyncCore(
			args,
			stdout,
			stderr,
			provider,
			new FakeTerminalFactory( terminal ),
			clock,
			cancellationToken
		);
		return new CommandResult(
			exitCode,
			Encoding.UTF8.GetString( stdout.ToArray() ),
			Encoding.UTF8.GetString( stderr.ToArray() )
		);
	}

	private sealed record CommandResult( int ExitCode, string Stdout, string Stderr );

	private sealed class FakeSlabProvider : IProcSlabProvider {
		private readonly Queue<ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>> values;
		private ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>> last;

		internal FakeSlabProvider(
			params ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>[] values
		) {
			ArgumentNullException.ThrowIfNull( values );
			if ( 0 == values.Length ) {
				throw new ArgumentException(
					"At least one slab observation is required.",
					nameof( values )
				);
			}
			this.values = new Queue<ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>>( values );
			this.last = values[ ^1 ];
		}

		internal int CaptureCount { get; private set; }
		internal Action? CaptureAction { get; set; }

		public Task<ProcObservedValue<IReadOnlyList<ProcSlabCacheEntry>>> GetSlabsAsync(
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			this.CaptureCount++;
			this.CaptureAction?.Invoke();
			if ( 0 < this.values.Count ) {
				this.last = this.values.Dequeue();
			}
			return Task.FromResult( this.last );
		}
	}

	private sealed class FakeClock : IMonotonicClock {
		private long ticks;

		public long GetTimestamp() {
			return this.ticks;
		}

		public TimeSpan GetElapsedTime(
			long startingTimestamp,
			long endingTimestamp
		) {
			return TimeSpan.FromTicks( endingTimestamp - startingTimestamp );
		}

		public ValueTask DelayAsync(
			TimeSpan delay,
			CancellationToken cancellationToken = default
		) {
			if ( TimeSpan.Zero > delay ) {
				throw new ArgumentOutOfRangeException( nameof( delay ) );
			}
			cancellationToken.ThrowIfCancellationRequested();
			this.Advance( delay );
			return ValueTask.CompletedTask;
		}

		internal void Advance( TimeSpan duration ) {
			if ( TimeSpan.Zero > duration ) {
				throw new ArgumentOutOfRangeException( nameof( duration ) );
			}
			this.ticks = checked( this.ticks + duration.Ticks );
		}
	}

	private sealed class FakeTerminalFactory : ISlabTopTerminalSessionFactory {
		private readonly FakeTerminal terminal;

		internal FakeTerminalFactory( FakeTerminal terminal ) {
			ArgumentNullException.ThrowIfNull( terminal );
			this.terminal = terminal;
		}

		public ValueTask<ISlabTopTerminalSession> OpenAsync(
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			this.terminal.OpenCount++;
			return ValueTask.FromResult<ISlabTopTerminalSession>( this.terminal );
		}
	}

	private sealed record ScheduledTerminalEvent(
		SlabTopTerminalEventKind Kind,
		SlabTopTerminalDimensions? Dimensions = null
	);

	private sealed class FakeTerminal : ISlabTopTerminalSession {
		private readonly FakeClock clock;
		private SlabTopTerminalDimensions dimensions;

		internal FakeTerminal(
			FakeClock clock,
			SlabTopTerminalDimensions dimensions,
			bool isInteractive = true
		) {
			ArgumentNullException.ThrowIfNull( clock );
			this.clock = clock;
			this.dimensions = dimensions;
			this.IsInteractive = isInteractive;
		}

		public bool IsInteractive { get; }
		public CancellationToken TerminationToken => CancellationToken.None;
		internal Queue<ScheduledTerminalEvent> Events { get; } = new();
		internal List<SlabTopRenderFrame> Frames { get; } = [];
		internal List<TimeSpan> Waits { get; } = [];
		internal int OpenCount { get; set; }
		internal int RepaintCount { get; private set; }
		internal bool Disposed { get; private set; }
		internal Action? BeforeRead { get; set; }

		public SlabTopTerminalDimensions GetDimensions() {
			return this.dimensions;
		}

		public ValueTask<SlabTopTerminalEvent> ReadEventAsync(
			TimeSpan timeout,
			CancellationToken cancellationToken = default
		) {
			if ( TimeSpan.Zero > timeout ) {
				throw new ArgumentOutOfRangeException( nameof( timeout ) );
			}
			Action? beforeRead = this.BeforeRead;
			this.BeforeRead = null;
			beforeRead?.Invoke();
			cancellationToken.ThrowIfCancellationRequested();
			this.Waits.Add( timeout );
			if ( 0 < this.Events.Count ) {
				ScheduledTerminalEvent scripted = this.Events.Dequeue();
				if ( scripted.Dimensions.HasValue ) {
					this.dimensions = scripted.Dimensions.Value;
				}
				if ( SlabTopTerminalEventKind.Timeout == scripted.Kind ) {
					this.clock.Advance( timeout );
				}
				return ValueTask.FromResult(
					new SlabTopTerminalEvent( scripted.Kind )
				);
			}

			this.clock.Advance( timeout );
			return ValueTask.FromResult(
				new SlabTopTerminalEvent( SlabTopTerminalEventKind.Timeout )
			);
		}

		public ValueTask RenderAsync(
			SlabTopRenderFrame frame,
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
}
