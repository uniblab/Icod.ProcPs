namespace Icod.ProcPs.Shared;

using System.Runtime.InteropServices;
using Icod.CommandFramework.Host;
using Icod.CommandFramework.Processes;

/// <summary>Augments the portable .NET process surface with documented Windows-native process metadata.</summary>
public sealed class WindowsProcProcessProvider : IProcProcessProvider {
	private readonly DotNetProcProcessProvider _portable;

	/// <inheritdoc />
	public ProcProcessCapabilities Capabilities => this._portable.Capabilities
		| ProcProcessCapabilities.Parentage
		| ProcProcessCapabilities.PlatformSessions;

	/// <summary>Initializes a Windows process provider over the shared identity inspector.</summary>
	public WindowsProcProcessProvider( IProcessInspector inspector ) {
		ArgumentNullException.ThrowIfNull( inspector );
		this._portable = new DotNetProcProcessProvider( inspector );
	}

	/// <inheritdoc />
	public async Task<ProcProcessCollection> GetProcessesAsync( CancellationToken cancellationToken = default ) {
		var portable = await this._portable.GetProcessesAsync( cancellationToken ).ConfigureAwait( false );
		if ( !OperatingSystem.IsWindows() ) return portable;
		var parents = TryReadParentProcessIds();
		var processes = new List<ProcProcessSnapshot>( portable.Processes.Count );
		foreach ( var process in portable.Processes ) {
			cancellationToken.ThrowIfCancellationRequested();
			processes.Add( Enrich( process, parents ) );
		}
		return new ProcProcessCollection( processes, portable.Diagnostics );
	}

	/// <inheritdoc />
	public async Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync( int processId, CancellationToken cancellationToken = default ) {
		var portable = await this._portable.GetProcessAsync( processId, cancellationToken ).ConfigureAwait( false );
		if ( !portable.HasValue || !OperatingSystem.IsWindows() ) return portable;
		return ProcObservedValue<ProcProcessSnapshot>.Available(
			Enrich( portable.Value, TryReadParentProcessIds() ),
			ProcObservationSource.WindowsNativeApi,
			ObservationFidelity.Equivalent
		);
	}

	/// <inheritdoc />
	public Task<ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>> GetMemoryMapsAsync( int processId, CancellationToken cancellationToken = default ) => this._portable.GetMemoryMapsAsync( processId, cancellationToken );

	private static ProcProcessSnapshot Enrich( ProcProcessSnapshot source, IReadOnlyDictionary<int, int>? parents ) {
		var parent = null != parents && parents.TryGetValue( source.ProcessId, out var parentId )
			? ProcObservedValue<int>.Available( parentId, ProcObservationSource.WindowsNativeApi, ObservationFidelity.Equivalent )
			: ProcObservedValue<int>.Missing( ProcObservationAvailability.Unavailable, "The parent process identifier was not present in the Tool Help snapshot." );
		var platformSession = TryReadPlatformSession( source.ProcessId );
		return Copy(
			source,
			parentProcessId: parent,
			platformSessionId: platformSession
		);
	}

	private static ProcObservedValue<int> TryReadPlatformSession( int processId ) {
		try {
			if ( !WindowsNative.ProcessIdToSessionId( checked( (uint)processId ), out var sessionId ) ) {
				return ProcObservedValue<int>.Missing( ProcObservationAvailability.Unavailable, $"ProcessIdToSessionId failed with Win32 error {Marshal.GetLastWin32Error()}." );
			}
			return ProcObservedValue<int>.Available( checked( (int)sessionId ), ProcObservationSource.WindowsNativeApi, ObservationFidelity.Exact );
		} catch ( DllNotFoundException exception ) {
			return ProcObservedValue<int>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		} catch ( EntryPointNotFoundException exception ) {
			return ProcObservedValue<int>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		} catch ( OverflowException exception ) {
			return ProcObservedValue<int>.Missing( ProcObservationAvailability.Malformed, exception.Message );
		}
	}

	private static IReadOnlyDictionary<int, int>? TryReadParentProcessIds() {
		IntPtr snapshot = IntPtr.Zero;
		try {
			snapshot = WindowsNative.CreateToolhelp32Snapshot( WindowsNative.Th32csSnapProcess, 0 );
			if ( WindowsNative.InvalidHandleValue == snapshot ) return null;
			var entry = new WindowsNative.ProcessEntry32 {
				Size = checked( (uint)Marshal.SizeOf<WindowsNative.ProcessEntry32>() ),
				ExeFile = string.Empty
			};
			if ( !WindowsNative.Process32FirstW( snapshot, ref entry ) ) return null;
			var result = new Dictionary<int, int>();
			do {
				if ( 0U < entry.ProcessId && int.MaxValue >= entry.ProcessId && int.MaxValue >= entry.ParentProcessId ) {
					result[ checked( (int)entry.ProcessId ) ] = checked( (int)entry.ParentProcessId );
				}
				entry.Size = checked( (uint)Marshal.SizeOf<WindowsNative.ProcessEntry32>() );
			} while ( WindowsNative.Process32NextW( snapshot, ref entry ) );
			return result;
		} catch ( DllNotFoundException ) {
			return null;
		} catch ( EntryPointNotFoundException ) {
			return null;
		} finally {
			if ( IntPtr.Zero != snapshot && WindowsNative.InvalidHandleValue != snapshot ) WindowsNative.CloseHandle( snapshot );
		}
	}

	private static ProcProcessSnapshot Copy(
		ProcProcessSnapshot source,
		ProcObservedValue<int>? parentProcessId = null,
		ProcObservedValue<int>? platformSessionId = null
	) => new( source.Identity ) {
		CommandName = source.CommandName,
		CommandLineArguments = source.CommandLineArguments,
		State = source.State,
		ParentProcessId = parentProcessId ?? source.ParentProcessId,
		ProcessGroupId = source.ProcessGroupId,
		ForegroundProcessGroupId = source.ForegroundProcessGroupId,
		SessionId = source.SessionId,
		PlatformSessionId = platformSessionId ?? source.PlatformSessionId,
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
		LifetimeStable = source.LifetimeStable
	};

	private static class WindowsNative {
		public const uint Th32csSnapProcess = 0x00000002;
		public static readonly IntPtr InvalidHandleValue = new( -1 );

		[StructLayout( LayoutKind.Sequential, CharSet = CharSet.Unicode )]
		public struct ProcessEntry32 {
			public uint Size;
			public uint UsageCount;
			public uint ProcessId;
			public UIntPtr DefaultHeapId;
			public uint ModuleId;
			public uint ThreadCount;
			public uint ParentProcessId;
			public int PriorityClassBase;
			public uint Flags;
			[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 260 )]
			public string ExeFile;
		}

		[DllImport( "kernel32.dll", SetLastError = true )]
		public static extern IntPtr CreateToolhelp32Snapshot( uint flags, uint processId );

		[DllImport( "kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true )]
		[return: MarshalAs( UnmanagedType.Bool )]
		public static extern bool Process32FirstW( IntPtr snapshot, ref ProcessEntry32 entry );

		[DllImport( "kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true )]
		[return: MarshalAs( UnmanagedType.Bool )]
		public static extern bool Process32NextW( IntPtr snapshot, ref ProcessEntry32 entry );

		[DllImport( "kernel32.dll", SetLastError = true )]
		[return: MarshalAs( UnmanagedType.Bool )]
		public static extern bool ProcessIdToSessionId( uint processId, out uint sessionId );

		[DllImport( "kernel32.dll", SetLastError = true )]
		[return: MarshalAs( UnmanagedType.Bool )]
		public static extern bool CloseHandle( IntPtr handle );
	}
}

/// <summary>Augments the portable .NET process surface with Darwin libproc and POSIX process metadata.</summary>
public sealed class MacOsProcProcessProvider : IProcProcessProvider {
	private readonly DotNetProcProcessProvider _portable;

	/// <inheritdoc />
	public ProcProcessCapabilities Capabilities => this._portable.Capabilities
		| ProcProcessCapabilities.Parentage
		| ProcProcessCapabilities.ProcessGroups
		| ProcProcessCapabilities.Sessions
		| ProcProcessCapabilities.Users
		| ProcProcessCapabilities.Terminals;

	/// <summary>Initializes a macOS process provider over the shared identity inspector.</summary>
	public MacOsProcProcessProvider( IProcessInspector inspector ) {
		ArgumentNullException.ThrowIfNull( inspector );
		this._portable = new DotNetProcProcessProvider( inspector );
	}

	/// <inheritdoc />
	public async Task<ProcProcessCollection> GetProcessesAsync( CancellationToken cancellationToken = default ) {
		var portable = await this._portable.GetProcessesAsync( cancellationToken ).ConfigureAwait( false );
		if ( !OperatingSystem.IsMacOS() ) return portable;
		var processes = new List<ProcProcessSnapshot>( portable.Processes.Count );
		var diagnostics = new List<ProcProviderDiagnostic>( portable.Diagnostics );
		foreach ( var process in portable.Processes ) {
			cancellationToken.ThrowIfCancellationRequested();
			var enriched = TryEnrich( process );
			if ( enriched.HasValue ) processes.Add( enriched.Value );
			else {
				processes.Add( process );
				diagnostics.Add( new ProcProviderDiagnostic( process.ProcessId, enriched.Availability, enriched.Diagnostic ?? "Darwin process enrichment failed." ) );
			}
		}
		return new ProcProcessCollection( processes, diagnostics );
	}

	/// <inheritdoc />
	public async Task<ProcObservedValue<ProcProcessSnapshot>> GetProcessAsync( int processId, CancellationToken cancellationToken = default ) {
		var portable = await this._portable.GetProcessAsync( processId, cancellationToken ).ConfigureAwait( false );
		if ( !portable.HasValue || !OperatingSystem.IsMacOS() ) return portable;
		return TryEnrich( portable.Value );
	}

	/// <inheritdoc />
	public Task<ProcObservedValue<IReadOnlyList<ProcMemoryMapEntry>>> GetMemoryMapsAsync( int processId, CancellationToken cancellationToken = default ) => this._portable.GetMemoryMapsAsync( processId, cancellationToken );

	private static ProcObservedValue<ProcProcessSnapshot> TryEnrich( ProcProcessSnapshot source ) {
		try {
			if ( !DarwinNative.TryReadBsdInfo( source.ProcessId, out var bsd ) ) {
				var error = Marshal.GetLastWin32Error();
				return ProcObservedValue<ProcProcessSnapshot>.Missing(
					MapDarwinError( error ),
					$"proc_pidinfo(PROC_PIDTBSDINFO) did not return a complete proc_bsdinfo record (errno {error})."
				);
			}
			var hasTaskInfo = DarwinNative.TryReadTaskInfo( source.ProcessId, out var task );
			var sessionId = DarwinNative.GetSessionId( source.ProcessId );
			var sessionError = 0 > sessionId ? Marshal.GetLastWin32Error() : 0;
			var snapshot = new ProcProcessSnapshot( source.Identity ) {
				CommandName = !string.IsNullOrWhiteSpace( bsd.Name )
					? ProcObservedValue<string>.Available( bsd.Name.TrimEnd( '\0' ), ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent )
					: !string.IsNullOrWhiteSpace( bsd.Command )
						? ProcObservedValue<string>.Available( bsd.Command.TrimEnd( '\0' ), ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent )
						: source.CommandName,
				CommandLineArguments = source.CommandLineArguments,
				State = ProcObservedValue<ProcProcessState>.Available( MapState( bsd.Status ), ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent ),
				ParentProcessId = ProcObservedValue<int>.Available( checked( (int)bsd.ParentProcessId ), ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent ),
				ProcessGroupId = ProcObservedValue<int>.Available( checked( (int)bsd.ProcessGroupId ), ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent ),
				ForegroundProcessGroupId = ProcObservedValue<int>.Available( unchecked( (int)bsd.TerminalProcessGroupId ), ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent ),
				SessionId = 0 <= sessionId
					? ProcObservedValue<int>.Available( sessionId, ProcObservationSource.PosixLibc, ObservationFidelity.Exact )
					: ProcObservedValue<int>.Missing( MapDarwinError( sessionError ), $"getsid failed for this process (errno {sessionError})." ),
				PlatformSessionId = source.PlatformSessionId,
				RealUserId = ProcObservedValue<uint>.Available( bsd.RealUserId, ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent ),
				EffectiveUserId = ProcObservedValue<uint>.Available( bsd.UserId, ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent ),
				RealGroupId = ProcObservedValue<uint>.Available( bsd.RealGroupId, ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent ),
				EffectiveGroupId = ProcObservedValue<uint>.Available( bsd.GroupId, ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent ),
				Terminal = uint.MaxValue != bsd.TerminalDevice
					? ProcObservedValue<ProcTerminalInfo>.Available( new ProcTerminalInfo( unchecked( (int)bsd.TerminalDevice ), null ), ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent )
					: ProcObservedValue<ProcTerminalInfo>.Missing( ProcObservationAvailability.Unavailable, "The process has no controlling terminal." ),
				Namespaces = source.Namespaces,
				NamespaceProcessIds = source.NamespaceProcessIds,
				Container = source.Container,
				UserCpuTicks = hasTaskInfo ? ProcObservedValue<ulong>.Available( task.TotalUser, ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent ) : source.UserCpuTicks,
				SystemCpuTicks = hasTaskInfo ? ProcObservedValue<ulong>.Available( task.TotalSystem, ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent ) : source.SystemCpuTicks,
				StartTimeTicks = ProcObservedValue<ulong>.Available( ToUnixHundredNanosecondTicks( bsd.StartSeconds, bsd.StartMicroseconds ), ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent ),
				VirtualMemoryBytes = hasTaskInfo ? ProcObservedValue<ulong>.Available( task.VirtualSize, ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent ) : source.VirtualMemoryBytes,
				ResidentMemoryBytes = hasTaskInfo ? ProcObservedValue<ulong>.Available( task.ResidentSize, ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent ) : source.ResidentMemoryBytes,
				NiceValue = ProcObservedValue<int>.Available( bsd.Nice, ProcObservationSource.DarwinLibProc, ObservationFidelity.Exact ),
				ThreadCount = hasTaskInfo ? ProcObservedValue<int>.Available( task.ThreadCount, ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent ) : source.ThreadCount,
				LifetimeStable = source.LifetimeStable
			};
			return ProcObservedValue<ProcProcessSnapshot>.Available( snapshot, ProcObservationSource.DarwinLibProc, ObservationFidelity.Equivalent );
		} catch ( DllNotFoundException exception ) {
			return ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		} catch ( EntryPointNotFoundException exception ) {
			return ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		} catch ( OverflowException exception ) {
			return ProcObservedValue<ProcProcessSnapshot>.Missing( ProcObservationAvailability.Malformed, exception.Message );
		}
	}

	private static ProcObservationAvailability MapDarwinError( int error ) => error switch {
		1 or 13 => ProcObservationAvailability.AccessDenied,
		3 => ProcObservationAvailability.Vanished,
		_ => ProcObservationAvailability.Unavailable
	};

	private static ProcProcessState MapState( uint status ) => status switch {
		1U => ProcProcessState.Unknown,
		2U => ProcProcessState.Running,
		3U => ProcProcessState.Sleeping,
		4U => ProcProcessState.Stopped,
		5U => ProcProcessState.Zombie,
		_ => ProcProcessState.Unknown
	};

	private static ulong ToUnixHundredNanosecondTicks( ulong seconds, ulong microseconds ) {
		var secondsPart = ulong.MaxValue / 10_000_000UL < seconds ? ulong.MaxValue : seconds * 10_000_000UL;
		var microsecondsPart = 1_000_000UL <= microseconds ? 9_999_990UL : microseconds * 10UL;
		return ulong.MaxValue - secondsPart < microsecondsPart ? ulong.MaxValue : secondsPart + microsecondsPart;
	}

	private static class DarwinNative {
		private const string LibProc = "/usr/lib/libproc.dylib";
		private const string LibSystem = "/usr/lib/libSystem.B.dylib";
		private const int ProcPidTBsdInfo = 3;
		private const int ProcPidTaskInfoFlavor = 4;

		[StructLayout( LayoutKind.Sequential, CharSet = CharSet.Ansi )]
		public struct BsdInfo {
			public uint Flags;
			public uint Status;
			public uint ExitStatus;
			public uint ProcessId;
			public uint ParentProcessId;
			public uint UserId;
			public uint GroupId;
			public uint RealUserId;
			public uint RealGroupId;
			public uint SavedUserId;
			public uint SavedGroupId;
			public uint Reserved;
			[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 16 )]
			public string? Command;
			[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 32 )]
			public string? Name;
			public uint FileCount;
			public uint ProcessGroupId;
			public uint JobControlCount;
			public uint TerminalDevice;
			public uint TerminalProcessGroupId;
			public int Nice;
			public ulong StartSeconds;
			public ulong StartMicroseconds;
		}

		[StructLayout( LayoutKind.Sequential )]
		public struct TaskInfo {
			public ulong VirtualSize;
			public ulong ResidentSize;
			public ulong TotalUser;
			public ulong TotalSystem;
			public ulong ThreadsUser;
			public ulong ThreadsSystem;
			public int Policy;
			public int Faults;
			public int PageIns;
			public int CowFaults;
			public int MessagesSent;
			public int MessagesReceived;
			public int MachSystemCalls;
			public int UnixSystemCalls;
			public int ContextSwitches;
			public int ThreadCount;
			public int RunningThreadCount;
			public int Priority;
		}

		[DllImport( LibProc, EntryPoint = "proc_pidinfo", SetLastError = true )]
		private static extern int ProcPidBsdInfo( int processId, int flavor, ulong argument, out BsdInfo buffer, int bufferSize );

		[DllImport( LibProc, EntryPoint = "proc_pidinfo", SetLastError = true )]
		private static extern int ProcPidTaskInfo( int processId, int flavor, ulong argument, out TaskInfo buffer, int bufferSize );

		[DllImport( LibSystem, EntryPoint = "getsid", SetLastError = true )]
		private static extern int GetSid( int processId );

		public static bool TryReadBsdInfo( int processId, out BsdInfo info ) {
			var size = Marshal.SizeOf<BsdInfo>();
			return size == ProcPidBsdInfo( processId, ProcPidTBsdInfo, 0, out info, size );
		}

		public static bool TryReadTaskInfo( int processId, out TaskInfo info ) {
			var size = Marshal.SizeOf<TaskInfo>();
			return size == ProcPidTaskInfo( processId, ProcPidTaskInfoFlavor, 0, out info, size );
		}

		public static int GetSessionId( int processId ) => GetSid( processId );
	}
}
