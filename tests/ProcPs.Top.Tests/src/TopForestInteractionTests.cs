/*
	Icod.ProcPs.Top.Tests
	Tests for procps-ng top forest focus and child-collapse interaction.
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

using Icod.Processes;
using Icod.ProcPs.Shared;
using Xunit;

/// <summary>Exercises procps-compatible forest focus and collapse behavior.</summary>
public sealed class TopForestInteractionTests {
	[Fact]
	public void ForestFocusRestrictsDisplayToTopmostSubtree() {
		TopSample sample = CreateSample();
		TopRuntimeState state = CreateForestState();

		Assert.True(
			TopRenderer.ToggleForestFocus(
				sample,
				state
			)
		);
		Assert.NotNull( state.ForestFocus );
		Assert.Equal(
			sample.Tasks[ 0 ].Process.Identity,
			state.ForestFocus
		);

		IReadOnlyList<string> lines = TopRenderer.RenderBatch(
			sample,
			state,
			120
		);
		Assert.Contains(
			lines,
			line => line.Contains(
				"root",
				StringComparison.Ordinal
			)
		);
		Assert.Contains(
			lines,
			line => line.Contains(
				"grandchild",
				StringComparison.Ordinal
			)
		);
		Assert.DoesNotContain(
			lines,
			line => line.Contains(
				"other",
				StringComparison.Ordinal
			)
		);

		Assert.True(
			TopRenderer.ToggleForestFocus(
				sample,
				state
			)
		);
		Assert.Null( state.ForestFocus );
		lines = TopRenderer.RenderBatch(
			sample,
			state,
			120
		);
		Assert.Contains(
			lines,
			line => line.Contains(
				"other",
				StringComparison.Ordinal
			)
		);
	}

	[Fact]
	public void ChildCollapseAggregatesSuppressedCpuAndCanExpandAgain() {
		TopSample sample = CreateSample();
		TopRuntimeState state = CreateForestState();

		Assert.True(
			TopRenderer.ToggleTopmostForestChildren(
				sample,
				state
			)
		);
		Assert.Contains(
			sample.Tasks[ 0 ].Process.Identity,
			state.CollapsedForestParents
		);

		IReadOnlyList<string> lines = TopRenderer.RenderBatch(
			sample,
			state,
			120
		);
		string rootLine = Assert.Single(
			lines,
			line => line.Contains(
				"root",
				StringComparison.Ordinal
			)
		);
		Assert.Contains(
			"100.0",
			rootLine,
			StringComparison.Ordinal
		);
		Assert.Equal(
			10.0,
			sample.Tasks[ 0 ].CpuPercentIrix
		);
		Assert.DoesNotContain(
			lines,
			line => line.Contains(
				"child-a",
				StringComparison.Ordinal
			)
		);
		Assert.DoesNotContain(
			lines,
			line => line.Contains(
				"child-b",
				StringComparison.Ordinal
			)
		);
		Assert.DoesNotContain(
			lines,
			line => line.Contains(
				"grandchild",
				StringComparison.Ordinal
			)
		);

		Assert.True(
			TopRenderer.ToggleTopmostForestChildren(
				sample,
				state
			)
		);
		Assert.DoesNotContain(
			sample.Tasks[ 0 ].Process.Identity,
			state.CollapsedForestParents
		);
		lines = TopRenderer.RenderBatch(
			sample,
			state,
			120
		);
		Assert.Contains(
			lines,
			line => line.Contains(
				"grandchild",
				StringComparison.Ordinal
			)
		);
	}

	[Fact]
	public void ForestRestrictionsAreTransientAndPerWindow() {
		TopSample sample = CreateSample();
		TopRuntimeState state = CreateForestState();

		Assert.True(
			TopRenderer.ToggleForestFocus(
				sample,
				state
			)
		);
		Assert.True(
			TopRenderer.ToggleTopmostForestChildren(
				sample,
				state
			)
		);
		state.SynchronizeCurrentWindow();

		state.ActivateWindow( 1 );
		Assert.Null( state.ForestFocus );
		Assert.Empty( state.CollapsedForestParents );

		state.ActivateWindow( 0 );
		Assert.NotNull( state.ForestFocus );
		Assert.NotEmpty( state.CollapsedForestParents );

		state.ClearForestRestrictions();
		Assert.Null( state.ForestFocus );
		Assert.Empty( state.CollapsedForestParents );
	}

	[Fact]
	public void ForestCommandsAreNoOpsOutsideForestMode() {
		TopSample sample = CreateSample();
		TopRuntimeState state = new();

		Assert.False(
			TopRenderer.ToggleForestFocus(
				sample,
				state
			)
		);
		Assert.False(
			TopRenderer.ToggleTopmostForestChildren(
				sample,
				state
			)
		);
		Assert.Null( state.ForestFocus );
		Assert.Empty( state.CollapsedForestParents );
	}

	[Fact]
	public void SortExitClearsForestRestrictions() {
		TopSample sample = CreateSample();
		TopRuntimeState state = CreateForestState();

		Assert.True(
			TopRenderer.ToggleForestFocus(
				sample,
				state
			)
		);
		Assert.True(
			TopRenderer.ToggleTopmostForestChildren(
				sample,
				state
			)
		);

		state.ExitForestForSort();

		Assert.False( state.Forest );
		Assert.Null( state.ForestFocus );
		Assert.Empty( state.CollapsedForestParents );
	}

	private static TopRuntimeState CreateForestState() {
		return new TopRuntimeState {
			Forest = true,
			SortField = TopFieldId.Pid,
			SortHighToLow = false
		};
	}

	private static TopSample CreateSample() {
		return new TopSample(
			new ProcSystemSnapshot(),
			[
				CreateTask(
					100,
					1,
					"root",
					10.0
				),
				CreateTask(
					110,
					100,
					"child-a",
					20.0
				),
				CreateTask(
					111,
					110,
					"grandchild",
					40.0
				),
				CreateTask(
					120,
					100,
					"child-b",
					30.0
				),
				CreateTask(
					200,
					1,
					"other",
					5.0
				)
			],
			new TopCpuSummary(
				false,
				0.0,
				0.0,
				0.0,
				100.0,
				0.0,
				0.0,
				0.0,
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

	private static TopTaskRow CreateTask(
		int processId,
		int parentProcessId,
		string command,
		double cpuPercent
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( command );
		ProcessIdentity identity = new(
			processId,
			new ProcessReuseToken(
				"test",
				$"start-{processId}"
			)
		);
		ProcProcessSnapshot process = new( identity ) {
			CommandName = Exact( command ),
			State = Exact( ProcProcessState.Sleeping ),
			ParentProcessId = Exact( parentProcessId ),
			NiceValue = Exact( 0 )
		};
		return new TopTaskRow(
			process,
			processId,
			"user",
			cpuPercent,
			0.0,
			0.0
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
