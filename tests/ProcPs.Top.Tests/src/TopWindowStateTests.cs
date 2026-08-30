/*
	Icod.ProcPs.Top.Tests
	Tests for top four-window state and persistence.
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

namespace Icod.ProcPs.Top.Tests;

using Xunit;

/// <summary>Exercises independent field-group state and JSON persistence.</summary>
public sealed class TopWindowStateTests {
	[Fact]
	public void WindowsKeepSortAndFieldStateIndependent() {
		TopRuntimeState state = new() {
			SortField = TopFieldId.Pid
		};
		state.VisibleFields.Remove(
			TopFieldId.Cpu
		);

		state.ActivateWindow( 1 );

		Assert.Equal( "2:Job", state.CurrentWindowLabel );
		Assert.Equal( TopFieldId.Cpu, state.SortField );
		Assert.Contains( TopFieldId.Cpu, state.VisibleFields );

		state.SortField = TopFieldId.Memory;
		state.ShowCommandLine = true;
		state.VisibleFields.Remove(
			TopFieldId.Pid
		);
		state.ActivateWindow( 0 );

		Assert.Equal( TopFieldId.Pid, state.SortField );
		Assert.DoesNotContain( TopFieldId.Cpu, state.VisibleFields );
		Assert.False( state.ShowCommandLine );

		state.ActivateWindow( 1 );

		Assert.Equal( TopFieldId.Memory, state.SortField );
		Assert.DoesNotContain( TopFieldId.Pid, state.VisibleFields );
		Assert.True( state.ShowCommandLine );
	}

	[Fact]
	public void ConfigurationRoundTripsAllWindowsAndCurrentSelection() {
		TopRuntimeState source = new() {
			AlternateDisplayMode = true,
			SortField = TopFieldId.Pid
		};
		source.ActivateWindow( 1 );
		source.SortField = TopFieldId.Memory;
		source.VisibleFields.Remove(
			TopFieldId.Pid
		);
		source.ActivateWindow( 2 );
		source.MaximumTasks = 7;

		string serialized = TopConfigurationCodec.Serialize(
			source
		);
		TopRuntimeState restored = new();
		TopConfigurationCodec.Apply(
			serialized,
			restored
		);

		Assert.True( restored.AlternateDisplayMode );
		Assert.Equal( 2, restored.CurrentWindowIndex );
		Assert.Equal( "3:Mem", restored.CurrentWindowLabel );
		Assert.Equal( 7, restored.MaximumTasks );

		restored.ActivateWindow( 0 );
		Assert.Equal( TopFieldId.Pid, restored.SortField );

		restored.ActivateWindow( 1 );
		Assert.Equal( TopFieldId.Memory, restored.SortField );
		Assert.DoesNotContain( TopFieldId.Pid, restored.VisibleFields );
	}
}
