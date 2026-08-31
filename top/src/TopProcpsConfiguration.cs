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

/// <summary>Reads native procps-ng top configuration formats from 3.2.8 through current 4.x.</summary>
internal static partial class TopProcpsConfigurationCodec {
	private const char NativeTransformedVersion = 'k';
	private const int NativeFieldOffset = 37;
	private const int NativeFieldVisibleMask = 1;
	private const int LegacyFieldVisibleMask = 0x80;
	private const int LegacyFieldValueMask = 0x7f;
	private const int LegacyFieldCount = 86;
	private const int LegacyOldFieldLimit = 28;
	private const string LegacyAddedThroughH = @"\]^_`abcdefghij";
	private const string LegacyAddedThroughJ = "klmnopqrstuvwxyz";
	private const string LegacyConversionFields = "%&*'(-0346789:;<=>?@ACDEFGML)+,./125BHIJKNOPQRSTUVWXYZ["
		+ LegacyAddedThroughH
		+ LegacyAddedThroughJ;

	private const int QsrtNormal = 0x000004;
	private const int ViewNoBold = 0x000008;
	private const int ShowTaskOn = 0x000010;
	private const int ShowIdleProcesses = 0x000020;
	private const int ShowCommandLineFlag = 0x000080;
	private const int ShowHighlightRowsFlag = 0x000100;
	private const int ShowHighlightColumnsFlag = 0x000200;
	private const int ShowHighlightBoldFlag = 0x000400;
	private const int ShowColorsFlag = 0x000800;
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
		ValidateVersion(
			version
		);

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
					windowIndex,
					version
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

	private static void ValidateVersion(
		char version
	) {
		if (
			version is < 'a' or > 'n'
			|| version is 'b' or 'c' or 'd' or 'e'
		) {
			throw new FormatException(
				$"procps rc version '{version}' is not supported"
			);
		}
	}

	private static ParsedWindow ParseWindow(
		IReadOnlyList<string> lines,
		ref int lineIndex,
		int windowIndex,
		char version
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
		TopSummaryGraphMode cpuSummaryGraphMode = ParseGraphMode(
			settings,
			"graph_cpus",
			windowIndex
		);
		TopSummaryGraphMode memorySummaryGraphMode = ParseGraphMode(
			settings,
			"graph_mems",
			windowIndex
		);
		IReadOnlyList<int>? legacyFields = null;
		if ( NativeTransformedVersion > version ) {
			LegacyWindowConfiguration legacy = ConvertLegacyWindowConfiguration(
				version,
				fieldsText.ToString(),
				winFlags,
				sortIndex,
				windowIndex
			);
			legacyFields = legacy.Fields;
			winFlags = legacy.WinFlags;
			sortIndex = legacy.SortIndex;
		}
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
			ColorsEnabled = 0 != (
				winFlags
				& ShowColorsFlag
			),
			Colors = ParseColorPalette(
				settings,
				windowIndex
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
				& ShowForestFlag
			),
			SingleCpuSummary = 0 != (
				winFlags
				& ViewCpuSummary
			),
			CpuSummaryVisible = 0 != (
				winFlags
				& ViewStates
			),
			CpuSummaryGraphMode = cpuSummaryGraphMode,
			MemorySummaryVisible = 0 != (
				winFlags
				& ViewMemory
			),
			MemorySummaryGraphMode = memorySummaryGraphMode
		};

		if ( legacyFields is null ) {
			ApplyFieldConfiguration(
				fieldsText.ToString(),
				state,
				windowIndex
			);
		} else {
			ApplyEncodedFieldConfiguration(
				legacyFields,
				state,
				windowIndex
			);
		}
		return new ParsedWindow(
			state,
			winFlags
		);
	}

	private static TopSummaryGraphMode ParseGraphMode(
		IReadOnlyDictionary<string, string> settings,
		string name,
		int windowIndex
	) {
		ArgumentNullException.ThrowIfNull( settings );
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		if ( windowIndex is < 0 or >= TopRuntimeState.WindowCount ) {
			throw new ArgumentOutOfRangeException(
				nameof( windowIndex )
			);
		}

		if ( !settings.TryGetValue( name, out string? text ) ) {
			return TopSummaryGraphMode.Detailed;
		}
		int value = ParseInteger(
			text,
			name
		);
		return value switch {
			0 => TopSummaryGraphMode.Detailed,
			1 => TopSummaryGraphMode.Bar,
			2 => TopSummaryGraphMode.Block,
			_ => throw new FormatException(
				$"procps window {windowIndex + 1} has invalid {name} value {value}"
			)
		};
	}

	private static LegacyWindowConfiguration ConvertLegacyWindowConfiguration(
		char version,
		string text,
		int winFlags,
		int sortIndex,
		int windowIndex
	) {
		ArgumentNullException.ThrowIfNull( text );

		byte[] fields = ReadLegacyFields(
			text,
			windowIndex
		);
		if ( 'a' == version ) {
			return ConvertProcps328WindowConfiguration(
				fields,
				winFlags,
				sortIndex,
				windowIndex
			);
		}

		if ( 'f' == version ) {
			winFlags |= ShowJustifyNumericRightFlag;
		}
		if ( version is 'f' or 'g' ) {
			fields = AppendLegacyFields(
				fields,
				LegacyAddedThroughH
			);
		}
		if ( version is 'f' or 'g' or 'h' or 'i' ) {
			fields = AppendLegacyFields(
				fields,
				LegacyAddedThroughJ
			);
		}
		if ( LegacyFieldCount > fields.Length ) {
			throw new FormatException(
				$"procps window {windowIndex + 1} has an incomplete legacy fieldscur list"
			);
		}
		if ( LegacyFieldCount < fields.Length ) {
			fields = fields[
				..LegacyFieldCount
			];
		}
		if ( sortIndex is < 0 or >= LegacyFieldCount ) {
			throw new FormatException(
				$"procps window {windowIndex + 1} has invalid legacy sort index {sortIndex}"
			);
		}

		return new LegacyWindowConfiguration(
			EncodeLegacyFields(
				fields,
				windowIndex
			),
			winFlags,
			sortIndex
		);
	}

	private static LegacyWindowConfiguration ConvertProcps328WindowConfiguration(
		byte[] fields,
		int winFlags,
		int sortIndex,
		int windowIndex
	) {
		ArgumentNullException.ThrowIfNull( fields );

		if ( LegacyOldFieldLimit <= fields.Length ) {
			throw new FormatException(
				$"procps window {windowIndex + 1} has too many release 3.2.8 fields"
			);
		}
		if ( sortIndex is < 0 or >= 26 ) {
			throw new FormatException(
				$"procps window {windowIndex + 1} has invalid release 3.2.8 sort index {sortIndex}"
			);
		}

		byte[] sourceFields = Encoding.ASCII.GetBytes(
			LegacyConversionFields
		);
		byte[] conversion = [
			.. sourceFields
		];
		byte[] normalized = [
			.. fields
		];
		int suseOomScorePosition = -1;
		int suseOomAdjustmentPosition = -1;
		for ( int index = 0; index < normalized.Length; index++ ) {
			if ( '[' == normalized[ index ] ) {
				normalized[ index ] = (byte)'{';
				suseOomScorePosition = index;
			} else if ( '\\' == normalized[ index ] ) {
				normalized[ index ] = (byte)'|';
				suseOomAdjustmentPosition = index;
			}

			byte value = normalized[ index ];
			byte lower = AsciiLower(
				value
			);
			int legacyIndex = lower - 'a';
			if ( legacyIndex is < 0 or >= LegacyOldFieldLimit ) {
				throw new FormatException(
					$"procps window {windowIndex + 1} has invalid release 3.2.8 field byte 0x{value:X2}"
				);
			}

			byte mapped = sourceFields[
				legacyIndex
			];
			if ( IsAsciiUpper( value ) ) {
				mapped |= LegacyFieldVisibleMask;
			}
			conversion[
				index
			] = mapped;
		}
		if ( 0 <= suseOomScorePosition ) {
			conversion[
				suseOomScorePosition
			] |= LegacyFieldVisibleMask;
		}
		if ( 0 <= suseOomAdjustmentPosition ) {
			conversion[
				suseOomAdjustmentPosition
			] |= LegacyFieldVisibleMask;
		}

		byte sortName = (byte)(
			'a'
			+ sortIndex
		);
		int sortPosition = -1;
		for ( int index = 0; index < normalized.Length; index++ ) {
			if ( sortName == AsciiLower( normalized[ index ] ) ) {
				sortPosition = index;
				break;
			}
		}
		int convertedSortIndex = 0;
		if ( 0 <= sortPosition ) {
			convertedSortIndex = (
				conversion[
					sortPosition
				]
				& LegacyFieldValueMask
			) - NativeFieldOffset;
		}

		int convertedFlags = ConvertProcps328WindowFlags(
			winFlags
		);
		convertedFlags |= ShowJustifyNumericRightFlag;
		return new LegacyWindowConfiguration(
			EncodeLegacyFields(
				conversion,
				windowIndex
			),
			convertedFlags,
			convertedSortIndex
		);
	}

	private static int ConvertProcps328WindowFlags(
		int flags
	) {
		const int oldViewNoBold = 0x000001;
		const int oldShowTaskOn = 0x000008;
		const int oldQsrtNormal = 0x000010;
		const int oldShowHighlightColumns = 0x000200;
		const int oldShowThreads = 0x010000;

		int remaining = flags;
		int converted = 0;
		MoveLegacyFlag(
			ref remaining,
			ref converted,
			oldViewNoBold,
			ViewNoBold
		);
		MoveLegacyFlag(
			ref remaining,
			ref converted,
			oldShowTaskOn,
			ShowTaskOn
		);
		MoveLegacyFlag(
			ref remaining,
			ref converted,
			oldQsrtNormal,
			QsrtNormal
		);
		MoveLegacyFlag(
			ref remaining,
			ref converted,
			oldShowHighlightColumns,
			ShowHighlightColumnsFlag
		);
		remaining &= ~oldShowThreads;
		return converted | remaining;
	}

	private static void MoveLegacyFlag(
		ref int source,
		ref int destination,
		int oldFlag,
		int newFlag
	) {
		if ( 0 == ( source & oldFlag ) ) {
			return;
		}

		source &= ~oldFlag;
		destination |= newFlag;
	}

	private static byte[] ReadLegacyFields(
		string text,
		int windowIndex
	) {
		ArgumentNullException.ThrowIfNull( text );

		string value = text.Trim();
		if ( 0 == value.Length ) {
			throw new FormatException(
				$"procps window {windowIndex + 1} has an empty legacy fieldscur list"
			);
		}
		if ( LegacyFieldCount < value.Length ) {
			throw new FormatException(
				$"procps window {windowIndex + 1} has too many legacy fields"
			);
		}

		var result = new byte[
			value.Length
		];
		for ( int index = 0; index < value.Length; index++ ) {
			char character = value[
				index
			];
			if (
				byte.MaxValue < character
				|| char.IsWhiteSpace( character )
			) {
				throw new FormatException(
					$"procps window {windowIndex + 1} has an invalid legacy fieldscur character"
				);
			}
			result[
				index
			] = (byte)character;
		}
		return result;
	}

	private static byte[] AppendLegacyFields(
		IReadOnlyCollection<byte> fields,
		string suffix
	) {
		ArgumentNullException.ThrowIfNull( fields );
		ArgumentNullException.ThrowIfNull( suffix );

		byte[] suffixBytes = Encoding.ASCII.GetBytes(
			suffix
		);
		var result = new byte[
			fields.Count
			+ suffixBytes.Length
		];
		int index = 0;
		foreach ( byte field in fields ) {
			result[
				index++
			] = field;
		}
		Array.Copy(
			suffixBytes,
			0,
			result,
			index,
			suffixBytes.Length
		);
		return result;
	}

	private static IReadOnlyList<int> EncodeLegacyFields(
		IReadOnlyList<byte> fields,
		int windowIndex
	) {
		ArgumentNullException.ThrowIfNull( fields );

		var result = new int[
			fields.Count
		];
		var seen = new HashSet<int>();
		for ( int index = 0; index < fields.Count; index++ ) {
			byte field = fields[
				index
			];
			int nativeField = (
				field
				& LegacyFieldValueMask
			) - NativeFieldOffset;
			if (
				nativeField is < 0 or >= LegacyFieldCount
				|| !seen.Add( nativeField )
			) {
				throw new FormatException(
					$"procps window {windowIndex + 1} has an invalid or duplicate legacy fieldscur value 0x{field:X2}"
				);
			}

			int visibility = ( 0 != (
				field
				& LegacyFieldVisibleMask
			) )
				? NativeFieldVisibleMask
				: 0
			;
			result[
				index
			] = (
				( nativeField + NativeFieldOffset )
				<< 1
			) | visibility;
		}
		return result;
	}

	private static bool IsAsciiUpper(
		byte value
	) {
		return value is >= (byte)'A' and <= (byte)'Z';
	}

	private static byte AsciiLower(
		byte value
	) {
		return ( IsAsciiUpper( value ) )
			? (byte)(
				value
				+ ( 'a' - 'A' )
			)
			: value
		;
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

		var encodedFields = new List<int>(
			parts.Length
		);
		foreach ( string part in parts ) {
			encodedFields.Add(
				ParseInteger(
					part,
					"fieldscur"
				)
			);
		}
		ApplyEncodedFieldConfiguration(
			encodedFields,
			state,
			windowIndex
		);
	}

	private static void ApplyEncodedFieldConfiguration(
		IReadOnlyList<int> encodedFields,
		TopWindowState state,
		int windowIndex
	) {
		ArgumentNullException.ThrowIfNull( encodedFields );
		ArgumentNullException.ThrowIfNull( state );

		if ( 0 == encodedFields.Count ) {
			throw new FormatException(
				$"procps window {windowIndex + 1} has an empty fieldscur list"
			);
		}

		var nativeSeen = new HashSet<int>();
		var supportedSeen = new HashSet<TopFieldId>();
		var order = new List<TopFieldId>();
		var visible = new HashSet<TopFieldId>();
		foreach ( int encoded in encodedFields ) {
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

	private static TopColorPalette ParseColorPalette(
		IReadOnlyDictionary<string, string> values,
		int windowIndex
	) {
		ArgumentNullException.ThrowIfNull( values );
		if ( windowIndex is < 0 or >= TopRuntimeState.WindowCount ) {
			throw new ArgumentOutOfRangeException(
				nameof( windowIndex )
			);
		}

		TopColorPalette fallback = TopColorPalette.ForWindow(
			windowIndex
		);
		return new TopColorPalette(
			ParseOptionalColor( values, "summclr", fallback.Summary ),
			ParseOptionalColor( values, "msgsclr", fallback.Messages ),
			ParseOptionalColor( values, "headclr", fallback.Header ),
			ParseOptionalColor( values, "taskclr", fallback.Tasks ),
			ParseOptionalColor( values, "task_xy", fallback.TaskAccent )
		);
	}

	private static int ParseOptionalColor(
		IReadOnlyDictionary<string, string> values,
		string name,
		int fallback
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
			return fallback;
		}
		int value = ParseInteger(
			text,
			name
		);
		if ( value is < -1 or > 255 ) {
			throw new FormatException(
				$"the procps color '{name}' must be -1 through 255"
			);
		}
		return value;
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
		if ( 0 > whole ) {
			throw new FormatException(
				"the procps delay must be nonnegative"
			);
		}

		if (
			!decimal.TryParse(
				$"0.{fractionalText}",
				NumberStyles.AllowDecimalPoint,
				CultureInfo.InvariantCulture,
				out decimal fractional
			)
		) {
			throw new FormatException(
				"the procps delay fraction is invalid"
			);
		}

		long fractionalTicks = decimal.ToInt64(
			decimal.Round(
				fractional * TimeSpan.TicksPerSecond,
				0,
				MidpointRounding.AwayFromZero
			)
		);
		return TimeSpan.FromTicks(
			( (long)whole * TimeSpan.TicksPerSecond )
			+ fractionalTicks
		);
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

	private sealed record LegacyWindowConfiguration(
		IReadOnlyList<int> Fields,
		int WinFlags,
		int SortIndex
	);
}
