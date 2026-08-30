/*
	Icod.ProcPs.Top.Tests
	Tests for top personal configuration persistence.
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

/// <summary>Exercises the Icod and procps-compatible top configuration contracts.</summary>
public sealed class TopConfigurationTests {
	[Fact]
	public void RoundTripsSupportedPersistentStateOnly() {
		TopRuntimeState source = new() {
			Delay = TimeSpan.FromSeconds( 1.25 ),
			SortField = TopFieldId.Pid,
			SortHighToLow = false,
			BoldEnabled = false,
			HighlightBold = false,
			HighlightRunning = false,
			HighlightSortColumn = true,
			NumericLeftJustified = true,
			CharacterRightJustified = true,
			SuppressZeros = true,
			MaximumTasks = 4,
			SummaryScale = TopMemoryScale.Gibibytes,
			TaskScale = TopMemoryScale.Mebibytes,
			ShowCommandLine = true,
			ShowThreads = true,
			HideIdle = true,
			Forest = true,
			IrixMode = false,
			SingleCpuSummary = false,
			SearchText = "transient",
			VerticalOffset = 8,
			HorizontalOffset = 9,
			SecureMode = true
		};
		source.ProcessIds.Add( 101 );
		source.FieldOrder.Remove( TopFieldId.Pid );
		source.FieldOrder.Insert( 1, TopFieldId.Pid );
		source.VisibleFields.Remove( TopFieldId.Cpu );
		Assert.True(
			TopOtherFilterParser.TryParse(
				"COMMAND=alpha",
				caseSensitive: true,
				source,
				out TopOtherFilter? filter,
				out string? error
			),
			error
		);
		source.OtherFilters.Add( filter! );

		string serialized = TopConfigurationCodec.Serialize( source );
		TopRuntimeState restored = new();
		TopConfigurationCodec.Apply(
			serialized,
			restored
		);

		Assert.Equal( source.Delay, restored.Delay );
		Assert.Equal( source.SortField, restored.SortField );
		Assert.Equal( source.SortHighToLow, restored.SortHighToLow );
		Assert.Equal( source.BoldEnabled, restored.BoldEnabled );
		Assert.Equal( source.HighlightBold, restored.HighlightBold );
		Assert.Equal( source.HighlightRunning, restored.HighlightRunning );
		Assert.Equal( source.HighlightSortColumn, restored.HighlightSortColumn );
		Assert.Equal( source.NumericLeftJustified, restored.NumericLeftJustified );
		Assert.Equal( source.CharacterRightJustified, restored.CharacterRightJustified );
		Assert.Equal( source.SuppressZeros, restored.SuppressZeros );
		Assert.Equal( source.MaximumTasks, restored.MaximumTasks );
		Assert.Equal( source.SummaryScale, restored.SummaryScale );
		Assert.Equal( source.TaskScale, restored.TaskScale );
		Assert.Equal( source.ShowCommandLine, restored.ShowCommandLine );
		Assert.Equal( source.ShowThreads, restored.ShowThreads );
		Assert.Equal( source.HideIdle, restored.HideIdle );
		Assert.Equal( source.Forest, restored.Forest );
		Assert.Equal( source.IrixMode, restored.IrixMode );
		Assert.Equal( source.SingleCpuSummary, restored.SingleCpuSummary );
		Assert.Equal( source.FieldOrder.ToArray(), restored.FieldOrder.ToArray() );
		Assert.True( source.VisibleFields.SetEquals( restored.VisibleFields ) );
		TopOtherFilter restoredFilter = Assert.Single( restored.OtherFilters );
		Assert.Equal( "COMMAND=alpha", restoredFilter.RawText );
		Assert.True( restoredFilter.CaseSensitive );

		Assert.Empty( restored.ProcessIds );
		Assert.Null( restored.UserFilter );
		Assert.Null( restored.SearchText );
		Assert.Equal( 0, restored.VerticalOffset );
		Assert.Equal( 0, restored.HorizontalOffset );
		Assert.False( restored.SecureMode );
	}

	[Fact]
	public void ResolvesIcodAndNativePersonalPaths() {
		string root = Path.GetFullPath(
			Path.Combine(
				Path.GetTempPath(),
				"icod-procps-top-config"
			)
		);
		var environment = new Dictionary<string, string?> {
			[ "XDG_CONFIG_HOME" ] = root,
			[ "HOME" ] = root
		};

		TopConfigurationPaths paths = TopConfigurationPaths.Resolve(
			name => environment.GetValueOrDefault( name )
		);

		Assert.Equal(
			Path.Combine( root, "procps", "icod-toprc.json" ),
			paths.PersonalPath
		);
		Assert.Equal(
			Path.Combine( root, ".icod-toprc.json" ),
			paths.LegacyPath
		);
		Assert.Equal(
			Path.Combine( root, "procps", "toprc" ),
			paths.NativePersonalPath
		);
		Assert.Equal(
			Path.Combine( root, ".toprc" ),
			paths.NativeLegacyPath
		);
	}

	[Fact]
	public async Task SystemRestrictionsApplySecureDelayForOrdinaryUser() {
		string root = CreateTemporaryDirectory();
		try {
			string restrictionsPath = Path.Combine(
				root,
				"toprc"
			);
			await File.WriteAllTextAsync(
				restrictionsPath,
				"s\n5.0 # enforced delay\n"
			);
			var store = new SystemTopConfigurationStore(
				_ => null,
				restrictionsPath,
				() => false
			);
			TopRuntimeState state = new() {
				Delay = TimeSpan.FromSeconds( 3 )
			};

			await store.LoadAsync(
				state,
				loadPersonalConfiguration: false,
				CancellationToken.None
			);

			Assert.True( state.SecureMode );
			Assert.Equal(
				TimeSpan.FromSeconds( 5 ),
				state.Delay
			);
		} finally {
			Directory.Delete(
				root,
				recursive: true
			);
		}
	}

	[Fact]
	public async Task SystemRestrictionsDoNotConstrainPrivilegedUser() {
		string root = CreateTemporaryDirectory();
		try {
			string restrictionsPath = Path.Combine(
				root,
				"toprc"
			);
			await File.WriteAllTextAsync(
				restrictionsPath,
				"s\n5.0\n"
			);
			var store = new SystemTopConfigurationStore(
				_ => null,
				restrictionsPath,
				() => true
			);
			TopRuntimeState state = new() {
				Delay = TimeSpan.FromSeconds( 2 )
			};

			await store.LoadAsync(
				state,
				loadPersonalConfiguration: false,
				CancellationToken.None
			);

			Assert.False( state.SecureMode );
			Assert.Equal(
				TimeSpan.FromSeconds( 2 ),
				state.Delay
			);
		} finally {
			Directory.Delete(
				root,
				recursive: true
			);
		}
	}

	[Fact]
	public async Task InvalidSystemRestrictionDelayRetainsBuiltInDelay() {
		string root = CreateTemporaryDirectory();
		try {
			string restrictionsPath = Path.Combine(
				root,
				"toprc"
			);
			await File.WriteAllTextAsync(
				restrictionsPath,
				"s\nnot-a-delay\n"
			);
			var store = new SystemTopConfigurationStore(
				_ => null,
				restrictionsPath,
				() => false
			);
			TopRuntimeState state = new() {
				Delay = TimeSpan.FromSeconds( 3 )
			};

			await store.LoadAsync(
				state,
				loadPersonalConfiguration: false,
				CancellationToken.None
			);

			Assert.True( state.SecureMode );
			Assert.Equal(
				TimeSpan.FromSeconds( 3 ),
				state.Delay
			);
		} finally {
			Directory.Delete(
				root,
				recursive: true
			);
		}
	}

	private static string CreateTemporaryDirectory() {
		string path = Path.Combine(
			Path.GetTempPath(),
			$"icod-procps-top-config-{Guid.NewGuid():N}"
		);
		Directory.CreateDirectory( path );
		return path;
	}
}
