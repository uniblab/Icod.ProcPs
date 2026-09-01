/*
	Icod.ProcPs.Top.Tests
	Tests for procps-ng top load, scroll-coordinate, and sort-field parity.
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

/// <summary>Exercises remaining core display and navigation state supported by the current model.</summary>
public sealed class TopDisplayParityTests {
	[Fact]
	public void LoadAndScrollFlagsRoundTripThroughIcodAndNativeConfiguration() {
		TopRuntimeState source = new() {
			LoadAverageVisible = false,
			ScrollCoordinatesVisible = true
		};

		string json = TopConfigurationCodec.Serialize(
			source
		);
		TopRuntimeState jsonRestored = new();
		TopConfigurationCodec.Apply(
			json,
			jsonRestored
		);
		Assert.False( jsonRestored.LoadAverageVisible );
		Assert.True( jsonRestored.ScrollCoordinatesVisible );

		string native = TopProcpsConfigurationCodec.Serialize(
			source
		);
		TopRuntimeState nativeRestored = new();
		TopProcpsConfigurationCodec.Apply(
			native,
			nativeRestored
		);
		Assert.False( nativeRestored.LoadAverageVisible );
		Assert.True( nativeRestored.ScrollCoordinatesVisible );
	}

	[Fact]
	public void LoadAverageToggleReleasesOneTaskRow() {
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

		state.LoadAverageVisible = false;

		Assert.Equal(
			15,
			TopRenderer.GetTaskPageSize(
				state,
				dimensions
			)
		);
	}

	[Fact]
	public void ScrollCoordinatesUseFilteredTaskAndFieldPositions() {
		TopRuntimeState state = new() {
			ScrollCoordinatesVisible = true,
			VerticalOffset = 1,
			HorizontalOffset = 8
		};
		TopRenderFrame frame = TopRenderer.RenderInteractive(
			CreateSample(),
			state,
			new TopTerminalDimensions(
				100,
				20
			)
		);

		Assert.Contains(
			"scroll coordinates: y = 2/2 (tasks), x = ",
			frame.Lines[ ^1 ].Text,
			StringComparison.Ordinal
		);
		Assert.Contains(
			"(fields)",
			frame.Lines[ ^1 ].Text,
			StringComparison.Ordinal
		);
	}

	[Fact]
	public void SortFieldMovementSkipsHiddenFields() {
		TopRuntimeState state = new() {
			SortField = TopFieldId.Cpu,
			HorizontalOffset = 16
		};
		state.FieldOrder.Clear();
		state.FieldOrder.AddRange(
			[
				TopFieldId.Pid,
				TopFieldId.Nice,
				TopFieldId.Cpu,
				TopFieldId.Command
			]
		);
		state.VisibleFields.Clear();
		state.VisibleFields.UnionWith(
			[
				TopFieldId.Pid,
				TopFieldId.Cpu,
				TopFieldId.Command
			]
		);

		Assert.True( state.MoveSortField( -1 ) );
		Assert.Equal(
			[
				TopFieldId.Cpu,
				TopFieldId.Nice,
				TopFieldId.Pid,
				TopFieldId.Command
			],
			state.FieldOrder
		);
		Assert.Equal( 0, state.HorizontalOffset );

		Assert.True( state.MoveSortField( 1 ) );
		Assert.Equal(
			[
				TopFieldId.Pid,
				TopFieldId.Nice,
				TopFieldId.Cpu,
				TopFieldId.Command
			],
			state.FieldOrder
		);
	}

	[Fact]
	public void ObservedProcpsIdentityFieldsRenderWithoutSynthesis() {
		TopRuntimeState state = new();
		TopTaskRow row = CreateTask(
			101,
			"alpha"
		);

		Assert.Equal(
			"1",
			TopRenderer.FieldDisplayValue(
				row,
				state,
				4,
				TopFieldId.ParentProcessId
			).Trim()
		);
		Assert.Equal(
			"1000",
			TopRenderer.FieldDisplayValue(
				row,
				state,
				4,
				TopFieldId.EffectiveUserId
			).Trim()
		);
		Assert.Equal(
			"900",
			TopRenderer.FieldDisplayValue(
				row,
				state,
				4,
				TopFieldId.RealUserId
			).Trim()
		);
		Assert.Equal(
			"100",
			TopRenderer.FieldDisplayValue(
				row,
				state,
				4,
				TopFieldId.EffectiveGroupId
			).Trim()
		);
		Assert.Equal(
			"101",
			TopRenderer.FieldDisplayValue(
				row,
				state,
				4,
				TopFieldId.ProcessGroupId
			).Trim()
		);
		Assert.Equal(
			"pts/2",
			TopRenderer.FieldDisplayValue(
				row,
				state,
				4,
				TopFieldId.Terminal
			).Trim()
		);
		Assert.Equal(
			"101",
			TopRenderer.FieldDisplayValue(
				row,
				state,
				4,
				TopFieldId.ForegroundProcessGroupId
			).Trim()
		);
		Assert.Equal(
			"77",
			TopRenderer.FieldDisplayValue(
				row,
				state,
				4,
				TopFieldId.SessionId
			).Trim()
		);
		Assert.Equal(
			"4",
			TopRenderer.FieldDisplayValue(
				row,
				state,
				4,
				TopFieldId.ThreadCount
			).Trim()
		);
		Assert.Equal(
			"-",
			TopRenderer.FieldDisplayValue(
				row,
				state,
				4,
				TopFieldId.Priority
			).Trim()
		);
	}

	[Fact]
	public void ObservedFieldsRoundTripThroughNativeConfiguration() {
		TopRuntimeState source = new() {
			SortField = TopFieldId.ParentProcessId
		};
		source.VisibleFields.Clear();
		source.VisibleFields.UnionWith(
			[
				TopFieldId.ParentProcessId,
				TopFieldId.EffectiveUserId,
				TopFieldId.RealUserId,
				TopFieldId.EffectiveGroupId,
				TopFieldId.ProcessGroupId,
				TopFieldId.Terminal,
				TopFieldId.ForegroundProcessGroupId,
				TopFieldId.SessionId,
				TopFieldId.ThreadCount
			]
		);

		string native = TopProcpsConfigurationCodec.Serialize(
			source
		);
		TopRuntimeState restored = new();
		TopProcpsConfigurationCodec.Apply(
			native,
			restored
		);

		Assert.Equal(
			TopFieldId.ParentProcessId,
			restored.SortField
		);
		foreach ( TopFieldId field in source.VisibleFields ) {
			Assert.Contains(
				field,
				restored.VisibleFields
			);
		}
	}

	private static TopSample CreateSample() {
		return new TopSample(
			new ProcSystemSnapshot(),
			[
				CreateTask( 101, "alpha" ),
				CreateTask( 202, "beta" )
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
		string command
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
			ParentProcessId = Exact( 1 ),
			EffectiveUserId = Exact( 1000U ),
			RealUserId = Exact( 900U ),
			EffectiveGroupId = Exact( 100U ),
			ProcessGroupId = Exact( processId ),
			Terminal = Exact(
				new ProcTerminalInfo(
					123,
					"/dev/pts/2"
				)
			),
			ForegroundProcessGroupId = Exact( processId ),
			SessionId = Exact( 77 ),
			NiceValue = Exact( 0 ),
			ThreadCount = Exact( 4 )
		};
		return new TopTaskRow(
			process,
			processId,
			"user",
			0.0,
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
