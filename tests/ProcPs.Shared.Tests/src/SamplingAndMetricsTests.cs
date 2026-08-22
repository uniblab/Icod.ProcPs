namespace Icod.ProcPs.Shared.Tests;

using Icod.ProcPs.Shared;
using Xunit;

public sealed class SamplingAndMetricsTests {
	[Fact]
	public void CounterDeltaHandlesWraparound() {
		Assert.Equal( 11UL, ProcCounterMath.Delta( 250, 5, 8 ) );
	}

	[Fact]
	public void CpuBusyPercentExcludesIdleDelta() {
		var before = new ProcCpuTimes( 10, 0, 10, 80, 0, 0, 0, 0, 0, 0 );
		var after = new ProcCpuTimes( 20, 0, 20, 160, 0, 0, 0, 0, 0, 0 );
		Assert.Equal( 20d, ProcCpuMath.BusyPercent( before, after ), 6 );
	}

	[Fact]
	public void LoadAverageParserReadsRunnableAndLastPid() {
		var load = LinuxProcSystemMetricsProvider.ParseLoadAverage( "0.10 0.20 0.30 2/100 4321\n" );
		Assert.Equal( 0.10, load.OneMinute );
		Assert.Equal( 2, load.Runnable );
		Assert.Equal( 100, load.TotalEntities );
		Assert.Equal( 4321, load.LastProcessId );
	}

	[Fact]
	public void CpuParserDoesNotDoubleCountGuestFieldsInTotal() {
		var cpu = LinuxProcSystemMetricsProvider.ParseCpu( "cpu 1 2 3 4 5 6 7 8 9 10\n" );
		Assert.Equal( 36UL, cpu.Total );
		Assert.Equal( 9UL, cpu.Guest );
		Assert.Equal( 10UL, cpu.GuestNice );
	}

	[Fact]
	public async Task LinuxProviderObservesUserSessionsWhenRunningOnLinux() {
		if ( !OperatingSystem.IsLinux() ) return;
		var provider = new LinuxProcSystemMetricsProvider();
		Assert.True( provider.Capabilities.HasFlag( ProcSystemCapabilities.UserSessions ) );
		var snapshot = await provider.GetSnapshotAsync();
		Assert.True( snapshot.UserSessions.HasValue, snapshot.UserSessions.Diagnostic );
		Assert.True( 0 <= snapshot.UserSessions.Value.Count );
	}
	[Fact]
	public void SystemMetricProviderSelectsThePrimaryPlatformBackend() {
		var capabilities = SystemProcSystemMetricsProvider.Instance.Capabilities;
		if ( OperatingSystem.IsLinux() ) {
			Assert.True( capabilities.HasFlag( ProcSystemCapabilities.VirtualMemory ) );
			Assert.True( capabilities.HasFlag( ProcSystemCapabilities.LoadAverage ) );
		} else if ( OperatingSystem.IsWindows() ) {
			Assert.True( capabilities.HasFlag( ProcSystemCapabilities.Memory ) );
			Assert.True( capabilities.HasFlag( ProcSystemCapabilities.CpuActivity ) );
			Assert.False( capabilities.HasFlag( ProcSystemCapabilities.LoadAverage ) );
		} else if ( OperatingSystem.IsMacOS() ) {
			Assert.True( capabilities.HasFlag( ProcSystemCapabilities.Memory ) );
			Assert.True( capabilities.HasFlag( ProcSystemCapabilities.LoadAverage ) );
		}
	}

	[Fact]
	public void LinuxMemoryInfoAlsoPopulatesNeutralFields() {
		var memory = new ProcMemoryInfo( new Dictionary<string, ulong> {
			[ "MemTotal" ] = 1_000UL,
			[ "MemFree" ] = 100UL,
			[ "MemAvailable" ] = 400UL,
			[ "Buffers" ] = 20UL,
			[ "Cached" ] = 200UL,
			[ "SReclaimable" ] = 30UL,
			[ "SwapTotal" ] = 500UL,
			[ "SwapFree" ] = 300UL
		} );
		Assert.Equal( 1_000UL, memory.TotalBytes );
		Assert.Equal( 100UL, memory.FreeBytes );
		Assert.Equal( 400UL, memory.AvailableBytes );
		Assert.Equal( 230UL, memory.CacheBytes );
		Assert.Equal( 500UL, memory.SwapTotalBytes );
		Assert.Equal( 300UL, memory.SwapFreeBytes );
	}

	[Fact]
	public async Task WindowsSystemProviderUsesNativeMetricsWhenRunningOnWindows() {
		if ( !OperatingSystem.IsWindows() ) return;
		var provider = new WindowsProcSystemMetricsProvider();
		Assert.True( provider.Capabilities.HasFlag( ProcSystemCapabilities.Memory ) );
		Assert.True( provider.Capabilities.HasFlag( ProcSystemCapabilities.CpuActivity ) );
		Assert.True( provider.Capabilities.HasFlag( ProcSystemCapabilities.Uptime ) );
		var snapshot = await provider.GetSnapshotAsync();
		Assert.True( snapshot.Memory.HasValue, snapshot.Memory.Diagnostic );
		Assert.Equal( ProcObservationSource.WindowsNativeApi, snapshot.Memory.Source );
		Assert.True( snapshot.Memory.Value.TotalBytes is > 0UL );
		Assert.True( snapshot.CpuActivity.HasValue, snapshot.CpuActivity.Diagnostic );
		Assert.Equal( ProcObservationSource.WindowsNativeApi, snapshot.CpuActivity.Source );
		Assert.Equal( 64, snapshot.CpuActivity.Value.CounterBitWidth );
		Assert.True( snapshot.Uptime.HasValue, snapshot.Uptime.Diagnostic );
		Assert.False( snapshot.LoadAverages.HasValue );
		Assert.Equal( ProcObservationAvailability.Unsupported, snapshot.LoadAverages.Availability );
	}

	[Fact]
	public async Task MacOsSystemProviderUsesNativeMetricsWhenRunningOnMacOs() {
		if ( !OperatingSystem.IsMacOS() ) return;
		var provider = new MacOsProcSystemMetricsProvider();
		Assert.True( provider.Capabilities.HasFlag( ProcSystemCapabilities.Memory ) );
		Assert.True( provider.Capabilities.HasFlag( ProcSystemCapabilities.CpuActivity ) );
		Assert.True( provider.Capabilities.HasFlag( ProcSystemCapabilities.LoadAverage ) );
		Assert.True( provider.Capabilities.HasFlag( ProcSystemCapabilities.UserSessions ) );
		var snapshot = await provider.GetSnapshotAsync();
		Assert.True( snapshot.Memory.HasValue, snapshot.Memory.Diagnostic );
		Assert.Equal( ProcObservationSource.DarwinMach, snapshot.Memory.Source );
		Assert.True( snapshot.Memory.Value.TotalBytes is > 0UL );
		Assert.True( snapshot.CpuActivity.HasValue, snapshot.CpuActivity.Diagnostic );
		Assert.Equal( 32, snapshot.CpuActivity.Value.CounterBitWidth );
		Assert.True( snapshot.LoadAverages.HasValue, snapshot.LoadAverages.Diagnostic );
		Assert.Equal( ProcObservationSource.PosixLibc, snapshot.LoadAverages.Source );
		Assert.True( snapshot.Uptime.HasValue, snapshot.Uptime.Diagnostic );
		Assert.Equal( ProcObservationSource.DarwinSysctl, snapshot.Uptime.Source );
		Assert.True( snapshot.UserSessions.HasValue, snapshot.UserSessions.Diagnostic );
		Assert.True( 0 <= snapshot.UserSessions.Value.Count );
	}

}
