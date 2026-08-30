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
using System.Text;
using System.Text.RegularExpressions;

/// <summary>Reads the transformed integer rcfile format used by procps-ng 4.x top.</summary>
internal static partial class TopProcpsConfigurationCodec {
	private const int NativeFieldOffset = 37;
	private const int NativeFieldVisibleMask = 1;

	private const int QsrtNormal = 0x000004;
	private const int ViewNoBold = 0x000008;
	private const int ShowTaskOn = 0x000010;
	private const int ShowIdleProcesses = 0x000020;
	private const int ShowCommandLineFlag = 0x000080;
	private const int ShowHighlightRowsFlag = 0x000100;
	private const int ShowHighlightColumnsFlag = 0x000200;
	private const int ShowHighlightBoldFlag = 0x000400;
	private const int ShowJustifyNumericRightFlag = 0x020000;
	private const int ShowJustifyStringsRightFlag = 0x040000;

	[GeneratedRegex(
		@"^Id:(?<id>[A-Za-z]),\s*Mode_altscr=(?<alt>-?\d+),\s*Mode_irixps=(?<irix>-?\d+),\s*Delay_time=(?<whole>-?\d+)\.(?<fract>\d+),\s*Curwin=(?<window>-?\d+)\s*$",
		RegexOptions.CultureInvariant
	)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(
		@"^window\s+#(?<window>\d+),\s*osel_tot=(?<count>\d+)\s*$",
		RegexOptions.CultureInvariant
	)]
	private static partial Regex OtherFilterWindowPattern();

	[GeneratedRegex(
		@"^\s*type=(?<type>-?\d+),\s*filter=(?<filter>.*)$",
		RegexOptions.CultureInvariant
	)]
	private static partial Regex OtherFilterPattern();

	internal static void Apply(
		string text,
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentNullException.ThrowIfNull( state );

		TopRuntimeState parsed = Parse(
			text
		);
		parsed.SynchronizeCurrentWindow();

		state.Delay = parsed.Delay;
		state.BoldEnabled = parsed.BoldEnabled;
		state.SuppressZeros = parsed.SuppressZeros;
		state.SummaryScale = parsed.SummaryScale;
		state.TaskScale = parsed.TaskScale;
		state.IrixMode = parsed.IrixMode;
		state.RestoreWindows(
			parsed.Windows,
			parsed.CurrentWindowIndex
		);
		state.AlternateDisplayMode = parsed.AlternateDisplayMode;
	}

	private static TopRuntimeState Parse(
		string text
	) {
		string normalized = text.Replace(
			"\r\n",
			"\n",
			StringComparison.Ordinal
		).Replace(
			'\r',
			'\n'
		);
		string[] lines = normalized.Split(
			'\n'
		);
		if ( 2 > lines.Length ) {
			throw new FormatException(
				"the procps configuration is missing its header"
			);
		}

		Match header = HeaderPattern().Match(
			lines[ 1 ].Trim()
		);
		if ( !header.Success ) {
			throw new FormatException(
				"the procps configuration header is malformed"
			);
		}

		char version = header.Groups[
			"id"
		].Value[ 0 ];
		if ( version is < 'k' or > 'n' ) {
			throw new FormatException(
				$"procps rc version '{version}' is not a supported transformed 4.x format"
			);
		}

		bool alternateDisplayMode = ParseBooleanInteger(
			header.Groups[ "alt" ].Value,
			"Mode_altscr"
		);
		bool irixMode = ParseBooleanInteger(
			header.Groups[ "irix" ].Value,
			"Mode_irixps"
		);
		TimeSpan delay = ParseProcpsDelay(
			header.Groups[ "whole" ].Value,
			header.Groups[ "fract" ].Value
		);
		int currentWindowIndex = ParseInteger(
			header.Groups[ "window" ].Value,
			"Curwin"
		);
		if (
			currentWindowIndex is < 0
				or >= TopRuntimeState.WindowCount
		) {
			throw new FormatException(
				$"invalid procps current window index {currentWindowIndex}"
			);
		}

		int lineIndex = 2;
		var parsedWindows = new List<ParsedWindow>(
			TopRuntimeState.WindowCount
		);
		for (
			int windowIndex = 0;
			windowIndex < TopRuntimeState.WindowCount;
			windowIndex++
		) {
			parsedWindows.Add(
				ParseWindow(
					lines,
					ref lineIndex,
					windowIndex
				)
			);
		}

		TopMemoryScale summaryScale = TopMemoryScale.Mebibytes;
		TopMemoryScale taskScale = TopMemoryScale.Kibibytes;
		bool suppressZeros = false;
		if (
			lineIndex < lines.Length
			&& lines[ lineIndex ].TrimStart().StartsWith(
				"Fixed_widest=",
				StringComparison.Ordinal
			)
		) {
			Dictionary<string, string> globals = ParseAssignments(
				lines[
					lineIndex++
				]
			);
			summaryScale = ParseMemoryScale(
				globals,
				"Summ_mscale",
				allowExbibytes: true
			);
			taskScale = ParseMemoryScale(
				globals,
				"Task_mscale",
				allowExbibytes: false
			);
			suppressZeros = ParseOptionalBooleanInteger(
				globals,
				"Zero_suppress"
			);
		}

		ParseOtherFilters(
			lines,
			lineIndex,
			parsedWindows
		);

		var result = new TopRuntimeState {
			Delay = delay,
			SuppressZeros = suppressZeros,
			SummaryScale = summaryScale,
			TaskScale = taskScale,
			IrixMode = irixMode,
			AlternateDisplayMode = alternateDisplayMode
		};
		var windows = new List<TopWindowState>(
			TopRuntimeState.WindowCount
		);
		foreach ( ParsedWindow parsedWindow in parsedWindows ) {
			windows.Add(
				parsedWindow.State
			);
		}
		result.RestoreWindows(
			windows,
			currentWindowIndex
		);
		result.BoldEnabled = 0 == (
			parsedWindows[
				currentWindowIndex
			].WinFlags
			& ViewNoBold
		);
		return result;
	}

	private static ParsedWindow ParseWindow(
		IReadOnlyList<string> lines,
		ref int lineIndex,
		int windowIndex
	) {
		ArgumentNullException.ThrowIfNull( lines );
		if ( windowIndex is < 0 or >= TopRuntimeState.WindowCount ) {
			throw new ArgumentOutOfRangeException(
				nameof( windowIndex )
			);
		}
		if ( lineIndex >= lines.Count ) {
			throw new FormatException(
				$"procps window {windowIndex + 1} is missing"
			);
		}

		string firstLine = lines[
			lineIndex++
		];
		const string fieldsMarker = "fieldscur=";
		int markerIndex = firstLine.IndexOf(
			fieldsMarker,
			StringComparison.Ordinal
		);
		if ( 0 > markerIndex ) {
			throw new FormatException(
				$"procps window {windowIndex + 1} has no fieldscur entry"
			);
		}

		string name = firstLine[
			..markerIndex
		].Trim();
		ValidateWindowName(
			name,
			windowIndex
		);

		var fieldsText = new StringBuilder(
			firstLine[
				( markerIndex + fieldsMarker.Length )..
			].Trim()
		);
		while (
			lineIndex < lines.Count
			&& !lines[ lineIndex ].Contains(
				"winflags=",
				StringComparison.Ordinal
			)
		) {
			string continuation = lines[
				lineIndex++
			].Trim();
			if ( 0 < continuation.Length ) {
				fieldsText.Append(
					' '
				);
				fieldsText.Append(
					continuation
				);
			}
		}
		if ( lineIndex >= lines.Count ) {
			throw new FormatException(
				$"procps window {windowIndex + 1} has no winflags entry"
			);
		}

		string winFlagsLine = lines[
			lineIndex++
		];
		if ( lineIndex >= lines.Count ) {
			throw new FormatException(
				$"procps window {windowIndex + 1} has no color/options entry"
			);
		}
		string colorLine = lines[
			lineIndex++
		];
		Dictionary<string, string> settings = ParseAssignments(
			$"{winFlagsLine},{colorLine}"
		);
		int winFlags = RequiredInteger(
			settings,
			"winflags"
		);
		int sortIndex = RequiredInteger(
			settings,
			"sortindx"
		);
		int maximumTasks = RequiredInteger(
			settings,
			"maxtasks"
		);
		if ( 0 > maximumTasks ) {
			throw new FormatException(
				$"procps window {windowIndex + 1} has a negative maximum task count"
			);
		}

		var state = new TopWindowState(
			name
		) {
			TaskDisplayVisible = 0 != (
				winFlags
				& ShowTaskOn
			),
			SortField = NativeSortField(
				sortIndex
			),
			SortHighToLow = 0 != (
				winFlags
				& QsrtNormal
			),
			HighlightBold = 0 != (
				winFlags
				& ShowHighlightBoldFlag
			),
			HighlightRunning = 0 != (
				winFlags
				& ShowHighlightRowsFlag
			),
			HighlightSortColumn = 0 != (
				winFlags
				& ShowHighlightColumnsFlag
			),
			NumericLeftJustified = 0 == (
				winFlags
				& ShowJustifyNumericRightFlag
			),
			CharacterRightJustified = 0 != (
				winFlags
				& ShowJustifyStringsRightFlag
			),
			MaximumTasks = maximumTasks,
			ShowCommandLine = 0 != (
				winFlags
				& ShowCommandLineFlag
			),
			HideIdle = 0 == (
				winFlags
				& ShowIdleProcesses
			),
			Forest = 0 != (
				winFlags
				& 0x000002
			)
		};

		ApplyFieldConfiguration(
			fieldsText.ToString(),
			state,
			windowIndex
		);
		return new ParsedWindow(
			state,
			winFlags
		);
	}

	private static void ApplyFieldConfiguration(
		string text,
		TopWindowState state,
		int windowIndex
	) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentNullException.ThrowIfNull( state );

		string[] parts = text.Split(
			(char[]?)null,
			StringSplitOptions.RemoveEmptyEntries
				| StringSplitOptions.TrimEntries
		);
		if ( 0 == parts.Length ) {
			throw new FormatException(
				$"procps window {windowIndex + 1} has an empty fieldscur list"
			);
		}

		var nativeSeen = new HashSet<int>();
		var supportedSeen = new HashSet<TopFieldId>();
		var order = new List<TopFieldId>();
		var visible = new HashSet<TopFieldId>();
		foreach ( string part in parts ) {
			int encoded = ParseInteger(
				part,
				"fieldscur"
			);
			if ( 0 >= encoded ) {
				throw new FormatException(
					$"procps window {windowIndex + 1} has invalid fieldscur value {encoded}"
				);
			}
			int nativeField = (
				encoded
				>> 1
			) - NativeFieldOffset;
			if (
				0 > nativeField
				|| !nativeSeen.Add( nativeField )
			) {
				throw new FormatException(
					$"procps window {windowIndex + 1} has an invalid or duplicate fieldscur value {encoded}"
				);
			}

			if (
				!TryMapNativeField(
					nativeField,
					out TopFieldId field
				)
			) {
				continue;
			}
			supportedSeen.Add(
				field
			);
			order.Add(
				field
			);
			if (
				0 != (
					encoded
					& NativeFieldVisibleMask
				)
			) {
				visible.Add(
					field
				);
			}
		}

		foreach ( TopFieldDefinition definition in TopFieldCatalog.Definitions ) {
			if ( supportedSeen.Add( definition.Id ) ) {
				order.Add(
					definition.Id
				);
			}
		}

		state.FieldOrder.Clear();
		state.FieldOrder.AddRange(
			order
		);
		state.VisibleFields.Clear();
		state.VisibleFields.UnionWith(
			visible
		);
	}

	private static void ParseOtherFilters(
		IReadOnlyList<string> lines,
		int lineIndex,
		IReadOnlyList<ParsedWindow> windows
	) {
		ArgumentNullException.ThrowIfNull( lines );
		ArgumentNullException.ThrowIfNull( windows );

		while (
			lineIndex < lines.Count
			&& !lines[ lineIndex ].Contains(
				"begin: saved other filter data",
				StringComparison.Ordinal
			)
		) {
			lineIndex++;
		}
		if ( lineIndex >= lines.Count ) {
			return;
		}
		lineIndex++;

		while ( lineIndex < lines.Count ) {
			string line = lines[
				lineIndex++
			].Trim();
			if ( 0 == line.Length ) {
				continue;
			}
			if ( line.Contains(
				"end  : saved other filter data",
				StringComparison.Ordinal
			) ) {
				return;
			}

			Match windowMatch = OtherFilterWindowPattern().Match(
				line
			);
			if ( !windowMatch.Success ) {
				throw new FormatException(
					"the procps Other Filter window entry is malformed"
				);
			}
			int windowIndex = ParseInteger(
				windowMatch.Groups[ "window" ].Value,
				"Other Filter window"
			);
			int count = ParseInteger(
				windowMatch.Groups[ "count" ].Value,
				"Other Filter count"
			);
			if (
				windowIndex is < 0
					or >= TopRuntimeState.WindowCount
				|| 0 > count
			) {
				throw new FormatException(
					"the procps Other Filter window entry is out of range"
				);
			}

			for ( int filterIndex = 0; filterIndex < count; filterIndex++ ) {
				if ( lineIndex >= lines.Count ) {
					throw new FormatException(
						"the procps Other Filter block ends unexpectedly"
					);
				}
				ParseOtherFilter(
					lines[
						lineIndex++
					],
					windows[
						windowIndex
					].State
				);
			}
		}

		throw new FormatException(
			"the procps Other Filter block has no end delimiter"
		);
	}

	private static void ParseOtherFilter(
		string line,
		TopWindowState window
	) {
		ArgumentNullException.ThrowIfNull( line );
		ArgumentNullException.ThrowIfNull( window );

		Match match = OtherFilterPattern().Match(
			line
		);
		if ( !match.Success ) {
			throw new FormatException(
				"a procps Other Filter entry is malformed"
			);
		}
		int type = ParseInteger(
			match.Groups[ "type" ].Value,
			"Other Filter type"
		);
		bool caseSensitive;
		if ( 'O' == type ) {
			caseSensitive = true;
		} else if ( 'o' == type ) {
			caseSensitive = false;
		} else {
			return;
		}

		string rawText = match.Groups[
			"filter"
		].Value;
		if (
			!FilterTargetsSupportedField(
				rawText
			)
		) {
			return;
		}

		var parsingState = new TopRuntimeState {
			NumericLeftJustified = window.NumericLeftJustified,
			CharacterRightJustified = window.CharacterRightJustified
		};
		parsingState.OtherFilters.AddRange(
			window.OtherFilters
		);
		if (
			!TopOtherFilterParser.TryParse(
				rawText,
				caseSensitive,
				parsingState,
				out TopOtherFilter? filter,
				out string? error
			)
		) {
			throw new FormatException(
				$"procps Other Filter '{rawText}' is invalid: {error}"
			);
		}
		window.OtherFilters.Add(
			filter!
		);
	}

	private static bool FilterTargetsSupportedField(
		string rawText
	) {
		ArgumentNullException.ThrowIfNull( rawText );

		ReadOnlySpan<char> value = rawText.AsSpan().Trim();
		if (
			!value.IsEmpty
			&& '!' == value[ 0 ]
		) {
			value = value[
				1..
			].TrimStart();
		}

		int operatorIndex = -1;
		for ( int index = 0; index < value.Length; index++ ) {
			if (
				value[ index ] is '=' or '<' or '>'
			) {
				operatorIndex = index;
				break;
			}
		}
		if ( 0 >= operatorIndex ) {
			return false;
		}

		string fieldName = value[
			..operatorIndex
		].Trim().ToString();
		return TopFieldCatalog.TryParseHeaderName(
			fieldName,
			out _
		);
	}

	private static Dictionary<string, string> ParseAssignments(
		string line
	) {
		ArgumentNullException.ThrowIfNull( line );

		var result = new Dictionary<string, string>(
			StringComparer.Ordinal
		);
		foreach (
			string part in line.Split(
				',',
				StringSplitOptions.RemoveEmptyEntries
					| StringSplitOptions.TrimEntries
			)
		) {
			int equalsIndex = part.IndexOf(
				'='
			);
			if (
				0 >= equalsIndex
				|| equalsIndex == part.Length - 1
			) {
				continue;
			}
			result[
				part[
					..equalsIndex
				].Trim()
			] = part[
				( equalsIndex + 1 )..
			].Trim();
		}
		return result;
	}

	private static int RequiredInteger(
		IReadOnlyDictionary<string, string> values,
		string name
	) {
		ArgumentNullException.ThrowIfNull( values );
		ArgumentException.ThrowIfNullOrWhiteSpace( name );

		if (
			!values.TryGetValue(
				name,
				out string? text
			)
			|| text is null
		) {
			throw new FormatException(
				$"the procps configuration is missing '{name}'"
			);
		}
		return ParseInteger(
			text,
			name
		);
	}

	private static int ParseInteger(
		string text,
		string name
	) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentException.ThrowIfNullOrWhiteSpace( name );

		if (
			!int.TryParse(
				text,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out int result
			)
		) {
			throw new FormatException(
				$"the procps value '{name}' is not an integer"
			);
		}
		return result;
	}

	private static bool ParseBooleanInteger(
		string text,
		string name
	) {
		int value = ParseInteger(
			text,
			name
		);
		if ( value is < 0 or > 1 ) {
			throw new FormatException(
				$"the procps value '{name}' must be zero or one"
			);
		}
		return 0 != value;
	}

	private static bool ParseOptionalBooleanInteger(
		IReadOnlyDictionary<string, string> values,
		string name
	) {
		ArgumentNullException.ThrowIfNull( values );
		ArgumentException.ThrowIfNullOrWhiteSpace( name );

		if (
			!values.TryGetValue(
				name,
				out string? text
			)
			|| text is null
		) {
			return false;
		}
		int value = ParseInteger(
			text,
			name
		);
		return 1 == value;
	}

	private static TimeSpan ParseProcpsDelay(
		string wholeText,
		string fractionalText
	) {
		int whole = ParseInteger(
			wholeText,
			"Delay_time"
		);
		int fractional = ParseInteger(
			fractionalText,
			"Delay_time fraction"
		);
		if (
			0 > whole
			|| 0 > fractional
		) {
			throw new FormatException(
				"the procps delay must be nonnegative"
			);
		}

		double seconds = whole + fractional / 1000.0;
		try {
			return TimeSpan.FromSeconds(
				seconds
			);
		} catch ( OverflowException exception ) {
			throw new FormatException(
				"the procps delay is too large",
				exception
			);
		}
	}

	private static TopMemoryScale ParseMemoryScale(
		IReadOnlyDictionary<string, string> values,
		string name,
		bool allowExbibytes
	) {
		ArgumentNullException.ThrowIfNull( values );
		ArgumentException.ThrowIfNullOrWhiteSpace( name );

		if (
			!values.TryGetValue(
				name,
				out string? text
			)
			|| text is null
		) {
			return TopMemoryScale.Kibibytes;
		}
		int value = ParseInteger(
			text,
			name
		);
		int maximum = ( allowExbibytes )
			? (int)TopMemoryScale.Exbibytes
			: (int)TopMemoryScale.Pebibytes
		;
		if (
			value is < 0
				|| value > maximum
		) {
			return TopMemoryScale.Kibibytes;
		}
		return (TopMemoryScale)value;
	}

	private static TopFieldId NativeSortField(
		int nativeField
	) {
		return (
			TryMapNativeField(
				nativeField,
				out TopFieldId field
			)
		)
			? field
			: TopFieldId.Cpu
		;
	}

	private static bool TryMapNativeField(
		int nativeField,
		out TopFieldId field
	) {
		switch ( nativeField ) {
			case 0:
				field = TopFieldId.Pid;
				return true;
			case 3:
				field = TopFieldId.User;
				return true;
			case 14:
				field = TopFieldId.Priority;
				return true;
			case 15:
				field = TopFieldId.Nice;
				return true;
			case 18:
				field = TopFieldId.Cpu;
				return true;
			case 20:
				field = TopFieldId.Time;
				return true;
			case 21:
				field = TopFieldId.Memory;
				return true;
			case 22:
				field = TopFieldId.VirtualMemory;
				return true;
			case 24:
				field = TopFieldId.ResidentMemory;
				return true;
			case 27:
				field = TopFieldId.SharedMemory;
				return true;
			case 31:
				field = TopFieldId.State;
				return true;
			case 32:
				field = TopFieldId.Command;
				return true;
			default:
				field = default;
				return false;
		}
	}

	private static void ValidateWindowName(
		string name,
		int windowIndex
	) {
		ArgumentNullException.ThrowIfNull( name );

		int byteCount = Encoding.UTF8.GetByteCount(
			name
		);
		if ( byteCount is < 1 or > 3 ) {
			throw new FormatException(
				$"procps window {windowIndex + 1} name must occupy 1 through 3 UTF-8 bytes"
			);
		}
	}

	private sealed record ParsedWindow(
		TopWindowState State,
		int WinFlags
	);
}
