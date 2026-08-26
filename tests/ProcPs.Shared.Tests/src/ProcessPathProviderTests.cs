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

/// <summary>Contains tests for process path provider.</summary>
public sealed class ProcessPathProviderTests {
	/// <summary>Verifies that system provider observes current executable without weakening identity.</summary>
	[Fact]
	public async Task SystemProviderObservesCurrentExecutableWithoutWeakeningIdentity() {
		var process = await SystemProcProcessProvider.Instance.GetProcessAsync( Environment.ProcessId );
		Assert.True( process.HasValue );
		var observed = await SystemProcProcessPathProvider.Instance.ObserveAsync( process.Value );
		Assert.True( observed.HasValue );
		Assert.True( observed.Value.ExecutablePath.HasValue );
		Assert.False( string.IsNullOrWhiteSpace( observed.Value.ExecutablePath.Value ) );
	}

	/// <summary>Verifies that current working directory capability matches supported platform policy.</summary>
	[Fact]
	public async Task CurrentWorkingDirectoryCapabilityMatchesSupportedPlatformPolicy() {
		var process = await SystemProcProcessProvider.Instance.GetProcessAsync( Environment.ProcessId );
		Assert.True( process.HasValue );
		var observed = await SystemProcProcessPathProvider.Instance.ObserveAsync( process.Value );
		Assert.True( observed.HasValue );
		if ( OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ) {
			Assert.True( observed.Value.WorkingDirectory.HasValue );
			Assert.False( string.IsNullOrWhiteSpace( observed.Value.WorkingDirectory.Value ) );
		} else if ( OperatingSystem.IsWindows() ) {
			Assert.False( observed.Value.WorkingDirectory.HasValue );
			Assert.Equal( ProcObservationAvailability.Unsupported, observed.Value.WorkingDirectory.Availability );
		}
	}
}
