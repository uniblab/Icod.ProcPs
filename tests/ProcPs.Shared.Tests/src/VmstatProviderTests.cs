namespace Icod.ProcPs.Shared.Tests;

using Icod.ProcPs.Shared;
using Xunit;

/// <summary>Contains tests for vmstat provider.</summary>
public sealed class VmstatProviderTests {
	/// <summary>Verifies that system provider advertises platform specific capabilities.</summary>
	[Fact]
	public void SystemProviderAdvertisesPlatformSpecificCapabilities() {
		var provider = SystemProcVmstatProvider.Instance;
		if ( OperatingSystem.IsLinux() ) {
			Assert.True( provider.Capabilities.HasFlag( ProcVmstatCapabilities.Disk ) );
			Assert.True( provider.Capabilities.HasFlag( ProcVmstatCapabilities.Statistics ) );
		} else if ( OperatingSystem.IsWindows() ) {
			Assert.Equal( ProcVmstatCapabilities.Memory | ProcVmstatCapabilities.Cpu, provider.Capabilities );
		} else if ( OperatingSystem.IsMacOS() ) {
			Assert.True( provider.Capabilities.HasFlag( ProcVmstatCapabilities.Paging ) );
		}
	}

	/// <summary>Verifies that native provider produces memory and cpu on supported primary platforms.</summary>
	[Fact]
	public async Task NativeProviderProducesMemoryAndCpuOnSupportedPrimaryPlatforms() {
		if ( !OperatingSystem.IsLinux() && !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() ) return;
		var snapshot = await SystemProcVmstatProvider.Instance.GetSnapshotAsync();
		Assert.True( snapshot.System.Memory.HasValue );
		Assert.True( snapshot.System.Cpu.HasValue || snapshot.System.CpuActivity.HasValue );
	}

	/// <summary>Verifies that darwin provider carries mach paging counters when available.</summary>
	[Fact]
	public async Task DarwinProviderCarriesMachPagingCountersWhenAvailable() {
		if ( !OperatingSystem.IsMacOS() ) return;
		var snapshot = await new MacOsProcVmstatProvider().GetSnapshotAsync();
		Assert.True( snapshot.Paging.HasValue );
		Assert.Equal( ProcObservationSource.DarwinMach, snapshot.Paging.Source );
		Assert.True( 0UL < snapshot.Paging.Value.PageSizeBytes );
	}

	/// <summary>Verifies that linux provider carries exact procfs vmstat provenance.</summary>
	[Fact]
	public async Task LinuxProviderCarriesExactProcfsVmstatProvenance() {
		if ( !OperatingSystem.IsLinux() ) return;
		var snapshot = await new LinuxProcVmstatProvider().GetSnapshotAsync();
		Assert.True( snapshot.SystemCounters.HasValue );
		Assert.Equal( ProcObservationSource.LinuxProcfs, snapshot.SystemCounters.Source );
		Assert.True( snapshot.Paging.HasValue );
		Assert.Equal( ProcObservationSource.LinuxProcfs, snapshot.Paging.Source );
	}
}
