namespace Icod.ProcPs.Shared;

using System.Globalization;
using Icod.CommandFramework.Host;

/// <summary>Describes the vmstat-oriented observations exposed by a provider.</summary>
[Flags]
public enum ProcVmstatCapabilities : ulong {
	/// <summary>No vmstat-specific capability is available.</summary>
	None = 0,
	/// <summary>Physical-memory observations are available.</summary>
	Memory = 1UL << 0,
	/// <summary>Aggregate CPU activity is available.</summary>
	Cpu = 1UL << 1,
	/// <summary>Runnable and blocked process counts are available.</summary>
	ProcessQueues = 1UL << 2,
	/// <summary>Paging and swap counters are available.</summary>
	Paging = 1UL << 3,
	/// <summary>Aggregate interrupt/context-switch/fork counters are available.</summary>
	SystemEvents = 1UL << 4,
	/// <summary>Per-disk block-I/O counters are available.</summary>
	Disk = 1UL << 5,
	/// <summary>Partition block-I/O counters are available.</summary>
	Partition = 1UL << 6,
	/// <summary>Slab allocator rows are available.</summary>
	Slab = 1UL << 7,
	/// <summary>The cumulative fork count is available.</summary>
	Forks = 1UL << 8,
	/// <summary>The complete procps-ng statistics summary can be produced.</summary>
	Statistics = 1UL << 9
}

/// <summary>Contains cumulative system counters used by procps-ng <c>vmstat</c>.</summary>
public sealed class ProcVmstatSystemCounters {
	/// <summary>Gets the number of runnable processes.</summary>
	public ulong RunningProcesses { get; }
	/// <summary>Gets the number of processes blocked in uninterruptible sleep.</summary>
	public ulong BlockedProcesses { get; }
	/// <summary>Gets cumulative interrupts since boot.</summary>
	public ulong Interrupts { get; }
	/// <summary>Gets cumulative CPU context switches since boot.</summary>
	public ulong ContextSwitches { get; }
	/// <summary>Gets the Unix boot timestamp when reported.</summary>
	public ulong BootTimeUnixSeconds { get; }
	/// <summary>Gets cumulative processes created since boot.</summary>
	public ulong Forks { get; }
	/// <summary>Initializes vmstat system counters.</summary>
	public ProcVmstatSystemCounters( ulong runningProcesses, ulong blockedProcesses, ulong interrupts, ulong contextSwitches, ulong bootTimeUnixSeconds, ulong forks ) {
		this.RunningProcesses = runningProcesses;
		this.BlockedProcesses = blockedProcesses;
		this.Interrupts = interrupts;
		this.ContextSwitches = contextSwitches;
		this.BootTimeUnixSeconds = bootTimeUnixSeconds;
		this.Forks = forks;
	}
}

/// <summary>Contains cumulative paging counters used by procps-ng <c>vmstat</c>.</summary>
public sealed class ProcVmstatPagingCounters {
	/// <summary>Gets cumulative data paged in, expressed in KiB.</summary>
	public ulong PageInKibibytes { get; }
	/// <summary>Gets cumulative data paged out, expressed in KiB.</summary>
	public ulong PageOutKibibytes { get; }
	/// <summary>Gets cumulative swap pages read.</summary>
	public ulong SwapInPages { get; }
	/// <summary>Gets cumulative swap pages written.</summary>
	public ulong SwapOutPages { get; }
	/// <summary>Gets the platform page size in bytes.</summary>
	public ulong PageSizeBytes { get; }
	/// <summary>Initializes cumulative paging counters.</summary>
	public ProcVmstatPagingCounters( ulong pageInKibibytes, ulong pageOutKibibytes, ulong swapInPages, ulong swapOutPages, ulong pageSizeBytes ) {
		this.PageInKibibytes = pageInKibibytes;
		this.PageOutKibibytes = pageOutKibibytes;
		this.SwapInPages = swapInPages;
		this.SwapOutPages = swapOutPages;
		this.PageSizeBytes = pageSizeBytes;
	}
}

/// <summary>Contains one Linux diskstats row.</summary>
public sealed class ProcDiskStatEntry {
	/// <summary>Gets the kernel major device number.</summary>
	public int MajorNumber { get; }
	/// <summary>Gets the kernel minor device number.</summary>
	public int MinorNumber { get; }
	/// <summary>Gets the kernel device name.</summary>
	public string Name { get; }
	/// <summary>Gets whether sysfs identifies this row as a partition.</summary>
	public bool IsPartition { get; }
	/// <summary>Gets completed reads.</summary>
	public ulong ReadsCompleted { get; }
	/// <summary>Gets merged reads.</summary>
	public ulong ReadsMerged { get; }
	/// <summary>Gets sectors read.</summary>
	public ulong SectorsRead { get; }
	/// <summary>Gets milliseconds spent reading.</summary>
	public ulong ReadMilliseconds { get; }
	/// <summary>Gets completed writes.</summary>
	public ulong WritesCompleted { get; }
	/// <summary>Gets merged writes.</summary>
	public ulong WritesMerged { get; }
	/// <summary>Gets sectors written.</summary>
	public ulong SectorsWritten { get; }
	/// <summary>Gets milliseconds spent writing.</summary>
	public ulong WriteMilliseconds { get; }
	/// <summary>Gets I/O operations currently in progress.</summary>
	public ulong IoInProgress { get; }
	/// <summary>Gets milliseconds spent performing I/O.</summary>
	public ulong IoMilliseconds { get; }
	/// <summary>Gets weighted milliseconds spent performing I/O.</summary>
	public ulong WeightedIoMilliseconds { get; }
	/// <summary>Initializes one disk statistics row.</summary>
	public ProcDiskStatEntry(
		int majorNumber, int minorNumber, string name, bool isPartition,
		ulong readsCompleted, ulong readsMerged, ulong sectorsRead, ulong readMilliseconds,
		ulong writesCompleted, ulong writesMerged, ulong sectorsWritten, ulong writeMilliseconds,
		ulong ioInProgress, ulong ioMilliseconds, ulong weightedIoMilliseconds
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		this.MajorNumber = majorNumber;
		this.MinorNumber = minorNumber;
		this.Name = name;
		this.IsPartition = isPartition;
		this.ReadsCompleted = readsCompleted;
		this.ReadsMerged = readsMerged;
		this.SectorsRead = sectorsRead;
		this.ReadMilliseconds = readMilliseconds;
		this.WritesCompleted = writesCompleted;
		this.WritesMerged = writesMerged;
		this.SectorsWritten = sectorsWritten;
		this.WriteMilliseconds = writeMilliseconds;
		this.IoInProgress = ioInProgress;
		this.IoMilliseconds = ioMilliseconds;
		this.WeightedIoMilliseconds = weightedIoMilliseconds;
	}
}

/// <summary>Contains one coherent vmstat-oriented observation.</summary>
public sealed class ProcVmstatSnapshot {
	/// <summary>Gets the reusable system snapshot captured for the same observation.</summary>
	public ProcSystemSnapshot System { get; init; } = new();
	/// <summary>Gets process-queue, system-event, boot-time, and fork counters.</summary>
	public ProcObservedValue<ProcVmstatSystemCounters> SystemCounters { get; init; } = ProcObservedValue<ProcVmstatSystemCounters>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets cumulative paging counters.</summary>
	public ProcObservedValue<ProcVmstatPagingCounters> Paging { get; init; } = ProcObservedValue<ProcVmstatPagingCounters>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets Linux disk and partition counters.</summary>
	public ProcObservedValue<IReadOnlyList<ProcDiskStatEntry>> Disks { get; init; } = ProcObservedValue<IReadOnlyList<ProcDiskStatEntry>>.Missing( ProcObservationAvailability.Unavailable );
}

/// <summary>Observes data needed by procps-ng <c>vmstat</c>.</summary>
public interface IProcVmstatProvider {
	/// <summary>Gets the vmstat-oriented capabilities exposed by this provider.</summary>
	ProcVmstatCapabilities Capabilities { get; }
	/// <summary>Captures one coherent best-effort vmstat snapshot.</summary>
	Task<ProcVmstatSnapshot> GetSnapshotAsync( CancellationToken cancellationToken = default );
}

/// <summary>Selects the strongest vmstat provider available for the current platform.</summary>
public sealed class SystemProcVmstatProvider : IProcVmstatProvider {
	private readonly IProcVmstatProvider _inner;
	/// <summary>Gets the shared system vmstat provider.</summary>
	public static SystemProcVmstatProvider Instance { get; } = new();
	/// <inheritdoc />
	public ProcVmstatCapabilities Capabilities => this._inner.Capabilities;
	/// <summary>Initializes a native vmstat provider for the current operating system.</summary>
	public SystemProcVmstatProvider() {
		this._inner = OperatingSystem.IsLinux()
			? new LinuxProcVmstatProvider()
			: OperatingSystem.IsWindows()
				? new WindowsProcVmstatProvider()
				: OperatingSystem.IsMacOS()
					? new MacOsProcVmstatProvider()
					: new PortableProcVmstatProvider();
	}
	/// <inheritdoc />
	public Task<ProcVmstatSnapshot> GetSnapshotAsync( CancellationToken cancellationToken = default ) => this._inner.GetSnapshotAsync( cancellationToken );
}

/// <summary>Reads authoritative procps-ng vmstat observations from Linux procfs/sysfs.</summary>
public sealed class LinuxProcVmstatProvider : IProcVmstatProvider {
	private readonly string _procRoot;
	private readonly string _sysRoot;
	private readonly IProcSystemMetricsProvider _metrics;
	/// <inheritdoc />
	public ProcVmstatCapabilities Capabilities => ProcVmstatCapabilities.Memory
		| ProcVmstatCapabilities.Cpu
		| ProcVmstatCapabilities.ProcessQueues
		| ProcVmstatCapabilities.Paging
		| ProcVmstatCapabilities.SystemEvents
		| ProcVmstatCapabilities.Disk
		| ProcVmstatCapabilities.Partition
		| ProcVmstatCapabilities.Slab
		| ProcVmstatCapabilities.Forks
		| ProcVmstatCapabilities.Statistics;
	/// <summary>Initializes a Linux vmstat provider over injectable procfs/sysfs roots.</summary>
	public LinuxProcVmstatProvider( string procRoot = "/proc", string sysRoot = "/sys", IProcSystemMetricsProvider? metricsProvider = null ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( procRoot );
		ArgumentException.ThrowIfNullOrWhiteSpace( sysRoot );
		this._procRoot = procRoot;
		this._sysRoot = sysRoot;
		this._metrics = metricsProvider ?? new LinuxProcSystemMetricsProvider( procRoot );
	}
	/// <inheritdoc />
	public async Task<ProcVmstatSnapshot> GetSnapshotAsync( CancellationToken cancellationToken = default ) {
		cancellationToken.ThrowIfCancellationRequested();
		var system = await this._metrics.GetSnapshotAsync( cancellationToken ).ConfigureAwait( false );
		return new ProcVmstatSnapshot {
			System = system,
			SystemCounters = await this.ObserveSystemCountersAsync( cancellationToken ).ConfigureAwait( false ),
			Paging = ObserveLinuxPaging( system ),
			Disks = await this.ObserveDisksAsync( cancellationToken ).ConfigureAwait( false )
		};
	}
	private async Task<ProcObservedValue<ProcVmstatSystemCounters>> ObserveSystemCountersAsync( CancellationToken cancellationToken ) {
		try {
			var text = await File.ReadAllTextAsync( System.IO.Path.Combine( this._procRoot, "stat" ), cancellationToken ).ConfigureAwait( false );
			return ProcObservedValue<ProcVmstatSystemCounters>.Available( ParseSystemCounters( text ), ProcObservationSource.LinuxProcfs, ObservationFidelity.Exact );
		} catch ( UnauthorizedAccessException exception ) {
			return ProcObservedValue<ProcVmstatSystemCounters>.Missing( ProcObservationAvailability.AccessDenied, exception.Message );
		} catch ( FileNotFoundException exception ) {
			return ProcObservedValue<ProcVmstatSystemCounters>.Missing( ProcObservationAvailability.Unavailable, exception.Message );
		} catch ( DirectoryNotFoundException exception ) {
			return ProcObservedValue<ProcVmstatSystemCounters>.Missing( ProcObservationAvailability.Unavailable, exception.Message );
		} catch ( IOException exception ) {
			return ProcObservedValue<ProcVmstatSystemCounters>.Missing( ProcObservationAvailability.Unavailable, exception.Message );
		} catch ( FormatException exception ) {
			return ProcObservedValue<ProcVmstatSystemCounters>.Missing( ProcObservationAvailability.Malformed, exception.Message );
		}
	}
	private async Task<ProcObservedValue<IReadOnlyList<ProcDiskStatEntry>>> ObserveDisksAsync( CancellationToken cancellationToken ) {
		try {
			var text = await File.ReadAllTextAsync( System.IO.Path.Combine( this._procRoot, "diskstats" ), cancellationToken ).ConfigureAwait( false );
			return ProcObservedValue<IReadOnlyList<ProcDiskStatEntry>>.Available( ParseDiskStats( text, this._sysRoot ), ProcObservationSource.LinuxProcfs, ObservationFidelity.Exact );
		} catch ( UnauthorizedAccessException exception ) {
			return ProcObservedValue<IReadOnlyList<ProcDiskStatEntry>>.Missing( ProcObservationAvailability.AccessDenied, exception.Message );
		} catch ( FileNotFoundException exception ) {
			return ProcObservedValue<IReadOnlyList<ProcDiskStatEntry>>.Missing( ProcObservationAvailability.Unavailable, exception.Message );
		} catch ( DirectoryNotFoundException exception ) {
			return ProcObservedValue<IReadOnlyList<ProcDiskStatEntry>>.Missing( ProcObservationAvailability.Unavailable, exception.Message );
		} catch ( IOException exception ) {
			return ProcObservedValue<IReadOnlyList<ProcDiskStatEntry>>.Missing( ProcObservationAvailability.Unavailable, exception.Message );
		} catch ( FormatException exception ) {
			return ProcObservedValue<IReadOnlyList<ProcDiskStatEntry>>.Missing( ProcObservationAvailability.Malformed, exception.Message );
		}
	}
	/// <summary>Parses the vmstat-related cumulative counters from Linux <c>/proc/stat</c>.</summary>
	public static ProcVmstatSystemCounters ParseSystemCounters( string text ) {
		ArgumentNullException.ThrowIfNull( text );
		var values = new Dictionary<string, ulong>( StringComparer.Ordinal );
		foreach ( var raw in text.Split( '\n' ) ) {
			var line = raw.Trim();
			if ( 0 == line.Length ) continue;
			var fields = line.Split( (char[]?)null, StringSplitOptions.RemoveEmptyEntries );
			if ( 2 > fields.Length ) continue;
			if ( fields[ 0 ] is not ( "intr" or "ctxt" or "btime" or "processes" or "procs_running" or "procs_blocked" ) ) continue;
			if ( !ulong.TryParse( fields[ 1 ], NumberStyles.None, CultureInfo.InvariantCulture, out var value ) ) throw new FormatException( $"Invalid /proc/stat value for {fields[ 0 ]}." );
			values[ fields[ 0 ] ] = value;
		}
		ulong Require( string name ) => values.TryGetValue( name, out var value ) ? value : throw new FormatException( $"Missing /proc/stat field {name}." );
		return new ProcVmstatSystemCounters( Require( "procs_running" ), Require( "procs_blocked" ), Require( "intr" ), Require( "ctxt" ), Require( "btime" ), Require( "processes" ) );
	}
	/// <summary>Parses Linux <c>/proc/diskstats</c> rows and classifies partitions through sysfs.</summary>
	/// <param name="text">The contents of Linux <c>/proc/diskstats</c>.</param>
	/// <param name="sysRoot">The sysfs root used to locate device metadata.</param>
	/// <param name="fileExists">An optional file-existence probe. Production callers normally omit this parameter; tests may supply one to emulate Linux sysfs paths on hosts whose native file system cannot represent them.</param>
	public static IReadOnlyList<ProcDiskStatEntry> ParseDiskStats( string text, string sysRoot = "/sys", Func<string, bool>? fileExists = null ) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentException.ThrowIfNullOrWhiteSpace( sysRoot );
		var partitionFileExists = fileExists ?? File.Exists;
		var rows = new List<ProcDiskStatEntry>();
		foreach ( var raw in text.Split( '\n' ) ) {
			var line = raw.Trim();
			if ( 0 == line.Length ) continue;
			var fields = line.Split( (char[]?)null, StringSplitOptions.RemoveEmptyEntries );
			if ( 14 > fields.Length ) continue;
			if ( !int.TryParse( fields[ 0 ], NumberStyles.None, CultureInfo.InvariantCulture, out var major ) || !int.TryParse( fields[ 1 ], NumberStyles.None, CultureInfo.InvariantCulture, out var minor ) ) throw new FormatException( "Invalid /proc/diskstats device number." );
			ulong Read( int index ) => ulong.TryParse( fields[ index ], NumberStyles.None, CultureInfo.InvariantCulture, out var value ) ? value : throw new FormatException( $"Invalid /proc/diskstats counter at field {index}." );
			var isPartition = partitionFileExists( System.IO.Path.Combine( sysRoot, "dev", "block", string.Concat( major.ToString( CultureInfo.InvariantCulture ), ":", minor.ToString( CultureInfo.InvariantCulture ) ), "partition" ) );
			rows.Add( new ProcDiskStatEntry( major, minor, fields[ 2 ], isPartition, Read( 3 ), Read( 4 ), Read( 5 ), Read( 6 ), Read( 7 ), Read( 8 ), Read( 9 ), Read( 10 ), Read( 11 ), Read( 12 ), Read( 13 ) ) );
		}
		return rows;
	}
	private static ProcObservedValue<ProcVmstatPagingCounters> ObserveLinuxPaging( ProcSystemSnapshot system ) {
		if ( !system.VirtualMemory.HasValue ) return ProcObservedValue<ProcVmstatPagingCounters>.Missing( system.VirtualMemory.Availability, system.VirtualMemory.Diagnostic );
		var values = system.VirtualMemory.Value;
		if ( !values.TryGetValue( "pgpgin", out var pageIn ) || !values.TryGetValue( "pgpgout", out var pageOut ) || !values.TryGetValue( "pswpin", out var swapIn ) || !values.TryGetValue( "pswpout", out var swapOut ) ) {
			return ProcObservedValue<ProcVmstatPagingCounters>.Missing( ProcObservationAvailability.Malformed, "Linux /proc/vmstat is missing paging counters required by vmstat." );
		}
		return ProcObservedValue<ProcVmstatPagingCounters>.Available( new ProcVmstatPagingCounters( pageIn, pageOut, swapIn, swapOut, checked( (ulong)Environment.SystemPageSize ) ), ProcObservationSource.LinuxProcfs, ObservationFidelity.Exact );
	}
}

/// <summary>Provides the defensible Windows subset of vmstat observations.</summary>
public sealed class WindowsProcVmstatProvider : IProcVmstatProvider {
	private readonly IProcSystemMetricsProvider _metrics;
	/// <inheritdoc />
	public ProcVmstatCapabilities Capabilities => ProcVmstatCapabilities.Memory | ProcVmstatCapabilities.Cpu;
	/// <summary>Initializes a Windows vmstat provider.</summary>
	public WindowsProcVmstatProvider( IProcSystemMetricsProvider? metricsProvider = null ) => this._metrics = metricsProvider ?? new WindowsProcSystemMetricsProvider();
	/// <inheritdoc />
	public async Task<ProcVmstatSnapshot> GetSnapshotAsync( CancellationToken cancellationToken = default ) => new() { System = await this._metrics.GetSnapshotAsync( cancellationToken ).ConfigureAwait( false ) };
}

/// <summary>Provides the defensible Darwin subset of vmstat observations.</summary>
public sealed class MacOsProcVmstatProvider : IProcVmstatProvider {
	private readonly IProcSystemMetricsProvider _metrics;
	/// <inheritdoc />
	public ProcVmstatCapabilities Capabilities => ProcVmstatCapabilities.Memory | ProcVmstatCapabilities.Cpu | ProcVmstatCapabilities.Paging;
	/// <summary>Initializes a macOS vmstat provider.</summary>
	public MacOsProcVmstatProvider( IProcSystemMetricsProvider? metricsProvider = null ) => this._metrics = metricsProvider ?? new MacOsProcSystemMetricsProvider();
	/// <inheritdoc />
	public async Task<ProcVmstatSnapshot> GetSnapshotAsync( CancellationToken cancellationToken = default ) {
		var system = await this._metrics.GetSnapshotAsync( cancellationToken ).ConfigureAwait( false );
		return new ProcVmstatSnapshot { System = system, Paging = ObservePaging( system.Memory ) };
	}
	private static ProcObservedValue<ProcVmstatPagingCounters> ObservePaging( ProcObservedValue<ProcMemoryInfo> memory ) {
		if ( !memory.HasValue ) return ProcObservedValue<ProcVmstatPagingCounters>.Missing( memory.Availability, memory.Diagnostic );
		var fields = memory.Value.Fields;
		if ( !fields.TryGetValue( "DarwinPageIns", out var pageIns ) || !fields.TryGetValue( "DarwinPageOuts", out var pageOuts ) || !fields.TryGetValue( "DarwinSwapIns", out var swapIns ) || !fields.TryGetValue( "DarwinSwapOuts", out var swapOuts ) || !fields.TryGetValue( "DarwinPageSize", out var pageSize ) ) {
			return ProcObservedValue<ProcVmstatPagingCounters>.Missing( ProcObservationAvailability.Unavailable, "Darwin Mach paging counters were not present in the memory observation." );
		}
		return ProcObservedValue<ProcVmstatPagingCounters>.Available(
			new ProcVmstatPagingCounters( PagesToKibibytes( pageIns, pageSize ), PagesToKibibytes( pageOuts, pageSize ), swapIns, swapOuts, pageSize ),
			ProcObservationSource.DarwinMach,
			ObservationFidelity.Equivalent
		);
	}
	private static ulong PagesToKibibytes( ulong pages, ulong pageSize ) {
		if ( 0 == pages || 0 == pageSize ) return 0;
		var bytes = ulong.MaxValue / pages < pageSize ? ulong.MaxValue : pages * pageSize;
		return bytes / 1024UL;
	}
}

/// <summary>Provides a conservative vmstat provider for unsupported operating systems.</summary>
public sealed class PortableProcVmstatProvider : IProcVmstatProvider {
	private readonly IProcSystemMetricsProvider _metrics;
	/// <inheritdoc />
	public ProcVmstatCapabilities Capabilities => ProcVmstatCapabilities.None;
	/// <summary>Initializes the portable fallback.</summary>
	public PortableProcVmstatProvider( IProcSystemMetricsProvider? metricsProvider = null ) => this._metrics = metricsProvider ?? new PortableProcSystemMetricsProvider();
	/// <inheritdoc />
	public async Task<ProcVmstatSnapshot> GetSnapshotAsync( CancellationToken cancellationToken = default ) => new() { System = await this._metrics.GetSnapshotAsync( cancellationToken ).ConfigureAwait( false ) };
}
