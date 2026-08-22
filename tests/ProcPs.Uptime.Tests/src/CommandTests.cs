namespace Icod.ProcPs.Uptime.Tests;

using System.Text;
using Icod.CommandFramework.Host;
using Icod.ProcPs.Shared;
using Xunit;

public sealed class CommandTests {
	[Fact]
	public async Task StandardOutputMatchesProcpsLayout() {
		var provider = CreateProvider( TimeSpan.FromSeconds( 93784 ), 2, new ProcLoadAverage( 1.25, 2.5, 3.75, 1, 10, 99 ) );
		var result = await InvokeAsync( [], provider, new FixedTimeProvider( new DateTimeOffset( 2026, 8, 7, 12, 34, 56, TimeSpan.Zero ) ) );
		Assert.Equal( 0, result.Status );
		Assert.Equal( $" 12:34:56 up 1 day,  2:03,  2 users,  load average: 1.25, 2.50, 3.75{Environment.NewLine}", result.Output );
		Assert.Equal( string.Empty, result.Error );
	}
	[Fact]
	public async Task StandardOutputAcceptsNeutralLoadAverages() {
		var snapshot = new ProcSystemSnapshot {
			Uptime = Available( new ProcUptimeInfo( TimeSpan.FromMinutes( 15 ), null ) ),
			UserSessions = Available( new ProcUserSessionInfo( 1 ) ),
			LoadAverages = Available( new ProcLoadAverages( 0.25, 0.5, 0.75 ) )
		};
		var result = await InvokeAsync( [], new FakeMetricsProvider( snapshot ), new FixedTimeProvider( new DateTimeOffset( 2026, 8, 7, 12, 34, 56, TimeSpan.Zero ) ) );
		Assert.Equal( 0, result.Status );
		Assert.Equal( $" 12:34:56 up 15 min,  1 user,  load average: 0.25, 0.50, 0.75{Environment.NewLine}", result.Output );
		Assert.Equal( string.Empty, result.Error );
	}

	[Fact]
	public async Task PrettyUsesProcpsDecompositionRules() {
		var provider = CreateProvider( TimeSpan.FromSeconds( 93784 ), 0, new ProcLoadAverage( 0, 0, 0, 0, 0, 0 ) );
		var result = await InvokeAsync( [ "--pretty" ], provider, TimeProvider.System );
		Assert.Equal( 0, result.Status );
		Assert.Equal( $"up 1 day, 2 hours, 3 minutes{Environment.NewLine}", result.Output );
		Assert.Equal( "up 60 minutes", Command.FormatPretty( 3600 ) );
	}
	[Fact]
	public async Task SinceUsesSelectedContainerUptime() {
		var provider = CreateProvider( TimeSpan.FromHours( 10 ), 1, new ProcLoadAverage( 0, 0, 0, 0, 0, 0 ), TimeSpan.FromHours( 2 ) );
		var result = await InvokeAsync( [ "--container", "--since" ], provider, new FixedTimeProvider( new DateTimeOffset( 2026, 8, 7, 12, 0, 0, TimeSpan.Zero ) ) );
		Assert.Equal( 0, result.Status );
		Assert.Equal( $"2026-08-07 10:00:00{Environment.NewLine}", result.Output );
		Assert.True( provider.ContainerUptimeRequested );
	}

	[Fact]
	public async Task SinceAppliesHistoricalLocalOffset() {
		var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule( new DateTime( 1, 1, 1, 2, 0, 0 ), 3, 2, DayOfWeek.Sunday );
		var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule( new DateTime( 1, 1, 1, 2, 0, 0 ), 11, 1, DayOfWeek.Sunday );
		var adjustment = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule( new DateTime( 2020, 1, 1 ), new DateTime( 2030, 12, 31 ), TimeSpan.FromHours( 1 ), daylightStart, daylightEnd );
		var zone = TimeZoneInfo.CreateCustomTimeZone( "Batch57-Eastern", TimeSpan.FromHours( -5 ), "Batch57 Eastern", "Batch57 Eastern", "Batch57 Eastern Daylight", [ adjustment ] );
		var provider = CreateProvider( TimeSpan.FromHours( 3 ), 1, new ProcLoadAverage( 0, 0, 0, 0, 0, 0 ) );
		var result = await InvokeAsync( [ "--since" ], provider, new FixedTimeProvider( new DateTimeOffset( 2026, 11, 1, 7, 0, 0, TimeSpan.Zero ), zone ) );
		Assert.Equal( 0, result.Status );
		Assert.Equal( $"2026-11-01 00:00:00{Environment.NewLine}", result.Output );
	}
	[Fact]
	public async Task LaterHelpOptionWinsOverPermutedOperand() {
		var provider = CreateProvider( TimeSpan.FromMinutes( 1 ), 0, new ProcLoadAverage( 0, 0, 0, 0, 0, 0 ) );
		var result = await InvokeAsync( [ "operand", "--help" ], provider, TimeProvider.System );
		Assert.Equal( 0, result.Status );
		Assert.StartsWith( $"{Environment.NewLine}Usage:{Environment.NewLine} uptime [options]", result.Output );
		Assert.Equal( string.Empty, result.Error );
	}
	[Fact]
	public void SynchronousVersionEntryPointRemainsAvailable() {
		using var output = new StringWriter();
		using var error = new StringWriter();
		var status = Command.Run( [ "--version" ], output, error );
		Assert.Equal( 0, status );
		Assert.Equal( $"uptime from procps-ng 4.0.6{Environment.NewLine}", output.ToString() );
		Assert.Equal( string.Empty, error.ToString() );
	}

	[Fact]
	public async Task ExtraOperandPrintsUsageWithoutInventedDiagnostic() {
		var provider = CreateProvider( TimeSpan.FromMinutes( 1 ), 0, new ProcLoadAverage( 0, 0, 0, 0, 0, 0 ) );
		var result = await InvokeAsync( [ "operand" ], provider, TimeProvider.System );
		Assert.Equal( 1, result.Status );
		Assert.Equal( string.Empty, result.Output );
		Assert.StartsWith( $"{Environment.NewLine}Usage:{Environment.NewLine} uptime [options]", result.Error );
	}

	[Fact]
	public async Task RawUsesSystemUptimeEvenAfterContainerOption() {
		var provider = CreateProvider( TimeSpan.FromSeconds( 123.5 ), 3, new ProcLoadAverage( 1, 2, 3, 0, 0, 0 ), TimeSpan.FromSeconds( 4 ) );
		var result = await InvokeAsync( [ "--container", "--raw" ], provider, new FixedTimeProvider( DateTimeOffset.FromUnixTimeSeconds( 1_700_000_000 ) ) );
		Assert.Equal( 0, result.Status );
		Assert.Equal( $"1700000000 123.500000 3 1.00 2.00 3.00{Environment.NewLine}", result.Output );
		Assert.False( provider.ContainerUptimeRequested );
	}
	[Fact]
	public async Task StandardToleratesUnavailableUserCount() {
		var snapshot = new ProcSystemSnapshot {
			Uptime = Available( new ProcUptimeInfo( TimeSpan.FromMinutes( 5 ), null ) ),
			LoadAverage = Available( new ProcLoadAverage( 0.1, 0.2, 0.3, 0, 0, 0 ) ),
			UserSessions = ProcObservedValue<ProcUserSessionInfo>.Missing( ProcObservationAvailability.Unsupported )
		};
		var result = await InvokeAsync( [], new FakeMetricsProvider( snapshot ), new FixedTimeProvider( new DateTimeOffset( 2026, 1, 1, 1, 2, 3, TimeSpan.Zero ) ) );
		Assert.Equal( 0, result.Status );
		Assert.Contains( ", ? users,  load average: 0.10, 0.20, 0.30", result.Output );
	}
	[Fact]
	public async Task RawRequiresUsersLikeUpstream() {
		var snapshot = new ProcSystemSnapshot { Uptime = Available( new ProcUptimeInfo( TimeSpan.FromSeconds( 1 ), null ) ), LoadAverage = Available( new ProcLoadAverage( 0, 0, 0, 0, 0, 0 ) ), UserSessions = ProcObservedValue<ProcUserSessionInfo>.Missing( ProcObservationAvailability.Unsupported ) };
		var result = await InvokeAsync( [ "--raw" ], new FakeMetricsProvider( snapshot ), TimeProvider.System );
		Assert.Equal( 1, result.Status );
		Assert.Equal( $"uptime: procps_users{Environment.NewLine}", result.Error );
	}
	[Fact]
	public async Task ReportsControlledContainerLimitation() {
		var provider = new FakeMetricsProvider( new ProcSystemSnapshot { Uptime = Available( new ProcUptimeInfo( TimeSpan.FromMinutes( 1 ), null ) ) } ) { Container = ProcObservedValue<ProcUptimeInfo>.Missing( ProcObservationAvailability.Unsupported, "not available" ) };
		var result = await InvokeAsync( [ "-c", "-p" ], provider, TimeProvider.System );
		Assert.Equal( 1, result.Status );
		Assert.Contains( "Cannot get container uptime", result.Error );
	}
	[Fact]
	public async Task LinuxProcfsUptimeRetainsExactProvenance() {
		var root = CreateTempDirectory();
		try {
			await File.WriteAllTextAsync( System.IO.Path.Combine( root, "uptime" ), "123.50 456.00\n" );
			var observation = await new LinuxProcSystemMetricsProvider( root ).GetUptimeAsync( containerMode: false );
			Assert.True( observation.HasValue );
			Assert.Equal( ProcObservationSource.LinuxProcfs, observation.Source );
			Assert.Equal( ObservationFidelity.Exact, observation.Fidelity );
			Assert.Equal( 123.5, observation.Value.Uptime.TotalSeconds, 6 );
		} finally { Directory.Delete( root, recursive: true ); }
	}
	[Fact]
	public async Task LinuxContainerUptimeIsMarkedDerived() {
		if ( !OperatingSystem.IsLinux() ) return;
		var root = CreateTempDirectory();
		try {
			Directory.CreateDirectory( System.IO.Path.Combine( root, "1" ) );
			await File.WriteAllTextAsync( System.IO.Path.Combine( root, "uptime" ), "1000.00 0.00\n" );
			await File.WriteAllTextAsync( System.IO.Path.Combine( root, "1", "stat" ), "1 (init) S 0 0 0 0 0 0 0 0 0 0 0 0 0 0 20 0 1 0 0 0 0\n" );
			var observation = await new LinuxProcSystemMetricsProvider( root ).GetUptimeAsync( containerMode: true );
			Assert.True( observation.HasValue );
			Assert.Equal( ProcObservationSource.Derived, observation.Source );
			Assert.Equal( ObservationFidelity.Exact, observation.Fidelity );
			Assert.Equal( 1000d, observation.Value.Uptime.TotalSeconds, 6 );
		} finally { Directory.Delete( root, recursive: true ); }
	}
	private static string CreateTempDirectory() { var path = System.IO.Path.Combine( System.IO.Path.GetTempPath(), $"icod-procps-uptime-{Guid.NewGuid():N}" ); Directory.CreateDirectory( path ); return path; }
	private static FakeMetricsProvider CreateProvider( TimeSpan uptime, int users, ProcLoadAverage load, TimeSpan? container = null ) => new( new ProcSystemSnapshot { Uptime = Available( new ProcUptimeInfo( uptime, null ) ), UserSessions = Available( new ProcUserSessionInfo( users ) ), LoadAverage = Available( load ) } ) { Container = Available( new ProcUptimeInfo( container ?? uptime, null ) ) };
	private static ProcObservedValue<T> Available<T>( T value ) => ProcObservedValue<T>.Available( value, ProcObservationSource.Configuration, ObservationFidelity.Exact );
	private static async Task<InvocationResult> InvokeAsync( string[] args, IProcSystemMetricsProvider provider, TimeProvider clock ) { using var output = new MemoryStream(); using var error = new MemoryStream(); var status = await Command.RunAsync( args, output, error, provider, clock ); return new InvocationResult( status, Encoding.UTF8.GetString( output.ToArray() ), Encoding.UTF8.GetString( error.ToArray() ) ); }
	private sealed record InvocationResult( int Status, string Output, string Error );
	private sealed class FixedTimeProvider( DateTimeOffset now, TimeZoneInfo? localTimeZone = null ) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; public override TimeZoneInfo LocalTimeZone => localTimeZone ?? TimeZoneInfo.Utc; }
	private sealed class FakeMetricsProvider( ProcSystemSnapshot snapshot ) : IProcSystemMetricsProvider {
		public ProcSystemCapabilities Capabilities => ProcSystemCapabilities.Uptime | ProcSystemCapabilities.LoadAverage | ProcSystemCapabilities.UserSessions;
		public ProcObservedValue<ProcUptimeInfo> Container { get; init; } = snapshot.Uptime;
		public bool ContainerUptimeRequested { get; private set; }
		public Task<ProcSystemSnapshot> GetSnapshotAsync( CancellationToken cancellationToken = default ) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult( snapshot ); }
		public Task<ProcObservedValue<ProcUptimeInfo>> GetUptimeAsync( bool containerMode, CancellationToken cancellationToken = default ) { cancellationToken.ThrowIfCancellationRequested(); ContainerUptimeRequested |= containerMode; return Task.FromResult( containerMode ? this.Container : snapshot.Uptime ); }
	}
}
