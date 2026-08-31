/*
	Icod.ProcPs.Top.Tests
	Tests for procps-ng top summary graph interaction and rendering.
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

using Icod.ProcPs.Shared;
using Xunit;

/// <summary>Exercises procps-compatible CPU and memory summary graph behavior.</summary>
public sealed class TopGraphInteractionTests {
	[Fact]
	public void CpuSummaryCycleMatchesProcpsOrder() {
		TopRuntimeState state = new();

		state.CycleCpuSummaryPresentation();
		Assert.True( state.CpuSummaryVisible );
		Assert.Equal(
			TopSummaryGraphMode.Bar,
			state.CpuSummaryGraphMode
		);

		state.CycleCpuSummaryPresentation();
		Assert.True( state.CpuSummaryVisible );
		Assert.Equal(
			TopSummaryGraphMode.Block,
			state.CpuSummaryGraphMode
		);

		state.CycleCpuSummaryPresentation();
		Assert.False( state.CpuSummaryVisible );
		Assert.Equal(
			TopSummaryGraphMode.Detailed,
			state.CpuSummaryGraphMode
		);

		state.CycleCpuSummaryPresentation();
		Assert.True( state.CpuSummaryVisible );
		Assert.Equal(
			TopSummaryGraphMode.Detailed,
			state.CpuSummaryGraphMode
		);
	}

	[Fact]
	public void HiddenSummaryRestoresRetainedSelector() {
		TopRuntimeState state = new() {
			CpuSummaryVisible = false,
			CpuSummaryGraphMode = TopSummaryGraphMode.Block,
			MemorySummaryVisible = false,
			MemorySummaryGraphMode = TopSummaryGraphMode.Bar
		};

		state.CycleCpuSummaryPresentation();
		state.CycleMemorySummaryPresentation();

		Assert.True( state.CpuSummaryVisible );
		Assert.Equal(
			TopSummaryGraphMode.Block,
			state.CpuSummaryGraphMode
		);
		Assert.True( state.MemorySummaryVisible );
		Assert.Equal(
			TopSummaryGraphMode.Bar,
			state.MemorySummaryGraphMode
		);
	}

	[Fact]
	public void RendererUsesConfiguredGraphModesAndVisibility() {
		TopRuntimeState state = new() {
			CpuSummaryGraphMode = TopSummaryGraphMode.Bar,
			MemorySummaryGraphMode = TopSummaryGraphMode.Block
		};
		TopSample sample = CreateSample();

		IReadOnlyList<string> lines = TopRenderer.RenderBatch(
			sample,
			state,
			80
		);

		Assert.Equal( 6, lines.Count );
		Assert.Contains( "|", lines[ 2 ], StringComparison.Ordinal );
		Assert.Contains( "#", lines[ 3 ], StringComparison.Ordinal );
		Assert.Contains( "#", lines[ 4 ], StringComparison.Ordinal );

		state.CpuSummaryVisible = false;
		lines = TopRenderer.RenderBatch(
			sample,
			state,
			80
		);

		Assert.Equal( 4, lines.Count );
		Assert.DoesNotContain(
			lines,
			line => line.StartsWith(
				"Tasks:",
				StringComparison.Ordinal
			)
		);
		Assert.DoesNotContain(
			lines,
			line => line.StartsWith(
				"%Cpu",
				StringComparison.Ordinal
			)
		);
	}

	[Fact]
	public void HiddenSummarySectionsReleaseTaskRows() {
		TopRuntimeState state = new();
		TopTerminalDimensions dimensions = new(
			80,
			20
		);

		Assert.Equal(
			14,
			TopRenderer.GetTaskPageSize(
				state,
				dimensions
			)
		);

		state.CpuSummaryVisible = false;
		Assert.Equal(
			16,
			TopRenderer.GetTaskPageSize(
				state,
				dimensions
			)
		);

		state.MemorySummaryVisible = false;
		Assert.Equal(
			18,
			TopRenderer.GetTaskPageSize(
				state,
				dimensions
			)
		);
	}

	private static TopSample CreateSample() {
		ProcSystemSnapshot system = new() {
			Memory = Exact(
				new ProcMemoryInfo(
					8UL * 1024 * 1024 * 1024,
					2UL * 1024 * 1024 * 1024,
					3UL * 1024 * 1024 * 1024,
					128UL * 1024 * 1024,
					512UL * 1024 * 1024,
					null,
					2UL * 1024 * 1024 * 1024,
					1UL * 1024 * 1024 * 1024
				)
			)
		};
		return new TopSample(
			system,
			Array.Empty<TopTaskRow>(),
			new TopCpuSummary(
				true,
				40.0,
				20.0,
				10.0,
				20.0,
				5.0,
				2.0,
				3.0,
				0.0,
				0.0
			),
			4,
			new DateTimeOffset(
				2026,
				8,
				31,
				0,
				0,
				0,
				TimeSpan.Zero
			)
		);
	}

	private static ProcObservedValue<T> Exact<T>(
		T value
	) {
		return ProcObservedValue<T>.Available(
			value,
			ProcObservationSource.LinuxProcfs,
			ProcObservationFidelity.Exact
		);
	}
}
