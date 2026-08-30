/*
	Icod.ProcPs.Top.Tests
	Tests for procps-ng top configuration interoperability.
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

using System.Globalization;
using System.Text;
using Xunit;

/// <summary>Exercises procps-ng top configuration compatibility and discovery.</summary>
public sealed class TopProcpsConfigurationTests {
	[Fact]
	public void CurrentProcpsConfigurationMapsSupportedState() {
		string configuration = BuildNativeConfiguration(
			delayWhole: 1,
			delayFraction: 500,
			currentWindow: 2
		);
		TopRuntimeState state = new();

		TopProcpsConfigurationCodec.Apply(
			configuration,
			state
		);

		Assert.Equal(
			TimeSpan.FromSeconds( 1.5 ),
			state.Delay
		);
		Assert.True( state.AlternateDisplayMode );
		Assert.False( state.IrixMode );
		Assert.Equal( 2, state.CurrentWindowIndex );
		Assert.Equal( "3:Mem", state.CurrentWindowLabel );
		Assert.Equal( TopMemoryScale.Gibibytes, state.SummaryScale );
		Assert.Equal( TopMemoryScale.Mebibytes, state.TaskScale );
		Assert.True( state.SuppressZeros );
		Assert.False( state.BoldEnabled );
		Assert.Equal( 7, state.MaximumTasks );
		Assert.Equal( TopFieldId.Memory, state.SortField );
		Assert.True( state.SortHighToLow );
		Assert.True( state.ShowCommandLine );
		Assert.False( state.HideIdle );
		Assert.True( state.HighlightRunning );
		Assert.True( state.HighlightSortColumn );
		Assert.True( state.HighlightBold );
		Assert.False( state.NumericLeftJustified );
		Assert.True( state.CharacterRightJustified );
		Assert.DoesNotContain( TopFieldId.Cpu, state.VisibleFields );
		Assert.Equal( TopFieldId.Pid, state.FieldOrder[ 0 ] );
		Assert.Equal( TopFieldId.User, state.FieldOrder[ 1 ] );

		TopOtherFilter filter = Assert.Single(
			state.OtherFilters
		);
		Assert.Equal( "COMMAND=alpha", filter.RawText );
		Assert.True( filter.CaseSensitive );
	}

	[Theory]
	[InlineData( 'f' )]
	[InlineData( 'g' )]
	[InlineData( 'h' )]
	[InlineData( 'i' )]
	[InlineData( 'j' )]
	public void Procps33CharacterConfigurationMapsSupportedState(
		char version
	) {
		string configuration = BuildLegacyConfiguration(
			version,
			currentWindow: 2
		);
		TopRuntimeState state = new();

		TopProcpsConfigurationCodec.Apply(
			configuration,
			state
		);

		Assert.True( state.AlternateDisplayMode );
		Assert.False( state.IrixMode );
		Assert.Equal( 2, state.CurrentWindowIndex );
		Assert.Equal( "3:Mem", state.CurrentWindowLabel );
		Assert.Equal( TopFieldId.Memory, state.SortField );
		Assert.True( state.SortHighToLow );
		Assert.True( state.ShowCommandLine );
		Assert.Contains( TopFieldId.Pid, state.VisibleFields );
		Assert.Contains( TopFieldId.Memory, state.VisibleFields );
		Assert.DoesNotContain( TopFieldId.Cpu, state.VisibleFields );
		if ( 'f' == version ) {
			Assert.False( state.NumericLeftJustified );
		}
	}

	[Fact]
	public void Procps328ConfigurationConvertsOldFieldsFlagsAndSort() {
		string configuration = BuildProcps328Configuration();
		TopRuntimeState state = new();

		TopProcpsConfigurationCodec.Apply(
			configuration,
			state
		);

		Assert.False( state.BoldEnabled );
		Assert.True( state.TaskDisplayVisible );
		Assert.Equal( TopFieldId.User, state.SortField );
		Assert.True( state.SortHighToLow );
		Assert.True( state.ShowCommandLine );
		Assert.True( state.HighlightSortColumn );
		Assert.False( state.NumericLeftJustified );
		Assert.Contains( TopFieldId.Pid, state.VisibleFields );
		Assert.Contains( TopFieldId.User, state.VisibleFields );
		Assert.Equal( TopFieldId.Pid, state.FieldOrder[ 0 ] );
		Assert.Equal( TopFieldId.User, state.FieldOrder[ 1 ] );
	}

	[Theory]
	[InlineData( 'b' )]
	[InlineData( 'c' )]
	[InlineData( 'd' )]
	[InlineData( 'e' )]
	public void ReservedLegacyConfigurationVersionIsRejected(
		char version
	) {
		string configuration = $"""
top's Config File (Linux processes with windows)
Id:{version}, Mode_altscr=0, Mode_irixps=1, Delay_time=3.0, Curwin=0
""";
		TopRuntimeState state = new();

		FormatException exception = Assert.Throws<FormatException>(
			() => TopProcpsConfigurationCodec.Apply(
				configuration,
				state
			)
		);

		Assert.Contains(
			"not supported",
			exception.Message,
			StringComparison.Ordinal
		);
	}

	[Fact]
	public async Task StoreReadsLegacyHighBitFieldsAsLatin1() {
		string root = CreateTemporaryDirectory();
		try {
			string path = Path.Combine(
				root,
				".toprc"
			);
			await File.WriteAllTextAsync(
				path,
				BuildLegacyConfiguration( 'j' ),
				Encoding.Latin1
			);

			var environment = new Dictionary<string, string?> {
				[ "HOME" ] = root
			};
			var store = new SystemTopConfigurationStore(
				name => environment.GetValueOrDefault( name ),
				systemRestrictionsPath: null,
				privilegedUserProvider: () => false,
				systemDefaultsPath: null,
				nativeConfigurationEnabled: true
			);
			TopRuntimeState state = new();

			await store.LoadAsync(
				state,
				loadPersonalConfiguration: true,
				CancellationToken.None
			);

			Assert.Contains( TopFieldId.Pid, state.VisibleFields );
			Assert.Contains( TopFieldId.User, state.VisibleFields );
			Assert.DoesNotContain( TopFieldId.Cpu, state.VisibleFields );
		} finally {
			Directory.Delete(
				root,
				recursive: true
			);
		}
	}

	[Fact]
	public async Task StoreUsesIcodThenLegacyNativeThenXdgThenSystemDefaults() {
		string root = CreateTemporaryDirectory();
		try {
			string xdg = Path.Combine(
				root,
				"xdg"
			);
			string icodPath = Path.Combine(
				xdg,
				"procps",
				"icod-toprc.json"
			);
			string nativeLegacyPath = Path.Combine(
				root,
				".toprc"
			);
			string nativeXdgPath = Path.Combine(
				xdg,
				"procps",
				"toprc"
			);
			string systemDefaultsPath = Path.Combine(
				root,
				"topdefaultrc"
			);
			Directory.CreateDirectory(
				Path.GetDirectoryName( icodPath )!
			);

			TopRuntimeState icod = new() {
				Delay = TimeSpan.FromSeconds( 0.75 )
			};
			await File.WriteAllTextAsync(
				icodPath,
				TopConfigurationCodec.Serialize( icod )
			);
			await File.WriteAllTextAsync(
				nativeLegacyPath,
				BuildNativeConfiguration(
					delayWhole: 1,
					delayFraction: 500
				)
			);
			await File.WriteAllTextAsync(
				nativeXdgPath,
				BuildNativeConfiguration(
					delayWhole: 2,
					delayFraction: 500
				)
			);
			await File.WriteAllTextAsync(
				systemDefaultsPath,
				BuildNativeConfiguration(
					delayWhole: 3,
					delayFraction: 500
				)
			);

			var environment = new Dictionary<string, string?> {
				[ "HOME" ] = root,
				[ "XDG_CONFIG_HOME" ] = xdg
			};
			var store = new SystemTopConfigurationStore(
				name => environment.GetValueOrDefault( name ),
				systemRestrictionsPath: null,
				privilegedUserProvider: () => false,
				systemDefaultsPath: systemDefaultsPath,
				nativeConfigurationEnabled: true
			);

			TopRuntimeState state = new();
			await store.LoadAsync(
				state,
				loadPersonalConfiguration: true,
				CancellationToken.None
			);
			Assert.Equal(
				TimeSpan.FromSeconds( 0.75 ),
				state.Delay
			);

			File.Delete(
				icodPath
			);
			state = new TopRuntimeState();
			await store.LoadAsync(
				state,
				loadPersonalConfiguration: true,
				CancellationToken.None
			);
			Assert.Equal(
				TimeSpan.FromSeconds( 1.5 ),
				state.Delay
			);

			File.Delete(
				nativeLegacyPath
			);
			state = new TopRuntimeState();
			await store.LoadAsync(
				state,
				loadPersonalConfiguration: true,
				CancellationToken.None
			);
			Assert.Equal(
				TimeSpan.FromSeconds( 2.5 ),
				state.Delay
			);

			File.Delete(
				nativeXdgPath
			);
			state = new TopRuntimeState();
			await store.LoadAsync(
				state,
				loadPersonalConfiguration: true,
				CancellationToken.None
			);
			Assert.Equal(
				TimeSpan.FromSeconds( 3.5 ),
				state.Delay
			);

			state = new TopRuntimeState();
			await store.LoadAsync(
				state,
				loadPersonalConfiguration: false,
				CancellationToken.None
			);
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

	private static string BuildLegacyConfiguration(
		char version,
		int currentWindow = 0
	) {
		int legacyFieldCount = version switch {
			'f' or 'g' => 55,
			'h' or 'i' => 70,
			'j' => 86,
			_ => throw new ArgumentOutOfRangeException(
				nameof( version )
			)
		};
		const int baseFlags = 0x000004
			| 0x000010
			| 0x000020
			| 0x000080
			| 0x000100
			| 0x000200
			| 0x000400
			| 0x040000;
		var builder = new StringBuilder();
		builder.AppendLine(
			"top's Config File (Linux processes with windows)"
		);
		builder.Append(
			"Id:"
		);
		builder.Append(
			version
		);
		builder.AppendLine(
			$", Mode_altscr=1, Mode_irixps=0, Delay_time=1.500, Curwin={currentWindow}"
		);

		string[] names = [
			"Def",
			"Job",
			"Mem",
			"Usr"
		];
		int[] sorts = [
			18,
			0,
			21,
			3
		];
		for ( int index = 0; index < TopRuntimeState.WindowCount; index++ ) {
			builder.Append(
				names[ index ]
			);
			builder.Append(
				"\tfieldscur="
			);
			for ( int nativeField = 0; nativeField < legacyFieldCount; nativeField++ ) {
				int encoded = nativeField + 37;
				if ( 18 != nativeField ) {
					encoded |= 0x80;
				}
				builder.Append(
					(char)encoded
				);
			}
			builder.AppendLine();

			int flags = baseFlags;
			if ( 2 == index ) {
				flags |= 0x000008;
			}
			builder.Append(
				"\twinflags="
			);
			builder.Append(
				flags.ToString(
					CultureInfo.InvariantCulture
				)
			);
			builder.Append(
				", sortindx="
			);
			builder.Append(
				sorts[ index ].ToString(
					CultureInfo.InvariantCulture
				)
			);
			builder.AppendLine(
				", maxtasks=0, graph_cpus=0, graph_mems=0, double_up=0, combine_cpus=0"
			);
			builder.AppendLine(
				"\tsummclr=1, msgsclr=1, headclr=1, taskclr=1"
			);
		}

		builder.AppendLine(
			"Fixed_widest=0, Summ_mscale=2, Task_mscale=1, Zero_suppress=1, Tics_scaled=0"
		);
		return builder.ToString();
	}

	private static string BuildProcps328Configuration() {
		const int oldFlags = 0x000001
			| 0x000008
			| 0x000010
			| 0x000080
			| 0x000200
			| 0x010000;
		const string oldFields = "AbcdEfghijklmnopqrstuvwxyz";
		var builder = new StringBuilder();
		builder.AppendLine(
			"top's Config File (Linux processes with windows)"
		);
		builder.AppendLine(
			"Id:a, Mode_altscr=1, Mode_irixps=0, Delay_time=3.000, Curwin=0"
		);

		string[] names = [
			"Def",
			"Job",
			"Mem",
			"Usr"
		];
		foreach ( string name in names ) {
			builder.Append(
				name
			);
			builder.Append(
				"\tfieldscur="
			);
			builder.AppendLine(
				oldFields
			);
			builder.Append(
				"\twinflags="
			);
			builder.Append(
				oldFlags.ToString(
					CultureInfo.InvariantCulture
				)
			);
			builder.AppendLine(
				", sortindx=4, maxtasks=0"
			);
			builder.AppendLine(
				"\tsummclr=1, msgsclr=1, headclr=1, taskclr=1"
			);
		}
		return builder.ToString();
	}

	private static string BuildNativeConfiguration(
		int delayWhole,
		int delayFraction,
		int currentWindow = 0
	) {
		const int baseFlags = 0x000004
			| 0x000010
			| 0x000020
			| 0x000080
			| 0x000100
			| 0x000200
			| 0x000400
			| 0x020000
			| 0x040000;
		var builder = new StringBuilder();
		builder.AppendLine(
			"top's Config File (Linux processes with windows)"
		);
		builder.Append(
			"Id:n, Mode_altscr=1, Mode_irixps=0, Delay_time="
		);
		builder.Append(
			delayWhole.ToString(
				CultureInfo.InvariantCulture
			)
		);
		builder.Append(
			'.'
		);
		builder.Append(
			delayFraction.ToString(
				CultureInfo.InvariantCulture
			)
		);
		builder.Append(
			", Curwin="
		);
		builder.AppendLine(
			currentWindow.ToString(
				CultureInfo.InvariantCulture
			)
		);

		string[] names = [
			"Def",
			"Job",
			"Mem",
			"Usr"
		];
		int[] sorts = [
			18,
			0,
			21,
			3
		];
		for ( int index = 0; index < TopRuntimeState.WindowCount; index++ ) {
			builder.Append(
				names[ index ]
			);
			builder.Append(
				"\tfieldscur="
			);
			foreach ( int nativeField in NativeFieldOrder() ) {
				bool visible = 18 != nativeField;
				builder.Append(
					' '
				);
				builder.Append(
					EncodeField(
						nativeField,
						visible
					).ToString(
						CultureInfo.InvariantCulture
					)
				);
			}
			builder.AppendLine();

			int flags = baseFlags;
			if ( 2 == index ) {
				flags |= 0x000008;
			}
			builder.Append(
				"\twinflags="
			);
			builder.Append(
				flags.ToString(
					CultureInfo.InvariantCulture
				)
			);
			builder.Append(
				", sortindx="
			);
			builder.Append(
				sorts[ index ].ToString(
					CultureInfo.InvariantCulture
				)
			);
			builder.Append(
				", maxtasks="
			);
			if ( 2 == index ) {
				builder.Append(
					"7"
				);
			} else {
				builder.Append(
					"0"
				);
			}
			builder.AppendLine(
				", graph_cpus=0, graph_mems=0, double_up=0, combine_cpus=0"
			);
			builder.AppendLine(
				"\tsummclr=1, msgsclr=1, headclr=1, taskclr=1, task_xy=1, core_types=0, cores_vs_cpus=0"
			);
		}

		builder.AppendLine(
			"Fixed_widest=0, Summ_mscale=2, Task_mscale=1, Zero_suppress=1, Tics_scaled=0"
		);
		builder.AppendLine();
		builder.AppendLine(
			"begin: saved other filter data -------------------"
		);
		builder.AppendLine(
			"window #2, osel_tot=1"
		);
		builder.AppendLine(
			"\ttype=79,\tfilter=COMMAND=alpha"
		);
		builder.AppendLine(
			"end  : saved other filter data -------------------"
		);
		return builder.ToString();
	}

	private static IEnumerable<int> NativeFieldOrder() {
		int[] preferred = [
			0,
			3,
			14,
			15,
			22,
			24,
			27,
			31,
			18,
			21,
			20,
			32
		];
		var yielded = new HashSet<int>();
		foreach ( int value in preferred ) {
			yielded.Add(
				value
			);
			yield return value;
		}
		for ( int value = 0; value <= 99; value++ ) {
			if ( yielded.Add( value ) ) {
				yield return value;
			}
		}
	}

	private static int EncodeField(
		int nativeField,
		bool visible
	) {
		ArgumentOutOfRangeException.ThrowIfNegative( nativeField );

		int visibility = ( visible )
			? 1
			: 0
		;
		return (
			( nativeField + 37 )
			<< 1
		) | visibility;
	}

	private static string CreateTemporaryDirectory() {
		string path = Path.Combine(
			Path.GetTempPath(),
			$"icod-procps-native-top-config-{Guid.NewGuid():N}"
		);
		Directory.CreateDirectory(
			path
		);
		return path;
	}
}
