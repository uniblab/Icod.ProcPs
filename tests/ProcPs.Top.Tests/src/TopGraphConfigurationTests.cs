/*
	Icod.ProcPs.Top.Tests
	Tests for top summary graph-state persistence.
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

/// <summary>Exercises procps-compatible summary graph selectors and visibility.</summary>
public sealed class TopGraphConfigurationTests {
	[Fact]
	public void DefaultsUseVisibleDetailedSummaries() {
		TopRuntimeState state = new();

		Assert.True( state.CpuSummaryVisible );
		Assert.Equal( TopSummaryGraphMode.Detailed, state.CpuSummaryGraphMode );
		Assert.True( state.MemorySummaryVisible );
		Assert.Equal( TopSummaryGraphMode.Detailed, state.MemorySummaryGraphMode );
	}

	[Fact]
	public void WindowActivationKeepsGraphStateIndependent() {
		TopRuntimeState state = new() {
			CpuSummaryVisible = false,
			CpuSummaryGraphMode = TopSummaryGraphMode.Block,
			MemorySummaryVisible = true,
			MemorySummaryGraphMode = TopSummaryGraphMode.Bar
		};

		state.ActivateWindow( 1 );

		Assert.True( state.CpuSummaryVisible );
		Assert.Equal( TopSummaryGraphMode.Detailed, state.CpuSummaryGraphMode );
		Assert.True( state.MemorySummaryVisible );
		Assert.Equal( TopSummaryGraphMode.Detailed, state.MemorySummaryGraphMode );

		state.ActivateWindow( 0 );

		Assert.False( state.CpuSummaryVisible );
		Assert.Equal( TopSummaryGraphMode.Block, state.CpuSummaryGraphMode );
		Assert.True( state.MemorySummaryVisible );
		Assert.Equal( TopSummaryGraphMode.Bar, state.MemorySummaryGraphMode );
	}

	[Fact]
	public void JsonRoundTripPreservesPerWindowGraphState() {
		TopRuntimeState source = new() {
			CpuSummaryVisible = false,
			CpuSummaryGraphMode = TopSummaryGraphMode.Block,
			MemorySummaryVisible = true,
			MemorySummaryGraphMode = TopSummaryGraphMode.Bar
		};
		source.ActivateWindow( 2 );
		source.CpuSummaryGraphMode = TopSummaryGraphMode.Bar;
		source.MemorySummaryVisible = false;
		source.MemorySummaryGraphMode = TopSummaryGraphMode.Block;

		string serialized = TopConfigurationCodec.Serialize( source );
		TopRuntimeState restored = new();
		TopConfigurationCodec.Apply( serialized, restored );

		Assert.Equal( 2, restored.CurrentWindowIndex );
		Assert.True( restored.CpuSummaryVisible );
		Assert.Equal( TopSummaryGraphMode.Bar, restored.CpuSummaryGraphMode );
		Assert.False( restored.MemorySummaryVisible );
		Assert.Equal( TopSummaryGraphMode.Block, restored.MemorySummaryGraphMode );

		restored.ActivateWindow( 0 );
		Assert.False( restored.CpuSummaryVisible );
		Assert.Equal( TopSummaryGraphMode.Block, restored.CpuSummaryGraphMode );
		Assert.True( restored.MemorySummaryVisible );
		Assert.Equal( TopSummaryGraphMode.Bar, restored.MemorySummaryGraphMode );
	}

	[Fact]
	public void NativeRoundTripPreservesHiddenSelectorState() {
		TopRuntimeState source = new() {
			CpuSummaryVisible = false,
			CpuSummaryGraphMode = TopSummaryGraphMode.Block,
			MemorySummaryVisible = true,
			MemorySummaryGraphMode = TopSummaryGraphMode.Bar
		};

		string serialized = TopProcpsConfigurationCodec.Serialize( source );

		Assert.Contains(
			"graph_cpus=2, graph_mems=1",
			serialized,
			StringComparison.Ordinal
		);

		TopRuntimeState restored = new();
		TopProcpsConfigurationCodec.Apply( serialized, restored );

		Assert.False( restored.CpuSummaryVisible );
		Assert.Equal( TopSummaryGraphMode.Block, restored.CpuSummaryGraphMode );
		Assert.True( restored.MemorySummaryVisible );
		Assert.Equal( TopSummaryGraphMode.Bar, restored.MemorySummaryGraphMode );
	}

	[Fact]
	public void NativeReadRejectsGraphSelectorOutsideProcpsRange() {
		string serialized = TopProcpsConfigurationCodec.Serialize(
			new TopRuntimeState()
		).Replace(
			"graph_cpus=0",
			"graph_cpus=3",
			StringComparison.Ordinal
		);

		Assert.Throws<FormatException>(
			() => TopProcpsConfigurationCodec.Apply(
				serialized,
				new TopRuntimeState()
			)
		);
	}
}
