namespace Icod.ProcPs.Shared;

using Icod.CommandFramework.Host;

/// <summary>Identifies the source from which a ProcPs observation was obtained.</summary>
public enum ProcObservationSource {
	/// <summary>No source produced a value.</summary>
	Unavailable = 0,
	/// <summary>The value came directly from Linux procfs.</summary>
	LinuxProcfs = 1,
	/// <summary>The value came from the cross-platform .NET process API.</summary>
	DotNetProcessApi = 2,
	/// <summary>The value came from another platform API with equivalent intent.</summary>
	PlatformApi = 3,
	/// <summary>The value was derived from one or more other observations.</summary>
	Derived = 4,
	/// <summary>The value came from explicit ProcPs configuration.</summary>
	Configuration = 5,
	/// <summary>The value came from a Windows native API.</summary>
	WindowsNativeApi = 6,
	/// <summary>The value came from a Darwin Mach API.</summary>
	DarwinMach = 7,
	/// <summary>The value came from Darwin libproc.</summary>
	DarwinLibProc = 8,
	/// <summary>The value came from a Darwin sysctl interface.</summary>
	DarwinSysctl = 9,
	/// <summary>The value came from a POSIX/libc API.</summary>
	PosixLibc = 10,
	/// <summary>The value came directly from Linux sysfs.</summary>
	LinuxSysfs = 11
}

/// <summary>Describes why a ProcPs observation does or does not contain a value.</summary>
public enum ProcObservationAvailability {
	/// <summary>A value is available.</summary>
	Available,
	/// <summary>The current platform does not expose the field with defensible semantics.</summary>
	Unsupported,
	/// <summary>The field exists but is unavailable in the current observation.</summary>
	Unavailable,
	/// <summary>The observer lacked permission to read the field.</summary>
	AccessDenied,
	/// <summary>The process or data source vanished during observation.</summary>
	Vanished,
	/// <summary>The process identifier was reused during the observation window.</summary>
	Reused,
	/// <summary>The source existed but its contents could not be interpreted safely.</summary>
	Malformed
}

/// <summary>Represents one platform-dependent value together with provenance and semantic fidelity.</summary>
/// <typeparam name="T">The observed value type.</typeparam>
public sealed class ProcObservedValue<T> {
	/// <summary>Gets the observation availability.</summary>
	public ProcObservationAvailability Availability { get; }
	/// <summary>Gets a diagnostic explaining an unavailable or degraded observation.</summary>
	public string? Diagnostic { get; }
	/// <summary>Gets the semantic fidelity relative to authoritative procps-ng/Linux semantics.</summary>
	public ObservationFidelity Fidelity { get; }
	/// <summary>Gets whether this observation contains a usable value.</summary>
	public bool HasValue => ProcObservationAvailability.Available == this.Availability;
	/// <summary>Gets the provenance source.</summary>
	public ProcObservationSource Source { get; }
	/// <summary>Gets the value. Consult <see cref="HasValue"/> before use.</summary>
	public T Value { get; }

	private ProcObservedValue(
		T value,
		ProcObservationAvailability availability,
		ProcObservationSource source,
		ObservationFidelity fidelity,
		string? diagnostic
	) {
		this.Value = value;
		this.Availability = availability;
		this.Source = source;
		this.Fidelity = fidelity;
		this.Diagnostic = diagnostic;
	}

	/// <summary>Creates an available observation.</summary>
	public static ProcObservedValue<T> Available(
		T value,
		ProcObservationSource source,
		ObservationFidelity fidelity
	) => new(
		value,
		ProcObservationAvailability.Available,
		source,
		fidelity,
		null
	);

	/// <summary>Creates an observation without a usable value.</summary>
	public static ProcObservedValue<T> Missing(
		ProcObservationAvailability availability,
		string? diagnostic = null
	) {
		if ( ProcObservationAvailability.Available == availability ) {
			throw new ArgumentOutOfRangeException( nameof( availability ) );
		}
		return new ProcObservedValue<T>(
			default!,
			availability,
			ProcObservationSource.Unavailable,
			ObservationFidelity.Unavailable,
			diagnostic
		);
	}
}

/// <summary>Describes suite-specific process-observation capabilities.</summary>
[Flags]
public enum ProcProcessCapabilities : ulong {
	/// <summary>No process-observation capability is available.</summary>
	None = 0,
	/// <summary>Processes can be enumerated.</summary>
	Enumeration = 1UL << 0,
	/// <summary>Stable process identities can be observed.</summary>
	Identity = 1UL << 1,
	/// <summary>Parent identifiers can be observed.</summary>
	Parentage = 1UL << 2,
	/// <summary>Process-group identifiers can be observed.</summary>
	ProcessGroups = 1UL << 3,
	/// <summary>Session identifiers can be observed.</summary>
	Sessions = 1UL << 4,
	/// <summary>User and group identifiers can be observed.</summary>
	Users = 1UL << 5,
	/// <summary>Controlling-terminal information can be observed.</summary>
	Terminals = 1UL << 6,
	/// <summary>Namespace membership can be observed.</summary>
	Namespaces = 1UL << 7,
	/// <summary>Container or cgroup context can be observed.</summary>
	Containers = 1UL << 8,
	/// <summary>Command-line arguments can be observed.</summary>
	CommandLine = 1UL << 9,
	/// <summary>CPU counters can be observed.</summary>
	CpuTimes = 1UL << 10,
	/// <summary>Per-process memory can be observed.</summary>
	Memory = 1UL << 11,
	/// <summary>Priority/nice information can be observed.</summary>
	Priority = 1UL << 12,
	/// <summary>Thread counts can be observed.</summary>
	Threads = 1UL << 13,
	/// <summary>Memory maps can be observed.</summary>
	MemoryMaps = 1UL << 14,
	/// <summary>A platform login/desktop session identifier can be observed when it is distinct from a POSIX process session.</summary>
	PlatformSessions = 1UL << 15
}

/// <summary>Describes suite-specific system-metric capabilities.</summary>
[Flags]
public enum ProcSystemCapabilities : ulong {
	/// <summary>No system metric is available.</summary>
	None = 0,
	/// <summary>Physical-memory metrics are available.</summary>
	Memory = 1UL << 0,
	/// <summary>Swap metrics are available.</summary>
	Swap = 1UL << 1,
	/// <summary>CPU activity counters are available.</summary>
	CpuActivity = 1UL << 2,
	/// <summary>Load averages are available.</summary>
	LoadAverage = 1UL << 3,
	/// <summary>System uptime is available.</summary>
	Uptime = 1UL << 4,
	/// <summary>Virtual-memory counters are available.</summary>
	VirtualMemory = 1UL << 5,
	/// <summary>Slab allocator metrics are available.</summary>
	Slab = 1UL << 6,
	/// <summary>Huge-page metrics are available.</summary>
	HugePages = 1UL << 7,
	/// <summary>User-session metrics are available.</summary>
	UserSessions = 1UL << 8
}

/// <summary>Describes a non-fatal provider diagnostic encountered while collecting a partial result.</summary>
public sealed class ProcProviderDiagnostic {
	/// <summary>Gets the associated process identifier, if any.</summary>
	public int? ProcessId { get; }
	/// <summary>Gets the observation availability classification.</summary>
	public ProcObservationAvailability Availability { get; }
	/// <summary>Gets the human-readable diagnostic message.</summary>
	public string Message { get; }
	/// <summary>Initializes a provider diagnostic.</summary>
	public ProcProviderDiagnostic( int? processId, ProcObservationAvailability availability, string message ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( message );
		this.ProcessId = processId;
		this.Availability = availability;
		this.Message = message;
	}
}
