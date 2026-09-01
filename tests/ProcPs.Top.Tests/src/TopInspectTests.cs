/*
	Icod.ProcPs.Top.Tests
	Tests for procps-compatible top Inspect support.
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

using System.Text;
using Icod.Processes;
using Icod.ProcPs.Shared;
using Xunit;

/// <summary>Exercises Inspect configuration, execution, navigation, and rendering.</summary>
public sealed class TopInspectTests {
	[Fact]
	public void NativeInspectEntryParsesAndExpandsPid() {
		TopInspectEntry entry = TopInspectEntry.ParseNative(
			"file\tNUMA Info\t/proc/%d/numa_maps"
		);

		Assert.Equal(
			TopInspectEntryType.File,
			entry.Type
		);
		Assert.Equal(
			"NUMA Info",
			entry.Name
		);
		Assert.Equal(
			"/proc/42/numa_maps",
			entry.Expand( 42 )
		);
		Assert.Equal(
			"file\tNUMA Info\t/proc/%d/numa_maps",
			entry.ToNativeLine()
		);
	}

	[Fact]
	public void InspectEntriesRoundTripThroughIcodAndNativeConfiguration() {
		TopRuntimeState source = new();
		source.InspectEntries.Add(
			new TopInspectEntry(
				TopInspectEntryType.File,
				"Status",
				"/proc/%d/status"
			)
		);
		source.InspectEntries.Add(
			new TopInspectEntry(
				TopInspectEntryType.Pipe,
				"Open Files",
				"lsof -P -p %d 2>&1"
			)
		);

		string json = TopConfigurationCodec.Serialize(
			source
		);
		TopRuntimeState jsonRestored = new();
		TopConfigurationCodec.Apply(
			json,
			jsonRestored
		);

		Assert.Equal( 2, jsonRestored.InspectEntries.Count );
		Assert.Equal(
			"Status",
			jsonRestored.InspectEntries[ 0 ].Name
		);
		Assert.Equal(
			TopInspectEntryType.Pipe,
			jsonRestored.InspectEntries[ 1 ].Type
		);

		string native = TopProcpsConfigurationCodec.Serialize(
			source
		);
		Assert.Contains(
			"file\tStatus\t/proc/%d/status",
			native,
			StringComparison.Ordinal
		);
		Assert.Contains(
			"pipe\tOpen Files\tlsof -P -p %d 2>&1",
			native,
			StringComparison.Ordinal
		);

		TopRuntimeState nativeRestored = new();
		TopProcpsConfigurationCodec.Apply(
			native,
			nativeRestored
		);
		Assert.Equal( 2, nativeRestored.InspectEntries.Count );
		Assert.Equal(
			"/proc/%d/status",
			nativeRestored.InspectEntries[ 0 ].Format
		);
	}

	[Fact]
	public async Task FileInspectExecutesPidExpansionAndSupportsSearch() {
		string directory = Path.Combine(
			Path.GetTempPath(),
			$"icod-top-inspect-{Guid.NewGuid():N}"
		);
		Directory.CreateDirectory(
			directory
		);
		try {
			string path = Path.Combine(
				directory,
				"status-42.txt"
			);
			await File.WriteAllTextAsync(
				path,
				"alpha\nbeta\ngamma\n",
				Encoding.UTF8
			);
			var entry = new TopInspectEntry(
				TopInspectEntryType.File,
				"Status",
				Path.Combine(
					directory,
					"status-%d.txt"
				)
			);
			var session = new TopInspectSession(
				42,
				[
					entry
				]
			);
			TopTerminalDimensions dimensions = new(
				80,
				10
			);

			Assert.Equal(
				TopInspectInputResult.Changed,
				await session.HandleInputAsync(
					new TopInputEvent(
						TopInputKey.Enter,
						null
					),
					dimensions,
					CancellationToken.None
				)
			);
			Assert.True( session.ViewingOutput );
			Assert.Equal(
				new[] {
					"alpha",
					"beta",
					"gamma"
				},
				session.Lines
			);

			_ = await session.HandleInputAsync(
				Character( '/' ),
				dimensions,
				CancellationToken.None
			);
			foreach ( char value in "gamma" ) {
				_ = await session.HandleInputAsync(
					Character( value ),
					dimensions,
					CancellationToken.None
				);
			}
			_ = await session.HandleInputAsync(
				new TopInputEvent(
					TopInputKey.Enter,
					null
				),
				dimensions,
				CancellationToken.None
			);

			Assert.Equal( 2, session.VerticalOffset );
			Assert.Equal( "gamma", session.SearchText );

			_ = await session.HandleInputAsync(
				Character( 'n' ),
				dimensions,
				CancellationToken.None
			);
			Assert.Equal(
				"Inspect string not found: gamma",
				session.Message
			);
		} finally {
			Directory.Delete(
				directory,
				recursive: true
			);
		}
	}

	[Fact]
	public async Task PipeInspectUsesPlatformShell() {
		string command = ( OperatingSystem.IsWindows() )
			? "echo pipe-output"
			: "printf 'pipe-output\\n'"
		;
		var entry = new TopInspectEntry(
			TopInspectEntryType.Pipe,
			"Pipe",
			command
		);

		IReadOnlyList<string> lines = await TopInspectExecutor.ExecuteAsync(
			entry,
			123,
			CancellationToken.None
		);

		Assert.Contains(
			"pipe-output",
			lines
		);
	}

	[Fact]
	public async Task RendererShowsChooserAndViewerState() {
		string path = Path.GetTempFileName();
		try {
			await File.WriteAllTextAsync(
				path,
				"one\ntwo\n",
				Encoding.UTF8
			);
			var session = new TopInspectSession(
				77,
				[
					new TopInspectEntry(
						TopInspectEntryType.File,
						"Status",
						path
					)
				]
			);
			TopTerminalDimensions dimensions = new(
				80,
				10
			);

			TopRenderFrame chooser = TopInspectRenderer.Render(
				session,
				dimensions,
				boldEnabled: true
			);
			Assert.Contains(
				"Inspection Pause at pid 77",
				chooser.Lines[ 0 ].Text,
				StringComparison.Ordinal
			);
			Assert.Contains(
				"[Status]",
				chooser.Lines[ 2 ].Text,
				StringComparison.Ordinal
			);

			_ = await session.HandleInputAsync(
				new TopInputEvent(
					TopInputKey.Enter,
					null
				),
				dimensions,
				CancellationToken.None
			);
			TopRenderFrame viewer = TopInspectRenderer.Render(
				session,
				dimensions,
				boldEnabled: true
			);
			Assert.Contains(
				"Inspect: Status",
				viewer.Lines[ 0 ].Text,
				StringComparison.Ordinal
			);
			Assert.Equal(
				"one",
				viewer.Lines[ 3 ].Text
			);
		} finally {
			File.Delete(
				path
			);
		}
	}


	[Fact]
	public void TopmostDisplayedPidFollowsSortAndScroll() {
		TopRuntimeState state = new() {
			SortField = TopFieldId.Pid,
			SortHighToLow = false,
			VerticalOffset = 1
		};
		TopSample sample = new(
			new ProcSystemSnapshot(),
			[
				CreateTask( 30 ),
				CreateTask( 10 ),
				CreateTask( 20 )
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
			1,
			DateTimeOffset.UnixEpoch
		);

		Assert.Equal(
			20,
			TopRenderer.GetTopmostProcessId(
				sample,
				state
			)
		);
	}

	[Theory]
	[InlineData( "file\tmissing" )]
	[InlineData( "other\tname\tvalue" )]
	[InlineData( "file\t\tvalue" )]
	public void MalformedNativeInspectEntryIsRejected(
		string text
	) {
		Assert.Throws<FormatException>(
			() => TopInspectEntry.ParseNative( text )
		);
	}


	private static TopTaskRow CreateTask(
		int processId
	) {
		ProcessIdentity identity = new(
			processId,
			new ProcessReuseToken(
				"test",
				$"start-{processId}"
			)
		);
		ProcProcessSnapshot process = new( identity ) {
			CommandName = Exact( $"task-{processId}" ),
			State = Exact( ProcProcessState.Sleeping ),
			NiceValue = Exact( 0 )
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

	private static TopInputEvent Character(
		char value
	) {
		return new TopInputEvent(
			TopInputKey.Character,
			new Rune(
				value
			)
		);
	}
}
