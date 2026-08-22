namespace Icod.ProcPs.Free.Tests;

using System.Text;
using Icod.CommandFramework.Host;
using Icod.ProcPs.Shared;
using Xunit;

public sealed class CommandTests {
	[Fact]
	public async Task DefaultOutputUsesProcpsDerivedMemoryValues() {
		var result = await InvokeAsync( [], new FakeMetricsProvider( SampleMemory() ) );
		Assert.Equal( 0, result.Status );
		var expected = string.Join( Environment.NewLine, [
			"               total        used        free      shared  buff/cache   available",
			"Mem:            1000         600         300          20         175         400",
			"Swap:            500         300         200",
			string.Empty
		] );
		Assert.Equal( expected, result.Output );
	}
	[Fact]
	public void NeutralMemoryFieldsRenderWithoutLinuxMeminfoKeys() {
		static ulong KiB( ulong value ) => value * 1024UL;
		var memory = new ProcMemoryInfo(
			totalBytes: KiB( 1000 ),
			freeBytes: KiB( 300 ),
			availableBytes: KiB( 400 ),
			buffersBytes: KiB( 50 ),
			cacheBytes: KiB( 125 ),
			sharedBytes: KiB( 20 ),
			swapTotalBytes: KiB( 500 ),
			swapFreeBytes: KiB( 200 )
		);
		var output = Command.Render( memory );
		var expected = string.Join( Environment.NewLine, [
			"               total        used        free      shared  buff/cache   available",
			"Mem:            1000         600         300          20         175         400",
			"Swap:            500         300         200",
			string.Empty
		] );
		Assert.Equal( expected, output );
	}

	[Fact]
	public async Task WideLoHiTotalAndCommittedColumnsAreRendered() {
		var result = await InvokeAsync( [ "--wide", "--lohi", "--total", "--committed" ], new FakeMetricsProvider( SampleMemory() ) );
		Assert.Equal( 0, result.Status );
		Assert.Contains( "buffers       cache", result.Output );
		Assert.Contains( "Mem:            1000         600         300          20          50         125         400", result.Output );
		Assert.Contains( "Low:            1000         700         300", result.Output );
		Assert.Contains( "High:            100          75          25", result.Output );
		Assert.Contains( "Total:          1500         900         500", result.Output );
		Assert.Contains( "Comm:           1500        1000         500", result.Output );
	}
	[Theory]
	[InlineData( 1024UL, 0, false, false, "1" )]
	[InlineData( 1024UL, 1, false, false, "1024" )]
	[InlineData( 1024UL * 1024UL, 3, false, false, "1" )]
	[InlineData( 1_000_000UL, 3, true, false, "1" )]
	[InlineData( 1024UL, 0, false, true, "1.0Ki" )]
	[InlineData( 1000UL, 0, true, true, "1.0K" )]
	[InlineData( 1152921504606846976UL, 0, false, true, "1024Pi" )]
	public void SizeScalingMatchesProcpsRules( ulong bytes, int exponent, bool si, bool human, string expected ) => Assert.Equal( expected, Command.FormatSize( bytes, exponent, si, human ) );

	[Fact]
	public async Task LaterHelpOptionWinsOverPermutedOperand() {
		var result = await InvokeAsync( [ "operand", "--help" ], new FakeMetricsProvider( SampleMemory() ) );
		Assert.Equal( 0, result.Status );
		Assert.StartsWith( $"{Environment.NewLine}Usage:{Environment.NewLine} free [options]", result.Output );
		Assert.Equal( string.Empty, result.Error );
	}
	[Fact]
	public void SynchronousVersionEntryPointRemainsAvailable() {
		using var output = new StringWriter();
		using var error = new StringWriter();
		var status = Command.Run( [ "--version" ], output, error );
		Assert.Equal( 0, status );
		Assert.Equal( $"free from procps-ng 4.0.6{Environment.NewLine}", output.ToString() );
		Assert.Equal( string.Empty, error.ToString() );
	}
	[Fact]
	public void InvalidMemAvailableFallsBackToMemFree() {
		static ulong KiB( ulong value ) => value * 1024UL;
		var memory = new ProcMemoryInfo( new Dictionary<string, ulong> {
			[ "MemTotal" ] = KiB( 1000 ),
			[ "MemFree" ] = KiB( 300 ),
			[ "MemAvailable" ] = KiB( 2000 )
		} );
		var output = Command.Render( memory );
		var memoryFields = output.Split( Environment.NewLine, StringSplitOptions.RemoveEmptyEntries )[ 1 ].Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		Assert.Equal( new[] { "Mem:", "1000", "700", "300", "0", "0", "300" }, memoryFields );
	}

	[Fact]
	public async Task ExtraOperandPrintsUsageWithoutInventedDiagnostic() {
		var result = await InvokeAsync( [ "operand" ], new FakeMetricsProvider( SampleMemory() ) );
		Assert.Equal( 1, result.Status );
		Assert.Equal( string.Empty, result.Output );
		Assert.StartsWith( $"{Environment.NewLine}Usage:{Environment.NewLine} free [options]", result.Error );
	}

	[Fact]
	public async Task MultipleUnitOptionsAreRejected() {
		var result = await InvokeAsync( [ "-m", "--giga" ], new FakeMetricsProvider( SampleMemory() ) );
		Assert.Equal( 1, result.Status );
		Assert.Equal( $"free: Multiple unit options don't make sense.{Environment.NewLine}", result.Error );
	}
	[Fact]
	public async Task LineModeUsesSingleLineSummary() {
		var result = await InvokeAsync( [ "--line" ], new FakeMetricsProvider( SampleMemory() ) );
		Assert.Equal( 0, result.Status );
		Assert.StartsWith( "SwapUse", result.Output );
		Assert.Contains( "CachUse", result.Output );
		Assert.Contains( " MemUse", result.Output );
		Assert.Contains( "MemFree", result.Output );
		Assert.Equal( 1, result.Output.Count( character => '\n' == character ) );
	}
	[Fact]
	public async Task RepeatCountSamplesAgainAndUsesRequestedDelay() {
		var provider = new FakeMetricsProvider( SampleMemory() );
		var delays = new List<TimeSpan>();
		var result = await InvokeAsync( [ "--seconds", "0.25", "--count", "2" ], provider, ( duration, _ ) => { delays.Add( duration ); return Task.CompletedTask; } );
		Assert.Equal( 0, result.Status );
		Assert.Equal( 2, provider.MemoryRequests );
		Assert.Single( delays );
		Assert.Equal( TimeSpan.FromMilliseconds( 250 ), delays[ 0 ] );
		Assert.Contains( string.Concat( Environment.NewLine, Environment.NewLine, "               total" ), result.Output );
	}
	[Fact]
	public async Task UnsupportedMemoryIsAControlledFailure() {
		var provider = new FakeMetricsProvider( ProcObservedValue<ProcMemoryInfo>.Missing( ProcObservationAvailability.Unsupported, "portable provider" ) );
		var result = await InvokeAsync( [], provider );
		Assert.Equal( 1, result.Status );
		Assert.Contains( "not available on this platform", result.Error );
	}
	[Fact]
	public async Task LinuxProcfsMemoryRetainsExactProvenance() {
		var root = CreateTempDirectory();
		try {
			await File.WriteAllTextAsync( System.IO.Path.Combine( root, "meminfo" ), "MemTotal:       2 kB\nMemFree:        1 kB\n" );
			var observation = await new LinuxProcSystemMetricsProvider( root ).GetMemoryAsync();
			Assert.True( observation.HasValue );
			Assert.Equal( ProcObservationSource.LinuxProcfs, observation.Source );
			Assert.Equal( ObservationFidelity.Exact, observation.Fidelity );
			Assert.Equal( 2048UL, observation.Value.Fields[ "MemTotal" ] );
			Assert.Equal( 1024UL, observation.Value.Fields[ "MemFree" ] );
		} finally { Directory.Delete( root, recursive: true ); }
	}
	[Fact]
	public async Task CommaRepeatIntervalMatchesProcpsParser() {
		var provider = new FakeMetricsProvider( SampleMemory() );
		var delays = new List<TimeSpan>();
		var result = await InvokeAsync( [ "--seconds", "0,25", "--count", "2" ], provider, ( duration, _ ) => { delays.Add( duration ); return Task.CompletedTask; } );
		Assert.Equal( 0, result.Status );
		Assert.Single( delays );
		Assert.Equal( TimeSpan.FromMilliseconds( 250 ), delays[ 0 ] );
	}
	[Fact]
	public async Task AmbiguousLongOptionReportsPossibilities() {
		var result = await InvokeAsync( [ "--s" ], new FakeMetricsProvider( SampleMemory() ) );
		Assert.Equal( 1, result.Status );
		Assert.Contains( "option '--s' is ambiguous", result.Error );
		Assert.Contains( "'--si'", result.Error );
		Assert.Contains( "'--seconds'", result.Error );
	}
	private static string CreateTempDirectory() { var path = System.IO.Path.Combine( System.IO.Path.GetTempPath(), $"icod-procps-free-{Guid.NewGuid():N}" ); Directory.CreateDirectory( path ); return path; }
	private static ProcObservedValue<ProcMemoryInfo> SampleMemory() {
		static ulong KiB( ulong value ) => value * 1024UL;
		var fields = new Dictionary<string, ulong> {
			[ "MemTotal" ] = KiB( 1000 ), [ "MemFree" ] = KiB( 300 ), [ "MemAvailable" ] = KiB( 400 ), [ "Shmem" ] = KiB( 20 ),
			[ "Buffers" ] = KiB( 50 ), [ "Cached" ] = KiB( 100 ), [ "SReclaimable" ] = KiB( 25 ), [ "SwapTotal" ] = KiB( 500 ), [ "SwapFree" ] = KiB( 200 ),
			[ "LowTotal" ] = 0, [ "LowFree" ] = 0, [ "HighTotal" ] = KiB( 100 ), [ "HighFree" ] = KiB( 25 ), [ "CommitLimit" ] = KiB( 1500 ), [ "Committed_AS" ] = KiB( 1000 )
		};
		return ProcObservedValue<ProcMemoryInfo>.Available( new ProcMemoryInfo( fields ), ProcObservationSource.Configuration, ObservationFidelity.Exact );
	}
	private static async Task<InvocationResult> InvokeAsync( string[] args, FakeMetricsProvider provider, Func<TimeSpan, CancellationToken, Task>? delay = null ) { using var output = new MemoryStream(); using var error = new MemoryStream(); var status = await Command.RunAsync( args, output, error, provider, delay ); return new InvocationResult( status, Encoding.UTF8.GetString( output.ToArray() ), Encoding.UTF8.GetString( error.ToArray() ) ); }
	private sealed record InvocationResult( int Status, string Output, string Error );
	private sealed class FakeMetricsProvider : IProcSystemMetricsProvider {
		private readonly ProcObservedValue<ProcMemoryInfo> _memory;
		public FakeMetricsProvider( ProcObservedValue<ProcMemoryInfo> memory ) => this._memory = memory;
		public ProcSystemCapabilities Capabilities => ProcSystemCapabilities.Memory | ProcSystemCapabilities.Swap;
		public int MemoryRequests { get; private set; }
		public Task<ProcObservedValue<ProcMemoryInfo>> GetMemoryAsync( CancellationToken cancellationToken = default ) { cancellationToken.ThrowIfCancellationRequested(); this.MemoryRequests++; return Task.FromResult( this._memory ); }
		public Task<ProcSystemSnapshot> GetSnapshotAsync( CancellationToken cancellationToken = default ) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult( new ProcSystemSnapshot { Memory = this._memory } ); }
	}
}
