namespace Icod.ProcPs.Shared;

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Icod.CommandFramework.Host;
using Icod.CommandFramework.Processes;

/// <summary>Contains executable, root, and current-working-directory observations for one process.</summary>
public sealed class ProcProcessPathInfo {
	/// <summary>Gets the executable pathname when observable.</summary>
	public ProcObservedValue<string> ExecutablePath { get; init; } = ProcObservedValue<string>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets the process root pathname when the host exposes chroot/root-namespace semantics.</summary>
	public ProcObservedValue<string> RootPath { get; init; } = ProcObservedValue<string>.Missing( ProcObservationAvailability.Unavailable );
	/// <summary>Gets the process current working directory when observable.</summary>
	public ProcObservedValue<string> WorkingDirectory { get; init; } = ProcObservedValue<string>.Missing( ProcObservationAvailability.Unavailable );
}

/// <summary>Observes process executable and directory paths without weakening process-reuse protection.</summary>
public interface IProcProcessPathProvider {
	/// <summary>Observes path information for the supplied reuse-protected process snapshot.</summary>
	Task<ProcObservedValue<ProcProcessPathInfo>> ObserveAsync( ProcProcessSnapshot process, CancellationToken cancellationToken = default );
}

/// <summary>Uses Linux procfs, Windows process APIs, or Darwin libproc to observe process paths.</summary>
public sealed class SystemProcProcessPathProvider : IProcProcessPathProvider {
	private readonly IProcessInspector inspector;
	private readonly string procRoot;
	private const int DarwinPathMax = 1024;
	private const int DarwinVnodeInfoSize = 152;
	private const int DarwinVnodeInfoPathSize = DarwinVnodeInfoSize + DarwinPathMax;
	private const int DarwinVnodePathInfoSize = DarwinVnodeInfoPathSize * 2;
	private const int DarwinProcPidVnodePathInfo = 9;

	/// <summary>Gets the system process-path provider.</summary>
	public static SystemProcProcessPathProvider Instance { get; } = new();

	/// <summary>Initializes the provider over the system process inspector and procfs root.</summary>
	public SystemProcProcessPathProvider() : this( SystemProcessInspector.Instance ) { }

	/// <summary>Initializes the provider over an injectable process inspector and procfs root.</summary>
	public SystemProcProcessPathProvider( IProcessInspector inspector, string procRoot = "/proc" ) {
		ArgumentNullException.ThrowIfNull( inspector );
		ArgumentException.ThrowIfNullOrWhiteSpace( procRoot );
		this.inspector = inspector;
		this.procRoot = procRoot;
	}

	/// <inheritdoc />
	public Task<ProcObservedValue<ProcProcessPathInfo>> ObserveAsync( ProcProcessSnapshot process, CancellationToken cancellationToken = default ) {
		ArgumentNullException.ThrowIfNull( process );
		cancellationToken.ThrowIfCancellationRequested();
		var before = this.inspector.ObserveIdentity( process.ProcessId );
		if ( !before.Succeeded ) return Task.FromResult( MissingFromOperation( before.Status, before.Message ) );
		if ( null != process.Identity.ReuseToken && !process.Identity.Equals( before.Value ) ) {
			return Task.FromResult( ProcObservedValue<ProcProcessPathInfo>.Missing( ProcObservationAvailability.Reused, $"Process identifier {process.ProcessId} was reused before path observation." ) );
		}

		ProcProcessPathInfo info;
		ProcObservationSource source;
		ObservationFidelity fidelity;
		if ( OperatingSystem.IsLinux() ) {
			info = this.ObserveLinux( process.ProcessId );
			source = ProcObservationSource.LinuxProcfs;
			fidelity = ObservationFidelity.Exact;
		} else if ( OperatingSystem.IsWindows() ) {
			info = ObserveWindows( process.ProcessId );
			source = ProcObservationSource.DotNetProcessApi;
			fidelity = ObservationFidelity.Equivalent;
		} else if ( OperatingSystem.IsMacOS() ) {
			info = ObserveDarwin( process.ProcessId );
			source = ProcObservationSource.DarwinLibProc;
			fidelity = ObservationFidelity.Equivalent;
		} else {
			info = new ProcProcessPathInfo {
				ExecutablePath = Unsupported( "Executable-path observation is not implemented for this platform." ),
				RootPath = Unsupported( "Process-root observation is not implemented for this platform." ),
				WorkingDirectory = Unsupported( "Working-directory observation is not implemented for this platform." )
			};
			source = ProcObservationSource.PlatformApi;
			fidelity = ObservationFidelity.Unavailable;
		}

		var after = this.inspector.ObserveIdentity( process.ProcessId );
		if ( !after.Succeeded ) return Task.FromResult( MissingFromOperation( after.Status, after.Message ) );
		if ( null != process.Identity.ReuseToken && !process.Identity.Equals( after.Value ) ) {
			return Task.FromResult( ProcObservedValue<ProcProcessPathInfo>.Missing( ProcObservationAvailability.Reused, $"Process identifier {process.ProcessId} was reused during path observation." ) );
		}
		return Task.FromResult( ProcObservedValue<ProcProcessPathInfo>.Available( info, source, fidelity ) );
	}

	private ProcProcessPathInfo ObserveLinux( int processId ) {
		var directory = System.IO.Path.Combine( this.procRoot, processId.ToString( CultureInfo.InvariantCulture ) );
		return new ProcProcessPathInfo {
			ExecutablePath = ReadUnixLink( System.IO.Path.Combine( directory, "exe" ), ProcObservationSource.LinuxProcfs, ObservationFidelity.Exact ),
			RootPath = ReadUnixLink( System.IO.Path.Combine( directory, "root" ), ProcObservationSource.LinuxProcfs, ObservationFidelity.Exact ),
			WorkingDirectory = ReadUnixLink( System.IO.Path.Combine( directory, "cwd" ), ProcObservationSource.LinuxProcfs, ObservationFidelity.Exact )
		};
	}

	private static ProcProcessPathInfo ObserveWindows( int processId ) => new() {
		ExecutablePath = ObserveWindowsExecutable( processId ),
		RootPath = Unsupported( "Windows does not expose a POSIX process-root/chroot pathname equivalent." ),
		WorkingDirectory = Unsupported( "Windows does not expose another process's current working directory through a stable documented API." )
	};

	private static ProcObservedValue<string> ObserveWindowsExecutable( int processId ) {
		try {
			using var process = Process.GetProcessById( processId );
			var path = process.MainModule?.FileName;
			return string.IsNullOrWhiteSpace( path )
				? ProcObservedValue<string>.Missing( ProcObservationAvailability.Unavailable, "The executable pathname is unavailable." )
				: ProcObservedValue<string>.Available( path, ProcObservationSource.DotNetProcessApi, ObservationFidelity.Equivalent );
		} catch ( ArgumentException exception ) {
			return ProcObservedValue<string>.Missing( ProcObservationAvailability.Vanished, exception.Message );
		} catch ( InvalidOperationException exception ) {
			return ProcObservedValue<string>.Missing( ProcObservationAvailability.Vanished, exception.Message );
		} catch ( Win32Exception exception ) {
			return ProcObservedValue<string>.Missing( ProcObservationAvailability.AccessDenied, exception.Message );
		} catch ( UnauthorizedAccessException exception ) {
			return ProcObservedValue<string>.Missing( ProcObservationAvailability.AccessDenied, exception.Message );
		}
	}

	private static ProcProcessPathInfo ObserveDarwin( int processId ) {
		var executable = ObserveDarwinExecutable( processId );
		var (workingDirectory, rootPath) = ObserveDarwinDirectories( processId );
		return new ProcProcessPathInfo {
			ExecutablePath = executable,
			WorkingDirectory = workingDirectory,
			RootPath = rootPath
		};
	}

	private static ProcObservedValue<string> ObserveDarwinExecutable( int processId ) {
		var buffer = new byte[ DarwinPathMax * 4 ];
		try {
			var length = proc_pidpath( processId, buffer, (uint)buffer.Length );
			if ( 0 < length ) {
				var terminator = Array.IndexOf( buffer, (byte)0, 0, Math.Min( length, buffer.Length ) );
				var count = 0 <= terminator ? terminator : Math.Min( length, buffer.Length );
				return ProcObservedValue<string>.Available( Encoding.UTF8.GetString( buffer, 0, count ), ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent );
			}
			return MissingFromErrno( "proc_pidpath" );
		} catch ( DllNotFoundException exception ) {
			return ProcObservedValue<string>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		} catch ( EntryPointNotFoundException exception ) {
			return ProcObservedValue<string>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		}
	}

	private static (ProcObservedValue<string> WorkingDirectory, ProcObservedValue<string> RootPath) ObserveDarwinDirectories( int processId ) {
		var pointer = Marshal.AllocHGlobal( DarwinVnodePathInfoSize );
		try {
			for ( var index = 0; index < DarwinVnodePathInfoSize; index++ ) Marshal.WriteByte( pointer, index, 0 );
			var result = proc_pidinfo( processId, DarwinProcPidVnodePathInfo, 0, pointer, DarwinVnodePathInfoSize );
			if ( DarwinVnodePathInfoSize != result ) {
				var missing = MissingFromErrno( "proc_pidinfo(PROC_PIDVNODEPATHINFO)" );
				return ( missing, missing );
			}
			var cwd = ReadNativeUtf8( IntPtr.Add( pointer, DarwinVnodeInfoSize ), DarwinPathMax );
			var root = ReadNativeUtf8( IntPtr.Add( pointer, DarwinVnodeInfoPathSize + DarwinVnodeInfoSize ), DarwinPathMax );
			return (
				string.IsNullOrEmpty( cwd ) ? ProcObservedValue<string>.Missing( ProcObservationAvailability.Unavailable, "Darwin returned an empty current-directory path." ) : ProcObservedValue<string>.Available( cwd, ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent ),
				string.IsNullOrEmpty( root ) ? ProcObservedValue<string>.Missing( ProcObservationAvailability.Unavailable, "Darwin returned an empty process-root path." ) : ProcObservedValue<string>.Available( root, ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent )
			);
		} catch ( DllNotFoundException exception ) {
			var missing = ProcObservedValue<string>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
			return ( missing, missing );
		} catch ( EntryPointNotFoundException exception ) {
			var missing = ProcObservedValue<string>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
			return ( missing, missing );
		} finally {
			Marshal.FreeHGlobal( pointer );
		}
	}

	private static string ReadNativeUtf8( IntPtr pointer, int maximumBytes ) {
		var length = 0;
		while ( length < maximumBytes && 0 != Marshal.ReadByte( pointer, length ) ) length++;
		if ( 0 == length ) return string.Empty;
		var bytes = new byte[ length ];
		Marshal.Copy( pointer, bytes, 0, length );
		return Encoding.UTF8.GetString( bytes );
	}

	private static ProcObservedValue<string> ReadUnixLink( string path, ProcObservationSource source, ObservationFidelity fidelity ) {
		var size = 256;
		while ( size <= 65536 ) {
			var buffer = new byte[ size ];
			var length = readlink( path, buffer, (nuint)buffer.Length );
			if ( 0 > length ) return MissingFromErrno( "readlink" );
			if ( length < buffer.Length ) return ProcObservedValue<string>.Available( Encoding.UTF8.GetString( buffer, 0, (int)length ), source, fidelity );
			size *= 2;
		}
		return ProcObservedValue<string>.Missing( ProcObservationAvailability.Unavailable, "Symbolic-link target exceeded the supported path buffer." );
	}

	private static ProcObservedValue<string> MissingFromErrno( string operation ) {
		var error = Marshal.GetLastPInvokeError();
		var availability = error switch {
			2 or 3 => ProcObservationAvailability.Vanished,
			1 or 13 => ProcObservationAvailability.AccessDenied,
			38 or 45 or 95 => ProcObservationAvailability.Unsupported,
			_ => ProcObservationAvailability.Unavailable
		};
		return ProcObservedValue<string>.Missing( availability, $"{operation} failed with errno {error}." );
	}

	private static ProcObservedValue<string> Unsupported( string diagnostic )
		=> ProcObservedValue<string>.Missing( ProcObservationAvailability.Unsupported, diagnostic );

	private static ProcObservedValue<ProcProcessPathInfo> MissingFromOperation( ProcessOperationStatus status, string? message ) => status switch {
		ProcessOperationStatus.AccessDenied => ProcObservedValue<ProcProcessPathInfo>.Missing( ProcObservationAvailability.AccessDenied, message ),
		ProcessOperationStatus.Vanished => ProcObservedValue<ProcProcessPathInfo>.Missing( ProcObservationAvailability.Vanished, message ),
		ProcessOperationStatus.Reused => ProcObservedValue<ProcProcessPathInfo>.Missing( ProcObservationAvailability.Reused, message ),
		ProcessOperationStatus.Unsupported => ProcObservedValue<ProcProcessPathInfo>.Missing( ProcObservationAvailability.Unsupported, message ),
		_ => ProcObservedValue<ProcProcessPathInfo>.Missing( ProcObservationAvailability.Unavailable, message )
	};

	[DllImport( "libc", SetLastError = true )]
	private static extern nint readlink( string path, byte[] buffer, nuint bufferSize );

	[DllImport( "/usr/lib/libproc.dylib", SetLastError = true )]
	private static extern int proc_pidpath( int processId, byte[] buffer, uint bufferSize );

	[DllImport( "/usr/lib/libproc.dylib", SetLastError = true )]
	private static extern int proc_pidinfo( int processId, int flavor, ulong argument, IntPtr buffer, int bufferSize );
}

/// <summary>Implements the procps-ng 4.0.6 pidof and pwdx command engines over shared process observations.</summary>
public static class ProcProcessLookupCommand {
	private const int Success = 0;
	private const int Failure = 1;
	private static readonly Encoding Utf8 = new UTF8Encoding( false );

	/// <summary>Runs the procps-ng 4.0.6 <c>pidof</c> profile.</summary>
	public static async Task<int> RunPidOfAsync(
		string[] args,
		Stream? stdout = null,
		Stream? stderr = null,
		IProcProcessProvider? processProvider = null,
		IProcProcessPathProvider? pathProvider = null,
		IProcMatchSupplementProvider? supplements = null,
		Func<bool>? privilegedRootCheckProvider = null,
		Func<int?>? currentParentProcessIdProvider = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		var parsed = ParsePidOf( args );
		if ( null != parsed.Error ) {
			if ( 0 < parsed.Error.Length ) await WriteLineAsync( stderr, $"pidof: {parsed.Error}", cancellationToken ).ConfigureAwait( false );
			if ( parsed.ShowUsageOnError ) await WriteAsync( stderr, NormalizeLineEndings( PidOfUsage ), cancellationToken ).ConfigureAwait( false );
			return Failure;
		}
		if ( parsed.ShowHelp ) {
			await WriteAsync( stdout, NormalizeLineEndings( PidOfUsage ), cancellationToken ).ConfigureAwait( false );
			return Success;
		}
		if ( parsed.ShowVersion ) {
			await WriteLineAsync( stdout, "pidof from procps-ng 4.0.6", cancellationToken ).ConfigureAwait( false );
			return Success;
		}
		if ( 0 == parsed.Programs.Count ) return Failure;

		var provider = processProvider ?? SystemProcProcessProvider.Instance;
		var paths = pathProvider ?? SystemProcProcessPathProvider.Instance;
		ProcProcessCollection collection;
		try {
			collection = await provider.GetProcessesAsync( cancellationToken ).ConfigureAwait( false );
		} catch ( OperationCanceledException ) { throw; }
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException ) {
			await WriteLineAsync( stderr, $"pidof: {exception.Message}", cancellationToken ).ConfigureAwait( false );
			return Failure;
		}

		IReadOnlyList<ProcProcessSnapshot> candidates = collection.Processes;
		if ( parsed.Lightweight ) {
			try {
				var supplemented = await ( supplements ?? SystemProcMatchSupplementProvider.Instance ).GetCandidatesAsync( collection.Processes, true, cancellationToken ).ConfigureAwait( false );
				candidates = supplemented.Select( candidate => candidate.Process ).ToArray();
			} catch ( PlatformNotSupportedException exception ) {
				await WriteLineAsync( stderr, $"pidof: {exception.Message}", cancellationToken ).ConfigureAwait( false );
				return Failure;
			}
		}

		var omitted = ResolveOmissions( parsed.OmitTokens, collection.Processes, currentParentProcessIdProvider, out var omitDiagnostics );
		foreach ( var diagnostic in omitDiagnostics ) await WriteLineAsync( stderr, $"pidof: illegal omit pid value ({diagnostic})!", cancellationToken ).ConfigureAwait( false );

		var checkRoot = parsed.CheckRoot && IsPrivilegedForRootCheck( privilegedRootCheckProvider );
		string? callerRoot = null;
		if ( checkRoot ) {
			var current = collection.Processes.FirstOrDefault( process => process.ProcessId == Environment.ProcessId );
			if ( null == current ) {
				var currentObserved = await provider.GetProcessAsync( Environment.ProcessId, cancellationToken ).ConfigureAwait( false );
				if ( currentObserved.HasValue ) current = currentObserved.Value;
			}
			if ( null != current ) {
				var currentPaths = await paths.ObserveAsync( current, cancellationToken ).ConfigureAwait( false );
				if ( currentPaths.HasValue && currentPaths.Value.RootPath.HasValue ) callerRoot = currentPaths.Value.RootPath.Value;
			}
			if ( null == callerRoot ) {
				await WriteLineAsync( stderr, "pidof: cannot determine the caller process root", cancellationToken ).ConfigureAwait( false );
				return Failure;
			}
		}

		var pathCache = new Dictionary<int, ProcObservedValue<ProcProcessPathInfo>>();
		var found = false;
		var first = true;
		foreach ( var program in parsed.Programs ) {
			if ( 0 == program.Length ) continue;
			var matches = new List<int>();
			foreach ( var process in candidates ) {
				cancellationToken.ThrowIfCancellationRequested();
				if ( omitted.Contains( process.ProcessId ) ) continue;
				if ( !pathCache.TryGetValue( process.ProcessId, out var pathObservation ) ) {
					pathObservation = await paths.ObserveAsync( process, cancellationToken ).ConfigureAwait( false );
					pathCache[ process.ProcessId ] = pathObservation;
				}
				if ( !pathObservation.HasValue && pathObservation.Availability is ProcObservationAvailability.Vanished or ProcObservationAvailability.Reused ) continue;
				if ( checkRoot && ( !pathObservation.HasValue || !pathObservation.Value.RootPath.HasValue || !string.Equals( callerRoot, pathObservation.Value.RootPath.Value, StringComparison.Ordinal ) ) ) continue;
				if ( MatchesPidOfProgram( process, pathObservation, program, parsed.ScriptsToo, parsed.WithWorkers ) ) matches.Add( process.ProcessId );
			}
			matches.Sort( static ( left, right ) => right.CompareTo( left ) );
			if ( 0 == matches.Count ) continue;
			found = true;
			foreach ( var processId in matches ) {
				if ( !parsed.Quiet ) {
					if ( !first ) await WriteAsync( stdout, parsed.Separator, cancellationToken ).ConfigureAwait( false );
					await WriteAsync( stdout, processId.ToString( CultureInfo.InvariantCulture ), cancellationToken ).ConfigureAwait( false );
					first = false;
				}
				if ( parsed.SingleShot ) break;
			}
		}
		if ( found && !parsed.Quiet ) await WriteAsync( stdout, Environment.NewLine, cancellationToken ).ConfigureAwait( false );
		return found ? Success : Failure;
	}

	/// <summary>Runs the procps-ng 4.0.6 <c>pwdx</c> profile.</summary>
	public static async Task<int> RunPwdxAsync(
		string[] args,
		Stream? stdout = null,
		Stream? stderr = null,
		IProcProcessProvider? processProvider = null,
		IProcProcessPathProvider? pathProvider = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		var parsed = ParsePwdx( args );
		if ( null != parsed.Error ) {
			if ( 0 < parsed.Error.Length ) await WriteLineAsync( stderr, $"pwdx: {parsed.Error}", cancellationToken ).ConfigureAwait( false );
			if ( parsed.ShowUsageOnError ) await WriteAsync( stderr, NormalizeLineEndings( PwdxUsage ), cancellationToken ).ConfigureAwait( false );
			return Failure;
		}
		if ( parsed.ShowHelp ) {
			await WriteAsync( stdout, NormalizeLineEndings( PwdxUsage ), cancellationToken ).ConfigureAwait( false );
			return Success;
		}
		if ( parsed.ShowVersion ) {
			await WriteLineAsync( stdout, "pwdx from procps-ng 4.0.6", cancellationToken ).ConfigureAwait( false );
			return Success;
		}
		if ( 0 == parsed.Targets.Count ) {
			await WriteAsync( stderr, NormalizeLineEndings( PwdxUsage ), cancellationToken ).ConfigureAwait( false );
			return Failure;
		}

		var provider = processProvider ?? SystemProcProcessProvider.Instance;
		var paths = pathProvider ?? SystemProcProcessPathProvider.Instance;
		var status = Success;
		foreach ( var target in parsed.Targets ) {
			cancellationToken.ThrowIfCancellationRequested();
			if ( !TryParsePwdxTarget( target, out var processId ) ) {
				await WriteLineAsync( stderr, $"pwdx: invalid process id: {target}", cancellationToken ).ConfigureAwait( false );
				return Failure;
			}
			var process = await provider.GetProcessAsync( processId, cancellationToken ).ConfigureAwait( false );
			if ( !process.HasValue ) {
				status = Failure;
				await WriteLineAsync( stderr, $"{target}: {DiagnosticFor( process.Availability, process.Diagnostic )}", cancellationToken ).ConfigureAwait( false );
				continue;
			}
			var observed = await paths.ObserveAsync( process.Value, cancellationToken ).ConfigureAwait( false );
			if ( !observed.HasValue ) {
				status = Failure;
				await WriteLineAsync( stderr, $"{target}: {DiagnosticFor( observed.Availability, observed.Diagnostic )}", cancellationToken ).ConfigureAwait( false );
				continue;
			}
			var cwd = observed.Value.WorkingDirectory;
			if ( !cwd.HasValue ) {
				status = Failure;
				await WriteLineAsync( stderr, $"{target}: {DiagnosticFor( cwd.Availability, cwd.Diagnostic )}", cancellationToken ).ConfigureAwait( false );
				continue;
			}
			await WriteLineAsync( stdout, $"{target}: {cwd.Value}", cancellationToken ).ConfigureAwait( false );
		}
		return status;
	}

	private static bool MatchesPidOfProgram( ProcProcessSnapshot process, ProcObservedValue<ProcProcessPathInfo> pathObservation, string program, bool scriptsToo, bool withWorkers ) {
		var commandLineAvailable = process.CommandLineArguments.HasValue;
		var arguments = commandLineAvailable ? process.CommandLineArguments.Value : Array.Empty<string>();
		if ( commandLineAvailable && 0 == arguments.Count && !withWorkers ) return false;
		var command = process.CommandName.HasValue ? process.CommandName.Value : string.Empty;
		var argv0 = 0 < arguments.Count ? arguments[ 0 ] : string.Empty;
		if ( argv0.StartsWith( '-' ) ) argv0 = argv0[ 1.. ];
		var argv0Base = BaseName( argv0 );
		var programBase = BaseName( program );
		var executable = pathObservation.HasValue && pathObservation.Value.ExecutablePath.HasValue ? pathObservation.Value.ExecutablePath.Value : string.Empty;
		var executableBase = BaseName( executable );
		if ( string.Equals( program, argv0Base, StringComparison.Ordinal )
			|| string.Equals( programBase, argv0, StringComparison.Ordinal )
			|| string.Equals( program, argv0, StringComparison.Ordinal )
			|| ( ( withWorkers || !commandLineAvailable ) && string.Equals( program, command, StringComparison.Ordinal ) )
			|| ( 0 < executableBase.Length && string.Equals( program, executableBase, StringComparison.Ordinal ) )
			|| ( 0 < executable.Length && string.Equals( program, executable, StringComparison.Ordinal ) ) ) return true;

		if ( scriptsToo && 1 < arguments.Count ) {
			var argv1 = arguments[ 1 ];
			var argv1Base = BaseName( argv1 );
			if ( 0 < command.Length && argv1Base.StartsWith( command, StringComparison.Ordinal )
				&& ( string.Equals( program, argv1Base, StringComparison.Ordinal )
					|| string.Equals( programBase, argv1, StringComparison.Ordinal )
					|| string.Equals( program, argv1, StringComparison.Ordinal ) ) ) return true;
		}
		return 0 < command.Length && argv0.Contains( ' ' ) && string.Equals( program, command, StringComparison.Ordinal );
	}

	private static string BaseName( string value ) {
		if ( string.IsNullOrEmpty( value ) ) return string.Empty;
		var slash = value.LastIndexOf( '/' );
		var backslash = value.LastIndexOf( '\\' );
		var index = Math.Max( slash, backslash );
		return index < value.Length - 1 ? value[ ( index + 1 ).. ] : 0 <= index ? string.Empty : value;
	}

	private static HashSet<int> ResolveOmissions(
		IReadOnlyList<string> tokens,
		IReadOnlyList<ProcProcessSnapshot> processes,
		Func<int?>? currentParentProcessIdProvider,
		out IReadOnlyList<string> diagnostics
	) {
		var result = new HashSet<int>();
		var errors = new List<string>();
		int? parent = currentParentProcessIdProvider?.Invoke();
		if ( !parent.HasValue ) {
			var current = processes.FirstOrDefault( process => process.ProcessId == Environment.ProcessId );
			if ( null != current && current.ParentProcessId.HasValue ) parent = current.ParentProcessId.Value;
		}
		foreach ( var token in tokens ) {
			if ( "%PPID" == token ) {
				if ( parent.HasValue && 0 < parent.Value ) result.Add( parent.Value ); else errors.Add( token );
				continue;
			}
			if ( int.TryParse( token, NumberStyles.None, CultureInfo.InvariantCulture, out var value ) && 0 <= value ) result.Add( value ); else errors.Add( token );
		}
		diagnostics = errors;
		return result;
	}

	private static bool IsPrivilegedForRootCheck( Func<bool>? provider ) {
		if ( null != provider ) return provider();
		if ( OperatingSystem.IsWindows() ) return false;
		try { return 0 == geteuid(); }
		catch ( DllNotFoundException ) { return false; }
		catch ( EntryPointNotFoundException ) { return false; }
	}

	private static bool TryParsePwdxTarget( string input, out int processId ) {
		var value = input.StartsWith( "/proc/", StringComparison.Ordinal ) ? input[ 6.. ] : input;
		return int.TryParse( value, NumberStyles.None, CultureInfo.InvariantCulture, out processId ) && 0 < processId;
	}

	private static string DiagnosticFor( ProcObservationAvailability availability, string? diagnostic ) => availability switch {
		ProcObservationAvailability.Vanished or ProcObservationAvailability.Reused => "No such process",
		ProcObservationAvailability.AccessDenied => "Permission denied",
		ProcObservationAvailability.Unsupported => diagnostic ?? "Operation not supported",
		_ => diagnostic ?? "Unable to observe process"
	};

	private static PidOfArguments ParsePidOf( string[] args ) {
		var result = new PidOfArguments();
		var options = true;
		for ( var index = 0; index < args.Length; index++ ) {
			var argument = args[ index ];
			if ( options && "--" == argument ) { options = false; continue; }
			if ( options && argument.StartsWith( "--", StringComparison.Ordinal ) && 2 < argument.Length ) {
				var equals = argument.IndexOf( '=' );
				var name = 0 <= equals ? argument[ 2..equals ] : argument[ 2.. ];
				var inlineValue = 0 <= equals ? argument[ ( equals + 1 ).. ] : null;
				string? RequiredValue() {
					if ( null != inlineValue ) return inlineValue;
					return ++index < args.Length ? args[ index ] : null;
				}
				switch ( name ) {
					case "single-shot": result.SingleShot = true; break;
					case "check-root": result.CheckRoot = true; break;
					case "quiet": result.Quiet = true; result.SingleShot = true; break;
					case "with-workers": result.WithWorkers = true; break;
					case "omit-pid": if ( !AddOmitTokens( result, RequiredValue() ) ) return result.Fail( "option '--omit-pid' requires an argument" ); break;
					case "separator": { var value = RequiredValue(); if ( null == value ) return result.Fail( "option '--separator' requires an argument" ); result.Separator = value; break; }
					case "lightweight": result.Lightweight = true; break;
					case "help": result.ShowHelp = true; break;
					case "version": result.ShowVersion = true; break;
					default: return result.Fail( $"unrecognized option '--{name}'" );
				}
				continue;
			}
			if ( options && argument.StartsWith( '-' ) && "-" != argument ) {
				for ( var characterIndex = 1; characterIndex < argument.Length; characterIndex++ ) {
					var option = argument[ characterIndex ];
					string? Value() {
						if ( characterIndex + 1 < argument.Length ) { var value = argument[ ( characterIndex + 1 ).. ]; characterIndex = argument.Length; return value; }
						return ++index < args.Length ? args[ index ] : null;
					}
					switch ( option ) {
						case 's': result.SingleShot = true; break;
						case 'c': result.CheckRoot = true; break;
						case 'q': result.Quiet = true; result.SingleShot = true; break;
						case 'w': result.WithWorkers = true; break;
						case 'x': result.ScriptsToo = true; break;
						case 't': result.Lightweight = true; break;
						case 'o': if ( !AddOmitTokens( result, Value() ) ) return result.Fail( "option requires an argument -- 'o'" ); break;
						case 'S':
						case 'd': { var value = Value(); if ( null == value ) return result.Fail( $"option requires an argument -- '{option}'" ); result.Separator = value; break; }
						case 'V': result.ShowVersion = true; break;
						case 'h': result.ShowHelp = true; break;
						case 'n':
						case 'm': break;
						default: return result.Fail( $"invalid option -- '{option}'" );
					}
				}
				continue;
			}
			result.Programs.Add( argument );
		}
		return result;
	}

	private static bool AddOmitTokens( PidOfArguments arguments, string? text ) {
		if ( null == text ) return false;
		arguments.OmitTokens.AddRange( text.Split( new[] { ',', ';', ':' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) );
		return true;
	}

	private static PwdxArguments ParsePwdx( string[] args ) {
		var result = new PwdxArguments();
		var options = true;
		foreach ( var argument in args ) {
			if ( options && "--" == argument ) { options = false; continue; }
			if ( options && ( "-h" == argument || "--help" == argument ) ) { result.ShowHelp = true; continue; }
			if ( options && ( "-V" == argument || "--version" == argument ) ) { result.ShowVersion = true; continue; }
			if ( options && argument.StartsWith( '-' ) && !argument.StartsWith( "/proc/", StringComparison.Ordinal ) ) return result.Fail( $"unrecognized option '{argument}'" );
			result.Targets.Add( argument );
		}
		return result;
	}

	private static string NormalizeLineEndings( string value ) {
		var normalized = value.Replace( "\r\n", "\n", StringComparison.Ordinal ).Replace( "\r", "\n", StringComparison.Ordinal );
		return "\n" == Environment.NewLine ? normalized : normalized.Replace( "\n", Environment.NewLine, StringComparison.Ordinal );
	}

	private static async Task WriteLineAsync( Stream? stream, string text, CancellationToken cancellationToken )
		=> await WriteAsync( stream, text + Environment.NewLine, cancellationToken ).ConfigureAwait( false );

	private static async Task WriteAsync( Stream? stream, string text, CancellationToken cancellationToken ) {
		if ( null == stream ) return;
		var bytes = Utf8.GetBytes( text );
		await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false );
	}

	private sealed class PidOfArguments {
		public List<string> Programs { get; } = [];
		public List<string> OmitTokens { get; } = [];
		public bool SingleShot;
		public bool ScriptsToo;
		public bool CheckRoot;
		public bool WithWorkers;
		public bool Lightweight;
		public bool Quiet;
		public string Separator = " ";
		public bool ShowHelp;
		public bool ShowVersion;
		public string? Error;
		public bool ShowUsageOnError = true;
		public PidOfArguments Fail( string error ) { this.Error = error; return this; }
	}

	private sealed class PwdxArguments {
		public List<string> Targets { get; } = [];
		public bool ShowHelp;
		public bool ShowVersion;
		public string? Error;
		public bool ShowUsageOnError = true;
		public PwdxArguments Fail( string error ) { this.Error = error; return this; }
	}

	private const string PidOfUsage = """

Usage:
 pidof [options] [program [...]]

Options:
 -s, --single-shot         return one PID only
 -c, --check-root          omit processes with different root
 -q                        quiet mode, only set the exit code
 -w, --with-workers        show kernel workers too
 -x                        also find shells running the named scripts
 -o, --omit-pid <PID,...>  omit processes with PID
 -t, --lightweight         list threads too
 -S, --separator SEP       use SEP as separator put between PIDs
 -h, --help                display this help and exit
 -V, --version             output version information and exit
""";

	private const string PwdxUsage = """

Usage:
 pwdx [options] pid...

Options:
 -h, --help                display this help and exit
 -V, --version             output version information and exit
""";

	[DllImport( "libc" )]
	private static extern uint geteuid();
}
