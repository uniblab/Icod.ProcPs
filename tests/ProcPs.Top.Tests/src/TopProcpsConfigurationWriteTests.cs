/*
	Icod.ProcPs.Top.Tests
	Tests for procps-ng top native configuration writing.
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
using Xunit;

/// <summary>Exercises guarded procps current-format serialization and mirroring.</summary>
public sealed class TopProcpsConfigurationWriteTests {
	[Fact]
	public void CurrentNativeSerializationRoundTripsSupportedState() {
		TopRuntimeState source = new() {
			Delay = TimeSpan.FromMilliseconds( 1235 ),
			BoldEnabled = false,
			SuppressZeros = true,
			SummaryScale = TopMemoryScale.Exbibytes,
			TaskScale = TopMemoryScale.Exbibytes,
			IrixMode = false,
			AlternateDisplayMode = true,
			SortField = TopFieldId.Memory,
			SortHighToLow = false,
			HighlightBold = false,
			HighlightRunning = false,
			HighlightSortColumn = true,
			NumericLeftJustified = true,
			CharacterRightJustified = true,
			MaximumTasks = 9,
			ShowCommandLine = true,
			HideIdle = true,
			Forest = true,
			SingleCpuSummary = false
		};
		source.RenameCurrentWindow(
			"Å"
		);
		source.FieldOrder.Remove(
			TopFieldId.User
		);
		source.FieldOrder.Insert(
			0,
			TopFieldId.User
		);
		source.VisibleFields.Remove(
			TopFieldId.Cpu
		);
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
		source.OtherFilters.Add(
			filter!
		);

		string serialized = TopProcpsConfigurationCodec.Serialize(
			source
		);
		Assert.StartsWith(
			TopProcpsConfigurationCodec.IcodOwnershipHeader + "\n",
			serialized,
			StringComparison.Ordinal
		);

		string[] lines = serialized.Split(
			'\n'
		);
		const string fieldsMarker = "fieldscur=";
		int markerIndex = lines[
			2
		].IndexOf(
			fieldsMarker,
			StringComparison.Ordinal
		);
		Assert.True( 0 <= markerIndex );
		string[] fields = lines[
			2
		][
			( markerIndex + fieldsMarker.Length )..
		].Split(
			' ',
			StringSplitOptions.RemoveEmptyEntries
				| StringSplitOptions.TrimEntries
		);
		Assert.Equal(
			81,
			fields.Length
		);

		byte[] bytes = Encoding.UTF8.GetBytes(
			serialized
		);
		string decoded = TopProcpsConfigurationCodec.Decode(
			bytes
		);
		TopRuntimeState restored = new();
		TopProcpsConfigurationCodec.Apply(
			decoded,
			restored
		);

		Assert.Equal(
			TimeSpan.FromMilliseconds( 1235 ),
			restored.Delay
		);
		Assert.False( restored.BoldEnabled );
		Assert.True( restored.SuppressZeros );
		Assert.Equal( TopMemoryScale.Exbibytes, restored.SummaryScale );
		Assert.Equal( TopMemoryScale.Pebibytes, restored.TaskScale );
		Assert.False( restored.IrixMode );
		Assert.True( restored.AlternateDisplayMode );
		Assert.Equal( "1:Å", restored.CurrentWindowLabel );
		Assert.Equal( TopFieldId.Memory, restored.SortField );
		Assert.False( restored.SortHighToLow );
		Assert.False( restored.HighlightBold );
		Assert.False( restored.HighlightRunning );
		Assert.True( restored.HighlightSortColumn );
		Assert.True( restored.NumericLeftJustified );
		Assert.True( restored.CharacterRightJustified );
		Assert.Equal( 9, restored.MaximumTasks );
		Assert.True( restored.ShowCommandLine );
		Assert.True( restored.HideIdle );
		Assert.True( restored.Forest );
		Assert.False( restored.SingleCpuSummary );
		Assert.Equal( TopFieldId.User, restored.FieldOrder[ 0 ] );
		Assert.DoesNotContain( TopFieldId.Cpu, restored.VisibleFields );
		TopOtherFilter restoredFilter = Assert.Single(
			restored.OtherFilters
		);
		Assert.Equal( "COMMAND=alpha", restoredFilter.RawText );
		Assert.True( restoredFilter.CaseSensitive );
	}

	[Fact]
	public async Task StoreCreatesAndRefreshesIcodOwnedNativeMirror() {
		string root = CreateTemporaryDirectory();
		try {
			string xdg = Path.Combine(
				root,
				"xdg"
			);
			var environment = new Dictionary<string, string?> {
				[ "HOME" ] = root,
				[ "XDG_CONFIG_HOME" ] = xdg
			};
			var store = new SystemTopConfigurationStore(
				name => environment.GetValueOrDefault( name ),
				systemRestrictionsPath: null,
				privilegedUserProvider: () => false,
				systemDefaultsPath: null,
				nativeConfigurationEnabled: true
			);
			TopRuntimeState state = new() {
				Delay = TimeSpan.FromSeconds( 1.25 )
			};

			string jsonPath = await store.SaveAsync(
				state,
				CancellationToken.None
			);
			string nativePath = Path.Combine(
				xdg,
				"procps",
				"toprc"
			);
			Assert.True( File.Exists( jsonPath ) );
			Assert.True( File.Exists( nativePath ) );
			Assert.Equal(
				TopProcpsConfigurationCodec.IcodOwnershipHeader,
				(
					await File.ReadAllLinesAsync(
						nativePath
					)
				)[ 0 ]
			);

			state.Delay = TimeSpan.FromSeconds( 2.5 );
			await store.SaveAsync(
				state,
				CancellationToken.None
			);
			byte[] bytes = await File.ReadAllBytesAsync(
				nativePath
			);
			TopRuntimeState restored = new();
			TopProcpsConfigurationCodec.Apply(
				TopProcpsConfigurationCodec.Decode(
					bytes
				),
				restored
			);
			Assert.Equal(
				TimeSpan.FromSeconds( 2.5 ),
				restored.Delay
			);
		} finally {
			Directory.Delete(
				root,
				recursive: true
			);
		}
	}

	[Fact]
	public async Task StoreDoesNotOverwriteForeignLegacyNativeConfiguration() {
		string root = CreateTemporaryDirectory();
		try {
			string xdg = Path.Combine(
				root,
				"xdg"
			);
			string legacyPath = Path.Combine(
				root,
				".toprc"
			);
			const string foreign = """
top's Config File (Linux processes with windows)
foreign procps configuration
""";
			await File.WriteAllTextAsync(
				legacyPath,
				foreign
			);
			var environment = new Dictionary<string, string?> {
				[ "HOME" ] = root,
				[ "XDG_CONFIG_HOME" ] = xdg
			};
			var store = new SystemTopConfigurationStore(
				name => environment.GetValueOrDefault( name ),
				systemRestrictionsPath: null,
				privilegedUserProvider: () => false,
				systemDefaultsPath: null,
				nativeConfigurationEnabled: true
			);

			await store.SaveAsync(
				new TopRuntimeState(),
				CancellationToken.None
			);

			Assert.Equal(
				foreign,
				await File.ReadAllTextAsync(
					legacyPath
				)
			);
			Assert.False(
				File.Exists(
					Path.Combine(
						xdg,
						"procps",
						"toprc"
					)
				)
			);
		} finally {
			Directory.Delete(
				root,
				recursive: true
			);
		}
	}

	[Fact]
	public async Task StoreDoesNotOverwriteForeignXdgNativeConfiguration() {
		string root = CreateTemporaryDirectory();
		try {
			string xdg = Path.Combine(
				root,
				"xdg"
			);
			string nativePath = Path.Combine(
				xdg,
				"procps",
				"toprc"
			);
			Directory.CreateDirectory(
				Path.GetDirectoryName( nativePath )!
			);
			const string foreign = """
top's Config File (Linux processes with windows)
foreign procps configuration
""";
			await File.WriteAllTextAsync(
				nativePath,
				foreign
			);
			var environment = new Dictionary<string, string?> {
				[ "HOME" ] = root,
				[ "XDG_CONFIG_HOME" ] = xdg
			};
			var store = new SystemTopConfigurationStore(
				name => environment.GetValueOrDefault( name ),
				systemRestrictionsPath: null,
				privilegedUserProvider: () => false,
				systemDefaultsPath: null,
				nativeConfigurationEnabled: true
			);

			await store.SaveAsync(
				new TopRuntimeState(),
				CancellationToken.None
			);

			Assert.Equal(
				foreign,
				await File.ReadAllTextAsync(
					nativePath
				)
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
			$"icod-procps-native-top-write-{Guid.NewGuid():N}"
		);
		Directory.CreateDirectory(
			path
		);
		return path;
	}
}
