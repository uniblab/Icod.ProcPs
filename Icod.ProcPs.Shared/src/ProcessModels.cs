namespace Icod.ProcPs.Shared;

using Icod.CommandFramework.Processes;
using Icod.CommandFramework.Host;

/// <summary>Classifies the Linux/procps process state code.</summary>
public enum ProcProcessState {
	/// <summary>The state is not known.</summary>
	Unknown,
	/// <summary>The task is running or runnable.</summary>
	Running,
	/// <summary>The task is interruptibly sleeping.</summary>
	Sleeping,
	/// <summary>The task is in uninterruptible disk sleep.</summary>
	DiskSleep,
	/// <summary>The task is stopped.</summary>
	Stopped,
	/// <summary>The task is stopped by tracing.</summary>
	TracingStop,
	/// <summary>The task is a zombie.</summary>
	Zombie,
	/// <summary>The task is dead.</summary>
	Dead,
	/// <summary>The task is an idle kernel thread.</summary>
	Idle,
	/// <summary>The task is waking.</summary>
	Waking,
	/// <summary>The task is parked.</summary>
	Parked
}

/// <summary>Describes the ProcPs view of a controlling terminal.</summary>
public sealed class ProcTerminalInfo {
	/// <summary>Gets the raw kernel terminal device number when available.</summary>
	public int DeviceNumber { get; }
	/// <summary>Gets a resolved terminal path when one can be observed.</summary>
	public string? Name { get; }
	/// <summary>Initializes terminal information.</summary>
	public ProcTerminalInfo( int deviceNumber, string? name ) {
		this.DeviceNumber = deviceNumber;
		this.Name = name;
	}
}

/// <summary>Describes one Linux namespace association.</summary>
public sealed class ProcNamespaceInfo {
	/// <summary>Gets the namespace kind such as <c>pid</c>, <c>mnt</c>, or <c>net</c>.</summary>
	public string Kind { get; }
	/// <summary>Gets the kernel namespace identifier when it can be parsed.</summary>
	public ulong? Identifier { get; }
	/// <summary>Gets the original procfs link target.</summary>
	public string LinkTarget { get; }
	/// <summary>Initializes namespace information.</summary>
	public ProcNamespaceInfo( string kind, string linkTarget, ulong? identifier ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( kind );
		ArgumentException.ThrowIfNullOrWhiteSpace( linkTarget );
		this.Kind = kind;
		this.LinkTarget = linkTarget;
		this.Identifier = identifier;
	}
}

/// <summary>Describes cgroup and derived container context for a process.</summary>
public sealed class ProcContainerInfo {
	/// <summary>Gets the selected cgroup path.</summary>
	public string CgroupPath { get; }
	/// <summary>Gets a derived container identifier when one can be recognized safely.</summary>
	public string? ContainerId { get; }
	/// <summary>Gets the recognized container runtime family when one can be inferred.</summary>
	public string? Runtime { get; }
	/// <summary>Initializes container information.</summary>
	public ProcContainerInfo( string cgroupPath, string? containerId = null, string? runtime = null ) {
		ArgumentNullException.ThrowIfNull( cgroupPath );
		this.CgroupPath = cgroupPath;
		this.ContainerId = containerId;
		this.Runtime = runtime;
	}
}

/// <summary>Contains the reusable procps-ng process snapshot consumed by process-oriented commands.</summary>
public sealed class ProcProcessSnapshot {
	/// <summary>Gets the shared process identity, including a reuse token when available.</summary>
	public ProcessIdentity Identity { get; }
	/// <summary>Gets the short command name.</summary>
	public ProcObservedValue<string> CommandName { get; init; } = ProcObservedValue<string>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets command-line arguments without the NUL separators used by procfs.</summary>
	public ProcObservedValue<IReadOnlyList<string>> CommandLineArguments { get; init; } = ProcObservedValue<IReadOnlyList<string>>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets the process state.</summary>
	public ProcObservedValue<ProcProcessState> State { get; init; } = ProcObservedValue<ProcProcessState>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets the parent process identifier.</summary>
	public ProcObservedValue<int> ParentProcessId { get; init; } = ProcObservedValue<int>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets the process-group identifier.</summary>
	public ProcObservedValue<int> ProcessGroupId { get; init; } = ProcObservedValue<int>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets the controlling terminal's foreground process-group identifier when available.</summary>
	public ProcObservedValue<int> ForegroundProcessGroupId { get; init; } = ProcObservedValue<int>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets the POSIX process-session identifier when the platform exposes that concept.</summary>
	public ProcObservedValue<int> SessionId { get; init; } = ProcObservedValue<int>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets a platform login or desktop-session identifier when that concept is distinct from a POSIX process session.</summary>
	public ProcObservedValue<int> PlatformSessionId { get; init; } = ProcObservedValue<int>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets the real user identifier.</summary>
	public ProcObservedValue<uint> RealUserId { get; init; } = ProcObservedValue<uint>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets the effective user identifier.</summary>
	public ProcObservedValue<uint> EffectiveUserId { get; init; } = ProcObservedValue<uint>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets the real group identifier.</summary>
	public ProcObservedValue<uint> RealGroupId { get; init; } = ProcObservedValue<uint>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets the effective group identifier.</summary>
	public ProcObservedValue<uint> EffectiveGroupId { get; init; } = ProcObservedValue<uint>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets the controlling-terminal information.</summary>
	public ProcObservedValue<ProcTerminalInfo> Terminal { get; init; } = ProcObservedValue<ProcTerminalInfo>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets namespace associations keyed by namespace kind.</summary>
	public ProcObservedValue<IReadOnlyDictionary<string, ProcNamespaceInfo>> Namespaces { get; init; } = ProcObservedValue<IReadOnlyDictionary<string, ProcNamespaceInfo>>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets the process identifiers visible through nested PID namespaces.</summary>
	public ProcObservedValue<IReadOnlyList<int>> NamespaceProcessIds { get; init; } = ProcObservedValue<IReadOnlyList<int>>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets cgroup/container context.</summary>
	public ProcObservedValue<ProcContainerInfo> Container { get; init; } = ProcObservedValue<ProcContainerInfo>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets user CPU ticks from the authoritative provider.</summary>
	public ProcObservedValue<ulong> UserCpuTicks { get; init; } = ProcObservedValue<ulong>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets system CPU ticks from the authoritative provider.</summary>
	public ProcObservedValue<ulong> SystemCpuTicks { get; init; } = ProcObservedValue<ulong>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets the provider-specific process start counter.</summary>
	public ProcObservedValue<ulong> StartTimeTicks { get; init; } = ProcObservedValue<ulong>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets virtual-memory size in bytes.</summary>
	public ProcObservedValue<ulong> VirtualMemoryBytes { get; init; } = ProcObservedValue<ulong>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets resident-memory size in bytes.</summary>
	public ProcObservedValue<ulong> ResidentMemoryBytes { get; init; } = ProcObservedValue<ulong>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets the procps nice value or the closest documented platform analogue.</summary>
	public ProcObservedValue<int> NiceValue { get; init; } = ProcObservedValue<int>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets the observed thread count.</summary>
	public ProcObservedValue<int> ThreadCount { get; init; } = ProcObservedValue<int>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets whether the process identity remained stable across the observation window.</summary>
	public ProcObservedValue<bool> LifetimeStable { get; init; } = ProcObservedValue<bool>.Missing( ProcObservationAvailability.Unavailable );

	/// <summary>Initializes a process snapshot for the supplied shared identity.</summary>
	public ProcProcessSnapshot( ProcessIdentity identity ) {
		ArgumentNullException.ThrowIfNull( identity );
		this.Identity = identity;
	}

	/// <summary>Gets the process identifier.</summary>
	public int ProcessId => this.Identity.ProcessId;
}

/// <summary>Contains a partially successful process enumeration and any non-fatal diagnostics.</summary>
public sealed class ProcProcessCollection {
	/// <summary>Gets provider diagnostics.</summary>
	public IReadOnlyList<ProcProviderDiagnostic> Diagnostics { get; }
	/// <summary>Gets observed process snapshots.</summary>
	public IReadOnlyList<ProcProcessSnapshot> Processes { get; }
	/// <summary>Initializes a process collection.</summary>
	public ProcProcessCollection( IEnumerable<ProcProcessSnapshot> processes, IEnumerable<ProcProviderDiagnostic>? diagnostics = null ) {
		ArgumentNullException.ThrowIfNull( processes );
		this.Processes = processes.ToArray();
		this.Diagnostics = null == diagnostics ? Array.Empty<ProcProviderDiagnostic>() : diagnostics.ToArray();
	}
}

/// <summary>Describes one virtual-memory map entry using Linux <c>/proc/PID/maps</c> semantics.</summary>
public sealed class ProcMemoryMapEntry {
	/// <summary>Gets the inclusive mapping start address.</summary>
	public ulong StartAddress { get; }
	/// <summary>Gets the exclusive mapping end address.</summary>
	public ulong EndAddress { get; }
	/// <summary>Gets mapping permission characters.</summary>
	public string Permissions { get; }
	/// <summary>Gets the file offset.</summary>
	public ulong Offset { get; }
	/// <summary>Gets the device field as rendered by procfs.</summary>
	public string Device { get; }
	/// <summary>Gets the inode number.</summary>
	public ulong Inode { get; }
	/// <summary>Gets the optional mapped pathname or bracketed pseudo-name.</summary>
	public string? Path { get; }
	/// <summary>Initializes a process memory-map entry.</summary>
	public ProcMemoryMapEntry( ulong startAddress, ulong endAddress, string permissions, ulong offset, string device, ulong inode, string? path ) {
		if ( endAddress < startAddress ) throw new ArgumentOutOfRangeException( nameof( endAddress ) );
		ArgumentException.ThrowIfNullOrWhiteSpace( permissions );
		ArgumentException.ThrowIfNullOrWhiteSpace( device );
		this.StartAddress = startAddress;
		this.EndAddress = endAddress;
		this.Permissions = permissions;
		this.Offset = offset;
		this.Device = device;
		this.Inode = inode;
		this.Path = path;
	}
}

/// <summary>Builds reusable parent/child relationship indexes from process snapshots.</summary>
public static class ProcProcessRelations {
	/// <summary>Builds a parent-PID to child-snapshot index, preserving input order within each parent.</summary>
	public static IReadOnlyDictionary<int, IReadOnlyList<ProcProcessSnapshot>> BuildChildrenIndex( IEnumerable<ProcProcessSnapshot> processes ) {
		ArgumentNullException.ThrowIfNull( processes );
		var mutable = new Dictionary<int, List<ProcProcessSnapshot>>();
		foreach ( var process in processes ) {
			if ( !process.ParentProcessId.HasValue ) continue;
			if ( !mutable.TryGetValue( process.ParentProcessId.Value, out var children ) ) {
				children = new List<ProcProcessSnapshot>();
				mutable.Add( process.ParentProcessId.Value, children );
			}
			children.Add( process );
		}
		return mutable.ToDictionary(
			pair => pair.Key,
			pair => (IReadOnlyList<ProcProcessSnapshot>)pair.Value.ToArray()
		);
	}
}
