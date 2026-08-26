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
	private static readonly string[] HelpLines = [
		"top help -- core interactive commands",
		" q              quit",
		" Enter/Space    refresh now",
		" P/M/N/T        sort by CPU, memory, PID, or time",
		" c              toggle command name / command line",
		" H              toggle thread display",
		" i              toggle idle-task suppression",
		" V              toggle process forest",
		" I              toggle Irix/Solaris CPU normalization",
		" E / e          cycle summary / task memory scale",
		" d or s         change refresh delay",
		" u / U          filter by effective / any observed user",
		" k              signal a process",
		" r              change a process nice value",
		" arrows/PgUp    scroll task display",
		" Home/End       jump to first/last task",
		" =              clear PID/user filters and scrolling",
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

		if ( state.ShowHelp ) {
			return RenderHelp( state, dimensions );
		}

		List<TopTaskRow> tasks = SelectAndOrderTasks( sample, state );
		int footerRows = state.Prompt is null && string.IsNullOrEmpty( state.Message ) ? 0 : 1;
		int availableTaskRows = Math.Max(
			0,
			dimensions.Rows - SummaryRows - TableHeaderRows - footerRows
		);
		int maxOffset = Math.Max( 0, tasks.Count - availableTaskRows );
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
			SliceForDisplay( BuildHeader(), state.HorizontalOffset, dimensions.Columns ),
			TopLineStyle.Header
		) );

		for ( int index = state.VerticalOffset;
			index < tasks.Count && lines.Count < dimensions.Rows - footerRows;
			index++ ) {
			string line = FormatTaskLine( tasks[ index ], state, sample.ProcessorCount );
			lines.Add( new TopRenderLine(
				SliceForDisplay( line, state.HorizontalOffset, dimensions.Columns )
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
		return new TopRenderFrame( lines, dimensions.Columns, dimensions.Rows );
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
		lines.Add( LimitRunes( BuildHeader(), width ) );
		foreach ( TopTaskRow task in SelectAndOrderTasks( sample, state ) ) {
			lines.Add( LimitRunes(
				FormatTaskLine( task, state, sample.ProcessorCount ),
				width
			) );
		}
		return lines;
	}

	internal static IReadOnlyList<string> ListFields() => [
		"PID      process or task identifier",
		"USER     effective user name or numeric identifier",
		"PR       portable priority derived from the observed nice value",
		"NI       observed nice value",
		"VIRT     virtual memory size",
		"RES      resident memory size",
		"SHR      shared resident memory (shown as unavailable until observed)",
		"S        task state",
		"%CPU     interval CPU utilization",
		"%MEM     resident memory percentage",
		"TIME+    cumulative observed CPU time",
		"COMMAND  command name or command line"
	];

	internal static bool TryParseSortField( string text, out TopSortField field ) {
		ArgumentNullException.ThrowIfNull( text );
		string normalized = text.Trim().TrimStart( '+', '-' ).ToUpperInvariant();
		field = normalized switch {
			"%CPU" or "CPU" or "P" => TopSortField.Cpu,
			"%MEM" or "MEM" or "M" => TopSortField.Memory,
			"PID" or "N" => TopSortField.Pid,
			"TIME" or "TIME+" or "T" => TopSortField.Time,
			"VIRT" => TopSortField.VirtualMemory,
			"RES" => TopSortField.ResidentMemory,
			"USER" => TopSortField.User,
			"COMMAND" or "CMD" => TopSortField.Command,
			"NI" or "NICE" => TopSortField.Nice,
			"S" or "STATE" => TopSortField.State,
			_ => default
		};
		return normalized is "%CPU" or "CPU" or "P"
			or "%MEM" or "MEM" or "M"
			or "PID" or "N"
			or "TIME" or "TIME+" or "T"
			or "VIRT" or "RES" or "USER"
			or "COMMAND" or "CMD" or "NI" or "NICE"
			or "S" or "STATE";
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
		return new TopRenderFrame( lines, dimensions.Columns, dimensions.Rows );
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

	private static string BuildHeader() =>
		"    PID USER       PR  NI     VIRT      RES      SHR S  %CPU %MEM     TIME+ COMMAND";

	private static string FormatTaskLine(
		TopTaskRow row,
		TopRuntimeState state,
		int processorCount
	) {
		ProcProcessSnapshot process = row.Process;
		double cpu = state.IrixMode
			? row.CpuPercentIrix
			: row.CpuPercentIrix / Math.Max( 1, processorCount );
		string command = FormatCommand( process, state.ShowCommandLine );
		if ( state.Forest && 0 < row.ForestDepth ) {
			command = new string( ' ', row.ForestDepth * 2 ) + "\\_ " + command;
		}
		string priority = process.NiceValue.HasValue
			? ( 20 + process.NiceValue.Value ).ToString( CultureInfo.InvariantCulture )
			: "?";
		string nice = process.NiceValue.HasValue
			? process.NiceValue.Value.ToString( CultureInfo.InvariantCulture )
			: "?";
		return string.Format(
			CultureInfo.InvariantCulture,
			"{0,7} {1,-8} {2,3} {3,3} {4,8} {5,8} {6,8} {7,1} {8,5:0.0} {9,4:0.0} {10,9} {11}",
			process.ProcessId,
			TruncateUser( row.User ),
			priority,
			nice,
			FormatTaskMemory( process.VirtualMemoryBytes, state.TaskScale ),
			FormatTaskMemory( process.ResidentMemoryBytes, state.TaskScale ),
			"-",
			StateCode( process ),
			cpu,
			row.MemoryPercent,
			FormatCpuTime( row.CpuSeconds ),
			command
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
		Comparison<TopTaskRow> comparison = SortComparison( state.SortField );
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

	private static Comparison<TopTaskRow> SortComparison( TopSortField field ) => field switch {
		TopSortField.Memory => ( left, right ) => Descending( left.MemoryPercent, right.MemoryPercent, left, right ),
		TopSortField.Pid => ( left, right ) => left.Process.ProcessId.CompareTo( right.Process.ProcessId ),
		TopSortField.Time => ( left, right ) => Descending( left.CpuSeconds ?? 0.0, right.CpuSeconds ?? 0.0, left, right ),
		TopSortField.VirtualMemory => ( left, right ) => Descending( ObservedOrZero( left.Process.VirtualMemoryBytes ), ObservedOrZero( right.Process.VirtualMemoryBytes ), left, right ),
		TopSortField.ResidentMemory => ( left, right ) => Descending( ObservedOrZero( left.Process.ResidentMemoryBytes ), ObservedOrZero( right.Process.ResidentMemoryBytes ), left, right ),
		TopSortField.User => ( left, right ) => TieBreak( string.Compare( left.User, right.User, StringComparison.Ordinal ), left, right ),
		TopSortField.Command => ( left, right ) => TieBreak( string.Compare( FormatCommand( left.Process, false ), FormatCommand( right.Process, false ), StringComparison.Ordinal ), left, right ),
		TopSortField.Nice => ( left, right ) => TieBreak( ObservedNice( left.Process ).CompareTo( ObservedNice( right.Process ) ), left, right ),
		TopSortField.State => ( left, right ) => TieBreak( string.Compare( StateCode( left.Process ), StateCode( right.Process ), StringComparison.Ordinal ), left, right ),
		_ => ( left, right ) => Descending( left.CpuPercentIrix, right.CpuPercentIrix, left, right )
	};

	private static int Descending<T>( T left, T right, TopTaskRow leftRow, TopTaskRow rightRow )
		where T : IComparable<T> {
		int result = right.CompareTo( left );
		return TieBreak( result, leftRow, rightRow );
	}

	private static int TieBreak( int comparison, TopTaskRow left, TopTaskRow right ) =>
		0 != comparison ? comparison : left.Process.ProcessId.CompareTo( right.Process.ProcessId );

	private static ulong ObservedOrZero( ProcObservedValue<ulong> value ) => value.HasValue ? value.Value : 0UL;
	private static int ObservedNice( ProcProcessSnapshot process ) => process.NiceValue.HasValue ? process.NiceValue.Value : int.MaxValue;

	private static bool IsState( TopTaskRow row, ProcProcessState state ) =>
		row.Process.State.HasValue && row.Process.State.Value == state;

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
