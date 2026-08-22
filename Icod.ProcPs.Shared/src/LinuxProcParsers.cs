namespace Icod.ProcPs.Shared;

using System.Globalization;
using System.Text;

/// <summary>Represents the reusable subset of Linux <c>/proc/PID/stat</c> fields required by ProcPs commands.</summary>
public sealed class LinuxProcStatRecord {
	/// <summary>Gets the command name enclosed by parentheses in procfs.</summary>
	public string CommandName { get; }
	/// <summary>Gets the one-character Linux task state.</summary>
	public char State { get; }
	/// <summary>Gets the parent process identifier.</summary>
	public int ParentProcessId { get; }
	/// <summary>Gets the process-group identifier.</summary>
	public int ProcessGroupId { get; }
	/// <summary>Gets the session identifier.</summary>
	public int SessionId { get; }
	/// <summary>Gets the raw controlling-terminal device number.</summary>
	public int TerminalDeviceNumber { get; }
	/// <summary>Gets the foreground process-group identifier for the controlling terminal.</summary>
	public int TerminalForegroundProcessGroupId { get; }
	/// <summary>Gets user CPU ticks.</summary>
	public ulong UserCpuTicks { get; }
	/// <summary>Gets system CPU ticks.</summary>
	public ulong SystemCpuTicks { get; }
	/// <summary>Gets the kernel scheduling priority value.</summary>
	public long Priority { get; }
	/// <summary>Gets the nice value.</summary>
	public int NiceValue { get; }
	/// <summary>Gets the task's thread count.</summary>
	public int ThreadCount { get; }
	/// <summary>Gets the process start tick.</summary>
	public ulong StartTimeTicks { get; }
	/// <summary>Gets virtual-memory size in bytes.</summary>
	public ulong VirtualMemoryBytes { get; }
	/// <summary>Gets resident-set size in pages.</summary>
	public long ResidentSetPages { get; }

	/// <summary>Initializes a parsed Linux stat record.</summary>
	public LinuxProcStatRecord(
		string commandName,
		char state,
		int parentProcessId,
		int processGroupId,
		int sessionId,
		int terminalDeviceNumber,
		ulong userCpuTicks,
		ulong systemCpuTicks,
		long priority,
		int niceValue,
		int threadCount,
		ulong startTimeTicks,
		ulong virtualMemoryBytes,
		long residentSetPages,
		int terminalForegroundProcessGroupId = 0
	) {
		this.CommandName = commandName;
		this.State = state;
		this.ParentProcessId = parentProcessId;
		this.ProcessGroupId = processGroupId;
		this.SessionId = sessionId;
		this.TerminalDeviceNumber = terminalDeviceNumber;
		this.TerminalForegroundProcessGroupId = terminalForegroundProcessGroupId;
		this.UserCpuTicks = userCpuTicks;
		this.SystemCpuTicks = systemCpuTicks;
		this.Priority = priority;
		this.NiceValue = niceValue;
		this.ThreadCount = threadCount;
		this.StartTimeTicks = startTimeTicks;
		this.VirtualMemoryBytes = virtualMemoryBytes;
		this.ResidentSetPages = residentSetPages;
	}
}

/// <summary>Contains parsed Linux status identifiers and namespace process IDs.</summary>
public sealed class LinuxProcStatusRecord {
	/// <summary>Gets the real user identifier.</summary>
	public uint? RealUserId { get; }
	/// <summary>Gets the effective user identifier.</summary>
	public uint? EffectiveUserId { get; }
	/// <summary>Gets the real group identifier.</summary>
	public uint? RealGroupId { get; }
	/// <summary>Gets the effective group identifier.</summary>
	public uint? EffectiveGroupId { get; }
	/// <summary>Gets nested PID-namespace process identifiers.</summary>
	public IReadOnlyList<int> NamespaceProcessIds { get; }
	/// <summary>Gets all parsed status values keyed by field name.</summary>
	public IReadOnlyDictionary<string, string> Fields { get; }
	/// <summary>Initializes a parsed status record.</summary>
	public LinuxProcStatusRecord(
		uint? realUserId,
		uint? effectiveUserId,
		uint? realGroupId,
		uint? effectiveGroupId,
		IEnumerable<int> namespaceProcessIds,
		IReadOnlyDictionary<string, string> fields
	) {
		this.RealUserId = realUserId;
		this.EffectiveUserId = effectiveUserId;
		this.RealGroupId = realGroupId;
		this.EffectiveGroupId = effectiveGroupId;
		this.NamespaceProcessIds = namespaceProcessIds.ToArray();
		this.Fields = fields;
	}
}

/// <summary>Provides fixture-testable parsers for the Linux procfs formats consumed by ProcPs.</summary>
public static class LinuxProcParsers {
	/// <summary>Parses one Linux <c>/proc/PID/stat</c> record.</summary>
	public static LinuxProcStatRecord ParseProcessStat( string text ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( text );
		var commandStart = text.IndexOf( '(' );
		var commandEnd = text.LastIndexOf( ')' );
		if ( 0 > commandStart || commandEnd <= commandStart || commandEnd + 2 >= text.Length ) {
			throw new FormatException( "Malformed /proc/PID/stat command field." );
		}
		var command = text[ ( commandStart + 1 )..commandEnd ];
		var fields = text[ ( commandEnd + 2 ).. ].Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		if ( 22 > fields.Length ) throw new FormatException( "Malformed /proc/PID/stat field count." );
		return new LinuxProcStatRecord(
			command,
			fields[ 0 ][ 0 ],
			ParseInt32( fields[ 1 ], "ppid" ),
			ParseInt32( fields[ 2 ], "pgrp" ),
			ParseInt32( fields[ 3 ], "session" ),
			ParseInt32( fields[ 4 ], "tty_nr" ),
			ParseUInt64( fields[ 11 ], "utime" ),
			ParseUInt64( fields[ 12 ], "stime" ),
			ParseInt64( fields[ 15 ], "priority" ),
			ParseInt32( fields[ 16 ], "nice" ),
			ParseInt32( fields[ 17 ], "num_threads" ),
			ParseUInt64( fields[ 19 ], "starttime" ),
			ParseUInt64( fields[ 20 ], "vsize" ),
			ParseInt64( fields[ 21 ], "rss" ),
			ParseInt32( fields[ 5 ], "tpgid" )
		);
	}

	/// <summary>Parses Linux <c>/proc/PID/status</c>.</summary>
	public static LinuxProcStatusRecord ParseProcessStatus( string text ) {
		ArgumentNullException.ThrowIfNull( text );
		var fields = new Dictionary<string, string>( StringComparer.Ordinal );
		foreach ( var line in text.Split( '\n' ) ) {
			var separator = line.IndexOf( ':' );
			if ( 0 >= separator ) continue;
			fields[ line[ ..separator ] ] = line[ ( separator + 1 ).. ].Trim();
		}
		var users = ParseUnsignedColumns( fields, "Uid" );
		var groups = ParseUnsignedColumns( fields, "Gid" );
		var namespacePids = Array.Empty<int>();
		if ( fields.TryGetValue( "NSpid", out var nspidText ) ) {
			namespacePids = nspidText.Split( (char[]?)null, StringSplitOptions.RemoveEmptyEntries )
				.Select( value => ParseInt32( value, "NSpid" ) )
				.ToArray();
		}
		return new LinuxProcStatusRecord(
			0 < users.Length ? users[ 0 ] : null,
			1 < users.Length ? users[ 1 ] : null,
			0 < groups.Length ? groups[ 0 ] : null,
			1 < groups.Length ? groups[ 1 ] : null,
			namespacePids,
			fields
		);
	}

	/// <summary>Parses a procfs NUL-delimited byte vector such as <c>cmdline</c>.</summary>
	public static IReadOnlyList<string> ParseNullDelimitedUtf8( ReadOnlySpan<byte> bytes ) {
		var values = new List<string>();
		var start = 0;
		for ( var index = 0; index <= bytes.Length; ++index ) {
			if ( index != bytes.Length && 0 != bytes[ index ] ) continue;
			if ( index > start ) values.Add( Encoding.UTF8.GetString( bytes[ start..index ] ) );
			start = index + 1;
		}
		return values;
	}

	/// <summary>Parses one Linux <c>/proc/PID/maps</c> line.</summary>
	public static ProcMemoryMapEntry ParseMemoryMapLine( string line ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( line );
		var fields = line.Split( ' ', 6, StringSplitOptions.RemoveEmptyEntries );
		if ( 5 > fields.Length ) throw new FormatException( "Malformed /proc/PID/maps line." );
		var range = fields[ 0 ].Split( '-', 2 );
		if ( 2 != range.Length ) throw new FormatException( "Malformed memory-map address range." );
		var start = ulong.Parse( range[ 0 ], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture );
		var end = ulong.Parse( range[ 1 ], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture );
		var offset = ulong.Parse( fields[ 2 ], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture );
		var inode = ulong.Parse( fields[ 4 ], NumberStyles.None, CultureInfo.InvariantCulture );
		return new ProcMemoryMapEntry( start, end, fields[ 1 ], offset, fields[ 3 ], inode, 6 == fields.Length ? fields[ 5 ] : null );
	}

	/// <summary>Maps a Linux task-state character to the shared ProcPs state model.</summary>
	public static ProcProcessState MapProcessState( char state ) => state switch {
		'R' => ProcProcessState.Running,
		'S' => ProcProcessState.Sleeping,
		'D' => ProcProcessState.DiskSleep,
		'T' => ProcProcessState.Stopped,
		't' => ProcProcessState.TracingStop,
		'Z' => ProcProcessState.Zombie,
		'X' or 'x' => ProcProcessState.Dead,
		'I' => ProcProcessState.Idle,
		'W' => ProcProcessState.Waking,
		'P' => ProcProcessState.Parked,
		_ => ProcProcessState.Unknown
	};

	/// <summary>Parses a procfs key/value file whose values are unsigned counters.</summary>
	public static IReadOnlyDictionary<string, ulong> ParseCounterFile( string text ) {
		ArgumentNullException.ThrowIfNull( text );
		var result = new Dictionary<string, ulong>( StringComparer.Ordinal );
		foreach ( var line in text.Split( '\n', StringSplitOptions.RemoveEmptyEntries ) ) {
			var fields = line.Split( (char[]?)null, StringSplitOptions.RemoveEmptyEntries );
			if ( 2 > fields.Length ) continue;
			if ( ulong.TryParse( fields[ 1 ], NumberStyles.None, CultureInfo.InvariantCulture, out var value ) ) result[ fields[ 0 ].TrimEnd( ':' ) ] = value;
		}
		return result;
	}

	/// <summary>Parses Linux <c>/proc/meminfo</c>, converting KiB fields to bytes.</summary>
	public static IReadOnlyDictionary<string, ulong> ParseMemInfo( string text ) {
		ArgumentNullException.ThrowIfNull( text );
		var result = new Dictionary<string, ulong>( StringComparer.Ordinal );
		foreach ( var line in text.Split( '\n', StringSplitOptions.RemoveEmptyEntries ) ) {
			var separator = line.IndexOf( ':' );
			if ( 0 >= separator ) continue;
			var key = line[ ..separator ];
			var fields = line[ ( separator + 1 ).. ].Split( (char[]?)null, StringSplitOptions.RemoveEmptyEntries );
			if ( 0 == fields.Length || !ulong.TryParse( fields[ 0 ], NumberStyles.None, CultureInfo.InvariantCulture, out var value ) ) continue;
			if ( 1 < fields.Length && string.Equals( fields[ 1 ], "kB", StringComparison.OrdinalIgnoreCase ) ) value = checked( value * 1024UL );
			result[ key ] = value;
		}
		return result;
	}

	private static uint[] ParseUnsignedColumns( IReadOnlyDictionary<string, string> fields, string key ) {
		if ( !fields.TryGetValue( key, out var text ) ) return Array.Empty<uint>();
		return text.Split( (char[]?)null, StringSplitOptions.RemoveEmptyEntries )
			.Select( value => uint.Parse( value, NumberStyles.None, CultureInfo.InvariantCulture ) )
			.ToArray();
	}
	private static int ParseInt32( string text, string field ) => int.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value )
		? value
		: throw new FormatException( $"Malformed {field} field." );
	private static long ParseInt64( string text, string field ) => long.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value )
		? value
		: throw new FormatException( $"Malformed {field} field." );
	private static ulong ParseUInt64( string text, string field ) => ulong.TryParse( text, NumberStyles.None, CultureInfo.InvariantCulture, out var value )
		? value
		: throw new FormatException( $"Malformed {field} field." );
}
