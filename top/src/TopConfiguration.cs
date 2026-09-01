/*
	top
	Interactively display processes and system activity.
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

namespace Icod.ProcPs.Top;

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Resolved Icod and native procps configuration paths for the current environment.</summary>
internal readonly record struct TopConfigurationPaths(
	string? PersonalPath,
	string? LegacyPath,
	string? NativePersonalPath,
	string? NativeLegacyPath
) {
	private const string ConfigurationFileName = "icod-toprc.json";
	private const string LegacyConfigurationFileName = ".icod-toprc.json";
	private const string NativeConfigurationFileName = "toprc";
	private const string NativeLegacyConfigurationFileName = ".toprc";

	internal static TopConfigurationPaths Resolve(
		Func<string, string?> environmentVariableProvider
	) {
		ArgumentNullException.ThrowIfNull( environmentVariableProvider );

		string? home = AbsoluteDirectory(
			environmentVariableProvider( "HOME" )
		);
		string? xdg = AbsoluteDirectory(
			environmentVariableProvider( "XDG_CONFIG_HOME" )
		);
		string? appData = AbsoluteDirectory(
			environmentVariableProvider( "APPDATA" )
		);

		string? personalPath = null;
		string? nativePersonalPath = null;
		if ( xdg is not null ) {
			personalPath = Path.Combine(
				xdg,
				"procps",
				ConfigurationFileName
			);
			nativePersonalPath = Path.Combine(
				xdg,
				"procps",
				NativeConfigurationFileName
			);
		} else if ( home is not null ) {
			personalPath = Path.Combine(
				home,
				".config",
				"procps",
				ConfigurationFileName
			);
			nativePersonalPath = Path.Combine(
				home,
				".config",
				"procps",
				NativeConfigurationFileName
			);
		} else if ( appData is not null ) {
			personalPath = Path.Combine(
				appData,
				"procps",
				ConfigurationFileName
			);
		}

		string? legacyPath = null;
		string? nativeLegacyPath = null;
		if ( home is not null ) {
			legacyPath = Path.Combine(
				home,
				LegacyConfigurationFileName
			);
			nativeLegacyPath = Path.Combine(
				home,
				NativeLegacyConfigurationFileName
			);
		}
		return new TopConfigurationPaths(
			personalPath,
			legacyPath,
			nativePersonalPath,
			nativeLegacyPath
		);
	}

	private static string? AbsoluteDirectory(
		string? path
	) {
		if ( string.IsNullOrWhiteSpace( path ) ) {
			return null;
		}
		return ( Path.IsPathFullyQualified( path ) )
			? path
			: null
		;
	}
}

/// <summary>Contains the effective restrictions read from the Linux system file.</summary>
internal readonly record struct TopSystemRestrictions(
	TimeSpan? Delay
);

/// <summary>Provides the privilege test used by procps-compatible system restrictions.</summary>
internal static class TopSystemIdentity {
	internal static bool IsPrivilegedUser() {
		if ( !OperatingSystem.IsLinux() ) {
			return false;
		}
		return 0U == NativeMethods.GetUserId();
	}

	private static class NativeMethods {
		[DllImport(
			"libc",
			EntryPoint = "getuid"
		)]
		internal static extern uint GetUserId();
	}
}

/// <summary>Uses the process environment and filesystem for top configuration.</summary>
internal sealed partial class SystemTopConfigurationStore {
	private const string LinuxSystemRestrictionsPath = "/etc/toprc";
	private const string LinuxSystemDefaultsPath = "/etc/topdefaultrc";
	private static readonly Encoding Utf8 = new UTF8Encoding( false );
	private readonly TopConfigurationPaths paths;
	private readonly string? systemRestrictionsPath;
	private readonly Func<bool> privilegedUserProvider;
	private readonly string? systemDefaultsPath;
	private readonly bool nativeConfigurationEnabled;

	internal SystemTopConfigurationStore(
		Func<string, string?> environmentVariableProvider
	) : this(
		environmentVariableProvider,
		( OperatingSystem.IsLinux() )
			? LinuxSystemRestrictionsPath
			: null,
		TopSystemIdentity.IsPrivilegedUser,
		( OperatingSystem.IsLinux() )
			? LinuxSystemDefaultsPath
			: null,
		OperatingSystem.IsLinux()
	) {
	}

	internal SystemTopConfigurationStore(
		Func<string, string?> environmentVariableProvider,
		string? systemRestrictionsPath,
		Func<bool> privilegedUserProvider
	) : this(
		environmentVariableProvider,
		systemRestrictionsPath,
		privilegedUserProvider,
		systemDefaultsPath: null,
		nativeConfigurationEnabled: false
	) {
	}

	internal SystemTopConfigurationStore(
		Func<string, string?> environmentVariableProvider,
		string? systemRestrictionsPath,
		Func<bool> privilegedUserProvider,
		string? systemDefaultsPath,
		bool nativeConfigurationEnabled
	) {
		ArgumentNullException.ThrowIfNull( environmentVariableProvider );
		ArgumentNullException.ThrowIfNull( privilegedUserProvider );
		ValidateOptionalPath(
			systemRestrictionsPath,
			nameof( systemRestrictionsPath )
		);
		ValidateOptionalPath(
			systemDefaultsPath,
			nameof( systemDefaultsPath )
		);

		this.paths = TopConfigurationPaths.Resolve(
			environmentVariableProvider
		);
		this.systemRestrictionsPath = systemRestrictionsPath;
		this.privilegedUserProvider = privilegedUserProvider;
		this.systemDefaultsPath = systemDefaultsPath;
		this.nativeConfigurationEnabled = nativeConfigurationEnabled;
	}

	internal async ValueTask LoadAsync(
		TopRuntimeState state,
		bool loadPersonalConfiguration,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( state );
		cancellationToken.ThrowIfCancellationRequested();

		TimeSpan builtInDelay = state.Delay;
		TopSystemRestrictions? restrictions = await ReadSystemRestrictionsAsync(
			cancellationToken
		).ConfigureAwait( false );

		if ( loadPersonalConfiguration ) {
			bool loaded = await LoadIcodPersonalConfigurationAsync(
				state,
				cancellationToken
			).ConfigureAwait( false );
			if (
				!loaded
				&& this.nativeConfigurationEnabled
			) {
				loaded = await LoadNativePersonalConfigurationAsync(
					state,
					cancellationToken
				).ConfigureAwait( false );
			}
			if (
				!loaded
				&& this.nativeConfigurationEnabled
			) {
				await LoadNativeConfigurationAsync(
					this.systemDefaultsPath,
					state,
					cancellationToken
				).ConfigureAwait( false );
			}
		}

		if (
			restrictions.HasValue
			&& !this.privilegedUserProvider()
		) {
			state.SecureMode = true;
			state.Delay = restrictions.Value.Delay
				?? builtInDelay;
		}
	}

	internal async ValueTask<string> SaveAsync(
		TopRuntimeState state,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( state );
		cancellationToken.ThrowIfCancellationRequested();

		string path = this.paths.PersonalPath
			?? throw new IOException(
				"unable to establish a personal top configuration path"
			);
		string? directory = Path.GetDirectoryName( path );
		if ( string.IsNullOrEmpty( directory ) ) {
			throw new IOException(
				"unable to establish the personal top configuration directory"
			);
		}
		Directory.CreateDirectory( directory );

		string temporaryPath = Path.Combine(
			directory,
			$".{Path.GetFileName( path )}.{Guid.NewGuid():N}.tmp"
		);
		try {
			await File.WriteAllTextAsync(
				temporaryPath,
				TopConfigurationCodec.Serialize( state ),
				Utf8,
				cancellationToken
			).ConfigureAwait( false );
			File.Move(
				temporaryPath,
				path,
				overwrite: true
			);
		} finally {
			TryDeleteTemporaryFile( temporaryPath );
		}
		await this.SaveNativeMirrorAsync(
			state,
			cancellationToken
		).ConfigureAwait( false );
		return path;
	}

	private async ValueTask<bool> LoadIcodPersonalConfigurationAsync(
		TopRuntimeState state,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( state );
		cancellationToken.ThrowIfCancellationRequested();

		string? path = FirstExistingPath(
			this.paths.PersonalPath,
			this.paths.LegacyPath
		);
		if ( path is null ) {
			return false;
		}

		string text = await File.ReadAllTextAsync(
			path,
			Utf8,
			cancellationToken
		).ConfigureAwait( false );
		try {
			TopConfigurationCodec.Apply(
				text,
				state
			);
		} catch ( FormatException exception ) {
			throw ConfigurationFormatException(
				path,
				exception
			);
		}
		return true;
	}

	private async ValueTask<bool> LoadNativePersonalConfigurationAsync(
		TopRuntimeState state,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( state );
		cancellationToken.ThrowIfCancellationRequested();

		string? path = FirstExistingPath(
			this.paths.NativeLegacyPath,
			this.paths.NativePersonalPath
		);
		return await LoadNativeConfigurationAsync(
			path,
			state,
			cancellationToken
		).ConfigureAwait( false );
	}

	private static async ValueTask<bool> LoadNativeConfigurationAsync(
		string? path,
		TopRuntimeState state,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( state );
		cancellationToken.ThrowIfCancellationRequested();

		if (
			path is null
			|| !File.Exists( path )
		) {
			return false;
		}

		byte[] bytes = await File.ReadAllBytesAsync(
			path,
			cancellationToken
		).ConfigureAwait( false );
		string text = TopProcpsConfigurationCodec.Decode(
			bytes
		);
		try {
			TopProcpsConfigurationCodec.Apply(
				text,
				state
			);
		} catch ( FormatException exception ) {
			throw ConfigurationFormatException(
				path,
				exception
			);
		}
		return true;
	}

	private static string? FirstExistingPath(
		string? first,
		string? second
	) {
		if (
			first is not null
			&& File.Exists( first )
		) {
			return first;
		}
		if (
			second is not null
			&& File.Exists( second )
		) {
			return second;
		}
		return null;
	}

	private static FormatException ConfigurationFormatException(
		string path,
		FormatException exception
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		ArgumentNullException.ThrowIfNull( exception );

		return new FormatException(
			$"configuration file '{path}' is invalid: {exception.Message}",
			exception
		);
	}

	private static void ValidateOptionalPath(
		string? path,
		string parameterName
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( parameterName );
		if (
			path is not null
			&& string.IsNullOrWhiteSpace( path )
		) {
			throw new ArgumentException(
				"The configuration path cannot be empty.",
				parameterName
			);
		}
	}

	private async ValueTask<TopSystemRestrictions?> ReadSystemRestrictionsAsync(
		CancellationToken cancellationToken
	) {
		cancellationToken.ThrowIfCancellationRequested();
		if (
			this.systemRestrictionsPath is null
			|| !File.Exists( this.systemRestrictionsPath )
		) {
			return null;
		}

		string[] lines = await File.ReadAllLinesAsync(
			this.systemRestrictionsPath,
			Utf8,
			cancellationToken
		).ConfigureAwait( false );
		if ( 0 == lines.Length ) {
			return null;
		}

		TimeSpan? delay = null;
		if (
			1 < lines.Length
			&& TryParseSystemRestrictionDelay(
				lines[ 1 ],
				out TimeSpan parsedDelay
			)
		) {
			delay = parsedDelay;
		}
		return new TopSystemRestrictions(
			delay
		);
	}

	private static bool TryParseSystemRestrictionDelay(
		string text,
		out TimeSpan delay
	) {
		ArgumentNullException.ThrowIfNull( text );

		delay = default;
		int commentIndex = text.IndexOf( '#' );
		string value = ( 0 <= commentIndex )
			? text[ ..commentIndex ]
			: text
		;
		value = value.Trim();
		if (
			!double.TryParse(
				value,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out double seconds
			)
			|| !double.IsFinite( seconds )
			|| 0.0 > seconds
		) {
			return false;
		}
		try {
			delay = TimeSpan.FromSeconds(
				seconds
			);
			return true;
		} catch ( OverflowException ) {
			return false;
		}
	}

	private static void TryDeleteTemporaryFile(
		string path
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		try {
			if ( File.Exists( path ) ) {
				File.Delete( path );
			}
		} catch ( Exception exception ) when (
			exception is IOException
				or UnauthorizedAccessException
		) {
			_ = exception;
		}
	}
}

/// <summary>Serializes the persistent four-window top configuration contract.</summary>
internal static class TopConfigurationCodec {
	private const string FormatName = "Icod.ProcPs.Top";
	private const int CurrentVersion = 1;
	private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

	internal static string Serialize(
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( state );

		state.SynchronizeCurrentWindow();
		return JsonSerializer.Serialize(
			CreateDocument( state ),
			SerializerOptions
		) + "\n";
	}

	internal static void Apply(
		string text,
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentNullException.ThrowIfNull( state );

		TopConfigurationDocument document;
		try {
			document = JsonSerializer.Deserialize<TopConfigurationDocument>(
				text,
				SerializerOptions
			) ?? throw new FormatException(
				"the configuration document is empty"
			);
		} catch ( JsonException exception ) {
			throw new FormatException(
				"the configuration JSON is malformed",
				exception
			);
		}
		if ( !string.Equals(
			document.Format,
			FormatName,
			StringComparison.Ordinal
		) ) {
			throw new FormatException(
				$"unsupported configuration format '{document.Format}'"
			);
		}
		if ( CurrentVersion != document.Version ) {
			throw new FormatException(
				$"unsupported configuration version {document.Version}"
			);
		}
		if (
			!double.IsFinite( document.DelaySeconds )
			|| 0.0 > document.DelaySeconds
		) {
			throw new FormatException(
				"the configured delay must be a nonnegative finite number"
			);
		}
		if ( 0 > document.MaximumTasks ) {
			throw new FormatException(
				"the configured maximum task count cannot be negative"
			);
		}
		if ( !TopFixedWidth.IsValid( document.FixedWidthExtra ) ) {
			throw new FormatException(
				$"the configured extra fixed width must be between -1 and {TopFixedWidth.MaximumExtra}"
			);
		}
		if ( !IsKnownField( document.SortField ) ) {
			throw new FormatException(
				$"unknown configured sort field '{document.SortField}'"
			);
		}
		if ( !Enum.IsDefined( typeof( TopMemoryScale ), document.SummaryScale ) ) {
			throw new FormatException(
				$"unknown configured summary memory scale '{document.SummaryScale}'"
			);
		}
		if ( !Enum.IsDefined( typeof( TopMemoryScale ), document.TaskScale ) ) {
			throw new FormatException(
				$"unknown configured task memory scale '{document.TaskScale}'"
			);
		}
		if ( !Enum.IsDefined( typeof( TopSummaryGraphMode ), document.CpuSummaryGraphMode ) ) {
			throw new FormatException(
				$"unknown configured CPU summary graph mode '{document.CpuSummaryGraphMode}'"
			);
		}
		if ( !Enum.IsDefined( typeof( TopSummaryGraphMode ), document.MemorySummaryGraphMode ) ) {
			throw new FormatException(
				$"unknown configured memory summary graph mode '{document.MemorySummaryGraphMode}'"
			);
		}

		TimeSpan delay;
		try {
			delay = TimeSpan.FromSeconds(
				document.DelaySeconds
			);
		} catch ( OverflowException exception ) {
			throw new FormatException(
				"the configured delay is too large",
				exception
			);
		}

		List<TopFieldId> fieldOrder = BuildFieldOrder(
			document.FieldOrder
		);
		HashSet<TopFieldId> visibleFields = BuildVisibleFields(
			document.VisibleFields
		);

		state.Delay = delay;
		state.SortField = document.SortField;
		state.SortHighToLow = document.SortHighToLow;
		state.BoldEnabled = document.BoldEnabled;
		state.HighlightBold = document.HighlightBold;
		state.HighlightRunning = document.HighlightRunning;
		state.ColorsEnabled = document.ColorsEnabled;
		state.Colors = BuildColorPalette(
			document.Colors,
			0
		);
		state.HighlightSortColumn = document.HighlightSortColumn;
		state.NumericLeftJustified = document.NumericLeftJustified;
		state.CharacterRightJustified = document.CharacterRightJustified;
		state.SuppressZeros = document.SuppressZeros;
		state.MaximumTasks = document.MaximumTasks;
		state.SummaryScale = document.SummaryScale;
		state.TaskScale = document.TaskScale;
		TopFixedWidth.Configure(
			state,
			document.FixedWidthExtra
		);
		state.ShowCommandLine = document.ShowCommandLine;
		state.ShowThreads = document.ShowThreads;
		state.HideIdle = document.HideIdle;
		state.Forest = document.Forest;
		state.IrixMode = document.IrixMode;
		state.LoadAverageVisible = document.LoadAverageVisible;
		state.ScrollCoordinatesVisible = document.ScrollCoordinatesVisible;
		state.SingleCpuSummary = document.SingleCpuSummary;
		state.CpuSummaryVisible = document.CpuSummaryVisible;
		state.CpuSummaryGraphMode = document.CpuSummaryGraphMode;
		state.MemorySummaryVisible = document.MemorySummaryVisible;
		state.MemorySummaryGraphMode = document.MemorySummaryGraphMode;

		state.FieldOrder.Clear();
		state.FieldOrder.AddRange( fieldOrder );
		state.VisibleFields.Clear();
		state.VisibleFields.UnionWith( visibleFields );
		state.OtherFilters.Clear();
		if ( document.OtherFilters is not null ) {
			foreach ( TopConfigurationFilterDocument persisted in document.OtherFilters ) {
				if ( string.IsNullOrEmpty( persisted.RawText ) ) {
					throw new FormatException(
						"a configured Other Filter has no criterion"
					);
				}
				if (
					!TopOtherFilterParser.TryParse(
						persisted.RawText,
						persisted.CaseSensitive,
						state,
						out TopOtherFilter? filter,
						out string? error
					)
				) {
					throw new FormatException(
						$"configured Other Filter '{persisted.RawText}' is invalid: {error}"
					);
				}
				state.OtherFilters.Add( filter! );
			}
		}

		state.InspectEntries.Clear();
		if ( document.InspectEntries is not null ) {
			foreach ( TopConfigurationInspectDocument persisted in document.InspectEntries ) {
				if (
					!Enum.IsDefined(
						typeof( TopInspectEntryType ),
						persisted.Type
					)
				) {
					throw new FormatException(
						$"unknown configured Inspect entry type '{persisted.Type}'"
					);
				}
				try {
					state.InspectEntries.Add(
						new TopInspectEntry(
							persisted.Type,
							persisted.Name,
							persisted.Format
						)
					);
				} catch ( ArgumentException exception ) {
					throw new FormatException(
						"the configured Inspect entry is invalid",
						exception
					);
				}
			}
		}

		if ( document.Windows is null ) {
			state.SynchronizeCurrentWindow();
		} else {
			if ( TopRuntimeState.WindowCount != document.Windows.Count ) {
				throw new FormatException(
					$"the configuration must contain exactly {TopRuntimeState.WindowCount} windows"
				);
			}
			if (
				0 > document.CurrentWindowIndex
				|| TopRuntimeState.WindowCount <= document.CurrentWindowIndex
			) {
				throw new FormatException(
					$"invalid current window index {document.CurrentWindowIndex}"
				);
			}
			var windows = new List<TopWindowState>(
				TopRuntimeState.WindowCount
			);
			for ( int index = 0; index < TopRuntimeState.WindowCount; index++ ) {
				windows.Add(
					BuildWindowState(
						document.Windows[ index ],
						index
					)
				);
			}
			state.RestoreWindows(
				windows,
				document.CurrentWindowIndex
			);
		}
		state.AlternateDisplayMode = document.AlternateDisplayMode;
	}

	private static TopConfigurationDocument CreateDocument(
		TopRuntimeState state
	) {
		var visibleFields = new List<TopFieldId>();
		foreach ( TopFieldId field in state.FieldOrder ) {
			if ( state.VisibleFields.Contains( field ) ) {
				visibleFields.Add( field );
			}
		}

		var filters = CreateFilterDocuments(
			state.OtherFilters
		);
		var inspectEntries = new List<TopConfigurationInspectDocument>(
			state.InspectEntries.Count
		);
		foreach ( TopInspectEntry entry in state.InspectEntries ) {
			inspectEntries.Add(
				new TopConfigurationInspectDocument {
					Type = entry.Type,
					Name = entry.Name,
					Format = entry.Format
				}
			);
		}
		var windows = new List<TopConfigurationWindowDocument>(
			TopRuntimeState.WindowCount
		);
		foreach ( TopWindowState window in state.Windows ) {
			windows.Add(
				CreateWindowDocument(
					window
				)
			);
		}

		return new TopConfigurationDocument {
			Format = FormatName,
			Version = CurrentVersion,
			DelaySeconds = state.Delay.TotalSeconds,
			SortField = state.SortField,
			SortHighToLow = state.SortHighToLow,
			BoldEnabled = state.BoldEnabled,
			HighlightBold = state.HighlightBold,
			HighlightRunning = state.HighlightRunning,
			HighlightSortColumn = state.HighlightSortColumn,
			NumericLeftJustified = state.NumericLeftJustified,
			CharacterRightJustified = state.CharacterRightJustified,
			SuppressZeros = state.SuppressZeros,
			MaximumTasks = state.MaximumTasks,
			SummaryScale = state.SummaryScale,
			TaskScale = state.TaskScale,
			FixedWidthExtra = state.FixedWidthExtra,
			ShowCommandLine = state.ShowCommandLine,
			ShowThreads = state.ShowThreads,
			HideIdle = state.HideIdle,
			Forest = state.Forest,
			IrixMode = state.IrixMode,
			LoadAverageVisible = state.LoadAverageVisible,
			ScrollCoordinatesVisible = state.ScrollCoordinatesVisible,
			SingleCpuSummary = state.SingleCpuSummary,
			CpuSummaryVisible = state.CpuSummaryVisible,
			CpuSummaryGraphMode = state.CpuSummaryGraphMode,
			MemorySummaryVisible = state.MemorySummaryVisible,
			MemorySummaryGraphMode = state.MemorySummaryGraphMode,
			ColorsEnabled = state.ColorsEnabled,
			Colors = CreateColorDocument( state.Colors ),
			AlternateDisplayMode = state.AlternateDisplayMode,
			CurrentWindowIndex = state.CurrentWindowIndex,
			FieldOrder = [ .. state.FieldOrder ],
			VisibleFields = visibleFields,
			OtherFilters = filters,
			InspectEntries = inspectEntries,
			Windows = windows
		};
	}

	private static TopConfigurationWindowDocument CreateWindowDocument(
		TopWindowState window
	) {
		ArgumentNullException.ThrowIfNull( window );

		var visibleFields = new List<TopFieldId>();
		foreach ( TopFieldId field in window.FieldOrder ) {
			if ( window.VisibleFields.Contains( field ) ) {
				visibleFields.Add( field );
			}
		}
		return new TopConfigurationWindowDocument {
			Name = window.Name,
			TaskDisplayVisible = window.TaskDisplayVisible,
			SortField = window.SortField,
			SortHighToLow = window.SortHighToLow,
			HighlightBold = window.HighlightBold,
			HighlightRunning = window.HighlightRunning,
			HighlightSortColumn = window.HighlightSortColumn,
			NumericLeftJustified = window.NumericLeftJustified,
			CharacterRightJustified = window.CharacterRightJustified,
			MaximumTasks = window.MaximumTasks,
			ShowCommandLine = window.ShowCommandLine,
			HideIdle = window.HideIdle,
			Forest = window.Forest,
			LoadAverageVisible = window.LoadAverageVisible,
			ScrollCoordinatesVisible = window.ScrollCoordinatesVisible,
			SingleCpuSummary = window.SingleCpuSummary,
			CpuSummaryVisible = window.CpuSummaryVisible,
			CpuSummaryGraphMode = window.CpuSummaryGraphMode,
			MemorySummaryVisible = window.MemorySummaryVisible,
			MemorySummaryGraphMode = window.MemorySummaryGraphMode,
			ColorsEnabled = window.ColorsEnabled,
			Colors = CreateColorDocument( window.Colors ),
			FieldOrder = [ .. window.FieldOrder ],
			VisibleFields = visibleFields,
			OtherFilters = CreateFilterDocuments(
				window.OtherFilters
			)
		};
	}

	private static List<TopConfigurationFilterDocument> CreateFilterDocuments(
		IReadOnlyList<TopOtherFilter> filters
	) {
		ArgumentNullException.ThrowIfNull( filters );

		var result = new List<TopConfigurationFilterDocument>(
			filters.Count
		);
		foreach ( TopOtherFilter filter in filters ) {
			result.Add(
				new TopConfigurationFilterDocument {
					RawText = filter.RawText,
					CaseSensitive = filter.CaseSensitive
				}
			);
		}
		return result;
	}

	private static TopWindowState BuildWindowState(
		TopConfigurationWindowDocument document,
		int index
	) {
		ArgumentNullException.ThrowIfNull( document );
		if ( index is < 0 or >= TopRuntimeState.WindowCount ) {
			throw new ArgumentOutOfRangeException(
				nameof( index )
			);
		}
		if ( 0 > document.MaximumTasks ) {
			throw new FormatException(
				$"window {index + 1} has a negative maximum task count"
			);
		}
		if ( !IsKnownField( document.SortField ) ) {
			throw new FormatException(
				$"window {index + 1} has unknown sort field '{document.SortField}'"
			);
		}
		if ( !Enum.IsDefined( typeof( TopSummaryGraphMode ), document.CpuSummaryGraphMode ) ) {
			throw new FormatException(
				$"window {index + 1} has unknown CPU summary graph mode '{document.CpuSummaryGraphMode}'"
			);
		}
		if ( !Enum.IsDefined( typeof( TopSummaryGraphMode ), document.MemorySummaryGraphMode ) ) {
			throw new FormatException(
				$"window {index + 1} has unknown memory summary graph mode '{document.MemorySummaryGraphMode}'"
			);
		}

		string name = ResolveWindowName(
			document.Name,
			index
		);
		var result = new TopWindowState(
			name
		) {
			TaskDisplayVisible = document.TaskDisplayVisible,
			SortField = document.SortField,
			SortHighToLow = document.SortHighToLow,
			HighlightBold = document.HighlightBold,
			HighlightRunning = document.HighlightRunning,
			HighlightSortColumn = document.HighlightSortColumn,
			NumericLeftJustified = document.NumericLeftJustified,
			CharacterRightJustified = document.CharacterRightJustified,
			MaximumTasks = document.MaximumTasks,
			ShowCommandLine = document.ShowCommandLine,
			HideIdle = document.HideIdle,
			Forest = document.Forest,
			LoadAverageVisible = document.LoadAverageVisible,
			ScrollCoordinatesVisible = document.ScrollCoordinatesVisible,
			SingleCpuSummary = document.SingleCpuSummary,
			CpuSummaryVisible = document.CpuSummaryVisible,
			CpuSummaryGraphMode = document.CpuSummaryGraphMode,
			MemorySummaryVisible = document.MemorySummaryVisible,
			MemorySummaryGraphMode = document.MemorySummaryGraphMode,
			ColorsEnabled = document.ColorsEnabled,
			Colors = BuildColorPalette(
				document.Colors,
				index
			)
		};

		result.FieldOrder.Clear();
		result.FieldOrder.AddRange(
			BuildFieldOrder(
				document.FieldOrder
			)
		);
		result.VisibleFields.Clear();
		result.VisibleFields.UnionWith(
			BuildVisibleFields(
				document.VisibleFields
			)
		);

		if ( document.OtherFilters is not null ) {
			var parsingState = new TopRuntimeState {
				NumericLeftJustified = result.NumericLeftJustified,
				CharacterRightJustified = result.CharacterRightJustified
			};
			foreach ( TopConfigurationFilterDocument persisted in document.OtherFilters ) {
				if ( string.IsNullOrEmpty( persisted.RawText ) ) {
					throw new FormatException(
						$"window {index + 1} has an Other Filter with no criterion"
					);
				}
				if (
					!TopOtherFilterParser.TryParse(
						persisted.RawText,
						persisted.CaseSensitive,
						parsingState,
						out TopOtherFilter? filter,
						out string? error
					)
				) {
					throw new FormatException(
						$"window {index + 1} Other Filter '{persisted.RawText}' is invalid: {error}"
					);
				}
				parsingState.OtherFilters.Add(
					filter!
				);
				result.OtherFilters.Add(
					filter!
				);
			}
		}
		return result;
	}

	private static TopConfigurationColorDocument CreateColorDocument(
		TopColorPalette colors
	) {
		return new TopConfigurationColorDocument {
			Summary = colors.Summary,
			Messages = colors.Messages,
			Header = colors.Header,
			Tasks = colors.Tasks,
			TaskAccent = colors.TaskAccent
		};
	}

	private static TopColorPalette BuildColorPalette(
		TopConfigurationColorDocument? document,
		int windowIndex
	) {
		if ( windowIndex is < 0 or >= TopRuntimeState.WindowCount ) {
			throw new ArgumentOutOfRangeException(
				nameof( windowIndex )
			);
		}

		TopColorPalette fallback = TopColorPalette.ForWindow(
			windowIndex
		);
		if ( document is null ) {
			return fallback;
		}
		try {
			return new TopColorPalette(
				document.Summary ?? fallback.Summary,
				document.Messages ?? fallback.Messages,
				document.Header ?? fallback.Header,
				document.Tasks ?? fallback.Tasks,
				document.TaskAccent ?? fallback.TaskAccent
			);
		} catch ( ArgumentOutOfRangeException exception ) {
			throw new FormatException(
				$"window {windowIndex + 1} contains a color outside the supported -1 through 255 range",
				exception
			);
		}
	}

	private static string ResolveWindowName(
		string? configured,
		int index
	) {
		if ( index is < 0 or >= TopRuntimeState.WindowCount ) {
			throw new ArgumentOutOfRangeException(
				nameof( index )
			);
		}
		if ( configured is null ) {
			return TopRuntimeState.GetWindowName(
				index
			);
		}

		string name = configured.Trim();
		int byteCount = Encoding.UTF8.GetByteCount(
			name
		);
		if ( byteCount is < 1 or > 3 ) {
			throw new FormatException(
				$"window {index + 1} name must occupy 1 through 3 UTF-8 bytes"
			);
		}
		return name;
	}

	private static List<TopFieldId> BuildFieldOrder(
		IReadOnlyList<TopFieldId>? configured
	) {
		var result = new List<TopFieldId>();
		var seen = new HashSet<TopFieldId>();
		if ( configured is not null ) {
			foreach ( TopFieldId field in configured ) {
				if ( !IsKnownField( field ) ) {
					throw new FormatException(
						$"unknown field '{field}' in configured field order"
					);
				}
				if ( !seen.Add( field ) ) {
					throw new FormatException(
						$"field '{field}' occurs more than once in configured field order"
					);
				}
				result.Add( field );
			}
		}
		foreach ( TopFieldDefinition definition in TopFieldCatalog.Definitions ) {
			if ( seen.Add( definition.Id ) ) {
				result.Add( definition.Id );
			}
		}
		return result;
	}

	private static HashSet<TopFieldId> BuildVisibleFields(
		IReadOnlyList<TopFieldId>? configured
	) {
		if ( configured is null ) {
			return TopFieldCatalog.CreateDefaultVisible();
		}

		var result = new HashSet<TopFieldId>();
		foreach ( TopFieldId field in configured ) {
			if ( !IsKnownField( field ) ) {
				throw new FormatException(
					$"unknown field '{field}' in configured visible fields"
				);
			}
			if ( !result.Add( field ) ) {
				throw new FormatException(
					$"field '{field}' occurs more than once in configured visible fields"
				);
			}
		}
		return result;
	}

	private static bool IsKnownField(
		TopFieldId field
	) {
		foreach ( TopFieldDefinition definition in TopFieldCatalog.Definitions ) {
			if ( definition.Id == field ) {
				return true;
			}
		}
		return false;
	}

	private static JsonSerializerOptions CreateSerializerOptions() {
		var result = new JsonSerializerOptions {
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = true
		};
		result.Converters.Add(
			new JsonStringEnumConverter()
		);
		return result;
	}
}

internal sealed class TopConfigurationDocument {
	public string Format { get; set; } = string.Empty;
	public int Version { get; set; }
	public double DelaySeconds { get; set; } = 3.0;
	public TopFieldId SortField { get; set; } = TopFieldId.Cpu;
	public bool SortHighToLow { get; set; } = true;
	public bool BoldEnabled { get; set; } = true;
	public bool HighlightBold { get; set; } = true;
	public bool HighlightRunning { get; set; } = true;
	public bool HighlightSortColumn { get; set; }
	public bool NumericLeftJustified { get; set; }
	public bool CharacterRightJustified { get; set; }
	public bool SuppressZeros { get; set; }
	public int MaximumTasks { get; set; }
	public TopMemoryScale SummaryScale { get; set; } = TopMemoryScale.Mebibytes;
	public TopMemoryScale TaskScale { get; set; } = TopMemoryScale.Kibibytes;
	public int FixedWidthExtra { get; set; }
	public bool ShowCommandLine { get; set; }
	public bool ShowThreads { get; set; }
	public bool HideIdle { get; set; }
	public bool Forest { get; set; }
	public bool IrixMode { get; set; } = true;
	public bool LoadAverageVisible { get; set; } = true;
	public bool ScrollCoordinatesVisible { get; set; }
	public bool SingleCpuSummary { get; set; } = true;
	public bool CpuSummaryVisible { get; set; } = true;
	public TopSummaryGraphMode CpuSummaryGraphMode { get; set; } = TopSummaryGraphMode.Detailed;
	public bool MemorySummaryVisible { get; set; } = true;
	public TopSummaryGraphMode MemorySummaryGraphMode { get; set; } = TopSummaryGraphMode.Detailed;
	public bool ColorsEnabled { get; set; } = true;
	public TopConfigurationColorDocument? Colors { get; set; }
	public bool AlternateDisplayMode { get; set; }
	public int CurrentWindowIndex { get; set; }
	public List<TopFieldId>? FieldOrder { get; set; }
	public List<TopFieldId>? VisibleFields { get; set; }
	public List<TopConfigurationFilterDocument>? OtherFilters { get; set; }
	public List<TopConfigurationInspectDocument>? InspectEntries { get; set; }
	public List<TopConfigurationWindowDocument>? Windows { get; set; }
}

internal sealed class TopConfigurationWindowDocument {
	public string? Name { get; set; }
	public bool TaskDisplayVisible { get; set; } = true;
	public TopFieldId SortField { get; set; } = TopFieldId.Cpu;
	public bool SortHighToLow { get; set; } = true;
	public bool HighlightBold { get; set; } = true;
	public bool HighlightRunning { get; set; } = true;
	public bool HighlightSortColumn { get; set; }
	public bool NumericLeftJustified { get; set; }
	public bool CharacterRightJustified { get; set; }
	public int MaximumTasks { get; set; }
	public bool ShowCommandLine { get; set; }
	public bool HideIdle { get; set; }
	public bool Forest { get; set; }
	public bool LoadAverageVisible { get; set; } = true;
	public bool ScrollCoordinatesVisible { get; set; }
	public bool SingleCpuSummary { get; set; } = true;
	public bool CpuSummaryVisible { get; set; } = true;
	public TopSummaryGraphMode CpuSummaryGraphMode { get; set; } = TopSummaryGraphMode.Detailed;
	public bool MemorySummaryVisible { get; set; } = true;
	public TopSummaryGraphMode MemorySummaryGraphMode { get; set; } = TopSummaryGraphMode.Detailed;
	public bool ColorsEnabled { get; set; } = true;
	public TopConfigurationColorDocument? Colors { get; set; }
	public List<TopFieldId>? FieldOrder { get; set; }
	public List<TopFieldId>? VisibleFields { get; set; }
	public List<TopConfigurationFilterDocument>? OtherFilters { get; set; }
}

internal sealed class TopConfigurationColorDocument {
	public int? Summary { get; set; }
	public int? Messages { get; set; }
	public int? Header { get; set; }
	public int? Tasks { get; set; }
	public int? TaskAccent { get; set; }
}

internal sealed class TopConfigurationFilterDocument {
	public string RawText { get; set; } = string.Empty;
	public bool CaseSensitive { get; set; }
}

internal sealed class TopConfigurationInspectDocument {
	public TopInspectEntryType Type { get; set; }
	public string Name { get; set; } = string.Empty;
	public string Format { get; set; } = string.Empty;
}
