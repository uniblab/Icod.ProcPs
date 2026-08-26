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
