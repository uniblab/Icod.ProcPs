/*
	Icod.ProcPs.Shared.Tests
	Tests for shared ProcPs process and system observation components.
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

namespace Icod.ProcPs.Shared.Tests;

using Icod.ProcPs.Shared;
using Icod.Timing;
using Xunit;

/// <summary>Contains tests for sampling and metrics.</summary>
public sealed class SamplingAndMetricsTests {
	/// <summary>Verifies that counter delta handles wraparound.</summary>
	[Fact]
	public void CounterDeltaHandlesWraparound() {
		Assert.Equal( 11UL, ProcCounterMath.Delta( 250, 5, 8 ) );
	}

	/// <summary>Verifies that the sampler accepts the standalone timing package contracts.</summary>
	[Fact]
	public void SamplerAcceptsStandaloneTimingContracts() {
		var sampler = new ProcSampler(
			SystemMonotonicClock.Instance,
			MonotonicPeriodicScheduler.Instance
		);

		Assert.NotNull( sampler );
	}

	/// <summary>Verifies that cpu busy percent excludes idle delta.</summary>
	[Fact]
	public void CpuBusyPercentExcludesIdleDelta() {
		var before = new ProcCpuTimes( 10, 0, 10, 80, 0, 0, 0, 0, 0, 0 );
		var after = new ProcCpuTimes( 20, 0, 20, 160, 0, 0, 0, 0, 0, 0 );
		Assert.Equal( 20d, ProcCpuMath.BusyPercent( before, after ), 6 );
	}

	/// <summary>Verifies that load average parser reads runnable and last pid.</summary>
	[Fact]
	public void LoadAverageParserReadsRunnableAndLastPid() {
		var load = LinuxProcSystemMetricsProvider.ParseLoadAverage( "0.10 0.20 0.30 2/100 4321\n" );
		Assert.Equal( 0.10, load.OneMinute );
		Assert.Equal( 2, load.Runnable );
		Assert.Equal( 100, load.TotalEntities );
		Assert.Equal( 4321, load.LastProcessId );
	}

	/// <summary>Verifies that cpu parser does not double count guest fields in total.</summary>
	[Fact]
	public void CpuParserDoesNotDoubleCountGuestFieldsInTotal() {
		var cpu = LinuxProcSystemMetricsProvider.ParseCpu( "cpu 1 2 3 4 5 6 7 8 9 10\n" );
		Assert.Equal( 36UL, cpu.Total );
		Assert.Equal( 9UL, cpu.Guest );
		Assert.Equal( 10UL, cpu.GuestNice );
	}

	/// <summary>Verifies that linux provider observes user sessions when running on linux.</summary>
	[Fact]
	public async Task LinuxProviderObservesUserSessionsWhenRunningOnLinux() {
		if ( !OperatingSystem.IsLinux() ) return;
		var provider = new LinuxProcSystemMetricsProvider();
		Assert.True( provider.Capabilities.HasFlag( ProcSystemCapabilities.UserSessions ) );
		var snapshot = await provider.GetSnapshotAsync();
		Assert.True( snapshot.UserSessions.HasValue, snapshot.UserSessions.Diagnostic );
		Assert.True( 0 <= snapshot.UserSessions.Value.Count );
	}
	/// <summary>Verifies that system metric provider selects the primary platform backend.</summary>
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

	/// <summary>Verifies that linux memory info also populates neutral fields.</summary>
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

	/// <summary>Verifies that windows system provider uses native metrics when running on windows.</summary>
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

	/// <summary>Verifies that mac os system provider uses native metrics when running on mac os.</summary>
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
