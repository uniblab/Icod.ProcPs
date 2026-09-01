/*
	Icod.ProcPs.Top.Tests
	Tests for procps-compatible top bottom windows.
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

/// <summary>Exercises bottom-window commands, observations, message recall, and rendering.</summary>
public sealed class TopBottomWindowTests {
	[Fact]
	public void ControlCommandsToggleAndTabMovesSelection() {
		TopRuntimeState state = new();

		Assert.True(
			TopBottomWindowCommands.TryHandle(
				Control( 'K' ),
				state,
				out TopCommandAction openAction
			)
		);
		Assert.Equal( TopCommandAction.Rerender, openAction );
		Assert.NotNull( state.BottomWindow );
		Assert.Equal(
			TopBottomWindowKind.CommandLine,
			state.BottomWindow!.Kind
		);
		state.BottomWindow.ReplaceContent(
			null,
			CreateSample(),
			"Command Line",
			[ "one", "two", "three" ]
		);

		Assert.True(
			TopBottomWindowCommands.TryHandle(
				new TopInputEvent(
					TopInputKey.Tab,
					null
				),
				state,
				out TopCommandAction tabAction
			)
		);
		Assert.Equal( TopCommandAction.Rerender, tabAction );
		Assert.Equal( 1, state.BottomWindow.SelectedIndex );
		Assert.True(
			TopBottomWindowCommands.TryHandle(
				new TopInputEvent(
					TopInputKey.Tab,
					null,
					TopInputModifiers.Shift
				),
				state,
				out _
			)
		);
		Assert.Equal( 0, state.BottomWindow.SelectedIndex );

		Assert.True(
			TopBottomWindowCommands.TryHandle(
				Control( 'K' ),
				state,
				out TopCommandAction closeAction
			)
		);
		Assert.Equal( TopCommandAction.Rerender, closeAction );
		Assert.Null( state.BottomWindow );
	}

	[Fact]
	public async Task CommandLineWindowFollowsTheFirstDisplayedTask() {
		TopRuntimeState state = new() {
			SortField = TopFieldId.Pid,
			SortHighToLow = false,
			VerticalOffset = 1,
			BottomWindow = new TopBottomWindowState(
				TopBottomWindowKind.CommandLine
			)
		};
		TopSample sample = CreateSample(
			CreateTask( 10, "first" ),
			CreateTask( 20, "second" )
		);

		await TopBottomWindowController.RefreshAsync(
			sample,
			state,
			ThrowingSupplementProvider.Instance,
			TestAccountResolver.Instance,
			CancellationToken.None
		);

		Assert.Contains(
			"pid 20",
			state.BottomWindow!.Title,
			StringComparison.Ordinal
		);
		Assert.Contains(
			"second",
			state.BottomWindow.Elements[ 0 ],
			StringComparison.Ordinal
		);

		state.VerticalOffset = 0;
		await TopBottomWindowController.RefreshAsync(
			sample,
			state,
			ThrowingSupplementProvider.Instance,
			TestAccountResolver.Instance,
			CancellationToken.None
		);
		Assert.Contains(
			"pid 10",
			state.BottomWindow.Title,
			StringComparison.Ordinal
		);
	}

	[Fact]
	public async Task SupplementBackedWindowsUseObservedProcessFacts() {
		TopRuntimeState state = new() {
			SortField = TopFieldId.Pid,
			SortHighToLow = false,
			BottomWindow = new TopBottomWindowState(
				TopBottomWindowKind.Capabilities
			)
		};
		TopSample sample = CreateSample(
			CreateTask( 42, "worker" )
		);
		var provider = new FixedSupplementProvider(
			new ProcMatchSupplement {
				ThreadGroupId = 42,
				Environment = Exact<IReadOnlyList<string>>(
					[ "A=B", "C=D" ]
				),
				LinuxStatusFields = Exact<IReadOnlyDictionary<string, string>>(
					new Dictionary<string, string>( StringComparer.Ordinal ) {
						[ "CapEff" ] = "0000000000000001",
						[ "Groups" ] = "27 1000"
					}
				)
			}
		);

		await TopBottomWindowController.RefreshAsync(
			sample,
			state,
			provider,
			TestAccountResolver.Instance,
			CancellationToken.None
		);
		Assert.Contains(
			state.BottomWindow!.Elements,
			line => line.Contains( "CHOWN", StringComparison.Ordinal )
		);

		state.BottomWindow = new TopBottomWindowState(
			TopBottomWindowKind.Environment
		);
		await TopBottomWindowController.RefreshAsync(
			sample,
			state,
			provider,
			TestAccountResolver.Instance,
			CancellationToken.None
		);
		Assert.Equal(
			new[] { "A=B", "C=D" },
			state.BottomWindow.Elements
		);

		state.BottomWindow = new TopBottomWindowState(
			TopBottomWindowKind.SupplementaryGroups
		);
		await TopBottomWindowController.RefreshAsync(
			sample,
			state,
			provider,
			TestAccountResolver.Instance,
			CancellationToken.None
		);
		Assert.Contains(
			"27 (sudo)",
			state.BottomWindow.Elements
		);
		Assert.Contains(
			"1000",
			state.BottomWindow.Elements
		);
	}

	[Fact]
	public async Task LoggedMessagesKeepTenAndBottomRenderingSanitizesControls() {
		TopRuntimeState state = new() {
			BottomWindow = new TopBottomWindowState(
				TopBottomWindowKind.LoggedMessages
			)
		};
		for ( int index = 0; index < 12; index++ ) {
			state.Message = $"message-{index}";
		}
		TopSample sample = CreateSample();

		await TopBottomWindowController.RefreshAsync(
			sample,
			state,
			ThrowingSupplementProvider.Instance,
			TestAccountResolver.Instance,
			CancellationToken.None
		);
		Assert.Equal( 10, state.MessageHistory.Count );
		Assert.Equal( "message-2", state.MessageHistory[ 0 ] );
		Assert.Equal( "message-11", state.MessageHistory[ 9 ] );

		state.BottomWindow.ReplaceContent(
			null,
			sample,
			"Command Line",
			[ "safe\u001b[31mtext" ]
		);
		TopRenderFrame source = new(
			Enumerable.Range( 0, 12 )
				.Select( index => new TopRenderLine( $"row-{index}" ) )
				.ToArray(),
			80,
			12,
			true
		);

		TopRenderFrame rendered = TopBottomWindowRenderer.Apply(
			source,
			state.BottomWindow
		);
		Assert.DoesNotContain(
			'\u001b',
			string.Join( '\n', rendered.Lines.Select( line => line.Text ) )
		);
		Assert.Contains(
			"safe [31mtext",
			string.Join( '\n', rendered.Lines.Select( line => line.Text ) ),
			StringComparison.Ordinal
		);
	}

	private static TopInputEvent Control(
		char value
	) {
		return new TopInputEvent(
			TopInputKey.Character,
			new Rune( value ),
			TopInputModifiers.Control
		);
	}

	private static TopSample CreateSample(
		params TopTaskRow[] tasks
	) {
		return new TopSample(
			new ProcSystemSnapshot(),
			tasks,
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
		ProcessIdentity identity = new(
			processId,
			new ProcessReuseToken(
				"test",
				$"start-{processId}"
			)
		);
		ProcProcessSnapshot process = new( identity ) {
			CommandName = Exact( command ),
			CommandLineArguments = Exact<IReadOnlyList<string>>(
				[ command, "--value" ]
			),
			State = Exact( ProcProcessState.Sleeping ),
			ParentProcessId = Exact( 1 ),
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

	private sealed class FixedSupplementProvider : IProcMatchSupplementProvider {
		private readonly ProcMatchSupplement supplement;

		internal FixedSupplementProvider(
			ProcMatchSupplement supplement
		) {
			ArgumentNullException.ThrowIfNull( supplement );
			this.supplement = supplement;
		}

		public Task<IReadOnlyList<ProcMatchCandidate>> GetCandidatesAsync(
			IReadOnlyList<ProcProcessSnapshot> processes,
			bool includeLightweightTasks,
			CancellationToken cancellationToken = default
		) {
			ArgumentNullException.ThrowIfNull( processes );
			cancellationToken.ThrowIfCancellationRequested();
			Assert.False( includeLightweightTasks );
			Assert.Single( processes );
			IReadOnlyList<ProcMatchCandidate> result = [
				new ProcMatchCandidate(
					processes[ 0 ],
					this.supplement
				)
			];
			return Task.FromResult( result );
		}
	}

	private sealed class ThrowingSupplementProvider : IProcMatchSupplementProvider {
		internal static ThrowingSupplementProvider Instance { get; } = new();

		private ThrowingSupplementProvider() {
		}

		public Task<IReadOnlyList<ProcMatchCandidate>> GetCandidatesAsync(
			IReadOnlyList<ProcProcessSnapshot> processes,
			bool includeLightweightTasks,
			CancellationToken cancellationToken = default
		) {
			throw new InvalidOperationException(
				"The supplement provider should not be used by this bottom window."
			);
		}
	}

	private sealed class TestAccountResolver : IProcAccountDisplayResolver {
		internal static TestAccountResolver Instance { get; } = new();

		private TestAccountResolver() {
		}

		public bool TryResolveUser( string text, out uint userId ) {
			ArgumentNullException.ThrowIfNull( text );
			userId = 0;
			return false;
		}

		public bool TryResolveGroup( string text, out uint groupId ) {
			ArgumentNullException.ThrowIfNull( text );
			groupId = 0;
			return false;
		}

		public bool TryGetUserName( uint id, out string name ) {
			name = string.Empty;
			return false;
		}

		public bool TryGetGroupName( uint id, out string name ) {
			if ( 27 == id ) {
				name = "sudo";
				return true;
			}
			name = string.Empty;
			return false;
		}
	}
}
