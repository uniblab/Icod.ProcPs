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

/// <summary>Writes current procps-ng top configuration mirrors owned by Icod.</summary>
internal static partial class TopProcpsConfigurationCodec {
	internal const string IcodOwnershipHeader = "Icod.ProcPs.Top Config File (Linux processes with windows)";
	private const char NativeCurrentVersion = 'n';
	private const int NativeCurrentFieldCount = 81;
	private const int ShowForestFlag = 0x000002;
	private const int ViewMemory = 0x001000;
	private const int ViewStates = 0x002000;
	private const int ViewLoadAverage = 0x004000;
	private const int ViewCpuSummary = 0x008000;

	internal static string Decode(
		ReadOnlySpan<byte> bytes
	) {
		string latin1 = Encoding.Latin1.GetString(
			bytes
		);
		char version = ReadConfigurationVersion(
			latin1
		);
		if ( NativeTransformedVersion > version ) {
			return latin1;
		}
		return Encoding.UTF8.GetString(
			bytes
		);
	}

	internal static bool IsIcodOwned(
		string? firstLine
	) {
		return string.Equals(
			firstLine,
			IcodOwnershipHeader,
			StringComparison.Ordinal
		);
	}

	internal static string Serialize(
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( state );

		state.SynchronizeCurrentWindow();
		(long delayWhole, int delayFraction) = EncodeDelay(
			state.Delay
		);
		var builder = new StringBuilder();
		builder.Append(
			IcodOwnershipHeader
		);
		builder.Append(
			'\n'
		);
		builder.AppendFormat(
			CultureInfo.InvariantCulture,
			"Id:{0}, Mode_altscr={1}, Mode_irixps={2}, Delay_time={3}.{4:000}, Curwin={5}\n",
			NativeCurrentVersion,
			BooleanInteger( state.AlternateDisplayMode ),
			BooleanInteger( state.IrixMode ),
			delayWhole,
			delayFraction,
			state.CurrentWindowIndex
		);

		for ( int index = 0; index < TopRuntimeState.WindowCount; index++ ) {
			AppendWindow(
				builder,
				state.Windows[ index ],
				index,
				state.BoldEnabled
			);
		}

		builder.AppendFormat(
			CultureInfo.InvariantCulture,
			"Fixed_widest=0, Summ_mscale={0}, Task_mscale={1}, Zero_suppress={2}, Tics_scaled=0\n",
			(int)state.SummaryScale,
			NativeTaskScale( state.TaskScale ),
			BooleanInteger( state.SuppressZeros )
		);
		builder.Append(
			'\n'
		);
		AppendOtherFilters(
			builder,
			state.Windows
		);
		return builder.ToString();
	}

	private static void AppendWindow(
		StringBuilder builder,
		TopWindowState window,
		int windowIndex,
		bool boldEnabled
	) {
		ArgumentNullException.ThrowIfNull( builder );
		ArgumentNullException.ThrowIfNull( window );
		if ( windowIndex is < 0 or >= TopRuntimeState.WindowCount ) {
			throw new ArgumentOutOfRangeException(
				nameof( windowIndex )
			);
		}
		if ( 0 > window.MaximumTasks ) {
			throw new InvalidOperationException(
				$"window {windowIndex + 1} has a negative maximum task count"
			);
		}
		ValidateWindowName(
			window.Name,
			windowIndex
		);

		builder.Append(
			window.Name
		);
		builder.Append(
			"\tfieldscur="
		);
		foreach ( int encodedField in BuildNativeFieldOrder( window ) ) {
			builder.Append(
				' '
			);
			builder.Append(
				encodedField.ToString(
					CultureInfo.InvariantCulture
				)
			);
		}
		builder.Append(
			'\n'
		);

		builder.AppendFormat(
			CultureInfo.InvariantCulture,
			"\twinflags={0}, sortindx={1}, maxtasks={2}, graph_cpus={3}, graph_mems={4}, double_up=0, combine_cpus=0\n",
			BuildWindowFlags(
				window,
				boldEnabled
			),
			NativeField(
				window.SortField
			),
			window.MaximumTasks,
			NativeGraphMode( window.CpuSummaryGraphMode ),
			NativeGraphMode( window.MemorySummaryGraphMode )
		);

		TopColorPalette colors = window.Colors;
		builder.AppendFormat(
			CultureInfo.InvariantCulture,
			"\tsummclr={0}, msgsclr={1}, headclr={2}, taskclr={3}, task_xy={4}, core_types=0, cores_vs_cpus=0\n",
			colors.Summary,
			colors.Messages,
			colors.Header,
			colors.Tasks,
			colors.TaskAccent
		);
	}

	private static IReadOnlyList<int> BuildNativeFieldOrder(
		TopWindowState window
	) {
		ArgumentNullException.ThrowIfNull( window );

		var result = new List<int>(
			NativeCurrentFieldCount
		);
		var nativeSeen = new HashSet<int>();
		foreach ( TopFieldId field in window.FieldOrder ) {
			if (
				TryMapFieldToNative(
					field,
					out int nativeField
				)
				&& nativeSeen.Add( nativeField )
			) {
				result.Add(
					EncodeNativeField(
						nativeField,
						window.VisibleFields.Contains( field )
					)
				);
			}
		}

		for ( int nativeField = 0; nativeField < NativeCurrentFieldCount; nativeField++ ) {
			if ( nativeSeen.Add( nativeField ) ) {
				result.Add(
					EncodeNativeField(
						nativeField,
						visible: false
					)
				);
			}
		}
		if ( NativeCurrentFieldCount != result.Count ) {
			throw new InvalidOperationException(
				"unable to build the complete native procps top field table"
			);
		}
		return result;
	}

	private static int EncodeNativeField(
		int nativeField,
		bool visible
	) {
		if ( nativeField is < 0 or >= NativeCurrentFieldCount ) {
			throw new ArgumentOutOfRangeException(
				nameof( nativeField )
			);
		}
		int visibility = ( visible )
			? NativeFieldVisibleMask
			: 0
		;
		return (
			( nativeField + NativeFieldOffset )
			<< 1
		) | visibility;
	}

	private static int BuildWindowFlags(
		TopWindowState window,
		bool boldEnabled
	) {
		ArgumentNullException.ThrowIfNull( window );

		int result = ViewLoadAverage;
		if ( window.CpuSummaryVisible ) {
			result |= ViewStates;
		}
		if ( window.MemorySummaryVisible ) {
			result |= ViewMemory;
		}
		if ( !boldEnabled ) {
			result |= ViewNoBold;
		}
		if ( window.ColorsEnabled ) {
			result |= ShowColorsFlag;
		}
		if ( window.TaskDisplayVisible ) {
			result |= ShowTaskOn;
		}
		if ( !window.HideIdle ) {
			result |= ShowIdleProcesses;
		}
		if ( window.ShowCommandLine ) {
			result |= ShowCommandLineFlag;
		}
		if ( window.HighlightRunning ) {
			result |= ShowHighlightRowsFlag;
		}
		if ( window.HighlightSortColumn ) {
			result |= ShowHighlightColumnsFlag;
		}
		if ( window.HighlightBold ) {
			result |= ShowHighlightBoldFlag;
		}
		if ( !window.NumericLeftJustified ) {
			result |= ShowJustifyNumericRightFlag;
		}
		if ( window.CharacterRightJustified ) {
			result |= ShowJustifyStringsRightFlag;
		}
		if ( window.SortHighToLow ) {
			result |= QsrtNormal;
		}
		if ( window.Forest ) {
			result |= ShowForestFlag;
		}
		if ( window.SingleCpuSummary ) {
			result |= ViewCpuSummary;
		}
		return result;
	}

	private static int NativeGraphMode(
		TopSummaryGraphMode mode
	) {
		return mode switch {
			TopSummaryGraphMode.Detailed => 0,
			TopSummaryGraphMode.Bar => 1,
			TopSummaryGraphMode.Block => 2,
			_ => throw new InvalidOperationException(
				$"unsupported top summary graph mode '{mode}'"
			)
		};
	}

	private static int NativeField(
		TopFieldId field
	) {
		if (
			!TryMapFieldToNative(
				field,
				out int nativeField
			)
		) {
			throw new InvalidOperationException(
				$"top field '{field}' has no native procps field mapping"
			);
		}
		return nativeField;
	}

	private static bool TryMapFieldToNative(
		TopFieldId field,
		out int nativeField
	) {
		switch ( field ) {
			case TopFieldId.Pid:
				nativeField = 0;
				return true;
			case TopFieldId.User:
				nativeField = 3;
				return true;
			case TopFieldId.Priority:
				nativeField = 14;
				return true;
			case TopFieldId.Nice:
				nativeField = 15;
				return true;
			case TopFieldId.Cpu:
				nativeField = 18;
				return true;
			case TopFieldId.Time:
				nativeField = 20;
				return true;
			case TopFieldId.Memory:
				nativeField = 21;
				return true;
			case TopFieldId.VirtualMemory:
				nativeField = 22;
				return true;
			case TopFieldId.ResidentMemory:
				nativeField = 24;
				return true;
			case TopFieldId.SharedMemory:
				nativeField = 27;
				return true;
			case TopFieldId.State:
				nativeField = 31;
				return true;
			case TopFieldId.Command:
				nativeField = 32;
				return true;
			default:
				nativeField = default;
				return false;
		}
	}

	private static void AppendOtherFilters(
		StringBuilder builder,
		IReadOnlyList<TopWindowState> windows
	) {
		ArgumentNullException.ThrowIfNull( builder );
		ArgumentNullException.ThrowIfNull( windows );

		builder.Append(
			"begin: saved other filter data -------------------\n"
		);
		for ( int windowIndex = 0; windowIndex < windows.Count; windowIndex++ ) {
			TopWindowState window = windows[
				windowIndex
			];
			builder.AppendFormat(
				CultureInfo.InvariantCulture,
				"window #{0}, osel_tot={1}\n",
				windowIndex,
				window.OtherFilters.Count
			);
			foreach ( TopOtherFilter filter in window.OtherFilters ) {
				if (
					filter.RawText.Contains(
						'\r'
					)
					|| filter.RawText.Contains(
						'\n'
					)
				) {
					throw new InvalidOperationException(
						"native procps Other Filters cannot contain line breaks"
					);
				}
				builder.AppendFormat(
					CultureInfo.InvariantCulture,
					"\ttype={0},\tfilter={1}\n",
					(int)(
						filter.CaseSensitive
							? 'O'
							: 'o'
					),
					filter.RawText
				);
			}
		}
		builder.Append(
			"end  : saved other filter data -------------------\n"
		);
	}

	private static int NativeTaskScale(
		TopMemoryScale scale
	) {
		return ( TopMemoryScale.Exbibytes == scale )
			? (int)TopMemoryScale.Pebibytes
			: (int)scale
		;
	}

	private static int BooleanInteger(
		bool value
	) {
		return ( value )
			? 1
			: 0
		;
	}

	private static (long Whole, int Fraction) EncodeDelay(
		TimeSpan delay
	) {
		if ( TimeSpan.Zero > delay ) {
			throw new InvalidOperationException(
				"the native procps delay cannot be negative"
			);
		}

		long milliseconds = delay.Ticks
			/ TimeSpan.TicksPerMillisecond;
		long remainder = delay.Ticks
			% TimeSpan.TicksPerMillisecond;
		if (
			TimeSpan.TicksPerMillisecond / 2
			<= remainder
		) {
			milliseconds++;
		}
		long whole = milliseconds / 1000;
		if ( int.MaxValue < whole ) {
			throw new InvalidOperationException(
				"the delay is too large for the native procps rcfile format"
			);
		}
		return (
			whole,
			(int)( milliseconds % 1000 )
		);
	}

	private static char ReadConfigurationVersion(
		string text
	) {
		ArgumentNullException.ThrowIfNull( text );

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
		var header = HeaderPattern().Match(
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
		return version;
	}

}
