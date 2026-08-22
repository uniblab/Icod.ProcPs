namespace Icod.ProcPs.Shared;

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Icod.CommandFramework.Host;
using Icod.CommandFramework.Processes;

/// <summary>Enumerates and observes process state using procps-ng field semantics.</summary>
public interface IProcProcessProvider {
	/// <summary>Gets the process-observation capabilities exposed by this provider.</summary>
	ProcProcessCapabilities Capabilities { get; }
	/// <summary>Enumerates observable processes, retaining non-fatal per-process diagnostics.</summary>
	Task<ProcProcessCollection> GetProcessesAsync( CancellationToken cancellationToken = default );
	/// <summary>Observes one process.</summary>
	Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync( int processId, CancellationToken cancellationToken = default );
	/// <summary>Observes one process's virtual-memory map.</summary>
	Task<ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>> GetMemoryMapsAsync( int processId, CancellationToken cancellationToken = default );
}

/// <summary>Selects the strongest native process provider available for the current platform.</summary>
public sealed class SystemProcProcessProvider : IProcProcessProvider {
	private readonly IProcProcessProvider _inner;
	/// <summary>Gets the shared system ProcPs process provider.</summary>
	public static SystemProcProcessProvider Instance { get; } = new();
	/// <inheritdoc />
	public ProcProcessCapabilities Capabilities => this._inner.Capabilities;
	/// <summary>Initializes a system provider using the platform's defensible observation source.</summary>
	public SystemProcProcessProvider() : this( SystemProcessInspector.Instance ) { }
	/// <summary>Initializes a system provider over an injectable shared process inspector.</summary>
	public SystemProcProcessProvider( IProcessInspector inspector ) {
		ArgumentNullException.ThrowIfNull( inspector );
		this._inner = OperatingSystem.IsLinux()
			? new LinuxProcProcessProvider( inspector )
			: OperatingSystem.IsWindows()
				? new WindowsProcProcessProvider( inspector )
				: OperatingSystem.IsMacOS()
					? new MacOsProcProcessProvider( inspector )
					: new DotNetProcProcessProvider( inspector );
	}
	/// <inheritdoc />
	public Task<ProcProcessCollection> GetProcessesAsync( CancellationToken cancellationToken = default ) => this._inner.GetProcessesAsync( cancellationToken );
	/// <inheritdoc />
	public Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync( int processId, CancellationToken cancellationToken = default ) => this._inner.GetProcessAsync( processId, cancellationToken );
	/// <inheritdoc />
	public Task<ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>> GetMemoryMapsAsync( int processId, CancellationToken cancellationToken = default ) => this._inner.GetMemoryMapsAsync( processId, cancellationToken );
}

/// <summary>Provides authoritative Linux process observations from procfs.</summary>
public sealed class LinuxProcProcessProvider : IProcProcessProvider {
	private readonly IProcessInspector _inspector;
	private readonly string _procRoot;
	/// <inheritdoc />
	public ProcProcessCapabilities Capabilities => ProcProcessCapabilities.Enumeration
		| ProcProcessCapabilities.Identity
		| ProcProcessCapabilities.Parentage
		| ProcProcessCapabilities.ProcessGroups
		| ProcProcessCapabilities.Sessions
		| ProcProcessCapabilities.Users
		| ProcProcessCapabilities.Terminals
		| ProcProcessCapabilities.Namespaces
		| ProcProcessCapabilities.Containers
		| ProcProcessCapabilities.CommandLine
		| ProcProcessCapabilities.CpuTimes
		| ProcProcessCapabilities.Memory
		| ProcProcessCapabilities.Priority
		| ProcProcessCapabilities.Threads
		| ProcProcessCapabilities.MemoryMaps;
	/// <summary>Initializes a Linux procfs provider.</summary>
	public LinuxProcProcessProvider( IProcessInspector inspector, string procRoot = "/proc" ) {
		ArgumentNullException.ThrowIfNull( inspector );
		ArgumentException.ThrowIfNullOrWhiteSpace( procRoot );
		this._inspector = inspector;
		this._procRoot = procRoot;
	}

	/// <inheritdoc />
	public async Task<ProcProcessCollection> GetProcessesAsync( CancellationToken cancellationToken = default ) {
		var processes = new List<ProcProcessSnapshot>();
		var diagnostics = new List<ProcProviderDiagnostic>();
		IEnumerable<string> directories;
		try {
			directories = Directory.EnumerateDirectories( this._procRoot ).ToArray();
		} catch ( UnauthorizedAccessException exception ) {
			return new ProcProcessCollection( processes, new[] { new ProcProviderDiagnostic( null, ProcObservationAvailability.AccessDenied, exception.Message ) } );
		} catch ( IOException exception ) {
			return new ProcProcessCollection( processes, new[] { new ProcProviderDiagnostic( null, ProcObservationAvailability.Unavailable, exception.Message ) } );
		}
		foreach ( var directory in directories ) {
			cancellationToken.ThrowIfCancellationRequested();
			if ( !int.TryParse( System.IO.Path.GetFileName( directory ), NumberStyles.None, CultureInfo.InvariantCulture, out var processId ) || 0 >= processId ) continue;
			var observed = await this.GetProcessAsync( processId, cancellationToken ).ConfigureAwait( false );
			if ( observed.HasValue ) {
				processes.Add( observed.Value );
			} else if ( ProcObservationAvailability.Vanished != observed.Availability && ProcObservationAvailability.Reused != observed.Availability ) {
				diagnostics.Add( new ProcProviderDiagnostic( processId, observed.Availability, observed.Diagnostic ?? "Process observation failed." ) );
			}
		}
		processes.Sort( static ( left, right ) => left.ProcessId.CompareTo( right.ProcessId ) );
		return new ProcProcessCollection( processes, diagnostics );
	}

	/// <inheritdoc />
	public async Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync( int processId, CancellationToken cancellationToken = default ) {
		if ( 0 >= processId ) return ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Malformed, "A positive process identifier is required." );
		var before = this._inspector.ObserveIdentity( processId );
		if ( !before.Succeeded ) return MissingFromOperation<ProcProcessSnapshot>( before.Status, before.Message );
		var directory = System.IO.Path.Combine( this._procRoot, processId.ToString( CultureInfo.InvariantCulture ) );
		try {
			var statTask = File.ReadAllTextAsync( System.IO.Path.Combine( directory, "stat" ), cancellationToken );
			var statusTask = File.ReadAllTextAsync( System.IO.Path.Combine( directory, "status" ), cancellationToken );
			var commandLineTask = File.ReadAllBytesAsync( System.IO.Path.Combine( directory, "cmdline" ), cancellationToken );
			var cgroupTask = ReadOptionalTextAsync( System.IO.Path.Combine( directory, "cgroup" ), cancellationToken );
			await Task.WhenAll( statTask, statusTask, commandLineTask, cgroupTask ).ConfigureAwait( false );
			var stat = LinuxProcParsers.ParseProcessStat( statTask.Result );
			var status = LinuxProcParsers.ParseProcessStatus( statusTask.Result );
			var namespaces = ReadNamespaces( directory );
			var terminal = ReadTerminal( directory, stat.TerminalDeviceNumber );
			var container = ParseContainer( cgroupTask.Result ?? string.Empty );
			var second = this._inspector.ObserveIdentity( processId );
			if ( !second.Succeeded ) return MissingFromOperation<ProcProcessSnapshot>( second.Status, second.Message );
			if ( null != before.Value!.ReuseToken && !before.Value.Equals( second.Value ) ) {
				return ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Reused, $"Process identifier {processId} was reused during observation." );
			}
			var rss = 0 <= stat.ResidentSetPages
				? ProcObservedValue<ulong>.Available( checked( (ulong)stat.ResidentSetPages * (ulong)Environment.SystemPageSize ), ProcObservationSource.LinuxProcfs, ObservationFidelity.Exact )
				: ProcObservedValue<ulong>.Missing( ProcObservationAvailability.Malformed, "Negative resident-set page count." );
			var snapshot = new ProcProcessSnapshot( second.Value! ) {
				CommandName = Exact( stat.CommandName ),
				CommandLineArguments = Exact<IReadOnlyList<string>>( LinuxProcParsers.ParseNullDelimitedUtf8( commandLineTask.Result ) ),
				State = Exact( LinuxProcParsers.MapProcessState( stat.State ) ),
				ParentProcessId = Exact( stat.ParentProcessId ),
				ProcessGroupId = Exact( stat.ProcessGroupId ),
				ForegroundProcessGroupId = Exact( stat.TerminalForegroundProcessGroupId ),
				SessionId = Exact( stat.SessionId ),
				RealUserId = OptionalExact( status.RealUserId, "Uid is absent from /proc/PID/status." ),
				EffectiveUserId = OptionalExact( status.EffectiveUserId, "Effective Uid is absent from /proc/PID/status." ),
				RealGroupId = OptionalExact( status.RealGroupId, "Gid is absent from /proc/PID/status." ),
				EffectiveGroupId = OptionalExact( status.EffectiveGroupId, "Effective Gid is absent from /proc/PID/status." ),
				Terminal = Exact( terminal ),
				Namespaces = namespaces,
				NamespaceProcessIds = Exact<IReadOnlyList<int>>( status.NamespaceProcessIds ),
				Container = container,
				UserCpuTicks = Exact( stat.UserCpuTicks ),
				SystemCpuTicks = Exact( stat.SystemCpuTicks ),
				StartTimeTicks = Exact( stat.StartTimeTicks ),
				VirtualMemoryBytes = Exact( stat.VirtualMemoryBytes ),
				ResidentMemoryBytes = rss,
				NiceValue = Exact( stat.NiceValue ),
				ThreadCount = Exact( stat.ThreadCount ),
				LifetimeStable = ProcObservedValue<bool>.Available( true, ProcObservationSource.Derived, ObservationFidelity.Exact )
			};
			return ProcObservedValue<ProcProcessSnapshot>.Available( snapshot, ProcObservationSource.LinuxProcfs, ObservationFidelity.Exact );
		} catch ( UnauthorizedAccessException exception ) {
			return ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.AccessDenied, exception.Message );
		} catch ( DirectoryNotFoundException exception ) {
			return ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Vanished, exception.Message );
		} catch ( FileNotFoundException exception ) {
			return ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Vanished, exception.Message );
		} catch ( IOException exception ) {
			return ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Unavailable, exception.Message );
		} catch ( FormatException exception ) {
			return ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Malformed, exception.Message );
		} catch ( OverflowException exception ) {
			return ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Malformed, exception.Message );
		}
	}

	/// <inheritdoc />
	public async Task<ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>> GetMemoryMapsAsync( int processId, CancellationToken cancellationToken = default ) {
		if ( 0 >= processId ) return ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.Malformed, "A positive process identifier is required." );
		var before = this._inspector.ObserveIdentity( processId );
		if ( !before.Succeeded ) return MissingFromOperation<IReadOnlyList<ProcMemoryMapEntry>>( before.Status, before.Message );
		try {
			var lines = await File.ReadAllLinesAsync( System.IO.Path.Combine( this._procRoot, processId.ToString( CultureInfo.InvariantCulture ), "maps" ), cancellationToken ).ConfigureAwait( false );
			var entries = lines.Select( LinuxProcParsers.ParseMemoryMapLine ).ToArray();
			var second = this._inspector.ObserveIdentity( processId );
			if ( !second.Succeeded ) return MissingFromOperation<IReadOnlyList<ProcMemoryMapEntry>>( second.Status, second.Message );
			if ( null != before.Value!.ReuseToken && !before.Value.Equals( second.Value ) ) {
				return ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.Reused, $"Process identifier {processId} was reused during memory-map observation." );
			}
			return ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Available( entries, ProcObservationSource.LinuxProcfs, ObservationFidelity.Exact );
		} catch ( UnauthorizedAccessException exception ) {
			return ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.AccessDenied, exception.Message );
		} catch ( DirectoryNotFoundException exception ) {
			return ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.Vanished, exception.Message );
		} catch ( FileNotFoundException exception ) {
			return ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.Vanished, exception.Message );
		} catch ( IOException exception ) {
			return ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.Unavailable, exception.Message );
		} catch ( FormatException exception ) {
			return ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.Malformed, exception.Message );
		} catch ( OverflowException exception ) {
			return ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.Malformed, exception.Message );
		}
	}

	private static ProcObservedValue<T> Exact<T>( T value ) => ProcObservedValue<T>.Available( value, ProcObservationSource.LinuxProcfs, ObservationFidelity.Exact );
	private static ProcObservedValue<T> OptionalExact<T>( T? value, string diagnostic ) where T : struct => value.HasValue ? Exact( value.Value ) : ProcObservedValue<T>.Missing( ProcObservationAvailability.Unavailable, diagnostic );
	private static async Task<string?> ReadOptionalTextAsync( string path, CancellationToken cancellationToken ) {
		try { return await File.ReadAllTextAsync( path, cancellationToken ).ConfigureAwait( false ); }
		catch ( FileNotFoundException ) { return null; }
		catch ( DirectoryNotFoundException ) { return null; }
		catch ( UnauthorizedAccessException ) { return null; }
	}
	private static ProcObservedValue<IReadOnlyDictionary<string, ProcNamespaceInfo>> ReadNamespaces( string processDirectory ) {
		try {
			var result = new Dictionary<string, ProcNamespaceInfo>( StringComparer.Ordinal );
			foreach ( var path in Directory.EnumerateFiles( System.IO.Path.Combine( processDirectory, "ns" ) ) ) {
				var name = System.IO.Path.GetFileName( path );
				var target = new FileInfo( path ).LinkTarget;
				if ( string.IsNullOrWhiteSpace( name ) || string.IsNullOrWhiteSpace( target ) ) continue;
				result[ name ] = new ProcNamespaceInfo( name, target, ParseNamespaceIdentifier( target ) );
			}
			return ProcObservedValue<IReadOnlyDictionary<string, ProcNamespaceInfo>>.Available( result, ProcObservationSource.LinuxProcfs, ObservationFidelity.Exact );
		} catch ( UnauthorizedAccessException exception ) {
			return ProcObservedValue<IReadOnlyDictionary<string, ProcNamespaceInfo>>.Missing( ProcObservationAvailability.AccessDenied, exception.Message );
		} catch ( IOException exception ) {
			return ProcObservedValue<IReadOnlyDictionary<string, ProcNamespaceInfo>>.Missing( ProcObservationAvailability.Unavailable, exception.Message );
		} catch ( NotSupportedException exception ) {
			return ProcObservedValue<IReadOnlyDictionary<string, ProcNamespaceInfo>>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		}
	}
	private static ulong? ParseNamespaceIdentifier( string target ) {
		var start = target.LastIndexOf( '[' );
		var end = target.LastIndexOf( ']' );
		return 0 <= start && end > start && ulong.TryParse( target[ ( start + 1 )..end ], NumberStyles.None, CultureInfo.InvariantCulture, out var value ) ? value : null;
	}
	private static ProcTerminalInfo ReadTerminal( string directory, int deviceNumber ) {
		string? name = null;
		try {
			var target = new FileInfo( System.IO.Path.Combine( directory, "fd", "0" ) ).LinkTarget;
			if ( null != target && target.StartsWith( "/dev/", StringComparison.Ordinal ) ) name = target;
		} catch ( IOException ) { }
		catch ( UnauthorizedAccessException ) { }
		catch ( NotSupportedException ) { }
		return new ProcTerminalInfo( deviceNumber, name );
	}
	private static ProcObservedValue<ProcContainerInfo> ParseContainer( string cgroupText ) {
		var path = cgroupText.Split( '\n', StringSplitOptions.RemoveEmptyEntries )
			.Select( line => line.Split( ':', 3 ) )
			.Where( fields => 3 == fields.Length )
			.Select( fields => fields[ 2 ] )
			.FirstOrDefault() ?? string.Empty;
		var (id, runtime) = DetectContainer( path );
		var fidelity = null == id ? ObservationFidelity.Exact : ObservationFidelity.Synthesized;
		return ProcObservedValue<ProcContainerInfo>.Available( new ProcContainerInfo( path, id, runtime ), null == id ? ProcObservationSource.LinuxProcfs : ProcObservationSource.Derived, fidelity );
	}
	private static (string? Id, string? Runtime) DetectContainer( string path ) {
		foreach ( var raw in path.Split( '/', StringSplitOptions.RemoveEmptyEntries ) ) {
			var segment = raw.EndsWith( ".scope", StringComparison.Ordinal ) ? raw[ ..^6 ] : raw;
			foreach ( var prefix in new[] { "docker-", "libpod-", "cri-containerd-", "crio-" } ) {
				if ( segment.StartsWith( prefix, StringComparison.Ordinal ) ) {
					var candidate = segment[ prefix.Length.. ];
					if ( IsHexIdentifier( candidate ) ) return ( candidate, prefix.TrimEnd( '-' ) );
				}
			}
			if ( IsHexIdentifier( segment ) ) return ( segment, null );
		}
		return ( null, null );
	}
	private static bool IsHexIdentifier( string text ) => 12 <= text.Length && 64 >= text.Length && text.All( static character => char.IsAsciiHexDigit( character ) );
	private static ProcObservedValue<T> MissingFromOperation<T>( ProcessOperationStatus status, string? message ) => status switch {
		ProcessOperationStatus.AccessDenied => ProcObservedValue<T>.Missing( ProcObservationAvailability.AccessDenied, message ),
		ProcessOperationStatus.Vanished => ProcObservedValue<T>.Missing( ProcObservationAvailability.Vanished, message ),
		ProcessOperationStatus.Reused => ProcObservedValue<T>.Missing( ProcObservationAvailability.Reused, message ),
		ProcessOperationStatus.Unsupported => ProcObservedValue<T>.Missing( ProcObservationAvailability.Unsupported, message ),
		_ => ProcObservedValue<T>.Missing( ProcObservationAvailability.Unavailable, message )
	};
}

/// <summary>Provides conservative fallback process observations using the cross-platform .NET process API.</summary>
public sealed class DotNetProcProcessProvider : IProcProcessProvider {
	private readonly IProcessInspector _inspector;
	/// <inheritdoc />
	public ProcProcessCapabilities Capabilities => ProcProcessCapabilities.Enumeration
		| ProcProcessCapabilities.Identity
		| ProcProcessCapabilities.CpuTimes
		| ProcProcessCapabilities.Memory
		| ProcProcessCapabilities.Priority
		| ProcProcessCapabilities.Threads;
	/// <summary>Initializes a portable process provider.</summary>
	public DotNetProcProcessProvider( IProcessInspector inspector ) {
		ArgumentNullException.ThrowIfNull( inspector );
		this._inspector = inspector;
	}
	/// <inheritdoc />
	public async Task<ProcProcessCollection> GetProcessesAsync( CancellationToken cancellationToken = default ) {
		var processes = new List<ProcProcessSnapshot>();
		var diagnostics = new List<ProcProviderDiagnostic>();
		foreach ( var process in Process.GetProcesses() ) {
			using ( process ) {
				cancellationToken.ThrowIfCancellationRequested();
				var observed = await this.GetProcessAsync( process.Id, cancellationToken ).ConfigureAwait( false );
				if ( observed.HasValue ) processes.Add( observed.Value );
				else if ( ProcObservationAvailability.Vanished != observed.Availability ) diagnostics.Add( new ProcProviderDiagnostic( process.Id, observed.Availability, observed.Diagnostic ?? "Process observation failed." ) );
			}
		}
		processes.Sort( static ( left, right ) => left.ProcessId.CompareTo( right.ProcessId ) );
		return new ProcProcessCollection( processes, diagnostics );
	}
	/// <inheritdoc />
	public Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync( int processId, CancellationToken cancellationToken = default ) {
		cancellationToken.ThrowIfCancellationRequested();
		if ( 0 >= processId ) return Task.FromResult( ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Malformed, "A positive process identifier is required." ) );
		var before = this._inspector.ObserveIdentity( processId );
		if ( !before.Succeeded ) return Task.FromResult( MissingFromOperation( before.Status, before.Message ) );
		try {
			using var process = Process.GetProcessById( processId );
			var snapshot = new ProcProcessSnapshot( before.Value! ) {
				CommandName = TryObserve( () => process.ProcessName, ObservationFidelity.Equivalent ),
				UserCpuTicks = TryObserve( () => checked( (ulong)process.UserProcessorTime.Ticks ), ObservationFidelity.Equivalent ),
				SystemCpuTicks = TryObserve( () => checked( (ulong)process.PrivilegedProcessorTime.Ticks ), ObservationFidelity.Equivalent ),
				StartTimeTicks = TryObserve( () => checked( (ulong)process.StartTime.ToUniversalTime().Ticks ), ObservationFidelity.Equivalent ),
				VirtualMemoryBytes = TryObserve( () => checked( (ulong)process.VirtualMemorySize64 ), ObservationFidelity.Equivalent ),
				ResidentMemoryBytes = TryObserve( () => checked( (ulong)process.WorkingSet64 ), ObservationFidelity.Equivalent ),
				NiceValue = TryObserve( () => process.BasePriority, ObservationFidelity.Approximated ),
				ThreadCount = TryObserve( () => process.Threads.Count, ObservationFidelity.Equivalent )
			};
			var second = this._inspector.ObserveIdentity( processId );
			if ( !second.Succeeded ) return Task.FromResult( MissingFromOperation( second.Status, second.Message ) );
			if ( null != before.Value!.ReuseToken && !before.Value.Equals( second.Value ) ) return Task.FromResult( ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Reused, $"Process identifier {processId} was reused during observation." ) );
			snapshot = CopyWithLifetime( snapshot, second.Value! );
			return Task.FromResult( ProcObservedValue<ProcProcessSnapshot>.Available( snapshot, ProcObservationSource.DotNetProcessApi, ObservationFidelity.Equivalent ) );
		} catch ( ArgumentException exception ) {
			return Task.FromResult( ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Vanished, exception.Message ) );
		} catch ( InvalidOperationException exception ) {
			return Task.FromResult( ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Vanished, exception.Message ) );
		} catch ( Win32Exception exception ) {
			return Task.FromResult( ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.AccessDenied, exception.Message ) );
		} catch ( UnauthorizedAccessException exception ) {
			return Task.FromResult( ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.AccessDenied, exception.Message ) );
		}
	}
	/// <inheritdoc />
	public Task<ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>> GetMemoryMapsAsync( int processId, CancellationToken cancellationToken = default ) {
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>.Missing( ProcObservationAvailability.Unsupported, "Linux /proc/PID/maps semantics are not available from the portable process API." ) );
	}
	private static ProcObservedValue<T> TryObserve<T>( Func<T> factory, ObservationFidelity fidelity ) {
		try { return ProcObservedValue<T>.Available( factory(), ProcObservationSource.DotNetProcessApi, fidelity ); }
		catch ( InvalidOperationException exception ) { return ProcObservedValue<T>.Missing( ProcObservationAvailability.Vanished, exception.Message ); }
		catch ( Win32Exception exception ) { return ProcObservedValue<T>.Missing( ProcObservationAvailability.AccessDenied, exception.Message ); }
		catch ( NotSupportedException exception ) { return ProcObservedValue<T>.Missing( ProcObservationAvailability.Unsupported, exception.Message ); }
	}
	private static ProcProcessSnapshot CopyWithLifetime( ProcProcessSnapshot source, ProcessIdentity identity ) => new( identity ) {
		CommandName = source.CommandName,
		CommandLineArguments = source.CommandLineArguments,
		State = source.State,
		ParentProcessId = source.ParentProcessId,
		ProcessGroupId = source.ProcessGroupId,
		ForegroundProcessGroupId = source.ForegroundProcessGroupId,
		SessionId = source.SessionId,
		PlatformSessionId = source.PlatformSessionId,
		RealUserId = source.RealUserId,
		EffectiveUserId = source.EffectiveUserId,
		RealGroupId = source.RealGroupId,
		EffectiveGroupId = source.EffectiveGroupId,
		Terminal = source.Terminal,
		Namespaces = source.Namespaces,
		NamespaceProcessIds = source.NamespaceProcessIds,
		Container = source.Container,
		UserCpuTicks = source.UserCpuTicks,
		SystemCpuTicks = source.SystemCpuTicks,
		StartTimeTicks = source.StartTimeTicks,
		VirtualMemoryBytes = source.VirtualMemoryBytes,
		ResidentMemoryBytes = source.ResidentMemoryBytes,
		NiceValue = source.NiceValue,
		ThreadCount = source.ThreadCount,
		LifetimeStable = ProcObservedValue<bool>.Available( true, ProcObservationSource.Derived, ObservationFidelity.Equivalent )
	};
	private static ProcObservedValue<ProcProcessSnapshot> MissingFromOperation( ProcessOperationStatus status, string? message ) => status switch {
		ProcessOperationStatus.AccessDenied => ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.AccessDenied, message ),
		ProcessOperationStatus.Vanished => ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Vanished, message ),
		ProcessOperationStatus.Reused => ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Reused, message ),
		ProcessOperationStatus.Unsupported => ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Unsupported, message ),
		_ => ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Unavailable, message )
	};
}
