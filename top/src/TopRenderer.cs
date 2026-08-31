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
using Icod.Processes;
using Icod.ProcPs.Shared;

/// <summary>Builds the terminal-independent screen model used by top.</summary>
internal static class TopRenderer {
	private const int TableHeaderRows = 1;

	private readonly record struct TopFormattedTaskLine(
		string Text,
		int SortFieldStart,
		int SortFieldLength
	);

	private static readonly string[] HelpLines = [
		"top help -- core interactive commands",
		" q              quit",
		" Enter/Space    refresh now",
		" 0              toggle zero suppression",
		" n or #         set maximum displayed tasks",
		" P/M/N/T        sort by CPU, memory, PID, or time",
		" R              reverse/normal sort direction",
		" A              toggle alternate-display mode",
		" a / w          next / previous window",
		" g / G          choose / rename current window",
		" - / _          show/hide current / all task windows",
		" +              reset and show all windows",
		" B/b/x/y        emphasis and highlighting",
		" z / Z          toggle colors / map window colors",
		" l              toggle load-average / uptime line",
		" t / m          cycle CPU / memory summary presentation",
		" C              toggle scroll-coordinate message",
		" < / >          move visible sort field left / right",
		" J / j          justify numeric / character columns",
		" f              manage task fields",
		" c              toggle command name / command line",
		" H              toggle thread display",
		" i              toggle idle-task suppression",
		" V              toggle process forest",
		" F / v          focus parent / hide-show children",
		" X              change extra fixed-width columns",
		" Y              inspect configured file / pipe output",
		" I              toggle Irix/Solaris CPU normalization",
		" E / e          cycle summary / task memory scale",
		" d or s         change refresh delay",
		" u / U          filter by effective / any observed user",
		" O / o          add case-sensitive / insensitive Other Filter",
		" L / &          locate string / locate next",
		" k              signal a process",
		" r              change a process nice value",
		" W              write personal configuration",
		" ^A/^G/^K/^L/^N/^P/^U  bottom information windows",
		" Tab/Shift+Tab  select bottom-window items",
		" arrows/PgUp    scroll task display",
		" Home/End       jump to first/last task",
		" =              clear display limits, filters, scrolling",
		" h or ?         close this help"
	];

	internal static TopRenderFrame RenderInteractive(
		TopSample sample,
		TopRuntimeState state,
		TopTerminalDimensions dimensions
	) {
		ArgumentNullException.ThrowIfNull( sample );
		ArgumentNullException.ThrowIfNull( state );
		if ( 1 > dimensions.Columns || 1 > dimensions.Rows ) {
			throw new ArgumentOutOfRangeException( nameof( dimensions ) );
		}

		if ( state.InspectSession is not null ) {
			return TopInspectRenderer.Render(
				state.InspectSession,
				dimensions,
				state.BoldEnabled
			);
		}
		if ( state.ColorManager is not null ) {
			return RenderColorManager(
				state,
				dimensions
			);
		}
		if ( state.ShowFieldManager ) {
			return RenderFieldManager(
				state,
				dimensions
			);
		}
		if ( state.ShowHelp ) {
			return RenderHelp( state, dimensions );
		}
		if ( state.AlternateDisplayMode ) {
			return TopBottomWindowRenderer.Apply(
				RenderAlternateInteractive(
					sample,
					state,
					dimensions
				),
				state.BottomWindow
			);
		}

		List<TopTaskRow> tasks = SelectAndOrderTasks( sample, state );
		int footerRows = (
			state.Prompt is null
			&& string.IsNullOrEmpty( state.Message )
			&& !ShouldShowScrollCoordinates( state )
		)
			? 0
			: 1
		;
		int availableTaskRows = AvailableTaskRows(
			state,
			dimensions,
			footerRows
		);
		int maxOffset = 0 < tasks.Count
			? tasks.Count - 1
			: 0;
		state.VerticalOffset = Math.Clamp( state.VerticalOffset, 0, maxOffset );
		state.HorizontalOffset = Math.Max( 0, state.HorizontalOffset );

		var lines = new List<TopRenderLine>( dimensions.Rows );
		foreach ( string line in BuildSummaryLines(
			sample,
			state,
			dimensions.Columns
		) ) {
			lines.Add( new TopRenderLine(
				SliceForDisplay( line, state.HorizontalOffset, dimensions.Columns ),
				TopLineStyle.Summary,
				ForegroundColor: ForegroundColor(
					state,
					state.Colors.Summary
				)
			) );
		}
		lines.Add( new TopRenderLine(
			SliceForDisplay( BuildHeader( state ), state.HorizontalOffset, dimensions.Columns ),
			TopLineStyle.Header,
			ForegroundColor: ForegroundColor(
				state,
				state.Colors.Header
			)
		) );

		int taskEndIndex = Math.Min(
			tasks.Count,
			state.VerticalOffset + availableTaskRows
		);
		for ( int index = state.VerticalOffset;
			index < taskEndIndex;
			index++ ) {
			TopTaskRow task = tasks[ index ];
			TopFormattedTaskLine formatted = FormatTaskLine(
				task,
				state,
				sample.ProcessorCount
			);
			string visibleLine = SliceForDisplay(
				formatted.Text,
				state.HorizontalOffset,
				dimensions.Columns
			);
			TopLineStyle lineStyle = TaskLineStyle(
				task,
				state
			);
			lines.Add( new TopRenderLine(
				visibleLine,
				lineStyle,
				SortColumnSpans(
					visibleLine,
					formatted,
					state,
					lineStyle
				),
				TaskForegroundColor(
					state,
					lineStyle
				)
			) );
		}

		while ( lines.Count < dimensions.Rows - footerRows ) {
			lines.Add( new TopRenderLine( string.Empty ) );
		}
		if ( 0 < footerRows ) {
			if ( state.Prompt is not null ) {
				lines.Add( new TopRenderLine(
					LimitRunes(
						$"{state.Prompt.Label}{state.Prompt.Buffer}",
						dimensions.Columns
					),
					TopLineStyle.Prompt,
					ForegroundColor: ForegroundColor(
						state,
						state.Colors.Messages
					)
				) );
			} else if ( !string.IsNullOrEmpty( state.Message ) ) {
				lines.Add( new TopRenderLine(
					LimitRunes( state.Message, dimensions.Columns ),
					TopLineStyle.Message,
					ForegroundColor: ForegroundColor(
						state,
						state.Colors.Messages
					)
				) );
			} else {
				lines.Add( new TopRenderLine(
					LimitRunes(
						BuildScrollCoordinatesMessage( sample, state ),
						dimensions.Columns
					),
					TopLineStyle.Message,
					ForegroundColor: ForegroundColor(
						state,
						state.Colors.Messages
					)
				) );
			}
		}
		return TopBottomWindowRenderer.Apply(
			new TopRenderFrame(
				lines,
				dimensions.Columns,
				dimensions.Rows,
				state.BoldEnabled
			),
			state.BottomWindow
		);
	}

	private static TopRenderFrame RenderAlternateInteractive(
		TopSample sample,
		TopRuntimeState state,
		TopTerminalDimensions dimensions
	) {
		ArgumentNullException.ThrowIfNull( sample );
		ArgumentNullException.ThrowIfNull( state );

		state.SynchronizeCurrentWindow();
		int currentWindowIndex = state.CurrentWindowIndex;
		int footerRows = (
			state.Prompt is null
			&& string.IsNullOrEmpty( state.Message )
			&& !ShouldShowScrollCoordinates( state )
		)
			? 0
			: 1
		;
		int visibleWindowCount = CountVisibleTaskWindows(
			state
		);
		var lines = new List<TopRenderLine>(
			dimensions.Rows
		);
		foreach ( string line in BuildSummaryLines(
			sample,
			state,
			dimensions.Columns
		) ) {
			lines.Add(
				new TopRenderLine(
					SliceForDisplay(
						line,
						state.HorizontalOffset,
						dimensions.Columns
					),
					TopLineStyle.Summary,
					ForegroundColor: ForegroundColor(
						state,
						state.Colors.Summary
					)
				)
			);
		}

		int summaryRows = GetSummaryRowCount(
			state
		);
		int windowAreaRows = dimensions.Rows - summaryRows - footerRows;
		if ( visibleWindowCount > windowAreaRows ) {
			lines.Add(
				new TopRenderLine(
					LimitRunes(
						$"alternate display requires at least {summaryRows + visibleWindowCount} terminal rows",
						dimensions.Columns
					),
					TopLineStyle.Message,
					ForegroundColor: ForegroundColor(
						state,
						state.Colors.Messages
					)
				)
			);
			while ( lines.Count < dimensions.Rows ) {
				lines.Add(
					new TopRenderLine( string.Empty )
				);
			}
			return new TopRenderFrame(
				lines,
				dimensions.Columns,
				dimensions.Rows,
				state.BoldEnabled
			);
		}

		int remainingTaskRows = Math.Max(
			0,
			windowAreaRows - visibleWindowCount
		);
		int remainingVisibleWindows = visibleWindowCount;

		for ( int windowIndex = 0; windowIndex < TopRuntimeState.WindowCount; windowIndex++ ) {
			if ( !state.Windows[ windowIndex ].TaskDisplayVisible ) {
				continue;
			}
			state.ActivateWindow(
				windowIndex
			);
			List<TopTaskRow> tasks = SelectAndOrderTasks(
				sample,
				state
			);
			int taskRows = AllocateNextAlternateTaskRows(
				state.MaximumTasks,
				remainingTaskRows,
				remainingVisibleWindows
			);
			remainingTaskRows -= taskRows;
			remainingVisibleWindows--;
			int maxOffset = ( 0 < taskRows )
				? Math.Max(
					0,
					tasks.Count - taskRows
				)
				: 0
			;
			state.VerticalOffset = Math.Clamp(
				state.VerticalOffset,
				0,
				maxOffset
			);
			state.HorizontalOffset = Math.Max(
				0,
				state.HorizontalOffset
			);

			char marker = ( windowIndex == currentWindowIndex )
				? '>'
				: ' '
			;
			string windowPrefix = $"{marker}{state.CurrentWindowLabel} ";
			int prefixLength = CountRunes(
				windowPrefix
			);
			int contentWidth = Math.Max(
				1,
				dimensions.Columns - prefixLength
			);
			string windowHeader = windowPrefix + SliceForDisplay(
				BuildHeader( state ),
				state.HorizontalOffset,
				contentWidth
			);
			lines.Add(
				new TopRenderLine(
					LimitRunes(
						windowHeader,
						dimensions.Columns
					),
					TopLineStyle.Header,
					ForegroundColor: ForegroundColor(
						state,
						state.Colors.Header
					)
				)
			);

			int end = Math.Min(
				tasks.Count,
				state.VerticalOffset + taskRows
			);
			for ( int index = state.VerticalOffset; index < end; index++ ) {
				TopTaskRow task = tasks[
					index
				];
				TopFormattedTaskLine formatted = FormatTaskLine(
					task,
					state,
					sample.ProcessorCount
				);
				string visibleContent = SliceForDisplay(
					formatted.Text,
					state.HorizontalOffset,
					contentWidth
				);
				string visibleLine = new string(
					' ',
					prefixLength
				) + visibleContent;
				TopLineStyle lineStyle = TaskLineStyle(
					task,
					state
				);
				IReadOnlyList<TopRenderSpan>? spans = SortColumnSpans(
					visibleContent,
					formatted,
					state,
					lineStyle
				);
				if ( spans is not null ) {
					spans = spans.Select(
						span => span with {
							Start = span.Start + prefixLength
						}
					).ToArray();
				}
				lines.Add(
					new TopRenderLine(
						visibleLine,
						lineStyle,
						spans,
						TaskForegroundColor(
							state,
							lineStyle
						)
					)
				);
			}
			for ( int index = end - state.VerticalOffset; index < taskRows; index++ ) {
				lines.Add(
					new TopRenderLine( string.Empty )
				);
			}
		}

		state.ActivateWindow(
			currentWindowIndex
		);
		while ( lines.Count < dimensions.Rows - footerRows ) {
			lines.Add(
				new TopRenderLine( string.Empty )
			);
		}
		if ( 0 < footerRows ) {
			if ( state.Prompt is not null ) {
				lines.Add(
					new TopRenderLine(
						LimitRunes(
							$"{state.Prompt.Label}{state.Prompt.Buffer}",
							dimensions.Columns
						),
						TopLineStyle.Prompt,
						ForegroundColor: ForegroundColor(
							state,
							state.Colors.Messages
						)
					)
				);
			} else if ( !string.IsNullOrEmpty( state.Message ) ) {
				lines.Add(
					new TopRenderLine(
						LimitRunes(
							state.Message,
							dimensions.Columns
						),
						TopLineStyle.Message,
						ForegroundColor: ForegroundColor(
							state,
							state.Colors.Messages
						)
					)
				);
			} else {
				lines.Add(
					new TopRenderLine(
						LimitRunes(
							BuildScrollCoordinatesMessage( sample, state ),
							dimensions.Columns
						),
						TopLineStyle.Message,
						ForegroundColor: ForegroundColor(
							state,
							state.Colors.Messages
						)
					)
				);
			}
		}
		return new TopRenderFrame(
			lines,
			dimensions.Columns,
			dimensions.Rows,
			state.BoldEnabled
		);
	}

	internal static IReadOnlyList<string> RenderBatch(
		TopSample sample,
		TopRuntimeState state,
		int width
	) {
		ArgumentNullException.ThrowIfNull( sample );
		ArgumentNullException.ThrowIfNull( state );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );
		List<TopTaskRow> tasks = SelectAndOrderTasks(
			sample,
			state
		);
		var lines = new List<string>();
		lines.AddRange(
			BuildSummaryLines(
				sample,
				state,
				width
			).Select(
				line => LimitRunes( line, width )
			)
		);
		lines.Add( LimitRunes( BuildHeader( state ), width ) );
		foreach ( TopTaskRow task in tasks ) {
			lines.Add( LimitRunes(
				FormatTaskLine(
					task,
					state,
					sample.ProcessorCount
				).Text,
				width
			) );
		}
		return lines;
	}

	internal static int GetEndOffset(
		TopSample sample,
		TopRuntimeState state,
		TopTerminalDimensions dimensions
	) {
		ArgumentNullException.ThrowIfNull( sample );
		ArgumentNullException.ThrowIfNull( state );
		if ( 0 >= dimensions.Columns || 0 >= dimensions.Rows ) {
			throw new ArgumentOutOfRangeException( nameof( dimensions ) );
		}

		List<TopTaskRow> tasks = SelectAndOrderTasks(
			sample,
			state
		);
		int availableTaskRows = GetTaskPageSize(
			state,
			dimensions
		);
		if ( 0 == tasks.Count || 0 == availableTaskRows ) {
			return 0;
		}
		return Math.Max(
			0,
			tasks.Count - availableTaskRows
		);
	}

	internal static int FindTaskOffset(
		TopSample sample,
		TopRuntimeState state,
		string searchText,
		int startIndex,
		TopTerminalDimensions dimensions
	) {
		ArgumentNullException.ThrowIfNull( sample );
		ArgumentNullException.ThrowIfNull( state );
		ArgumentException.ThrowIfNullOrEmpty( searchText );
		ArgumentOutOfRangeException.ThrowIfNegative( startIndex );
		if ( 0 >= dimensions.Columns || 0 >= dimensions.Rows ) {
			throw new ArgumentOutOfRangeException( nameof( dimensions ) );
		}

		List<TopTaskRow> tasks = SelectAndOrderTasks(
			sample,
			state
		);
		for ( int index = startIndex; index < tasks.Count; index++ ) {
			string visibleLine = SliceForDisplay(
				FormatTaskLine(
					tasks[ index ],
					state,
					sample.ProcessorCount
				).Text,
				state.HorizontalOffset,
				dimensions.Columns
			);
			if ( visibleLine.Contains( searchText, StringComparison.Ordinal ) ) {
				return index;
			}
		}
		return -1;
	}

	private static int AvailableTaskRows(
		TopRuntimeState state,
		TopTerminalDimensions dimensions,
		int footerRows
	) {
		ArgumentNullException.ThrowIfNull( state );
		if ( 0 >= dimensions.Columns || 0 >= dimensions.Rows ) {
			throw new ArgumentOutOfRangeException( nameof( dimensions ) );
		}
		if ( 0 > footerRows || 1 < footerRows ) {
			throw new ArgumentOutOfRangeException( nameof( footerRows ) );
		}

		int result = Math.Max(
			0,
			dimensions.Rows - GetSummaryRowCount( state ) - TableHeaderRows - footerRows
		);
		if ( 0 < state.MaximumTasks ) {
			result = Math.Min(
				result,
				state.MaximumTasks
			);
		}
		return result;
	}

	internal static int GetTaskPageSize(
		TopRuntimeState state,
		TopTerminalDimensions dimensions
	) {
		ArgumentNullException.ThrowIfNull( state );
		if ( 0 >= dimensions.Columns || 0 >= dimensions.Rows ) {
			throw new ArgumentOutOfRangeException( nameof( dimensions ) );
		}

		int footerRows = (
			state.Prompt is null
			&& string.IsNullOrEmpty( state.Message )
			&& !ShouldShowScrollCoordinates( state )
		)
			? 0
			: 1
		;
		if ( !state.AlternateDisplayMode ) {
			return AvailableTaskRows(
				state,
				dimensions,
				footerRows
			);
		}

		state.SynchronizeCurrentWindow();
		if ( !state.TaskDisplayVisible ) {
			return 0;
		}

		int windowAreaRows = Math.Max(
			0,
			dimensions.Rows - GetSummaryRowCount( state ) - footerRows
		);
		int visibleWindowCount = CountVisibleTaskWindows(
			state
		);
		int remainingTaskRows = Math.Max(
			0,
			windowAreaRows - visibleWindowCount
		);
		int remainingVisibleWindows = visibleWindowCount;
		for ( int index = 0; index < TopRuntimeState.WindowCount; index++ ) {
			TopWindowState window = state.Windows[
				index
			];
			if ( !window.TaskDisplayVisible ) {
				continue;
			}
			int taskRows = AllocateNextAlternateTaskRows(
				window.MaximumTasks,
				remainingTaskRows,
				remainingVisibleWindows
			);
			if ( index == state.CurrentWindowIndex ) {
				return taskRows;
			}
			remainingTaskRows -= taskRows;
			remainingVisibleWindows--;
		}
		return 0;
	}

	private static int CountVisibleTaskWindows(
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( state );

		int result = 0;
		foreach ( TopWindowState window in state.Windows ) {
			if ( window.TaskDisplayVisible ) {
				result++;
			}
		}
		return result;
	}

	private static int AllocateNextAlternateTaskRows(
		int maximumTasks,
		int remainingTaskRows,
		int remainingVisibleWindows
	) {
		ArgumentOutOfRangeException.ThrowIfNegative( maximumTasks );
		ArgumentOutOfRangeException.ThrowIfNegative( remainingTaskRows );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( remainingVisibleWindows );

		if ( 1 == remainingVisibleWindows ) {
			return remainingTaskRows;
		}
		if ( 0 < maximumTasks ) {
			return Math.Min(
				maximumTasks,
				remainingTaskRows
			);
		}

		int result = remainingTaskRows / remainingVisibleWindows;
		if ( 0 < remainingTaskRows % remainingVisibleWindows ) {
			result++;
		}
		return result;
	}

	internal static IReadOnlyList<string> ListFields() {
		var result = new List<string>( TopFieldCatalog.Definitions.Count );
		foreach ( TopFieldDefinition definition in TopFieldCatalog.Definitions ) {
			result.Add(
				$"{definition.Name,-9}{definition.Description}"
			);
		}
		return result;
	}

	internal static bool TryParseSortField(
		string text,
		out TopFieldId field
	) {
		ArgumentNullException.ThrowIfNull( text );
		return TopFieldCatalog.TryParse(
			text,
			out field
		);
	}

	internal static bool TryParseSortOverride(
		string text,
		out TopFieldId field,
		out bool? highToLow
	) {
		ArgumentNullException.ThrowIfNull( text );

		string normalized = text.Trim();
		highToLow = null;
		if ( 0 < normalized.Length ) {
			if ( '+' == normalized[ 0 ] ) {
				highToLow = true;
				normalized = normalized[ 1.. ];
			} else if ( '-' == normalized[ 0 ] ) {
				highToLow = false;
				normalized = normalized[ 1.. ];
			}
		}
		if ( TryParseSortField( normalized, out field ) ) {
			return true;
		}

		highToLow = null;
		return false;
	}

	internal static TopMemoryScale NextScale( TopMemoryScale scale ) => scale switch {
		TopMemoryScale.Kibibytes => TopMemoryScale.Mebibytes,
		TopMemoryScale.Mebibytes => TopMemoryScale.Gibibytes,
		TopMemoryScale.Gibibytes => TopMemoryScale.Tebibytes,
		TopMemoryScale.Tebibytes => TopMemoryScale.Pebibytes,
		TopMemoryScale.Pebibytes => TopMemoryScale.Exbibytes,
		_ => TopMemoryScale.Kibibytes
	};

	internal static bool TryParseScale( string text, out TopMemoryScale scale ) {
		ArgumentNullException.ThrowIfNull( text );
		if ( 1 != text.Length ) {
			scale = default;
			return false;
		}
		scale = char.ToLowerInvariant( text[ 0 ] ) switch {
			'k' => TopMemoryScale.Kibibytes,
			'm' => TopMemoryScale.Mebibytes,
			'g' => TopMemoryScale.Gibibytes,
			't' => TopMemoryScale.Tebibytes,
			'p' => TopMemoryScale.Pebibytes,
			'e' => TopMemoryScale.Exbibytes,
			_ => default
		};
		return char.ToLowerInvariant( text[ 0 ] ) is 'k' or 'm' or 'g' or 't' or 'p' or 'e';
	}

	internal static TopRenderFrame RenderColorManager(
		TopRuntimeState state,
		TopTerminalDimensions dimensions
	) {
		ArgumentNullException.ThrowIfNull( state );
		if ( 0 >= dimensions.Columns || 0 >= dimensions.Rows ) {
			throw new ArgumentOutOfRangeException(
				nameof( dimensions )
			);
		}
		TopColorManagerState manager = state.ColorManager
			?? throw new InvalidOperationException(
				"The color mapping screen was requested without an active color manager."
			);

		state.VerticalOffset = 0;
		state.HorizontalOffset = 0;
		var lines = new List<TopRenderLine>(
			dimensions.Rows
		);
		lines.Add(
			new TopRenderLine(
				LimitRunes(
					$"Help for color mapping - Current Window = {state.CurrentWindowLabel}",
					dimensions.Columns
				),
				TopLineStyle.Header,
				ForegroundColor: ForegroundColor(
					state,
					state.Colors.Header
				)
			)
		);
		lines.Add(
			new TopRenderLine(
				LimitRunes(
					$"B:Bold {ToggleStateLabel( state.BoldEnabled )}  b:Highlight {ToggleStateLabel( state.HighlightBold )}  z:Colors {ToggleStateLabel( state.ColorsEnabled )}",
					dimensions.Columns
				),
				TopLineStyle.Dim
			)
		);

		(TopColorTarget Target, char Key, string Label)[] targets = [
			( TopColorTarget.Summary, 'S', "summary data" ),
			( TopColorTarget.Messages, 'M', "messages/prompts" ),
			( TopColorTarget.Header, 'H', "column headers" ),
			( TopColorTarget.Tasks, 'T', "task rows" ),
			( TopColorTarget.TaskAccent, 'X', "highlighted tasks/columns" )
		];
		foreach ( var target in targets ) {
			int color = TopColorManagerState.GetColor(
				state.Colors,
				target.Target
			);
			char marker = ( manager.Target == target.Target )
				? '>'
				: ' '
			;
			TopLineStyle style = ( manager.Target == target.Target )
				? TopLineStyle.HighlightReverse
				: TopLineStyle.Default
			;
			lines.Add(
				new TopRenderLine(
					LimitRunes(
						$"{marker} {target.Key} {target.Label,-27} {ColorValueLabel( color )}",
						dimensions.Columns
					),
					style,
					ForegroundColor: ForegroundColor(
						state,
						color
					)
				)
			);
		}

		lines.Add(
			new TopRenderLine(
				LimitRunes(
					"S/M/H/T/X target; 0-7/@ color; Up/Down cycle -1..255",
					dimensions.Columns
				),
				TopLineStyle.Dim
			)
		);
		lines.Add(
			new TopRenderLine(
				LimitRunes(
					"a/w window; B/b/z toggles; Enter apply; q/Esc cancel",
					dimensions.Columns
				),
				TopLineStyle.Dim
			)
		);
		while ( lines.Count < dimensions.Rows ) {
			lines.Add(
				new TopRenderLine( string.Empty )
			);
		}
		if ( dimensions.Rows < lines.Count ) {
			lines.RemoveRange(
				dimensions.Rows,
				lines.Count - dimensions.Rows
			);
		}
		return new TopRenderFrame(
			lines,
			dimensions.Columns,
			dimensions.Rows,
			state.BoldEnabled
		);
	}

	private static string ToggleStateLabel(
		bool enabled
	) {
		return ( enabled )
			? "On"
			: "Off"
		;
	}

	private static string ColorValueLabel(
		int color
	) {
		if ( -1 == color ) {
			return "@ (terminal default)";
		}
		return color.ToString(
			CultureInfo.InvariantCulture
		);
	}

	private static int? ForegroundColor(
		TopRuntimeState state,
		int color
	) {
		ArgumentNullException.ThrowIfNull( state );
		return ( state.ColorsEnabled )
			? color
			: null
		;
	}

	private static int? TaskForegroundColor(
		TopRuntimeState state,
		TopLineStyle lineStyle
	) {
		ArgumentNullException.ThrowIfNull( state );

		if ( !state.ColorsEnabled ) {
			return null;
		}
		return (
			lineStyle is TopLineStyle.HighlightBold
				or TopLineStyle.HighlightReverse
		)
			? state.Colors.TaskAccent
			: state.Colors.Tasks
		;
	}

	private static TopRenderFrame RenderHelp(
		TopRuntimeState state,
		TopTerminalDimensions dimensions
	) {
		var lines = new List<TopRenderLine>( dimensions.Rows );
		for ( int index = 0; index < HelpLines.Length && index < dimensions.Rows; index++ ) {
			lines.Add( new TopRenderLine(
				LimitRunes( HelpLines[ index ], dimensions.Columns ),
				0 == index ? TopLineStyle.Header : TopLineStyle.Default
			) );
		}
		while ( lines.Count < dimensions.Rows ) {
			lines.Add( new TopRenderLine( string.Empty ) );
		}
		state.VerticalOffset = 0;
		state.HorizontalOffset = 0;
		return new TopRenderFrame(
			lines,
			dimensions.Columns,
			dimensions.Rows,
			state.BoldEnabled
		);
	}

	private static TopRenderFrame RenderFieldManager(
		TopRuntimeState state,
		TopTerminalDimensions dimensions
	) {
		ArgumentNullException.ThrowIfNull( state );
		if ( 0 >= dimensions.Columns || 0 >= dimensions.Rows ) {
			throw new ArgumentOutOfRangeException( nameof( dimensions ) );
		}
		if ( 0 == state.FieldOrder.Count ) {
			throw new InvalidOperationException(
				"The top field order cannot be empty."
			);
		}

		state.FieldCursor = Math.Clamp(
			state.FieldCursor,
			0,
			state.FieldOrder.Count - 1
		);
		TopFieldDefinition sortDefinition = TopFieldCatalog.Get(
			state.SortField
		);
		var lines = new List<TopRenderLine>( dimensions.Rows ) {
			new(
				LimitRunes(
					$"Fields Management for {state.CurrentWindowLabel} - sort field: {sortDefinition.Name}",
					dimensions.Columns
				),
				TopLineStyle.Header
			),
			new(
				LimitRunes(
					"Markers: > selected, * displayed, S sort, M moving",
					dimensions.Columns
				),
				TopLineStyle.Dim
			),
			new(
				LimitRunes(
					"Up/Down/Pg/Home/End move; d/Space display; s sort; Right move; Left/Enter place; q/Esc return",
					dimensions.Columns
				),
				TopLineStyle.Dim
			)
		};

		int listRows = Math.Max(
			1,
			dimensions.Rows - 3
		);
		int maxOffset = Math.Max(
			0,
			state.FieldOrder.Count - listRows
		);
		int offset = Math.Clamp(
			state.FieldCursor - listRows + 1,
			0,
			maxOffset
		);
		int end = Math.Min(
			state.FieldOrder.Count,
			offset + listRows
		);
		for ( int index = offset; index < end; index++ ) {
			TopFieldId field = state.FieldOrder[ index ];
			TopFieldDefinition definition = TopFieldCatalog.Get(
				field
			);

			string selectedMarker = " ";
			if ( index == state.FieldCursor ) {
				selectedMarker = ">";
			}
			string visibleMarker = " ";
			if ( state.VisibleFields.Contains( field ) ) {
				visibleMarker = "*";
			}
			string sortMarker = " ";
			if ( field == state.SortField ) {
				sortMarker = "S";
			}
			string moveMarker = " ";
			if (
				state.FieldMoveActive
				&& index == state.FieldCursor
			) {
				moveMarker = "M";
			}
			string markers = string.Concat(
				selectedMarker,
				visibleMarker,
				sortMarker,
				moveMarker
			);
			string lineText = $"{markers} {definition.Name,-8} {definition.Description}";
			TopLineStyle lineStyle = TopLineStyle.Default;
			if ( index == state.FieldCursor ) {
				lineStyle = TopLineStyle.HighlightReverse;
			}
			lines.Add(
				new TopRenderLine(
					LimitRunes(
						lineText,
						dimensions.Columns
					),
					lineStyle
				)
			);
		}
		while ( lines.Count < dimensions.Rows ) {
			lines.Add(
				new TopRenderLine( string.Empty )
			);
		}
		return new TopRenderFrame(
			lines,
			dimensions.Columns,
			dimensions.Rows,
			state.BoldEnabled
		);
	}

	private static bool ShouldShowScrollCoordinates(
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( state );
		return state.ScrollCoordinatesVisible
			&& (
				!state.AlternateDisplayMode
				|| state.TaskDisplayVisible
			);
	}

	private static string BuildScrollCoordinatesMessage(
		TopSample sample,
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( sample );
		ArgumentNullException.ThrowIfNull( state );

		List<TopTaskRow> tasks = SelectAndOrderTasks(
			sample,
			state
		);
		int taskPosition = ( 0 == tasks.Count )
			? 0
			: Math.Min(
				tasks.Count,
				state.VerticalOffset + 1
			)
		;

		var visibleFields = new List<TopFieldDefinition>();
		foreach ( TopFieldId field in state.FieldOrder ) {
			if ( state.VisibleFields.Contains( field ) ) {
				visibleFields.Add(
					TopFieldCatalog.Get( field )
				);
			}
		}

		int fieldPosition = 0;
		int displacement = 0;
		if ( 0 < visibleFields.Count ) {
			int remaining = Math.Max(
				0,
				state.HorizontalOffset
			);
			for ( int index = 0; index < visibleFields.Count; index++ ) {
				int width = TopFixedWidth.Width(
					state,
					visibleFields[ index ]
				);
				bool last = index == visibleFields.Count - 1;
				if ( last || remaining < width + 1 ) {
					fieldPosition = index + 1;
					displacement = remaining;
					break;
				}
				remaining -= width + 1;
			}
		}

		string result = string.Format(
			CultureInfo.InvariantCulture,
			"scroll coordinates: y = {0}/{1} (tasks), x = {2}/{3} (fields)",
			taskPosition,
			tasks.Count,
			fieldPosition,
			visibleFields.Count
		);
		if ( 0 < displacement ) {
			result += string.Format(
				CultureInfo.InvariantCulture,
				" + {0}",
				displacement
			);
		}
		return result;
	}

	private static IReadOnlyList<string> BuildSummaryLines(
		TopSample sample,
		TopRuntimeState state,
		int width
	) {
		ArgumentNullException.ThrowIfNull( sample );
		ArgumentNullException.ThrowIfNull( state );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );

		var result = new List<string>(
			GetSummaryRowCount( state )
		);
		if ( state.LoadAverageVisible ) {
			result.Add(
				BuildTopLine( sample )
			);
		}
		if ( state.CpuSummaryVisible ) {
			string taskLabel = ( state.ShowThreads )
				? "Threads"
				: "Tasks"
			;
			IReadOnlyList<TopTaskRow> visible = ApplyFilters(
				sample.Tasks,
				state,
				sample.ProcessorCount
			).ToList().AsReadOnly();
			int running = visible.Count( row => IsState( row, ProcProcessState.Running ) );
			int sleeping = visible.Count( row => IsState( row, ProcProcessState.Sleeping ) );
			int stopped = visible.Count(
				row => IsState( row, ProcProcessState.Stopped )
					|| IsState( row, ProcProcessState.TracingStop )
			);
			int zombie = visible.Count( row => IsState( row, ProcProcessState.Zombie ) );
			result.Add(
				$"{taskLabel}: {visible.Count,5} total, {running,4} running, {sleeping,4} sleeping, {stopped,4} stopped, {zombie,4} zombie"
			);
			result.Add(
				BuildCpuLine(
					sample.CpuSummary,
					sample.ProcessorCount,
					state.SingleCpuSummary,
					state.CpuSummaryGraphMode,
					width
				)
			);
		}
		if ( state.MemorySummaryVisible ) {
			result.Add(
				BuildMemoryLine(
					sample.System,
					state.SummaryScale,
					state.MemorySummaryGraphMode,
					width
				)
			);
			result.Add(
				BuildSwapLine(
					sample.System,
					state.SummaryScale,
					state.MemorySummaryGraphMode,
					width
				)
			);
		}
		return result;
	}

	internal static int GetSummaryRowCount(
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( state );

		int result = ( state.LoadAverageVisible )
			? 1
			: 0
		;
		if ( state.CpuSummaryVisible ) {
			result += 2;
		}
		if ( state.MemorySummaryVisible ) {
			result += 2;
		}
		return result;
	}

	private static string BuildTopLine( TopSample sample ) {
		string uptime = sample.System.Uptime.HasValue
			? FormatUptime( sample.System.Uptime.Value.Uptime )
			: "up ?";
		string users = sample.System.UserSessions.HasValue
			? $"{sample.System.UserSessions.Value.Count} user{( 1 == sample.System.UserSessions.Value.Count ? string.Empty : "s" )}"
			: "? users";
		string load = sample.System.LoadAverage.HasValue
			? string.Format(
				CultureInfo.InvariantCulture,
				"{0:0.00}, {1:0.00}, {2:0.00}",
				sample.System.LoadAverage.Value.OneMinute,
				sample.System.LoadAverage.Value.FiveMinutes,
				sample.System.LoadAverage.Value.FifteenMinutes
			)
			: sample.System.LoadAverages.HasValue
				? string.Format(
					CultureInfo.InvariantCulture,
					"{0:0.00}, {1:0.00}, {2:0.00}",
					sample.System.LoadAverages.Value.OneMinute,
					sample.System.LoadAverages.Value.FiveMinutes,
					sample.System.LoadAverages.Value.FifteenMinutes
				)
				: "?, ?, ?";
		return $"top - {sample.ObservedAt:HH:mm:ss} {uptime}, {users}, load average: {load}";
	}

	private static string BuildCpuLine(
		TopCpuSummary cpu,
		int processorCount,
		bool singleCpuSummary,
		TopSummaryGraphMode mode,
		int width
	) {
		ArgumentNullException.ThrowIfNull( cpu );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( processorCount );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );

		string prefix = ( singleCpuSummary )
			? "%Cpu(s)"
			: $"%Cpu(s/{processorCount})"
		;
		if ( TopSummaryGraphMode.Detailed == mode ) {
			if ( cpu.LinuxDetailed ) {
				return string.Format(
					CultureInfo.InvariantCulture,
					"{0}: {1,5:0.0} us, {2,5:0.0} sy, {3,5:0.0} ni, {4,5:0.0} id, {5,5:0.0} wa, {6,5:0.0} hi, {7,5:0.0} si, {8,5:0.0} st",
					prefix,
					cpu.User,
					cpu.System,
					cpu.Nice,
					cpu.Idle,
					cpu.Wait,
					cpu.Irq,
					cpu.SoftIrq,
					cpu.Steal
				);
			}
			return string.Format(
				CultureInfo.InvariantCulture,
				"{0}: {1,5:0.0} us, {2,5:0.0} sy, {3,5:0.0} ni, {4,5:0.0} id, {5,5:0.0} wa, {6,5:0.0} ot",
				prefix,
				cpu.User,
				cpu.System,
				cpu.Nice,
				cpu.Idle,
				cpu.Wait,
				cpu.Other
			);
		}

		double user = ClampPercentage(
			cpu.User + cpu.Nice
		);
		double system = ClampPercentage(
			cpu.System + cpu.Irq + cpu.SoftIrq + cpu.Other
		);
		double total = ClampPercentage(
			user + system
		);
		return string.Format(
			CultureInfo.InvariantCulture,
			"{0}: {1,5:0.0}/{2,5:0.0} {3,5:0.0}% {4}",
			prefix,
			user,
			system,
			total,
			BuildSummaryGraph(
				total,
				mode,
				width
			)
		);
	}

	private static string BuildMemoryLine(
		ProcSystemSnapshot system,
		TopMemoryScale scale,
		TopSummaryGraphMode mode,
		int width
	) {
		ArgumentNullException.ThrowIfNull( system );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );

		if ( !system.Memory.HasValue ) {
			return $"{ScaleLabel( scale )} Mem : unavailable";
		}
		ProcMemoryInfo memory = system.Memory.Value;
		ulong? total = memory.TotalBytes;
		ulong? free = memory.FreeBytes;
		ulong? bufferCache = SumNullable( memory.BuffersBytes, memory.CacheBytes );
		ulong? used = total.HasValue && free.HasValue
			? SaturatingSubtract( total.Value, SaturatingAdd( free.Value, bufferCache ?? 0UL ) )
			: null;
		if ( TopSummaryGraphMode.Detailed == mode ) {
			return string.Format(
				CultureInfo.InvariantCulture,
				"{0} Mem : {1,10} total, {2,10} free, {3,10} used, {4,10} buff/cache",
				ScaleLabel( scale ),
				FormatMemory( total, scale ),
				FormatMemory( free, scale ),
				FormatMemory( used, scale ),
				FormatMemory( bufferCache, scale )
			);
		}

		ulong? available = memory.AvailableBytes;
		if ( !available.HasValue && free.HasValue ) {
			available = SaturatingAdd(
				free.Value,
				bufferCache ?? 0UL
			);
		}
		double percentage = UsedPercentage(
			total,
			( available.HasValue && total.HasValue )
				? SaturatingSubtract( total.Value, available.Value )
				: null
		);
		return BuildMemoryGraphLine(
			"Mem ",
			total,
			percentage,
			scale,
			mode,
			width
		);
	}

	private static string BuildSwapLine(
		ProcSystemSnapshot system,
		TopMemoryScale scale,
		TopSummaryGraphMode mode,
		int width
	) {
		ArgumentNullException.ThrowIfNull( system );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );

		if ( !system.Memory.HasValue ) {
			return $"{ScaleLabel( scale )} Swap: unavailable";
		}
		ProcMemoryInfo memory = system.Memory.Value;
		ulong? total = memory.SwapTotalBytes;
		ulong? free = memory.SwapFreeBytes;
		ulong? used = total.HasValue && free.HasValue
			? SaturatingSubtract( total.Value, free.Value )
			: null;
		if ( TopSummaryGraphMode.Detailed == mode ) {
			return string.Format(
				CultureInfo.InvariantCulture,
				"{0} Swap: {1,10} total, {2,10} free, {3,10} used, {4,10} avail Mem",
				ScaleLabel( scale ),
				FormatMemory( total, scale ),
				FormatMemory( free, scale ),
				FormatMemory( used, scale ),
				FormatMemory( memory.AvailableBytes, scale )
			);
		}

		return BuildMemoryGraphLine(
			"Swap",
			total,
			UsedPercentage(
				total,
				used
			),
			scale,
			mode,
			width
		);
	}

	private static string BuildMemoryGraphLine(
		string label,
		ulong? total,
		double percentage,
		TopMemoryScale scale,
		TopSummaryGraphMode mode,
		int width
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( label );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );

		return string.Format(
			CultureInfo.InvariantCulture,
			"{0} {1}: {2,5:0.0}%/{3,-10} {4}",
			ScaleLabel( scale ),
			label,
			percentage,
			FormatMemory( total, scale ),
			BuildSummaryGraph(
				percentage,
				mode,
				width
			)
		);
	}

	private static double UsedPercentage(
		ulong? total,
		ulong? used
	) {
		if (
			!total.HasValue
			|| 0UL == total.Value
			|| !used.HasValue
		) {
			return 0.0;
		}
		return ClampPercentage(
			100.0 * used.Value / total.Value
		);
	}

	private static double ClampPercentage(
		double value
	) {
		return Math.Clamp(
			value,
			0.0,
			100.0
		);
	}

	private static string BuildSummaryGraph(
		double percentage,
		TopSummaryGraphMode mode,
		int width
	) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );
		char fill = mode switch {
			TopSummaryGraphMode.Bar => '|',
			TopSummaryGraphMode.Block => '#',
			_ => throw new InvalidOperationException(
				$"Graph output is unavailable for summary mode '{mode}'."
			)
		};
		int graphWidth = Math.Clamp(
			width - 30,
			10,
			100
		);
		int filled = Math.Clamp(
			(int)Math.Round(
				graphWidth * ClampPercentage( percentage ) / 100.0,
				MidpointRounding.AwayFromZero
			),
			0,
			graphWidth
		);
		return string.Concat(
			"[",
			new string( fill, filled ),
			new string( ' ', graphWidth - filled ),
			"]"
		);
	}

	private static string BuildHeader(
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( state );

		var fields = new List<string>();
		foreach ( TopFieldId field in state.FieldOrder ) {
			if ( !state.VisibleFields.Contains( field ) ) {
				continue;
			}
			TopFieldDefinition definition = TopFieldCatalog.Get(
				field
			);
			bool leftJustified;
			if ( definition.Numeric ) {
				leftJustified = state.NumericLeftJustified;
			} else {
				leftJustified = !state.CharacterRightJustified;
			}
			fields.Add(
				AlignField(
					definition.Name,
					TopFixedWidth.Width(
						state,
						definition
					),
					leftJustified
				)
			);
		}
		return string.Join(
			" ",
			fields
		);
	}

	private static TopFormattedTaskLine FormatTaskLine(
		TopTaskRow row,
		TopRuntimeState state,
		int processorCount
	) {
		ArgumentNullException.ThrowIfNull( row );
		ArgumentNullException.ThrowIfNull( state );

		var builder = new StringBuilder();
		int runePosition = 0;
		int sortFieldStart = -1;
		int sortFieldLength = 0;
		bool first = true;
		foreach ( TopFieldId field in state.FieldOrder ) {
			if ( !state.VisibleFields.Contains( field ) ) {
				continue;
			}

			string fieldText = FieldDisplayValue(
				row,
				state,
				processorCount,
				field
			);

			if ( !first ) {
				builder.Append( ' ' );
				runePosition++;
			}
			first = false;

			int fieldLength = CountRunes( fieldText );
			if ( field == state.SortField ) {
				sortFieldStart = runePosition;
				sortFieldLength = fieldLength;
			}
			builder.Append( fieldText );
			runePosition += fieldLength;
		}

		return new TopFormattedTaskLine(
			builder.ToString(),
			sortFieldStart,
			sortFieldLength
		);
	}

	internal static string FieldDisplayValue(
		TopTaskRow row,
		TopRuntimeState state,
		int processorCount,
		TopFieldId field
	) {
		ArgumentNullException.ThrowIfNull( row );
		ArgumentNullException.ThrowIfNull( state );

		TopFieldDefinition definition = TopFieldCatalog.Get(
			field
		);
		string fieldText = definition.Formatter(
			row,
			state,
			processorCount
		);
		if ( TopFieldId.Command == field ) {
			return FormatCommandField(
				fieldText,
				state.CharacterRightJustified
			);
		}

		bool leftJustified;
		if ( definition.Numeric ) {
			leftJustified = state.NumericLeftJustified;
		} else {
			leftJustified = !state.CharacterRightJustified;
		}
		return AlignField(
			fieldText,
			TopFixedWidth.Width(
				state,
				definition
			),
			leftJustified
		);
	}

	internal static string FieldAlign(
		string text,
		int width,
		bool leftJustified
	) => AlignField(
		text,
		width,
		leftJustified
	);

	private static string AlignField(
		string text,
		int width,
		bool leftJustified
	) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );

		int length = CountRunes( text );
		if ( width <= length ) {
			return text;
		}

		string padding = new(
			' ',
			width - length
		);
		if ( leftJustified ) {
			return string.Concat(
				text,
				padding
			);
		}
		return string.Concat(
			padding,
			text
		);
	}

	private static string FormatCommandField(
		string command,
		bool rightJustified
	) {
		ArgumentNullException.ThrowIfNull( command );

		if ( !rightJustified ) {
			return command;
		}
		return AlignField(
			command,
			7,
			leftJustified: false
		);
	}

	internal static TopTaskRow? GetTopmostTask(
		TopSample sample,
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( sample );
		ArgumentNullException.ThrowIfNull( state );

		if ( !state.TaskDisplayVisible ) {
			return null;
		}
		List<TopTaskRow> tasks = SelectAndOrderTasks(
			sample,
			state
		);
		if ( 0 == tasks.Count ) {
			return null;
		}
		int topIndex = Math.Clamp(
			state.VerticalOffset,
			0,
			tasks.Count - 1
		);
		return tasks[ topIndex ];
	}

	internal static int? GetTopmostProcessId(
		TopSample sample,
		TopRuntimeState state
	) {
		return GetTopmostTask(
			sample,
			state
		)?.Process.ProcessId;
	}

	internal static bool ToggleForestFocus(
		TopSample sample,
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( sample );
		ArgumentNullException.ThrowIfNull( state );

		if ( !state.Forest ) {
			return false;
		}
		if ( state.ForestFocus is not null ) {
			state.ForestFocus = null;
			state.VerticalOffset = 0;
			state.SynchronizeCurrentWindow();
			return true;
		}

		List<TopTaskRow> tasks = SelectAndOrderTasks(
			sample,
			state
		);
		if ( 0 == tasks.Count ) {
			return false;
		}
		int topIndex = Math.Clamp(
			state.VerticalOffset,
			0,
			tasks.Count - 1
		);
		state.ForestFocus = tasks[
			topIndex
		].Process.Identity;
		state.VerticalOffset = 0;
		state.SynchronizeCurrentWindow();
		return true;
	}

	internal static bool ToggleTopmostForestChildren(
		TopSample sample,
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( sample );
		ArgumentNullException.ThrowIfNull( state );

		if ( !state.Forest ) {
			return false;
		}
		List<TopTaskRow> tasks = SelectAndOrderTasks(
			sample,
			state
		);
		if ( 0 == tasks.Count ) {
			return false;
		}
		int topIndex = Math.Clamp(
			state.VerticalOffset,
			0,
			tasks.Count - 1
		);
		ProcessIdentity target = tasks[
			topIndex
		].Process.Identity;
		List<TopTaskRow> candidates = SelectTaskCandidates(
			sample,
			state
		);
		if ( !TopForestProjection.HasChildren( candidates, target ) ) {
			return false;
		}

		if ( !state.CollapsedForestParents.Remove( target ) ) {
			state.CollapsedForestParents.Add(
				target
			);
		}
		state.SynchronizeCurrentWindow();
		return true;
	}

	private static List<TopTaskRow> SelectAndOrderTasks(
		TopSample sample,
		TopRuntimeState state
	) {
		List<TopTaskRow> tasks = SelectTaskCandidates(
			sample,
			state
		);
		Comparison<TopTaskRow> comparison = SortComparison(
			state.SortField,
			state.SortHighToLow
		);
		if ( state.Forest ) {
			List<TopTaskRow> forest = TopForestProjection.Order(
				tasks,
				comparison,
				state
			);
			TopFixedWidth.Observe(
				forest,
				state
			);
			return forest;
		}
		foreach ( TopTaskRow row in tasks ) {
			row.ForestDepth = 0;
		}
		tasks.Sort( comparison );
		TopFixedWidth.Observe(
			tasks,
			state
		);
		return tasks;
	}

	private static List<TopTaskRow> SelectTaskCandidates(
		TopSample sample,
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( sample );
		ArgumentNullException.ThrowIfNull( state );

		List<TopTaskRow> tasks = ApplyFilters(
			sample.Tasks,
			state,
			sample.ProcessorCount
		).ToList();
		if ( state.HideIdle ) {
			tasks = tasks.Where(
				row => 0.0001 < row.CpuPercentIrix
					|| IsState(
						row,
						ProcProcessState.Running
					)
			).ToList();
		}
		return tasks;
	}

	private static IEnumerable<TopTaskRow> ApplyFilters(
		IReadOnlyList<TopTaskRow> tasks,
		TopRuntimeState state,
		int processorCount
	) {
		IEnumerable<TopTaskRow> filtered = tasks;
		if ( 0 < state.ProcessIds.Count ) {
			filtered = filtered.Where( row =>
				state.ProcessIds.Contains( row.Process.ProcessId )
				|| state.ProcessIds.Contains( row.ThreadGroupId )
			);
		}
		if ( state.UserFilter is not null ) {
			TopUserFilter filter = state.UserFilter;
			filtered = filtered.Where( row => {
				bool matches = filter.AnyUser
					? MatchesObservedUser( row.Process, filter.UserId )
					: row.Process.EffectiveUserId.HasValue
						&& row.Process.EffectiveUserId.Value == filter.UserId;
				return filter.Negate ? !matches : matches;
			} );
		}
		if ( 0 < state.OtherFilters.Count ) {
			filtered = filtered.Where(
				row => MatchesOtherFilters(
					row,
					state,
					processorCount
				)
			);
		}
		return filtered;
	}

	private static bool MatchesOtherFilters(
		TopTaskRow row,
		TopRuntimeState state,
		int processorCount
	) {
		ArgumentNullException.ThrowIfNull( row );
		ArgumentNullException.ThrowIfNull( state );

		foreach ( TopOtherFilter filter in state.OtherFilters ) {
			if ( !state.VisibleFields.Contains( filter.Field ) ) {
				continue;
			}

			string fieldText = FieldDisplayValue(
				row,
				state,
				processorCount,
				filter.Field
			);
			StringComparison comparison = filter.CaseSensitive
				? StringComparison.Ordinal
				: StringComparison.OrdinalIgnoreCase;
			bool matches;
			switch ( filter.Operator ) {
				case TopOtherFilterOperator.Equality:
					matches = fieldText.Contains(
						filter.SelectionValue,
						comparison
					);
					break;
				case TopOtherFilterOperator.LessThan:
					matches = 0 > string.Compare(
						fieldText,
						filter.SelectionValue,
						comparison
					);
					break;
				case TopOtherFilterOperator.GreaterThan:
					matches = 0 < string.Compare(
						fieldText,
						filter.SelectionValue,
						comparison
					);
					break;
				default:
					throw new InvalidOperationException(
						"The Other Filter operator was not recognized."
					);
			}
			if ( filter.Include != matches ) {
				return false;
			}
		}
		return true;
	}

	private static bool MatchesObservedUser( ProcProcessSnapshot process, uint userId ) =>
		( process.EffectiveUserId.HasValue && process.EffectiveUserId.Value == userId )
		|| ( process.RealUserId.HasValue && process.RealUserId.Value == userId );

	private static List<TopTaskRow> OrderForest(
		IReadOnlyList<TopTaskRow> tasks,
		Comparison<TopTaskRow> comparison
	) {
		var byId = tasks
			.GroupBy( row => row.Process.ProcessId )
			.ToDictionary( group => group.Key, group => group.First() );
		var children = new Dictionary<int, List<TopTaskRow>>();
		var roots = new List<TopTaskRow>();
		foreach ( TopTaskRow row in tasks ) {
			if ( row.Process.ParentProcessId.HasValue
				&& byId.ContainsKey( row.Process.ParentProcessId.Value ) ) {
				if ( !children.TryGetValue( row.Process.ParentProcessId.Value, out List<TopTaskRow>? list ) ) {
					list = [];
					children.Add( row.Process.ParentProcessId.Value, list );
				}
				list.Add( row );
			} else {
				roots.Add( row );
			}
		}
		roots.Sort( comparison );
		foreach ( List<TopTaskRow> list in children.Values ) {
			list.Sort( comparison );
		}
		var result = new List<TopTaskRow>( tasks.Count );
		var visited = new HashSet<int>();
		void Add( TopTaskRow row, int depth ) {
			if ( !visited.Add( row.Process.ProcessId ) ) {
				return;
			}
			row.ForestDepth = depth;
			result.Add( row );
			if ( children.TryGetValue( row.Process.ProcessId, out List<TopTaskRow>? list ) ) {
				foreach ( TopTaskRow child in list ) {
					Add( child, depth + 1 );
				}
			}
		}
		foreach ( TopTaskRow root in roots ) {
			Add( root, 0 );
		}
		foreach ( TopTaskRow row in tasks ) {
			Add( row, 0 );
		}
		return result;
	}

	private static Comparison<TopTaskRow> SortComparison(
		TopFieldId field,
		bool highToLow
	) {
		Comparison<TopTaskRow> comparison = TopFieldCatalog.Get(
			field
		).HighToLowComparison;
		return ( highToLow )
			? comparison
			: ( left, right ) => comparison( right, left )
		;
	}

	private static int Descending<T>( T left, T right, TopTaskRow leftRow, TopTaskRow rightRow )
		where T : IComparable<T> {
		int result = right.CompareTo( left );
		return TieBreak( result, leftRow, rightRow );
	}

	internal static int CompareFieldDescending<T>(
		T left,
		T right,
		TopTaskRow leftRow,
		TopTaskRow rightRow
	)
		where T : IComparable<T> {
		ArgumentNullException.ThrowIfNull( leftRow );
		ArgumentNullException.ThrowIfNull( rightRow );
		return Descending(
			left,
			right,
			leftRow,
			rightRow
		);
	}

	internal static ulong FieldObservedOrZero(
		ProcObservedValue<ulong> value
	) => ObservedOrZero( value );

	internal static int FieldObservedPriority(
		ProcProcessSnapshot process
	) {
		ArgumentNullException.ThrowIfNull( process );
		if ( !process.NiceValue.HasValue ) {
			return int.MaxValue;
		}
		return 20 + process.NiceValue.Value;
	}

	internal static int FieldObservedNice(
		ProcProcessSnapshot process
	) {
		ArgumentNullException.ThrowIfNull( process );
		return ObservedNice( process );
	}

	internal static string FieldStateCode(
		ProcProcessSnapshot process
	) {
		ArgumentNullException.ThrowIfNull( process );
		return StateCode( process );
	}

	internal static string FieldCommand(
		ProcProcessSnapshot process,
		bool commandLine
	) {
		ArgumentNullException.ThrowIfNull( process );
		return FormatCommand(
			process,
			commandLine
		);
	}

	internal static string FieldTruncateUser(
		string user,
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( user );
		ArgumentNullException.ThrowIfNull( state );
		return TopFixedWidth.Format(
			user,
			state,
			TopFieldId.User
		);
	}

	internal static string FieldCpuTime(
		double? seconds
	) => FormatCpuTime( seconds );

	internal static string FieldTaskMemory(
		ProcObservedValue<ulong> bytes,
		TopMemoryScale scale
	) => FormatTaskMemory(
		bytes,
		scale
	);

	private static int TieBreak( int comparison, TopTaskRow left, TopTaskRow right ) =>
		0 != comparison ? comparison : left.Process.ProcessId.CompareTo( right.Process.ProcessId );

	private static ulong ObservedOrZero( ProcObservedValue<ulong> value ) => value.HasValue ? value.Value : 0UL;
	private static int ObservedNice( ProcProcessSnapshot process ) => process.NiceValue.HasValue ? process.NiceValue.Value : int.MaxValue;

	private static bool IsState( TopTaskRow row, ProcProcessState state ) =>
		row.Process.State.HasValue && row.Process.State.Value == state;

	private static TopLineStyle TaskLineStyle(
		TopTaskRow row,
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( row );
		ArgumentNullException.ThrowIfNull( state );

		if (
			!state.HighlightRunning
			|| !IsState( row, ProcProcessState.Running )
		) {
			return TopLineStyle.Default;
		}
		if ( state.HighlightBold ) {
			return TopLineStyle.HighlightBold;
		}
		return TopLineStyle.HighlightReverse;
	}

	private static IReadOnlyList<TopRenderSpan>? SortColumnSpans(
		string visibleLine,
		TopFormattedTaskLine formatted,
		TopRuntimeState state,
		TopLineStyle lineStyle
	) {
		ArgumentNullException.ThrowIfNull( visibleLine );
		ArgumentNullException.ThrowIfNull( state );

		if ( !state.HighlightSortColumn ) {
			return null;
		}
		if (
			0 > formatted.SortFieldStart
			|| 0 >= formatted.SortFieldLength
		) {
			return null;
		}

		int visibleLength = CountRunes( visibleLine );
		int visibleStart = state.HorizontalOffset;
		int fieldStart = formatted.SortFieldStart;
		int fieldEnd = fieldStart + formatted.SortFieldLength;
		int visibleEnd = visibleStart + visibleLength;
		if (
			fieldEnd <= visibleStart
			|| visibleEnd <= fieldStart
		) {
			return null;
		}

		int relativeStart = Math.Max(
			0,
			fieldStart - visibleStart
		);
		int relativeEnd = Math.Min(
			visibleLength,
			fieldEnd - visibleStart
		);
		int start = StringIndexAtRuneOffset(
			visibleLine,
			relativeStart
		);
		int end = StringIndexAtRuneOffset(
			visibleLine,
			relativeEnd
		);
		if ( end <= start ) {
			return null;
		}

		return [
			new TopRenderSpan(
				start,
				end - start,
				SortColumnStyle(
					state,
					lineStyle
				),
				ForegroundColor(
					state,
					state.Colors.TaskAccent
				)
			)
		];
	}

	private static TopLineStyle SortColumnStyle(
		TopRuntimeState state,
		TopLineStyle lineStyle
	) {
		ArgumentNullException.ThrowIfNull( state );

		TopLineStyle preferred = ( state.HighlightBold )
			? TopLineStyle.HighlightBold
			: TopLineStyle.HighlightReverse
		;
		if ( lineStyle != preferred ) {
			return preferred;
		}
		return ( state.HighlightBold )
			? TopLineStyle.HighlightReverse
			: TopLineStyle.HighlightBold
		;
	}

	private static int CountRunes(
		string text
	) {
		ArgumentNullException.ThrowIfNull( text );

		int result = 0;
		foreach ( Rune rune in text.EnumerateRunes() ) {
			_ = rune;
			result++;
		}
		return result;
	}

	private static int StringIndexAtRuneOffset(
		string text,
		int runeOffset
	) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentOutOfRangeException.ThrowIfNegative( runeOffset );

		int stringIndex = 0;
		int runeIndex = 0;
		foreach ( Rune rune in text.EnumerateRunes() ) {
			if ( runeOffset <= runeIndex ) {
				break;
			}
			stringIndex += rune.Utf16SequenceLength;
			runeIndex++;
		}
		return stringIndex;
	}

	private static string StateCode( ProcProcessSnapshot process ) {
		if ( !process.State.HasValue ) {
			return "?";
		}
		return process.State.Value switch {
			ProcProcessState.Running => "R",
			ProcProcessState.Sleeping => "S",
			ProcProcessState.DiskSleep => "D",
			ProcProcessState.Stopped => "T",
			ProcProcessState.TracingStop => "t",
			ProcProcessState.Zombie => "Z",
			ProcProcessState.Dead => "X",
			ProcProcessState.Idle => "I",
			ProcProcessState.Waking => "W",
			ProcProcessState.Parked => "P",
			_ => "?"
		};
	}

	private static string FormatCommand( ProcProcessSnapshot process, bool commandLine ) {
		if ( commandLine
			&& process.CommandLineArguments.HasValue
			&& 0 < process.CommandLineArguments.Value.Count ) {
			return string.Join( " ", process.CommandLineArguments.Value );
		}
		return process.CommandName.HasValue
			? process.CommandName.Value
			: process.ProcessId.ToString( CultureInfo.InvariantCulture );
	}

	private static string TruncateUser( string user ) => LimitRunes( user, 8 );

	private static string FormatCpuTime( double? seconds ) {
		if ( !seconds.HasValue || 0.0 > seconds.Value ) {
			return "?";
		}
		long minutes = (long)( seconds.Value / 60.0 );
		double remainder = seconds.Value - ( minutes * 60.0 );
		return string.Format(
			CultureInfo.InvariantCulture,
			"{0}:{1:00.00}",
			minutes,
			remainder
		);
	}

	private static string FormatUptime( TimeSpan uptime ) {
		if ( TimeSpan.Zero > uptime ) {
			uptime = TimeSpan.Zero;
		}
		if ( 0 < uptime.Days ) {
			return string.Format(
				CultureInfo.InvariantCulture,
				"up {0} day{1}, {2}:{3:00}",
				uptime.Days,
				1 == uptime.Days ? string.Empty : "s",
				uptime.Hours,
				uptime.Minutes
			);
		}
		return string.Format(
			CultureInfo.InvariantCulture,
			"up {0}:{1:00}",
			(int)uptime.TotalHours,
			uptime.Minutes
		);
	}

	private static string FormatMemory( ulong? bytes, TopMemoryScale scale ) {
		if ( !bytes.HasValue ) {
			return "?";
		}
		double value = bytes.Value / ScaleDivisor( scale );
		return value.ToString( "0.0", CultureInfo.InvariantCulture );
	}

	private static string FormatTaskMemory(
		ProcObservedValue<ulong> bytes,
		TopMemoryScale scale
	) {
		if ( !bytes.HasValue ) {
			return "?";
		}
		double value = bytes.Value / ScaleDivisor( scale );
		return value switch {
			>= 100000.0 => value.ToString( "0", CultureInfo.InvariantCulture ),
			>= 1000.0 => value.ToString( "0.0", CultureInfo.InvariantCulture ),
			_ => value.ToString( "0.00", CultureInfo.InvariantCulture )
		};
	}

	private static string ScaleLabel( TopMemoryScale scale ) => scale switch {
		TopMemoryScale.Kibibytes => "KiB",
		TopMemoryScale.Mebibytes => "MiB",
		TopMemoryScale.Gibibytes => "GiB",
		TopMemoryScale.Tebibytes => "TiB",
		TopMemoryScale.Pebibytes => "PiB",
		_ => "EiB"
	};

	private static double ScaleDivisor( TopMemoryScale scale ) => scale switch {
		TopMemoryScale.Kibibytes => 1024.0,
		TopMemoryScale.Mebibytes => 1024.0 * 1024.0,
		TopMemoryScale.Gibibytes => 1024.0 * 1024.0 * 1024.0,
		TopMemoryScale.Tebibytes => 1024.0 * 1024.0 * 1024.0 * 1024.0,
		TopMemoryScale.Pebibytes => 1024.0 * 1024.0 * 1024.0 * 1024.0 * 1024.0,
		_ => 1024.0 * 1024.0 * 1024.0 * 1024.0 * 1024.0 * 1024.0
	};

	private static ulong? SumNullable( ulong? left, ulong? right ) {
		if ( !left.HasValue && !right.HasValue ) {
			return null;
		}
		return SaturatingAdd( left ?? 0UL, right ?? 0UL );
	}

	private static ulong SaturatingAdd( ulong left, ulong right ) =>
		ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

	private static ulong SaturatingSubtract( ulong left, ulong right ) =>
		left < right ? 0UL : left - right;

	private static string SliceForDisplay( string text, int offset, int width ) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentOutOfRangeException.ThrowIfNegative( offset );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );
		var builder = new StringBuilder();
		int position = 0;
		int written = 0;
		foreach ( Rune rune in text.EnumerateRunes() ) {
			if ( position++ < offset ) {
				continue;
			}
			if ( written >= width ) {
				break;
			}
			builder.Append( rune.ToString() );
			written++;
		}
		return builder.ToString();
	}

	private static string LimitRunes( string text, int width ) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );
		return SliceForDisplay( text, 0, width );
	}
}
