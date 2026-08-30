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

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Resolved personal configuration paths for the current environment.</summary>
internal readonly record struct TopConfigurationPaths(
	string? PersonalPath,
	string? LegacyPath
) {
	private const string ConfigurationFileName = "icod-toprc.json";
	private const string LegacyConfigurationFileName = ".icod-toprc.json";

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
		if ( xdg is not null ) {
			personalPath = Path.Combine(
				xdg,
				"procps",
				ConfigurationFileName
			);
		} else if ( home is not null ) {
			personalPath = Path.Combine(
				home,
				".config",
				"procps",
				ConfigurationFileName
			);
		} else if ( appData is not null ) {
			personalPath = Path.Combine(
				appData,
				"procps",
				ConfigurationFileName
			);
		}

		string? legacyPath = null;
		if ( home is not null ) {
			legacyPath = Path.Combine(
				home,
				LegacyConfigurationFileName
			);
		}
		return new TopConfigurationPaths(
			personalPath,
			legacyPath
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

/// <summary>Uses the process environment and filesystem for personal top configuration.</summary>
internal sealed class SystemTopConfigurationStore {
	private static readonly Encoding Utf8 = new UTF8Encoding( false );
	private readonly TopConfigurationPaths paths;

	internal SystemTopConfigurationStore(
		Func<string, string?> environmentVariableProvider
	) {
		ArgumentNullException.ThrowIfNull( environmentVariableProvider );
		this.paths = TopConfigurationPaths.Resolve(
			environmentVariableProvider
		);
	}

	internal async ValueTask LoadAsync(
		TopRuntimeState state,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( state );
		cancellationToken.ThrowIfCancellationRequested();

		string? path = null;
		if (
			this.paths.PersonalPath is not null
			&& File.Exists( this.paths.PersonalPath )
		) {
			path = this.paths.PersonalPath;
		} else if (
			this.paths.LegacyPath is not null
			&& File.Exists( this.paths.LegacyPath )
		) {
			path = this.paths.LegacyPath;
		}
		if ( path is null ) {
			return;
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
			throw new FormatException(
				$"configuration file '{path}' is invalid: {exception.Message}",
				exception
			);
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
		return path;
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

/// <summary>Serializes the persistent single-window top configuration contract.</summary>
internal static class TopConfigurationCodec {
	private const string FormatName = "Icod.ProcPs.Top";
	private const int CurrentVersion = 1;
	private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

	internal static string Serialize(
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( state );

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
		state.HighlightSortColumn = document.HighlightSortColumn;
		state.NumericLeftJustified = document.NumericLeftJustified;
		state.CharacterRightJustified = document.CharacterRightJustified;
		state.SuppressZeros = document.SuppressZeros;
		state.MaximumTasks = document.MaximumTasks;
		state.SummaryScale = document.SummaryScale;
		state.TaskScale = document.TaskScale;
		state.ShowCommandLine = document.ShowCommandLine;
		state.ShowThreads = document.ShowThreads;
		state.HideIdle = document.HideIdle;
		state.Forest = document.Forest;
		state.IrixMode = document.IrixMode;
		state.SingleCpuSummary = document.SingleCpuSummary;

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

		var filters = new List<TopConfigurationFilterDocument>(
			state.OtherFilters.Count
		);
		foreach ( TopOtherFilter filter in state.OtherFilters ) {
			filters.Add(
				new TopConfigurationFilterDocument {
					RawText = filter.RawText,
					CaseSensitive = filter.CaseSensitive
				}
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
			ShowCommandLine = state.ShowCommandLine,
			ShowThreads = state.ShowThreads,
			HideIdle = state.HideIdle,
			Forest = state.Forest,
			IrixMode = state.IrixMode,
			SingleCpuSummary = state.SingleCpuSummary,
			FieldOrder = [ .. state.FieldOrder ],
			VisibleFields = visibleFields,
			OtherFilters = filters
		};
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
	public bool ShowCommandLine { get; set; }
	public bool ShowThreads { get; set; }
	public bool HideIdle { get; set; }
	public bool Forest { get; set; }
	public bool IrixMode { get; set; } = true;
	public bool SingleCpuSummary { get; set; } = true;
	public List<TopFieldId>? FieldOrder { get; set; }
	public List<TopFieldId>? VisibleFields { get; set; }
	public List<TopConfigurationFilterDocument>? OtherFilters { get; set; }
}

internal sealed class TopConfigurationFilterDocument {
	public string RawText { get; set; } = string.Empty;
	public bool CaseSensitive { get; set; }
}
