namespace Icod.ProcPs.Shared;

/// <summary>Identifies fields in the reusable ProcPs process-reporting catalog.</summary>
public enum ProcReportFieldKind {
	/// <summary>Unsupported compatibility placeholder.</summary>
	Unsupported,
	/// <summary>Process identifier.</summary>
	Pid,
	/// <summary>Lightweight-process or thread identifier.</summary>
	ThreadId,
	/// <summary>Thread-group identifier.</summary>
	ThreadGroupId,
	/// <summary>Parent process identifier.</summary>
	ParentPid,
	/// <summary>Process-group identifier.</summary>
	ProcessGroup,
	/// <summary>Session identifier.</summary>
	Session,
	/// <summary>Effective user identifier.</summary>
	EffectiveUserId,
	/// <summary>Real user identifier.</summary>
	RealUserId,
	/// <summary>Effective group identifier.</summary>
	EffectiveGroupId,
	/// <summary>Real group identifier.</summary>
	RealGroupId,
	/// <summary>Effective user name.</summary>
	EffectiveUserName,
	/// <summary>Real user name.</summary>
	RealUserName,
	/// <summary>Effective group name.</summary>
	EffectiveGroupName,
	/// <summary>Real group name.</summary>
	RealGroupName,
	/// <summary>Controlling terminal.</summary>
	Terminal,
	/// <summary>Single-character process state.</summary>
	State,
	/// <summary>Composite process status.</summary>
	Stat,
	/// <summary>Nice value.</summary>
	Nice,
	/// <summary>Display priority.</summary>
	Priority,
	/// <summary>Thread count.</summary>
	Threads,
	/// <summary>Resident-memory size.</summary>
	ResidentMemory,
	/// <summary>Virtual-memory size.</summary>
	VirtualMemory,
	/// <summary>Virtual-memory size expressed in pages.</summary>
	SizePages,
	/// <summary>Command with arguments.</summary>
	Command,
	/// <summary>Short command name.</summary>
	CommandName,
	/// <summary>Process environment.</summary>
	Environment,
	/// <summary>Elapsed time.</summary>
	Elapsed,
	/// <summary>Elapsed time in seconds.</summary>
	ElapsedSeconds,
	/// <summary>Accumulated CPU time.</summary>
	CpuTime,
	/// <summary>Lifetime CPU percentage.</summary>
	CpuPercent,
	/// <summary>Resident-memory percentage.</summary>
	MemoryPercent,
	/// <summary>Compact process start time.</summary>
	Start,
	/// <summary>Long process start time.</summary>
	StartLong,
	/// <summary>Cgroup path.</summary>
	Cgroup,
	/// <summary>Container identifier.</summary>
	Container,
	/// <summary>Nested PID-namespace identifiers.</summary>
	NamespacePid,
	/// <summary>IPC namespace identifier.</summary>
	IpcNamespace,
	/// <summary>Mount namespace identifier.</summary>
	MountNamespace,
	/// <summary>Network namespace identifier.</summary>
	NetNamespace,
	/// <summary>PID namespace identifier.</summary>
	PidNamespace,
	/// <summary>User namespace identifier.</summary>
	UserNamespace,
	/// <summary>UTS namespace identifier.</summary>
	UtsNamespace,
	/// <summary>Security label.</summary>
	SecurityLabel,
	/// <summary>Blocked signal mask.</summary>
	SignalBlocked,
	/// <summary>Caught signal mask.</summary>
	SignalCaught,
	/// <summary>Ignored signal mask.</summary>
	SignalIgnored,
	/// <summary>Pending signal mask.</summary>
	SignalPending,
	/// <summary>Inheritable capability mask.</summary>
	CapabilityInheritable,
	/// <summary>Permitted capability mask.</summary>
	CapabilityPermitted,
	/// <summary>Effective capability mask.</summary>
	CapabilityEffective,
	/// <summary>Bounding capability mask.</summary>
	CapabilityBounding,
	/// <summary>Ambient capability mask.</summary>
	CapabilityAmbient
}

/// <summary>Describes one reusable ProcPs process-reporting field.</summary>
public sealed class ProcReportFieldDefinition {
	/// <summary>Gets the field kind.</summary>
	public ProcReportFieldKind Kind { get; }
	/// <summary>Gets the canonical field name.</summary>
	public string Name { get; }
	/// <summary>Gets the default column header.</summary>
	public string Header { get; }
	/// <summary>Gets the default display width.</summary>
	public int Width { get; }
	/// <summary>Gets whether the field is right aligned.</summary>
	public bool RightAligned { get; }

	/// <summary>Initializes a reusable process-reporting field definition.</summary>
	public ProcReportFieldDefinition( ProcReportFieldKind kind, string name, string header, int width, bool rightAligned ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		ArgumentNullException.ThrowIfNull( header );
		if ( 0 > width ) {
			throw new ArgumentOutOfRangeException( nameof( width ) );
		}
		this.Kind = kind;
		this.Name = name;
		this.Header = header;
		this.Width = width;
		this.RightAligned = rightAligned;
	}
}

/// <summary>Supplies the process-reporting field and alias vocabulary shared by ProcPs commands.</summary>
public static class ProcReportFieldCatalog {
	private static readonly IReadOnlyDictionary<string, ProcReportFieldDefinition> AliasesValue = Create();
	private static readonly IReadOnlyList<ProcReportFieldDefinition> DefinitionsValue = AliasesValue.Values.Distinct().ToArray();

	/// <summary>Gets registered canonical names and aliases using case-insensitive lookup.</summary>
	public static IReadOnlyDictionary<string, ProcReportFieldDefinition> Aliases => AliasesValue;

	/// <summary>Gets each registered field definition once.</summary>
	public static IReadOnlyList<ProcReportFieldDefinition> Definitions => DefinitionsValue;

	/// <summary>Attempts to resolve a canonical field name or compatibility alias.</summary>
	public static bool TryGet( string name, out ProcReportFieldDefinition definition ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		if ( AliasesValue.TryGetValue( name, out var value ) ) {
			definition = value;
			return true;
		}
		definition = null!;
		return false;
	}

	private static IReadOnlyDictionary<string, ProcReportFieldDefinition> Create() {
		var fields = new Dictionary<string, ProcReportFieldDefinition>( StringComparer.OrdinalIgnoreCase );
		Add( fields, ProcReportFieldKind.Pid, "pid", "PID", 5, true );
		Add( fields, ProcReportFieldKind.ThreadId, "lwp", "LWP", 5, true, "spid", "tid" );
		Add( fields, ProcReportFieldKind.ThreadGroupId, "tgid", "TGID", 5, true, "thgrpid" );
		Add( fields, ProcReportFieldKind.ParentPid, "ppid", "PPID", 5, true );
		Add( fields, ProcReportFieldKind.ProcessGroup, "pgid", "PGID", 5, true, "pgrp" );
		Add( fields, ProcReportFieldKind.Session, "sid", "SID", 5, true, "sess" );
		Add( fields, ProcReportFieldKind.EffectiveUserId, "uid", "UID", 5, true, "euid" );
		Add( fields, ProcReportFieldKind.RealUserId, "ruid", "RUID", 5, true );
		Add( fields, ProcReportFieldKind.EffectiveGroupId, "gid", "GID", 5, true, "egid" );
		Add( fields, ProcReportFieldKind.RealGroupId, "rgid", "RGID", 5, true );
		Add( fields, ProcReportFieldKind.EffectiveUserName, "user", "USER", 8, false, "uname", "euser" );
		Add( fields, ProcReportFieldKind.RealUserName, "ruser", "RUSER", 8, false );
		Add( fields, ProcReportFieldKind.EffectiveGroupName, "group", "GROUP", 8, false, "egroup" );
		Add( fields, ProcReportFieldKind.RealGroupName, "rgroup", "RGROUP", 8, false );
		Add( fields, ProcReportFieldKind.Terminal, "tty", "TTY", 8, false, "tname", "tt" );
		Add( fields, ProcReportFieldKind.State, "state", "S", 1, false, "s" );
		Add( fields, ProcReportFieldKind.Stat, "stat", "STAT", 4, false );
		Add( fields, ProcReportFieldKind.Nice, "ni", "NI", 3, true, "nice" );
		Add( fields, ProcReportFieldKind.Priority, "pri", "PRI", 3, true, "priority" );
		Add( fields, ProcReportFieldKind.Threads, "nlwp", "NLWP", 4, true, "thcount" );
		Add( fields, ProcReportFieldKind.ResidentMemory, "rss", "RSS", 6, true, "rssize" );
		Add( fields, ProcReportFieldKind.VirtualMemory, "vsz", "VSZ", 7, true, "vsize" );
		Add( fields, ProcReportFieldKind.SizePages, "sz", "SZ", 6, true );
		Add( fields, ProcReportFieldKind.CommandName, "comm", "COMMAND", 15, false, "ucmd", "ucomm" );
		Add( fields, ProcReportFieldKind.Command, "args", "COMMAND", 20, false, "cmd", "command" );
		Add( fields, ProcReportFieldKind.Environment, "environ", "ENVIRONMENT", 20, false, "env" );
		Add( fields, ProcReportFieldKind.Elapsed, "etime", "ELAPSED", 11, true );
		Add( fields, ProcReportFieldKind.ElapsedSeconds, "etimes", "ELAPSED", 7, true );
		Add( fields, ProcReportFieldKind.CpuTime, "time", "TIME", 8, true, "cputime" );
		Add( fields, ProcReportFieldKind.CpuPercent, "pcpu", "%CPU", 4, true, "%cpu", "c" );
		Add( fields, ProcReportFieldKind.MemoryPercent, "pmem", "%MEM", 4, true, "%mem" );
		Add( fields, ProcReportFieldKind.Start, "start", "START", 5, false, "stime" );
		Add( fields, ProcReportFieldKind.StartLong, "lstart", "STARTED", 24, false );
		Add( fields, ProcReportFieldKind.Cgroup, "cgroup", "CGROUP", 20, false );
		Add( fields, ProcReportFieldKind.Container, "container", "CONTAINER", 12, false, "docker" );
		Add( fields, ProcReportFieldKind.NamespacePid, "nspid", "NSPID", 12, false );
		Add( fields, ProcReportFieldKind.IpcNamespace, "ipcns", "IPCNS", 10, true );
		Add( fields, ProcReportFieldKind.MountNamespace, "mntns", "MNTNS", 10, true );
		Add( fields, ProcReportFieldKind.NetNamespace, "netns", "NETNS", 10, true );
		Add( fields, ProcReportFieldKind.PidNamespace, "pidns", "PIDNS", 10, true );
		Add( fields, ProcReportFieldKind.UserNamespace, "userns", "USERNS", 10, true );
		Add( fields, ProcReportFieldKind.UtsNamespace, "utsns", "UTSNS", 10, true );
		Add( fields, ProcReportFieldKind.SecurityLabel, "label", "LABEL", 20, false, "context" );
		Add( fields, ProcReportFieldKind.SignalBlocked, "blocked", "BLOCKED", 16, false, "sig", "sigmask" );
		Add( fields, ProcReportFieldKind.SignalCaught, "caught", "CAUGHT", 16, false );
		Add( fields, ProcReportFieldKind.SignalIgnored, "ignored", "IGNORED", 16, false );
		Add( fields, ProcReportFieldKind.SignalPending, "pending", "PENDING", 16, false );
		Add( fields, ProcReportFieldKind.CapabilityInheritable, "capinh", "CAPINH", 16, false );
		Add( fields, ProcReportFieldKind.CapabilityPermitted, "capprm", "CAPPRM", 16, false );
		Add( fields, ProcReportFieldKind.CapabilityEffective, "capeff", "CAPEFF", 16, false );
		Add( fields, ProcReportFieldKind.CapabilityBounding, "capbnd", "CAPBND", 16, false );
		Add( fields, ProcReportFieldKind.CapabilityAmbient, "capamb", "CAPAMB", 16, false );
		foreach ( var name in new[] { "psr", "wchan", "addr", "f", "flags" } ) {
			Add( fields, ProcReportFieldKind.Unsupported, name, name.ToUpperInvariant(), Math.Max( name.Length, 4 ), false );
		}
		return fields;
	}

	private static void Add(
		IDictionary<string, ProcReportFieldDefinition> fields,
		ProcReportFieldKind kind,
		string name,
		string header,
		int width,
		bool rightAligned,
		params string[] aliases
	) {
		ArgumentNullException.ThrowIfNull( fields );
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		ArgumentNullException.ThrowIfNull( header );
		ArgumentNullException.ThrowIfNull( aliases );
		var definition = new ProcReportFieldDefinition( kind, name, header, width, rightAligned );
		fields[ name ] = definition;
		foreach ( var alias in aliases ) {
			fields[ alias ] = definition;
		}
	}
}
