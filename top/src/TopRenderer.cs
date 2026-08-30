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
using Icod.ProcPs.Shared;

/// <summary>Builds the terminal-independent screen model used by top.</summary>
internal static class TopRenderer {
	private const int SummaryRows = 5;
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
		" B/b/x/y        emphasis and highlighting",
		" J / j          justify numeric / character columns",
		" f              manage task fields",
		" c              toggle command name / command line",
		" H              toggle thread display",
		" i              toggle idle-task suppression",
		" V              toggle process forest",
		" I              toggle Irix/Solaris CPU normalization",
		" E / e          cycle summary / task memory scale",
		" d or s         change refresh delay",
		" u / U          filter by effective / any observed user",
		" L / &          locate string / locate next",
		" k              signal a process",
		" r              change a process nice value",
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
		if ( 0 >= dimensions.Columns || 0 >= dimensions.Rows ) {
			throw new ArgumentOutOfRangeException( nameof( dimensions ) );
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

		List<TopTaskRow> tasks = SelectAndOrderTasks( sample, state );
		int footerRows = state.Prompt is null && string.IsNullOrEmpty( state.Message )
			? 0
			: 1;
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
		foreach ( string line in BuildSummaryLines( sample, state ) ) {
			lines.Add( new TopRenderLine(
				SliceForDisplay( line, state.HorizontalOffset, dimensions.Columns ),
				TopLineStyle.Summary
			) );
		}
		lines.Add( new TopRenderLine(
			SliceForDisplay( BuildHeader( state ), state.HorizontalOffset, dimensions.Columns ),
			TopLineStyle.Header
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
					TopLineStyle.Prompt
				) );
			} else {
				lines.Add( new TopRenderLine(
					LimitRunes( state.Message ?? string.Empty, dimensions.Columns ),
					TopLineStyle.Message
				) );
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
		var lines = new List<string>();
		lines.AddRange( BuildSummaryLines( sample, state ).Select( line => LimitRunes( line, width ) ) );
		lines.Add( LimitRunes( BuildHeader( state ), width ) );
		foreach ( TopTaskRow task in SelectAndOrderTasks( sample, state ) ) {
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
		int footerRows = state.Prompt is null && string.IsNullOrEmpty( state.Message )
			? 0
			: 1;
		int availableTaskRows = AvailableTaskRows(
			state,
			dimensions,
			footerRows
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
			dimensions.Rows - SummaryRows - TableHeaderRows - footerRows
		);
		if ( 0 < state.MaximumTasks ) {
			result = Math.Min(
				result,
				state.MaximumTasks
			);
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
					$"Fields Management for 1:Def - sort field: {sortDefinition.Name}",
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

	private static IReadOnlyList<string> BuildSummaryLines(
		TopSample sample,
		TopRuntimeState state
	) {
		string taskLabel = state.ShowThreads ? "Threads" : "Tasks";
		IReadOnlyList<TopTaskRow> visible = ApplyFilters( sample.Tasks, state ).ToList().AsReadOnly();
		int running = visible.Count( row => IsState( row, ProcProcessState.Running ) );
		int sleeping = visible.Count( row => IsState( row, ProcProcessState.Sleeping ) );
		int stopped = visible.Count( row => IsState( row, ProcProcessState.Stopped ) || IsState( row, ProcProcessState.TracingStop ) );
		int zombie = visible.Count( row => IsState( row, ProcProcessState.Zombie ) );
		return [
			BuildTopLine( sample ),
			$"{taskLabel}: {visible.Count,5} total, {running,4} running, {sleeping,4} sleeping, {stopped,4} stopped, {zombie,4} zombie",
			BuildCpuLine( sample.CpuSummary, sample.ProcessorCount, state.SingleCpuSummary ),
			BuildMemoryLine( sample.System, state.SummaryScale ),
			BuildSwapLine( sample.System, state.SummaryScale )
		];
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
		bool singleCpuSummary
	) {
		string prefix = singleCpuSummary
			? "%Cpu(s)"
			: $"%Cpu(s/{processorCount})";
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

	private static string BuildMemoryLine(
		ProcSystemSnapshot system,
		TopMemoryScale scale
	) {
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

	private static string BuildSwapLine(
		ProcSystemSnapshot system,
		TopMemoryScale scale
	) {
		if ( !system.Memory.HasValue ) {
			return $"{ScaleLabel( scale )} Swap: unavailable";
		}
		ProcMemoryInfo memory = system.Memory.Value;
		ulong? total = memory.SwapTotalBytes;
		ulong? free = memory.SwapFreeBytes;
		ulong? used = total.HasValue && free.HasValue
			? SaturatingSubtract( total.Value, free.Value )
			: null;
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
					definition.Width,
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

			TopFieldDefinition definition = TopFieldCatalog.Get(
				field
			);
			string fieldText = definition.Formatter(
				row,
				state,
				processorCount
			);
			if ( TopFieldId.Command == field ) {
				fieldText = FormatCommandField(
					fieldText,
					state.CharacterRightJustified
				);
			} else {
				bool leftJustified;
				if ( definition.Numeric ) {
					leftJustified = state.NumericLeftJustified;
				} else {
					leftJustified = !state.CharacterRightJustified;
				}
				fieldText = AlignField(
					fieldText,
					definition.Width,
					leftJustified
				);
			}

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

	private static List<TopTaskRow> SelectAndOrderTasks(
		TopSample sample,
		TopRuntimeState state
	) {
		List<TopTaskRow> tasks = ApplyFilters( sample.Tasks, state ).ToList();
		if ( state.HideIdle ) {
			tasks = tasks.Where( row => 0.0001 < row.CpuPercentIrix || IsState( row, ProcProcessState.Running ) ).ToList();
		}
		Comparison<TopTaskRow> comparison = SortComparison(
			state.SortField,
			state.SortHighToLow
		);
		if ( state.Forest ) {
			return OrderForest( tasks, comparison );
		}
		foreach ( TopTaskRow row in tasks ) {
			row.ForestDepth = 0;
		}
		tasks.Sort( comparison );
		return tasks;
	}

	private static IEnumerable<TopTaskRow> ApplyFilters(
		IReadOnlyList<TopTaskRow> tasks,
		TopRuntimeState state
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
		return filtered;
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
		string user
	) {
		ArgumentNullException.ThrowIfNull( user );
		return TruncateUser( user );
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
