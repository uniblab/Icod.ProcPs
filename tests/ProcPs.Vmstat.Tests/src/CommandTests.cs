namespace Icod.ProcPs.Vmstat.Tests;

using System.Text;
using Icod.CommandFramework.Host;
using Icod.ProcPs.Shared;
using Xunit;

public sealed class CommandTests {
	[Fact]
	public void HeaderTracksActiveWideAndTimestampModes() {
		var normal = Command.RenderDefaultHeader();
		Assert.StartsWith( "procs -----------memory", normal );
		Assert.Contains( "buff  cache", normal );
		var alternate = Command.RenderDefaultHeader( active: true, wide: true, timestamp: true );
		Assert.StartsWith( string.Concat( "--procs-- -----------------------memory", "----------------------" ), alternate );
		Assert.Contains( "inact       active", alternate );
		Assert.Contains( "timestamp", alternate );
	}

	[Theory]
	[InlineData( 1_048_576UL, 'b', 1_048_576UL )]
	[InlineData( 1_000_000UL, 'k', 1000UL )]
	[InlineData( 1_048_576UL, 'K', 1024UL )]
	[InlineData( 1_000_000UL, 'm', 1UL )]
	[InlineData( 1_048_576UL, 'M', 1UL )]
	public void UnitConversionMatchesProcpsUnitFamilies( ulong bytes, char unit, ulong expected ) => Assert.Equal( expected, Command.ConvertBytes( bytes, unit ) );

	[Fact]
	public async Task DefaultReportUsesSinceBootRatesForFirstRow() {
		var result = await InvokeAsync( [], new FakeProvider( FullSnapshot( interrupts: 1000, contextSwitches: 2000, pageIn: 500, pageOut: 600, swapIn: 10, swapOut: 20, uptimeSeconds: 100 ) ) );
		Assert.Equal( 0, result.Status );
		Assert.Equal( string.Empty, result.Error );
		var lines = result.Output.Split( Environment.NewLine, StringSplitOptions.RemoveEmptyEntries );
		Assert.Equal( 3, lines.Length );
		var fields = lines[ 2 ].Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		Assert.Equal( "2", fields[ 0 ] );
		Assert.Equal( "1", fields[ 1 ] );
		Assert.Equal( "5", fields[ 8 ] );
		Assert.Equal( "6", fields[ 9 ] );
		Assert.Equal( "10", fields[ 10 ] );
		Assert.Equal( "20", fields[ 11 ] );
	}


	[Fact]
	public async Task InitialContextSwitchRateUsesCpuDivisorWhileInterruptsUseUptime() {
		var result = await InvokeAsync( [], new FakeProvider( FullSnapshot( interrupts: 1000, contextSwitches: 1000, uptimeSeconds: 10 ) ) );
		Assert.Equal( 0, result.Status );
		var fields = result.Output.Split( Environment.NewLine, StringSplitOptions.RemoveEmptyEntries )[ 2 ].Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		Assert.Equal( "100", fields[ 10 ] );
		Assert.Equal( "10", fields[ 11 ] );
	}

	[Fact]
	public async Task GuestTicksRemainInCpuDivisorBeforeUserSubtraction() {
		var cpu = new ProcCpuTimes( 100, 0, 0, 0, 0, 0, 0, 0, 50, 0 );
		var result = await InvokeAsync( [], new FakeProvider( FullSnapshot( cpuTimes: cpu ) ) );
		Assert.Equal( 0, result.Status );
		var fields = result.Output.Split( Environment.NewLine, StringSplitOptions.RemoveEmptyEntries )[ 2 ].Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		Assert.Equal( "50", fields[ 12 ] );
		Assert.Equal( "50", fields[ 17 ] );
	}

	[Fact]
	public async Task LinuxIdleDebtMatchesProcpsBackwardIdleHandling() {
		var before = new ProcCpuTimes( 100, 0, 0, 100, 0, 0, 0, 0, 0, 0 );
		var backwards = new ProcCpuTimes( 110, 0, 0, 90, 0, 0, 0, 0, 0, 0 );
		var recovered = new ProcCpuTimes( 120, 0, 0, 110, 0, 0, 0, 0, 0, 0 );
		var provider = new FakeProvider( FullSnapshot( cpuTimes: before ), FullSnapshot( cpuTimes: backwards ), FullSnapshot( cpuTimes: recovered ) );
		var result = await InvokeAsync( [ "--no-first", "1", "2" ], provider, ( _, __ ) => Task.CompletedTask );
		Assert.Equal( 0, result.Status );
		var rows = result.Output.Split( Environment.NewLine, StringSplitOptions.RemoveEmptyEntries );
		var firstFields = rows[ 2 ].Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		var secondFields = rows[ 3 ].Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		Assert.Equal( "100", firstFields[ 12 ] );
		Assert.Equal( "0", firstFields[ 14 ] );
		Assert.Equal( "50", secondFields[ 12 ] );
		Assert.Equal( "50", secondFields[ 14 ] );
	}

	[Fact]
	public async Task NoFirstWaitsThenUsesCounterDeltas() {
		var provider = new FakeProvider(
			FullSnapshot( interrupts: 1000, contextSwitches: 2000, pageIn: 500, pageOut: 600, swapIn: 10, swapOut: 20, uptimeSeconds: 100 ),
			FullSnapshot( interrupts: 1010, contextSwitches: 2020, pageIn: 505, pageOut: 606, swapIn: 12, swapOut: 24, uptimeSeconds: 101 )
		);
		var delays = new List<TimeSpan>();
		var result = await InvokeAsync( [ "--no-first" ], provider, ( duration, _ ) => { delays.Add( duration ); return Task.CompletedTask; } );
		Assert.Equal( 0, result.Status );
		Assert.Equal( 2, provider.RequestCount );
		Assert.Single( delays );
		Assert.Equal( TimeSpan.FromSeconds( 1 ), delays[ 0 ] );
		var fields = result.Output.Split( Environment.NewLine, StringSplitOptions.RemoveEmptyEntries )[ 2 ].Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		Assert.Equal( "5", fields[ 8 ] );
		Assert.Equal( "6", fields[ 9 ] );
		Assert.Equal( "10", fields[ 10 ] );
		Assert.Equal( "20", fields[ 11 ] );
	}

	[Fact]
	public async Task DelayAndCountProduceRequestedNumberOfRows() {
		var provider = new FakeProvider( FullSnapshot(), FullSnapshot(), FullSnapshot() );
		var delays = new List<TimeSpan>();
		var result = await InvokeAsync( [ "2", "3" ], provider, ( duration, _ ) => { delays.Add( duration ); return Task.CompletedTask; } );
		Assert.Equal( 0, result.Status );
		Assert.Equal( 3, provider.RequestCount );
		Assert.Equal( 2, delays.Count );
		Assert.All( delays, delay => Assert.Equal( TimeSpan.FromSeconds( 2 ), delay ) );
		Assert.Equal( 5, result.Output.Split( Environment.NewLine, StringSplitOptions.RemoveEmptyEntries ).Length );
	}

	[Fact]
	public async Task PartialPlatformRendersKnownFieldsAndExplicitPlaceholders() {
		var memory = ProcObservedValue<ProcMemoryInfo>.Available( new ProcMemoryInfo( 8UL * 1024, 2UL * 1024, 3UL * 1024 ), ProcObservationSource.WindowsNativeApi, ObservationFidelity.Approximated );
		var cpu = ProcObservedValue<ProcCpuActivity>.Available( new ProcCpuActivity( 10, 20, 70 ), ProcObservationSource.WindowsNativeApi, ObservationFidelity.Equivalent );
		var snapshot = new ProcVmstatSnapshot { System = new ProcSystemSnapshot { Memory = memory, CpuActivity = cpu, Uptime = Available( new ProcUptimeInfo( TimeSpan.FromSeconds( 10 ), null ) ) } };
		var result = await InvokeAsync( [], new FakeProvider( ProcVmstatCapabilities.Memory | ProcVmstatCapabilities.Cpu, snapshot ) );
		Assert.Equal( 0, result.Status );
		Assert.Contains( "unavailable fields are shown as '-'", result.Error );
		Assert.Contains( "-", result.Output );
		var fields = result.Output.Split( Environment.NewLine, StringSplitOptions.RemoveEmptyEntries )[ 2 ].Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		Assert.Equal( "10", fields[ ^6 ] );
		Assert.Equal( "20", fields[ ^5 ] );
		Assert.Equal( "70", fields[ ^4 ] );
	}

	[Fact]
	public async Task UnsupportedSpecializedModeIsControlled() {
		var result = await InvokeAsync( [ "--disk" ], new FakeProvider( ProcVmstatCapabilities.Memory | ProcVmstatCapabilities.Cpu, FullSnapshot() ) );
		Assert.Equal( 1, result.Status );
		Assert.Contains( "disk statistics", result.Error );
		Assert.Equal( string.Empty, result.Output );
	}

	[Fact]
	public async Task ForkModeUsesCumulativeProcessCount() {
		var result = await InvokeAsync( [ "--forks" ], new FakeProvider( FullSnapshot( forks: 12345 ) ) );
		Assert.Equal( 0, result.Status );
		Assert.Equal( string.Concat( "        12345 forks", Environment.NewLine ), result.Output );
	}

	[Fact]
	public async Task DiskAndPartitionModesUseParsedRows() {
		var snapshot = FullSnapshot();
		var disk = await InvokeAsync( [ "--disk" ], new FakeProvider( snapshot ) );
		Assert.Equal( 0, disk.Status );
		Assert.Contains( "sda", disk.Output );
		Assert.DoesNotContain( "sda1", disk.Output );
		var partition = await InvokeAsync( [ "--partition", "/dev/sda1" ], new FakeProvider( snapshot ) );
		Assert.Equal( 0, partition.Status );
		Assert.Contains( "sda1", partition.Output );
		Assert.Contains( "requested writes", partition.Output );
	}


	[Fact]
	public async Task DiskAndSlabOneHeaderModesSuppressRepeatedHeaders() {
		static Task NoDelay( TimeSpan _, CancellationToken __ ) => Task.CompletedTask;
		var disk = await InvokeAsync( [ "--disk", "--one-header", "1", "2" ], new FakeProvider( FullSnapshot(), FullSnapshot() ), NoDelay );
		Assert.Equal( 0, disk.Status );
		Assert.Equal( 1, CountOccurrences( disk.Output, "disk- ------------reads------------" ) );
		var slab = await InvokeAsync( [ "--slabs", "--one-header", "1", "2" ], new FakeProvider( FullSnapshot(), FullSnapshot() ), NoDelay );
		Assert.Equal( 0, slab.Status );
		Assert.Equal( 1, CountOccurrences( slab.Output, "Cache                       Num" ) );
	}

	[Fact]
	public async Task PartitionAndSlabIgnoreTimestampOptionLikeProcps() {
		var partition = await InvokeAsync( [ "--partition", "sda1", "--timestamp" ], new FakeProvider( FullSnapshot() ) );
		Assert.Equal( 0, partition.Status );
		Assert.DoesNotContain( "2026-08-07", partition.Output );
		var slab = await InvokeAsync( [ "--slabs", "--timestamp" ], new FakeProvider( FullSnapshot() ) );
		Assert.Equal( 0, slab.Status );
		Assert.DoesNotContain( "2026-08-07", slab.Output );
	}

	[Fact]
	public async Task SlabModeSortsCacheNames() {
		var result = await InvokeAsync( [ "--slabs" ], new FakeProvider( FullSnapshot() ) );
		Assert.Equal( 0, result.Status );
		var alpha = result.Output.IndexOf( "alpha", StringComparison.Ordinal );
		var zeta = result.Output.IndexOf( "zeta", StringComparison.Ordinal );
		Assert.True( 0 <= alpha );
		Assert.True( alpha < zeta );
	}

	[Fact]
	public async Task StatisticsModeIncludesKernelTotals() {
		var result = await InvokeAsync( [ "--stats" ], new FakeProvider( FullSnapshot( forks: 123 ) ) );
		Assert.Equal( 0, result.Status );
		Assert.Contains( "total memory", result.Output );
		Assert.Contains( "interrupts", result.Output );
		Assert.EndsWith( string.Concat( "forks", Environment.NewLine ), result.Output );
	}


	[Fact]
	public async Task StatisticsRetainRawUserAndNiceCpuTicks() {
		var cpu = new ProcCpuTimes( 100, 20, 30, 40, 5, 2, 3, 4, 50, 5 );
		var result = await InvokeAsync( [ "--stats" ], new FakeProvider( FullSnapshot( cpuTimes: cpu ) ) );
		Assert.Equal( 0, result.Status );
		Assert.Contains( string.Concat( "          100 non-nice user cpu ticks", Environment.NewLine ), result.Output );
		Assert.Contains( string.Concat( "           20 nice user cpu ticks", Environment.NewLine ), result.Output );
	}

	[Fact]
	public async Task UnitOptionUsesFirstArgumentCharacterLikeProcps() {
		var accepted = await InvokeAsync( [ "--stats", "-SKiB" ], new FakeProvider( FullSnapshot() ) );
		Assert.Equal( 0, accepted.Status );
		var rejected = await InvokeAsync( [ "-Sz" ], new FakeProvider( FullSnapshot() ) );
		Assert.Equal( 1, rejected.Status );
		Assert.Contains( "-S requires k, K, m or M (default is KiB)", rejected.Error );
	}

	[Fact]
	public async Task ConflictingReportModesPrintUsage() {
		var result = await InvokeAsync( [ "--disk", "--stats" ], new FakeProvider( FullSnapshot() ) );
		Assert.Equal( 1, result.Status );
		Assert.StartsWith( string.Concat( Environment.NewLine, "Usage:", Environment.NewLine, " vmstat" ), result.Error );
	}

	[Fact]
	public async Task LaterHelpOptionWinsOverPermutedOperand() {
		var result = await InvokeAsync( [ "1", "--help" ], new FakeProvider( FullSnapshot() ) );
		Assert.Equal( 0, result.Status );
		Assert.StartsWith( string.Concat( Environment.NewLine, "Usage:", Environment.NewLine, " vmstat" ), result.Output );
		Assert.Equal( string.Empty, result.Error );
	}

	[Fact]
	public async Task VersionMatchesPinnedProcpsRelease() {
		var result = await InvokeAsync( [ "--version" ], new FakeProvider( FullSnapshot() ) );
		Assert.Equal( 0, result.Status );
		Assert.Equal( string.Concat( "vmstat from procps-ng 4.0.6", Environment.NewLine ), result.Output );
	}

	[Fact]
	public async Task LinuxProviderParsesStatDiskAndPartitionFixtures() {
		var root = CreateTempDirectory();
		var proc = System.IO.Path.Combine( root, "proc" );
		var sys = System.IO.Path.Combine( root, "sys" );
		Directory.CreateDirectory( proc );
		var partitionMarker = System.IO.Path.Combine( sys, "dev", "block", "8:1", "partition" );
		bool FileExists( string path ) => string.Equals( path, partitionMarker, StringComparison.Ordinal );
		try {
			await File.WriteAllTextAsync( System.IO.Path.Combine( proc, "stat" ), "cpu 1 2 3 4 5 6 7 8 9 10\nintr 100 1 2\nctxt 200\nbtime 300\nprocesses 400\nprocs_running 5\nprocs_blocked 6\n" );
			await File.WriteAllTextAsync( System.IO.Path.Combine( proc, "diskstats" ), "8 0 sda 1 2 3 4 5 6 7 8 9 10 11\n8 1 sda1 12 13 14 15 16 17 18 19 20 21 22\n" );
			var counters = LinuxProcVmstatProvider.ParseSystemCounters( await File.ReadAllTextAsync( System.IO.Path.Combine( proc, "stat" ) ) );
			Assert.Equal( 5UL, counters.RunningProcesses );
			Assert.Equal( 400UL, counters.Forks );
			var rows = LinuxProcVmstatProvider.ParseDiskStats( await File.ReadAllTextAsync( System.IO.Path.Combine( proc, "diskstats" ) ), sys, FileExists );
			Assert.Equal( 2, rows.Count );
			Assert.False( rows[ 0 ].IsPartition );
			Assert.True( rows[ 1 ].IsPartition );
			Assert.Equal( 18UL, rows[ 1 ].SectorsWritten );
		} finally { Directory.Delete( root, recursive: true ); }
	}

	[Fact]
	public void CounterDeltaHandlesWraparound() => Assert.Equal( 4UL, ProcCounterMath.Delta( uint.MaxValue - 1UL, 2UL, 32 ) );

	[Fact]
	public async Task CancellationReturnsShellCompatibleStatus() {
		using var source = new CancellationTokenSource();
		var provider = new FakeProvider( FullSnapshot(), FullSnapshot() );
		var result = await InvokeAsync( [ "1", "2" ], provider, ( _, token ) => { source.Cancel(); return Task.FromCanceled( token ); }, source.Token );
		Assert.Equal( 130, result.Status );
	}

	private static int CountOccurrences( string text, string value ) {
		var count = 0; var offset = 0;
		while ( true ) { var index = text.IndexOf( value, offset, StringComparison.Ordinal ); if ( 0 > index ) return count; count++; offset = index + value.Length; }
	}
	private static string CreateTempDirectory() { var path = System.IO.Path.Combine( System.IO.Path.GetTempPath(), string.Concat( "icod-procps-vmstat-", Guid.NewGuid().ToString( "N" ) ) ); Directory.CreateDirectory( path ); return path; }
	private static ProcObservedValue<T> Available<T>( T value ) => ProcObservedValue<T>.Available( value, ProcObservationSource.Configuration, ObservationFidelity.Exact );
	private static ProcVmstatSnapshot FullSnapshot( ulong interrupts = 1000, ulong contextSwitches = 2000, ulong pageIn = 500, ulong pageOut = 600, ulong swapIn = 10, ulong swapOut = 20, ulong uptimeSeconds = 100, ulong forks = 1234, ProcCpuTimes? cpuTimes = null ) {
		static ulong KiB( ulong value ) => value * 1024UL;
		var memory = new ProcMemoryInfo( new Dictionary<string, ulong> {
			[ "MemTotal" ] = KiB( 1000 ), [ "MemFree" ] = KiB( 300 ), [ "MemAvailable" ] = KiB( 400 ), [ "Buffers" ] = KiB( 50 ), [ "Cached" ] = KiB( 100 ), [ "SReclaimable" ] = KiB( 25 ), [ "SwapCached" ] = KiB( 5 ),
			[ "SwapTotal" ] = KiB( 500 ), [ "SwapFree" ] = KiB( 200 ), [ "Active" ] = KiB( 400 ), [ "Inactive" ] = KiB( 250 )
		} );
		var vm = new Dictionary<string, ulong> { [ "pgpgin" ] = pageIn, [ "pgpgout" ] = pageOut, [ "pswpin" ] = swapIn, [ "pswpout" ] = swapOut, [ "pgfree" ] = 10 };
		var system = new ProcSystemSnapshot {
			Memory = Available( memory ), Cpu = Available( cpuTimes ?? new ProcCpuTimes( 40, 10, 20, 20, 5, 2, 3, 0, 5, 0 ) ),
			CpuActivity = Available( new ProcCpuActivity( 40, 25, 20, 10, 5 ) ), Uptime = Available( new ProcUptimeInfo( TimeSpan.FromSeconds( uptimeSeconds ), null ) ),
			VirtualMemory = Available<IReadOnlyDictionary<string, ulong>>( vm ),
			Slab = Available<IReadOnlyList<ProcSlabEntry>>( [ new ProcSlabEntry( "zeta", 1, 2, 64, 8, 1 ), new ProcSlabEntry( "alpha", 3, 4, 128, 4, 1 ) ] )
		};
		return new ProcVmstatSnapshot {
			System = system,
			SystemCounters = Available( new ProcVmstatSystemCounters( 2, 1, interrupts, contextSwitches, 1_700_000_000, forks ) ),
			Paging = Available( new ProcVmstatPagingCounters( pageIn, pageOut, swapIn, swapOut, 4096 ) ),
			Disks = Available<IReadOnlyList<ProcDiskStatEntry>>( [
				new ProcDiskStatEntry( 8, 0, "sda", false, 10, 2, 30, 40, 50, 6, 70, 80, 1, 90, 100 ),
				new ProcDiskStatEntry( 8, 1, "sda1", true, 3, 0, 4, 5, 6, 0, 7, 8, 0, 9, 10 )
			] )
		};
	}
	private static async Task<InvocationResult> InvokeAsync( string[] args, FakeProvider provider, Func<TimeSpan, CancellationToken, Task>? delay = null, CancellationToken cancellationToken = default ) {
		using var output = new MemoryStream(); using var error = new MemoryStream();
		var status = await Command.RunAsync( args, output, error, provider, delay, () => new DateTimeOffset( 2026, 8, 7, 23, 0, 0, TimeSpan.FromHours( -4 ) ), cancellationToken );
		return new InvocationResult( status, Encoding.UTF8.GetString( output.ToArray() ), Encoding.UTF8.GetString( error.ToArray() ) );
	}
	private sealed record InvocationResult( int Status, string Output, string Error );
	private sealed class FakeProvider : IProcVmstatProvider {
		private readonly Queue<ProcVmstatSnapshot> _snapshots;
		private ProcVmstatSnapshot _last;
		public FakeProvider( params ProcVmstatSnapshot[] snapshots ) : this( ProcVmstatCapabilities.Memory | ProcVmstatCapabilities.Cpu | ProcVmstatCapabilities.ProcessQueues | ProcVmstatCapabilities.Paging | ProcVmstatCapabilities.SystemEvents | ProcVmstatCapabilities.Disk | ProcVmstatCapabilities.Partition | ProcVmstatCapabilities.Slab | ProcVmstatCapabilities.Forks | ProcVmstatCapabilities.Statistics, snapshots ) { }
		public FakeProvider( ProcVmstatCapabilities capabilities, params ProcVmstatSnapshot[] snapshots ) { if ( 0 == snapshots.Length ) throw new ArgumentException( "At least one snapshot is required.", nameof( snapshots ) ); this.Capabilities = capabilities; this._snapshots = new Queue<ProcVmstatSnapshot>( snapshots ); this._last = snapshots[ ^1 ]; }
		public ProcVmstatCapabilities Capabilities { get; }
		public int RequestCount { get; private set; }
		public Task<ProcVmstatSnapshot> GetSnapshotAsync( CancellationToken cancellationToken = default ) { cancellationToken.ThrowIfCancellationRequested(); this.RequestCount++; if ( 0 < this._snapshots.Count ) this._last = this._snapshots.Dequeue(); return Task.FromResult( this._last ); }
	}
}
