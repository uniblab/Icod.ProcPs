/*
	Icod.ProcPs.Uptime.Tests
	Windows integration tests for the uptime command implementation.
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

namespace Icod.ProcPs.Uptime.Tests;

using System.Text;
using Icod.ProcPs.Shared;
using Xunit;

/// <summary>Contains Windows integration tests for command.</summary>
public sealed class WindowsCommandTests {
	/// <summary>Verifies that standard mode reports Windows uptime when Unix load averages are unavailable.</summary>
	[Fact]
	public async Task StandardModeReportsWindowsUptimeWithoutLoadAverage() {
		if ( !OperatingSystem.IsWindows() ) {
			return;
		}

		using var output = new MemoryStream();
		using var error = new MemoryStream();
		var status = await Command.RunAsync(
			[],
			output,
			error,
			new WindowsProcSystemMetricsProvider()
		);
		var outputText = Encoding.UTF8.GetString( output.ToArray() );
		var errorText = Encoding.UTF8.GetString( error.ToArray() );

		Assert.Equal( 0, status );
		Assert.Contains( " up ", outputText );
		Assert.Contains( " user", outputText );
		Assert.DoesNotContain( "load average:", outputText );
		Assert.Equal( string.Empty, errorText );
	}
}
