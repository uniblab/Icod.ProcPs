/*
	Icod.ProcPs.Tload.Tests
	Tests for the tload command implementation.
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

namespace Icod.ProcPs.Tload.Tests;

using System.Runtime.CompilerServices;
using System.Text;
using Icod.ProcPs.Shared;
using Icod.Timing;
using Xunit;

/// <summary>Exercises the managed procps-ng-inspired <c>tload</c> command.</summary>
public sealed class TloadCommandTests {
	/// <summary>Verifies default rendering and the five-second fixed-rate cadence.</summary>
	[Fact]
	public async Task DefaultReportRendersLoadGraphAndUsesDefaultCadence() {
		var terminal = new FakeTerminal(
			new TloadTerminalDimensions( 40, 10 )
		);
		var scheduler = new FiniteScheduler( 1 );
		RunResult result = await RunAsync(
			[],
			terminal,
			scheduler,
			new FakeMetricsProvider( Available( 0.5d, 0.25d, 0.1d ) )
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( TimeSpan.FromSeconds( 5d ), scheduler.LastInterval );
		Assert.True( scheduler.LastFireImmediately );
		Assert.Equal( PeriodicMissedTickPolicy.SkipMissed, scheduler.LastMissedTickPolicy );
		Assert.Single( terminal.Frames );
		Assert.Contains(
			" 0.50, 0.25, 0.10",
			terminal.Frames[ 0 ],
			StringComparison.Ordinal
		);
		Assert.Contains( '*', terminal.Frames[ 0 ] );
		Assert.True( terminal.Disposed );
		Assert.Equal( string.Empty, result.Stderr );
	}

	/// <summary>Verifies delay and vertical-scale options affect sampling and rendering.</summary>
	[Fact]
	public async Task DelayAndScaleControlsAreApplied() {
		var dimensions = new TloadTerminalDimensions( 40, 10 );
		var terminal = new FakeTerminal( dimensions );
		var scheduler = new FiniteScheduler( 1 );
		RunResult result = await RunAsync(
			[ "--delay", "2", "--scale=2" ],
			terminal,
			scheduler,
			new FakeMetricsProvider( Available( 1d, 0.5d, 0.25d ) )
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( TimeSpan.FromSeconds( 2d ), scheduler.LastInterval );
		string frame = terminal.Frames[ 0 ];
		Assert.Equal(
			'*',
			frame[ ( dimensions.Rows - 1 ) * dimensions.Columns ]
		);
		Assert.Equal(
			'=',
			frame[ ( dimensions.Rows - 2 ) * dimensions.Columns ]
		);
	}

	/// <summary>Verifies fractional scale marks use procps row-conversion semantics.</summary>
	[Fact]
	public void FractionalScaleUsesProcpsRowTruncation() {
		var graph = new TloadGraphState( 1.5d );
		var dimensions = new TloadTerminalDimensions( 10, 10 );
		string frame = graph.Render(
			new ProcLoadAverages( 0d, 0d, 0d ),
			dimensions
		);

		Assert.Equal( '-', frame[ ( 8 * dimensions.Columns ) ] );
		Assert.Equal( ' ', frame[ ( 9 * dimensions.Columns ) ] );
	}

	/// <summary>Verifies sub-unit scales remain bounded while preserving all visible scale rows.</summary>
	[Fact]
	public void SubUnitScaleMarksEachVisibleRow() {
		var graph = new TloadGraphState( 0.5d );
		var dimensions = new TloadTerminalDimensions( 10, 10 );
		string frame = graph.Render(
			new ProcLoadAverages( 0d, 0d, 0d ),
			dimensions
		);

		Assert.Equal( '-', frame[ ( 9 * dimensions.Columns ) ] );
		Assert.Equal( '-', frame[ ( 5 * dimensions.Columns ) ] );
	}

	/// <summary>Verifies an explicit terminal operand is routed to the output factory.</summary>
	[Fact]
	public async Task SelectedTerminalOperandIsPassedToFactory() {
		var terminal = new FakeTerminal(
			new TloadTerminalDimensions( 40, 10 )
		);
		var factory = new FakeTerminalFactory( terminal );
		RunResult result = await RunAsync(
			[ "/dev/pts/7" ],
			terminal,
			new FiniteScheduler( 1 ),
			new FakeMetricsProvider( Available( 0.5d, 0.25d, 0.1d ) ),
			factory
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "/dev/pts/7", factory.TerminalPath );
		Assert.Equal( 1, factory.OpenCount );
	}

	/// <summary>Verifies the double-dash delimiter preserves the optional terminal operand.</summary>
	[Fact]
	public async Task DoubleDashSelectsTerminalOperand() {
		var terminal = new FakeTerminal(
			new TloadTerminalDimensions( 40, 10 )
		);
		var factory = new FakeTerminalFactory( terminal );
		RunResult result = await RunAsync(
			[ "--", "-tty-name" ],
			terminal,
			new FiniteScheduler( 1 ),
			new FakeMetricsProvider( Available( 0.5d, 0.25d, 0.1d ) ),
			factory
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "-tty-name", factory.TerminalPath );
	}

	/// <summary>Verifies the command does not require standard output to be interactive.</summary>
	[Fact]
	public async Task RedirectedStandardOutputRemainsSupported() {
		var terminal = new FakeTerminal(
			new TloadTerminalDimensions( 80, 25 )
		);
		var factory = new FakeTerminalFactory( terminal );
		RunResult result = await RunAsync(
			[],
			terminal,
			new FiniteScheduler( 1 ),
			new FakeMetricsProvider( Available( 0.5d, 0.25d, 0.1d ) ),
			factory
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Null( factory.TerminalPath );
		Assert.NotNull( factory.StandardOutput );
		Assert.Single( terminal.Frames );
	}

	/// <summary>Verifies geometry changes reset the scrolling graph and use the new dimensions.</summary>
	[Fact]
	public async Task ResizeClearsAndRendersAtNewGeometry() {
		var terminal = new FakeTerminal(
			new TloadTerminalDimensions( 10, 4 ),
			new TloadTerminalDimensions( 10, 4 ),
			new TloadTerminalDimensions( 12, 5 )
		);
		RunResult result = await RunAsync(
			[],
			terminal,
			new FiniteScheduler( 2 ),
			new FakeMetricsProvider(
				Available( 0.5d, 0.25d, 0.1d ),
				Available( 0.75d, 0.4d, 0.2d )
			)
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( 2, terminal.Frames.Count );
		Assert.Equal( 39, terminal.Frames[ 0 ].Length );
		Assert.Equal( 59, terminal.Frames[ 1 ].Length );
	}

	/// <summary>Verifies unavailable load averages produce a controlled diagnostic.</summary>
	[Fact]
	public async Task MissingLoadAverageIsControlledFailure() {
		var terminal = new FakeTerminal(
			new TloadTerminalDimensions( 80, 25 )
		);
		RunResult result = await RunAsync(
			[],
			terminal,
			new FiniteScheduler( 1 ),
			new FakeMetricsProvider(
				ProcObservedValue<ProcLoadAverages>.Missing(
					ProcObservationAvailability.Unsupported,
					"load averages are unsupported"
				)
			)
		);

		Assert.Equal( 1, result.ExitCode );
		Assert.Contains(
			"load averages are unsupported",
			result.Stderr,
			StringComparison.Ordinal
		);
		Assert.True( terminal.Disposed );
	}

	/// <summary>Verifies invalid provider values are rejected instead of rendered.</summary>
	[Fact]
	public async Task InvalidLoadAverageIsControlledFailure() {
		var terminal = new FakeTerminal(
			new TloadTerminalDimensions( 80, 25 )
		);
		RunResult result = await RunAsync(
			[],
			terminal,
			new FiniteScheduler( 1 ),
			new FakeMetricsProvider( Available( double.NaN, 0.25d, 0.1d ) )
		);

		Assert.Equal( 1, result.ExitCode );
		Assert.Contains(
			"invalid value",
			result.Stderr,
			StringComparison.Ordinal
		);
		Assert.Empty( terminal.Frames );
	}

	/// <summary>Verifies cancellation returns the conventional interruption status.</summary>
	[Fact]
	public async Task CancellationReturnsCanceledAndDisposesTerminal() {
		var terminal = new FakeTerminal(
			new TloadTerminalDimensions( 80, 25 )
		);
		RunResult result = await RunAsync(
			[],
			terminal,
			new FiniteScheduler( 1, cancelAfterTicks: true ),
			new FakeMetricsProvider( Available( 0.5d, 0.25d, 0.1d ) )
		);

		Assert.Equal( 130, result.ExitCode );
		Assert.Single( terminal.Frames );
		Assert.True( terminal.Disposed );
	}

	/// <summary>Verifies frame-write failures remain controlled and dispose the output session.</summary>
	[Fact]
	public async Task WriteFailureIsControlledAndDisposesTerminal() {
		var terminal = new FakeTerminal(
			new TloadTerminalDimensions( 80, 25 )
		) {
			WriteFailure = new IOException( "simulated write failure" )
		};
		RunResult result = await RunAsync(
			[],
			terminal,
			new FiniteScheduler( 1 ),
			new FakeMetricsProvider( Available( 0.5d, 0.25d, 0.1d ) )
		);

		Assert.Equal( 1, result.ExitCode );
		Assert.Contains(
			"simulated write failure",
			result.Stderr,
			StringComparison.Ordinal
		);
		Assert.True( terminal.Disposed );
	}

	/// <summary>Verifies invalid option values produce deterministic failures before opening the terminal.</summary>
	/// <param name="args">Command-line arguments under test.</param>
	/// <param name="expected">Expected diagnostic fragment.</param>
	[Theory]
	[MemberData( nameof( InvalidArgumentData ) )]
	public async Task InvalidArgumentsReturnFailure(
		string[] args,
		string expected
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentException.ThrowIfNullOrWhiteSpace( expected );
		var terminal = new FakeTerminal(
			new TloadTerminalDimensions( 80, 25 )
		);
		var factory = new FakeTerminalFactory( terminal );
		RunResult result = await RunAsync(
			args,
			terminal,
			new FiniteScheduler( 1 ),
			new FakeMetricsProvider( Available( 0.5d, 0.25d, 0.1d ) ),
			factory
		);

		Assert.Equal( 1, result.ExitCode );
		Assert.Contains( expected, result.Stderr, StringComparison.Ordinal );
		Assert.Equal( 0, factory.OpenCount );
	}

	/// <summary>Provides invalid command-line cases.</summary>
	public static TheoryData<string[], string> InvalidArgumentData => new() {
		{ new[] { "-d", "0" }, "delay must be positive integer" },
		{ new[] { "-d", "-1" }, "delay must be positive integer" },
		{ new[] { "-d", "4294967296" }, "too large delay value" },
		{ new[] { "-s", "-1" }, "scale cannot be negative" },
		{ new[] { "-s", "not-a-number" }, "failed to parse scale argument" },
		{ new[] { "--unknown" }, "unrecognized option" },
		{ new[] { "tty-one", "tty-two" }, "too many terminal operands" }
	};

	/// <summary>Verifies help and version complete without opening a terminal session.</summary>
	[Fact]
	public async Task HelpAndVersionDoNotOpenTerminal() {
		var terminal = new FakeTerminal(
			new TloadTerminalDimensions( 80, 25 )
		);
		var factory = new FakeTerminalFactory( terminal );
		var metrics = new FakeMetricsProvider( Available( 0.5d, 0.25d, 0.1d ) );

		RunResult help = await RunAsync(
			[ "--help" ],
			terminal,
			new FiniteScheduler( 1 ),
			metrics,
			factory
		);
		Assert.Equal( 0, help.ExitCode );
		Assert.Contains( "Usage:", help.Stdout, StringComparison.Ordinal );
		Assert.Equal( 0, factory.OpenCount );

		RunResult version = await RunAsync(
			[ "--version" ],
			terminal,
			new FiniteScheduler( 1 ),
			metrics,
			factory
		);
		Assert.Equal( 0, version.ExitCode );
		Assert.Equal(
			$"Icod.ProcPs.Tload (1.0.1) inspired by procps-ng 4.0.6{Environment.NewLine}",
			version.Stdout
		);
		Assert.Equal( string.Empty, version.Stderr );
		Assert.Equal( 0, factory.OpenCount );
	}

	/// <summary>Verifies the synchronous version entry point remains available.</summary>
	[Fact]
	public void SynchronousVersionEntryPointRemainsAvailable() {
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		int status = Command.Run(
			[ "--version" ],
			output,
			error
		);

		Assert.Equal( 0, status );
		Assert.Equal(
			$"Icod.ProcPs.Tload (1.0.1) inspired by procps-ng 4.0.6{Environment.NewLine}",
			Encoding.UTF8.GetString( output.ToArray() )
		);
		Assert.Equal( string.Empty, Encoding.UTF8.GetString( error.ToArray() ) );
	}

	private static ProcObservedValue<ProcLoadAverages> Available(
		double one,
		double five,
		double fifteen
	) {
		return ProcObservedValue<ProcLoadAverages>.Available(
			new ProcLoadAverages( one, five, fifteen ),
			ProcObservationSource.PlatformApi,
			ProcObservationFidelity.Equivalent
		);
	}

	private static async Task<RunResult> RunAsync(
		IReadOnlyList<string> args,
		FakeTerminal terminal,
		FiniteScheduler scheduler,
		FakeMetricsProvider metrics,
		FakeTerminalFactory? terminalFactory = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( terminal );
		ArgumentNullException.ThrowIfNull( scheduler );
		ArgumentNullException.ThrowIfNull( metrics );

		await using var stdout = new MemoryStream();
		await using var stderr = new MemoryStream();
		terminalFactory ??= new FakeTerminalFactory( terminal );
		var sampler = new ProcSampler(
			new FakeClock(),
			scheduler
		);
		int exitCode = await Command.RunAsyncCore(
			args,
			stdout,
			stderr,
			metrics,
			sampler,
			terminalFactory,
			cancellationToken
		);
		return new RunResult(
			exitCode,
			Encoding.UTF8.GetString( stdout.ToArray() ),
			Encoding.UTF8.GetString( stderr.ToArray() )
		);
	}

	private sealed record RunResult(
		int ExitCode,
		string Stdout,
		string Stderr
	);

	private sealed class FakeClock : IMonotonicClock {
		private long timestamp;

		public long GetTimestamp() {
			return Interlocked.Increment( ref this.timestamp );
		}

		public TimeSpan GetElapsedTime(
			long startingTimestamp,
			long endingTimestamp
		) {
			return TimeSpan.FromSeconds(
				Math.Max( 0L, endingTimestamp - startingTimestamp )
			);
		}

		public ValueTask DelayAsync(
			TimeSpan delay,
			CancellationToken cancellationToken = default
		) {
			if ( TimeSpan.Zero > delay ) {
				throw new ArgumentOutOfRangeException( nameof( delay ) );
			}
			cancellationToken.ThrowIfCancellationRequested();
			Interlocked.Add(
				ref this.timestamp,
				Math.Max( 1L, (long)Math.Ceiling( delay.TotalSeconds ) )
			);
			return ValueTask.CompletedTask;
		}
	}

	private sealed class FiniteScheduler : IPeriodicScheduler {
		private readonly int ticks;
		private readonly bool cancelAfterTicks;

		public TimeSpan LastInterval { get; private set; }
		public bool LastFireImmediately { get; private set; }
		public PeriodicMissedTickPolicy LastMissedTickPolicy { get; private set; }

		internal FiniteScheduler(
			int ticks,
			bool cancelAfterTicks = false
		) {
			ArgumentOutOfRangeException.ThrowIfNegative( ticks );
			this.ticks = ticks;
			this.cancelAfterTicks = cancelAfterTicks;
		}

		public async IAsyncEnumerable<PeriodicTick> ScheduleAsync(
			TimeSpan interval,
			bool fireImmediately = false,
			[EnumeratorCancellation] CancellationToken cancellationToken = default,
			PeriodicMissedTickPolicy missedTickPolicy = PeriodicMissedTickPolicy.SkipMissed
		) {
			if ( TimeSpan.Zero >= interval ) {
				throw new ArgumentOutOfRangeException( nameof( interval ) );
			}

			this.LastInterval = interval;
			this.LastFireImmediately = fireImmediately;
			this.LastMissedTickPolicy = missedTickPolicy;
			for ( int index = 0; index < this.ticks; index++ ) {
				cancellationToken.ThrowIfCancellationRequested();
				yield return new PeriodicTick(
					index,
					TimeSpan.FromTicks( interval.Ticks * index ),
					TimeSpan.FromTicks( interval.Ticks * index )
				);
				await Task.Yield();
			}
			if ( this.cancelAfterTicks ) {
				throw new OperationCanceledException();
			}
		}
	}

	private sealed class FakeMetricsProvider : IProcSystemMetricsProvider {
		private readonly Queue<ProcObservedValue<ProcLoadAverages>> values;
		private ProcObservedValue<ProcLoadAverages> last;

		public ProcSystemCapabilities Capabilities => ProcSystemCapabilities.LoadAverage;

		internal FakeMetricsProvider(
			params ProcObservedValue<ProcLoadAverages>[] values
		) {
			ArgumentNullException.ThrowIfNull( values );
			if ( 0 == values.Length ) {
				throw new ArgumentException(
					"At least one load-average observation is required.",
					nameof( values )
				);
			}

			this.values = new Queue<ProcObservedValue<ProcLoadAverages>>( values );
			this.last = values[ 0 ];
		}

		public Task<ProcSystemSnapshot> GetSnapshotAsync(
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			if ( 0 < this.values.Count ) {
				this.last = this.values.Dequeue();
			}
			return Task.FromResult(
				new ProcSystemSnapshot {
					LoadAverages = this.last
				}
			);
		}
	}

	private sealed class FakeTerminalFactory : ITloadTerminalSessionFactory {
		private readonly FakeTerminal terminal;

		public int OpenCount { get; private set; }
		public string? TerminalPath { get; private set; }
		public Stream? StandardOutput { get; private set; }

		internal FakeTerminalFactory( FakeTerminal terminal ) {
			ArgumentNullException.ThrowIfNull( terminal );
			this.terminal = terminal;
		}

		public ValueTask<ITloadTerminalSession> OpenAsync(
			string? terminalPath,
			Stream standardOutput,
			CancellationToken cancellationToken = default
		) {
			ArgumentNullException.ThrowIfNull( standardOutput );
			cancellationToken.ThrowIfCancellationRequested();
			this.OpenCount++;
			this.TerminalPath = terminalPath;
			this.StandardOutput = standardOutput;
			return ValueTask.FromResult<ITloadTerminalSession>( this.terminal );
		}
	}

	private sealed class FakeTerminal : ITloadTerminalSession {
		private readonly Queue<TloadTerminalDimensions> dimensions;

		public List<string> Frames { get; } = [];
		public Exception? WriteFailure { get; init; }
		public bool Disposed { get; private set; }

		internal FakeTerminal(
			params TloadTerminalDimensions[] dimensions
		) {
			ArgumentNullException.ThrowIfNull( dimensions );
			if ( 0 == dimensions.Length ) {
				throw new ArgumentException(
					"At least one terminal geometry is required.",
					nameof( dimensions )
				);
			}
			this.dimensions = new Queue<TloadTerminalDimensions>( dimensions );
		}

		public TloadTerminalDimensions GetDimensions() {
			if ( 1 < this.dimensions.Count ) {
				return this.dimensions.Dequeue();
			}
			return this.dimensions.Peek();
		}

		public ValueTask WriteFrameAsync(
			string frame,
			CancellationToken cancellationToken = default
		) {
			ArgumentNullException.ThrowIfNull( frame );
			cancellationToken.ThrowIfCancellationRequested();
			if ( this.WriteFailure is not null ) {
				throw this.WriteFailure;
			}
			this.Frames.Add( frame );
			return ValueTask.CompletedTask;
		}

		public ValueTask DisposeAsync() {
			this.Disposed = true;
			return ValueTask.CompletedTask;
		}
	}
}
