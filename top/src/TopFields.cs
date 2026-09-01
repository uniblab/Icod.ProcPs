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

using Icod.ProcPs.Shared;

/// <summary>Identifies one task field known to top.</summary>
internal enum TopFieldId {
	Pid,
	ParentProcessId,
	EffectiveUserId,
	User,
	RealUserId,
	EffectiveGroupId,
	ProcessGroupId,
	Terminal,
	ForegroundProcessGroupId,
	SessionId,
	Priority,
	Nice,
	ThreadCount,
	VirtualMemory,
	ResidentMemory,
	SharedMemory,
	State,
	Cpu,
	Memory,
	Time,
	Command
}

/// <summary>Formats one task field for the current top presentation state.</summary>
internal delegate string TopFieldFormatter(
	TopTaskRow row,
	TopRuntimeState state,
	int processorCount
);

/// <summary>Describes one task field's stable presentation and sort behavior.</summary>
internal sealed class TopFieldDefinition {
	internal TopFieldDefinition(
		TopFieldId id,
		string name,
		string description,
		int width,
		bool numeric,
		bool defaultVisible,
		TopFieldFormatter formatter,
		Comparison<TopTaskRow> highToLowComparison
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		ArgumentException.ThrowIfNullOrWhiteSpace( description );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );
		ArgumentNullException.ThrowIfNull( formatter );
		ArgumentNullException.ThrowIfNull( highToLowComparison );

		this.Id = id;
		this.Name = name;
		this.Description = description;
		this.Width = width;
		this.Numeric = numeric;
		this.DefaultVisible = defaultVisible;
		this.Formatter = formatter;
		this.HighToLowComparison = highToLowComparison;
	}

	internal TopFieldId Id { get; }
	internal string Name { get; }
	internal string Description { get; }
	internal int Width { get; }
	internal bool Numeric { get; }
	internal bool DefaultVisible { get; }
	internal TopFieldFormatter Formatter { get; }
	internal Comparison<TopTaskRow> HighToLowComparison { get; }
}

/// <summary>Owns the stable task-field identities and behavior used by top.</summary>
internal static class TopFieldCatalog {
	private static readonly TopFieldDefinition[] FieldDefinitions = [
		new(
			TopFieldId.Pid,
			"PID",
			"process or task identifier",
			7,
			numeric: true,
			defaultVisible: true,
			formatter: ( row, state, processorCount ) =>
				row.Process.ProcessId.ToString(
					System.Globalization.CultureInfo.InvariantCulture
				),
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					left.Process.ProcessId,
					right.Process.ProcessId,
					left,
					right
				)
		),
		new(
			TopFieldId.ParentProcessId,
			"PPID",
			"parent process identifier",
			7,
			numeric: true,
			defaultVisible: false,
			formatter: ( row, state, processorCount ) =>
				FormatObserved( row.Process.ParentProcessId ),
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					SortObserved( left.Process.ParentProcessId ),
					SortObserved( right.Process.ParentProcessId ),
					left,
					right
				)
		),
		new(
			TopFieldId.EffectiveUserId,
			"UID",
			"effective user identifier",
			5,
			numeric: true,
			defaultVisible: false,
			formatter: ( row, state, processorCount ) =>
				FormatObserved( row.Process.EffectiveUserId ),
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					SortObserved( left.Process.EffectiveUserId ),
					SortObserved( right.Process.EffectiveUserId ),
					left,
					right
				)
		),
		new(
			TopFieldId.User,
			"USER",
			"effective user name or numeric identifier",
			8,
			numeric: false,
			defaultVisible: true,
			formatter: ( row, state, processorCount ) =>
				TopRenderer.FieldTruncateUser(
					row.User,
					state
				),
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					left.User,
					right.User,
					left,
					right
				)
		),
		new(
			TopFieldId.RealUserId,
			"RUID",
			"real user identifier",
			5,
			numeric: true,
			defaultVisible: false,
			formatter: ( row, state, processorCount ) =>
				FormatObserved( row.Process.RealUserId ),
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					SortObserved( left.Process.RealUserId ),
					SortObserved( right.Process.RealUserId ),
					left,
					right
				)
		),
		new(
			TopFieldId.EffectiveGroupId,
			"GID",
			"effective group identifier",
			5,
			numeric: true,
			defaultVisible: false,
			formatter: ( row, state, processorCount ) =>
				FormatObserved( row.Process.EffectiveGroupId ),
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					SortObserved( left.Process.EffectiveGroupId ),
					SortObserved( right.Process.EffectiveGroupId ),
					left,
					right
				)
		),
		new(
			TopFieldId.ProcessGroupId,
			"PGRP",
			"process group identifier",
			7,
			numeric: true,
			defaultVisible: false,
			formatter: ( row, state, processorCount ) =>
				FormatObserved( row.Process.ProcessGroupId ),
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					SortObserved( left.Process.ProcessGroupId ),
					SortObserved( right.Process.ProcessGroupId ),
					left,
					right
				)
		),
		new(
			TopFieldId.Terminal,
			"TTY",
			"controlling terminal",
			8,
			numeric: false,
			defaultVisible: false,
			formatter: ( row, state, processorCount ) =>
				FormatTerminal( row.Process.Terminal ),
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					FormatTerminal( left.Process.Terminal ),
					FormatTerminal( right.Process.Terminal ),
					left,
					right
				)
		),
		new(
			TopFieldId.ForegroundProcessGroupId,
			"TPGID",
			"terminal foreground process group identifier",
			7,
			numeric: true,
			defaultVisible: false,
			formatter: ( row, state, processorCount ) =>
				FormatObserved( row.Process.ForegroundProcessGroupId ),
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					SortObserved( left.Process.ForegroundProcessGroupId ),
					SortObserved( right.Process.ForegroundProcessGroupId ),
					left,
					right
				)
		),
		new(
			TopFieldId.SessionId,
			"SID",
			"session identifier",
			7,
			numeric: true,
			defaultVisible: false,
			formatter: ( row, state, processorCount ) =>
				FormatObserved( row.Process.SessionId ),
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					SortObserved( left.Process.SessionId ),
					SortObserved( right.Process.SessionId ),
					left,
					right
				)
		),
		new(
			TopFieldId.Priority,
			"PR",
			"scheduler priority (unavailable until observed)",
			3,
			numeric: true,
			defaultVisible: true,
			formatter: ( row, state, processorCount ) => "-",
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					0,
					0,
					left,
					right
				)
		),
		new(
			TopFieldId.Nice,
			"NI",
			"observed nice value",
			3,
			numeric: true,
			defaultVisible: true,
			formatter: ( row, state, processorCount ) => {
				if ( !row.Process.NiceValue.HasValue ) {
					return "?";
				}
				return row.Process.NiceValue.Value.ToString(
					System.Globalization.CultureInfo.InvariantCulture
				);
			},
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					TopRenderer.FieldObservedNice( left.Process ),
					TopRenderer.FieldObservedNice( right.Process ),
					left,
					right
				)
		),
		new(
			TopFieldId.ThreadCount,
			"nTH",
			"number of threads",
			3,
			numeric: true,
			defaultVisible: false,
			formatter: ( row, state, processorCount ) =>
				FormatObserved( row.Process.ThreadCount ),
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					SortObserved( left.Process.ThreadCount ),
					SortObserved( right.Process.ThreadCount ),
					left,
					right
				)
		),
		new(
			TopFieldId.VirtualMemory,
			"VIRT",
			"virtual memory size",
			8,
			numeric: true,
			defaultVisible: true,
			formatter: ( row, state, processorCount ) => {
				if (
					state.SuppressZeros
					&& row.Process.VirtualMemoryBytes.HasValue
					&& 0UL == row.Process.VirtualMemoryBytes.Value
				) {
					return string.Empty;
				}
				return TopRenderer.FieldTaskMemory(
					row.Process.VirtualMemoryBytes,
					state.TaskScale
				);
			},
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					TopRenderer.FieldObservedOrZero( left.Process.VirtualMemoryBytes ),
					TopRenderer.FieldObservedOrZero( right.Process.VirtualMemoryBytes ),
					left,
					right
				)
		),
		new(
			TopFieldId.ResidentMemory,
			"RES",
			"resident memory size",
			8,
			numeric: true,
			defaultVisible: true,
			formatter: ( row, state, processorCount ) => {
				if (
					state.SuppressZeros
					&& row.Process.ResidentMemoryBytes.HasValue
					&& 0UL == row.Process.ResidentMemoryBytes.Value
				) {
					return string.Empty;
				}
				return TopRenderer.FieldTaskMemory(
					row.Process.ResidentMemoryBytes,
					state.TaskScale
				);
			},
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					TopRenderer.FieldObservedOrZero( left.Process.ResidentMemoryBytes ),
					TopRenderer.FieldObservedOrZero( right.Process.ResidentMemoryBytes ),
					left,
					right
				)
		),
		new(
			TopFieldId.SharedMemory,
			"SHR",
			"shared resident memory (shown as unavailable until observed)",
			8,
			numeric: true,
			defaultVisible: true,
			formatter: ( row, state, processorCount ) => "-",
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					0UL,
					0UL,
					left,
					right
				)
		),
		new(
			TopFieldId.State,
			"S",
			"task state",
			1,
			numeric: false,
			defaultVisible: true,
			formatter: ( row, state, processorCount ) =>
				TopRenderer.FieldStateCode( row.Process ),
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					TopRenderer.FieldStateCode( left.Process ),
					TopRenderer.FieldStateCode( right.Process ),
					left,
					right
				)
		),
		new(
			TopFieldId.Cpu,
			"%CPU",
			"interval CPU utilization",
			5,
			numeric: true,
			defaultVisible: true,
			formatter: ( row, state, processorCount ) => {
				double cpu = row.CpuPercentIrix;
				if ( !state.IrixMode ) {
					cpu /= Math.Max(
						1,
						processorCount
					);
				}
				if ( state.SuppressZeros && 0.0 == cpu ) {
					return string.Empty;
				}
				return cpu.ToString(
					"0.0",
					System.Globalization.CultureInfo.InvariantCulture
				);
			},
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					left.CpuPercentIrix,
					right.CpuPercentIrix,
					left,
					right
				)
		),
		new(
			TopFieldId.Memory,
			"%MEM",
			"resident memory percentage",
			4,
			numeric: true,
			defaultVisible: true,
			formatter: ( row, state, processorCount ) => {
				if ( state.SuppressZeros && 0.0 == row.MemoryPercent ) {
					return string.Empty;
				}
				return row.MemoryPercent.ToString(
					"0.0",
					System.Globalization.CultureInfo.InvariantCulture
				);
			},
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					left.MemoryPercent,
					right.MemoryPercent,
					left,
					right
				)
		),
		new(
			TopFieldId.Time,
			"TIME+",
			"cumulative observed CPU time",
			9,
			numeric: true,
			defaultVisible: true,
			formatter: ( row, state, processorCount ) => {
				if (
					state.SuppressZeros
					&& row.CpuSeconds.HasValue
					&& 0.0 == row.CpuSeconds.Value
				) {
					return string.Empty;
				}
				return TopRenderer.FieldCpuTime( row.CpuSeconds );
			},
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					left.CpuSeconds ?? 0.0,
					right.CpuSeconds ?? 0.0,
					left,
					right
				)
		),
		new(
			TopFieldId.Command,
			"COMMAND",
			"command name or command line",
			7,
			numeric: false,
			defaultVisible: true,
			formatter: ( row, state, processorCount ) => {
				string command = TopRenderer.FieldCommand(
					row.Process,
					state.ShowCommandLine
				);
				if ( state.Forest && 0 < row.ForestDepth ) {
					command = new string(
						' ',
						row.ForestDepth * 2
					) + "\\_ " + command;
				}
				return command;
			},
			highToLowComparison: ( left, right ) =>
				TopRenderer.CompareFieldDescending(
					TopRenderer.FieldCommand( left.Process, false ),
					TopRenderer.FieldCommand( right.Process, false ),
					left,
					right
				)
		)
	];

	private static string FormatObserved(
		ProcObservedValue<int> value
	) {
		return value.HasValue
			? value.Value.ToString(
				System.Globalization.CultureInfo.InvariantCulture
			)
			: "?"
		;
	}

	private static string FormatObserved(
		ProcObservedValue<uint> value
	) {
		return value.HasValue
			? value.Value.ToString(
				System.Globalization.CultureInfo.InvariantCulture
			)
			: "?"
		;
	}

	private static long SortObserved(
		ProcObservedValue<int> value
	) {
		return value.HasValue
			? value.Value
			: long.MinValue
		;
	}

	private static long SortObserved(
		ProcObservedValue<uint> value
	) {
		return value.HasValue
			? value.Value
			: long.MinValue
		;
	}

	private static string FormatTerminal(
		ProcObservedValue<ProcTerminalInfo> value
	) {
		if ( !value.HasValue ) {
			return "?";
		}
		string? name = value.Value.Name;
		if ( string.IsNullOrWhiteSpace( name ) ) {
			return "?";
		}
		const string devicePrefix = "/dev/";
		return name.StartsWith(
			devicePrefix,
			StringComparison.Ordinal
		)
			? name[
				devicePrefix.Length..
			]
			: name
		;
	}

	internal static IReadOnlyList<TopFieldDefinition> Definitions => FieldDefinitions;

	internal static List<TopFieldId> CreateDefaultOrder() {
		var result = new List<TopFieldId>( FieldDefinitions.Length );
		foreach ( TopFieldDefinition definition in FieldDefinitions ) {
			result.Add( definition.Id );
		}
		return result;
	}

	internal static HashSet<TopFieldId> CreateDefaultVisible() {
		var result = new HashSet<TopFieldId>();
		foreach ( TopFieldDefinition definition in FieldDefinitions ) {
			if ( definition.DefaultVisible ) {
				result.Add( definition.Id );
			}
		}
		return result;
	}

	internal static TopFieldDefinition Get(
		TopFieldId field
	) {
		int index = (int)field;
		if ( 0 > index || FieldDefinitions.Length <= index ) {
			throw new ArgumentOutOfRangeException( nameof( field ) );
		}
		return FieldDefinitions[ index ];
	}

	internal static bool TryParse(
		string text,
		out TopFieldId field
	) {
		ArgumentNullException.ThrowIfNull( text );

		switch ( text.Trim().ToUpperInvariant() ) {
			case "%CPU":
			case "CPU":
			case "P":
				field = TopFieldId.Cpu;
				return true;
			case "%MEM":
			case "MEM":
			case "M":
				field = TopFieldId.Memory;
				return true;
			case "PID":
			case "N":
				field = TopFieldId.Pid;
				return true;
			case "PPID":
				field = TopFieldId.ParentProcessId;
				return true;
			case "UID":
				field = TopFieldId.EffectiveUserId;
				return true;
			case "RUID":
				field = TopFieldId.RealUserId;
				return true;
			case "GID":
				field = TopFieldId.EffectiveGroupId;
				return true;
			case "PGRP":
				field = TopFieldId.ProcessGroupId;
				return true;
			case "TTY":
				field = TopFieldId.Terminal;
				return true;
			case "TPGID":
				field = TopFieldId.ForegroundProcessGroupId;
				return true;
			case "SID":
				field = TopFieldId.SessionId;
				return true;
			case "NTH":
				field = TopFieldId.ThreadCount;
				return true;
			case "TIME":
			case "TIME+":
			case "T":
				field = TopFieldId.Time;
				return true;
			case "VIRT":
				field = TopFieldId.VirtualMemory;
				return true;
			case "RES":
				field = TopFieldId.ResidentMemory;
				return true;
			case "SHR":
				field = TopFieldId.SharedMemory;
				return true;
			case "USER":
				field = TopFieldId.User;
				return true;
			case "COMMAND":
			case "CMD":
				field = TopFieldId.Command;
				return true;
			case "PR":
			case "PRIORITY":
				field = TopFieldId.Priority;
				return true;
			case "NI":
			case "NICE":
				field = TopFieldId.Nice;
				return true;
			case "S":
			case "STATE":
				field = TopFieldId.State;
				return true;
			default:
				field = default;
				return false;
		}
	}

	internal static bool TryParseHeaderName(
		string text,
		out TopFieldId field
	) {
		ArgumentNullException.ThrowIfNull( text );

		foreach ( TopFieldDefinition definition in FieldDefinitions ) {
			if ( string.Equals(
				definition.Name,
				text,
				StringComparison.Ordinal
			) ) {
				field = definition.Id;
				return true;
			}
		}
		field = default;
		return false;
	}
}
