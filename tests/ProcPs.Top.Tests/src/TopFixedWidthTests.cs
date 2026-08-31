/*
	Icod.ProcPs.Top.Tests
	Tests for procps-ng top X / Fixed_widest behavior.
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

/// <summary>Exercises fixed-width persistence, truncation, and automatic widening.</summary>
public sealed class TopFixedWidthTests {
	[Fact]
	public void FixedWidthRoundTripsThroughIcodAndNativeConfiguration() {
		TopRuntimeState source = new();
		TopFixedWidth.Configure(
			source,
			4
		);

		string json = TopConfigurationCodec.Serialize(
			source
		);
		TopRuntimeState jsonRestored = new();
		TopConfigurationCodec.Apply(
			json,
			jsonRestored
		);
		Assert.Equal( 4, jsonRestored.FixedWidthExtra );

		string native = TopProcpsConfigurationCodec.Serialize(
			source
		);
		Assert.Contains(
			"Fixed_widest=4",
			native,
			StringComparison.Ordinal
		);
		TopRuntimeState nativeRestored = new();
		TopProcpsConfigurationCodec.Apply(
			native,
			nativeRestored
		);
		Assert.Equal( 4, nativeRestored.FixedWidthExtra );

		TopFixedWidth.Configure(
			source,
			TopFixedWidth.Automatic
		);
		native = TopProcpsConfigurationCodec.Serialize(
			source
		);
		nativeRestored = new TopRuntimeState();
		TopProcpsConfigurationCodec.Apply(
			native,
			nativeRestored
		);
		Assert.Equal(
			TopFixedWidth.Automatic,
			nativeRestored.FixedWidthExtra
		);
	}

	[Fact]
	public void DefaultWidthMarksTruncationAndExplicitWidthExpandsUser() {
		TopRuntimeState state = UserOnlyState();
		TopSample sample = CreateSample(
			"abcdefghijkl"
		);

		IReadOnlyList<string> lines = TopRenderer.RenderBatch(
			sample,
			state,
			80
		);
		Assert.Equal(
			"abcdefg+",
			lines[ ^1 ]
		);

		TopFixedWidth.Configure(
			state,
			4
		);
		lines = TopRenderer.RenderBatch(
			sample,
			state,
			80
		);
		Assert.Equal(
			"abcdefghijkl",
			lines[ ^1 ]
		);
	}

	[Fact]
	public void AutomaticWidthGrowsButDoesNotShrinkUntilReset() {
		TopRuntimeState state = UserOnlyState();
		TopFixedWidth.Configure(
			state,
			TopFixedWidth.Automatic
		);

		_ = TopRenderer.RenderBatch(
			CreateSample( "abcdefghijkl" ),
			state,
			80
		);
		Assert.Equal(
			12,
			TopFixedWidth.Width(
				state,
				TopFieldCatalog.Get( TopFieldId.User )
			)
		);

		IReadOnlyList<string> lines = TopRenderer.RenderBatch(
			CreateSample( "bob" ),
			state,
			80
		);
		Assert.Equal(
			12,
			TopFixedWidth.Width(
				state,
				TopFieldCatalog.Get( TopFieldId.User )
			)
		);
		Assert.Equal(
			"bob         ",
			lines[ ^1 ]
		);

		TopFixedWidth.Configure(
			state,
			0
		);
		Assert.Equal(
			8,
			TopFixedWidth.Width(
				state,
				TopFieldCatalog.Get( TopFieldId.User )
			)
		);
	}

	[Fact]
	public void RelationalOtherFilterUsesCurrentFixedWidth() {
		TopRuntimeState state = UserOnlyState();
		TopFixedWidth.Configure(
			state,
			4
		);

		Assert.True(
			TopOtherFilterParser.TryParse(
				"USER>bob",
				caseSensitive: true,
				state,
				out TopOtherFilter? filter,
				out string? error
			),
			error
		);
		Assert.NotNull( filter );
		Assert.Equal(
			"bob         ",
			filter!.SelectionValue
		);
	}

	[Fact]
	public void InvalidNativeFixedWidthFallsBackToProcpsDefault() {
		TopRuntimeState source = new();
		string native = TopProcpsConfigurationCodec.Serialize(
			source
		).Replace(
			"Fixed_widest=0",
			"Fixed_widest=513",
			StringComparison.Ordinal
		);
		TopRuntimeState restored = new();

		TopProcpsConfigurationCodec.Apply(
			native,
			restored
		);

		Assert.Equal( 0, restored.FixedWidthExtra );
	}

	private static TopRuntimeState UserOnlyState() {
		TopRuntimeState result = new() {
			SortField = TopFieldId.User
		};
		result.FieldOrder.Clear();
		result.FieldOrder.Add(
			TopFieldId.User
		);
		result.VisibleFields.Clear();
		result.VisibleFields.Add(
			TopFieldId.User
		);
		return result;
	}

	private static TopSample CreateSample(
		string user
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( user );
		ProcessIdentity identity = new(
			100,
			new ProcessReuseToken(
				"test",
				"start-100"
			)
		);
		ProcProcessSnapshot process = new( identity ) {
			CommandName = Exact( "worker" ),
			State = Exact( ProcProcessState.Sleeping ),
			ParentProcessId = Exact( 1 ),
			NiceValue = Exact( 0 )
		};
		TopTaskRow row = new(
			process,
			100,
			user,
			0.0,
			0.0,
			0.0
		);
		return new TopSample(
			new ProcSystemSnapshot(),
			[ row ],
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
