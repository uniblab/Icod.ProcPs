namespace Icod.ProcPs.Shared;

using System.Runtime.InteropServices;
using Icod.CommandFramework.Host;

/// <summary>Observes Windows system metrics through documented Win32 APIs.</summary>
public sealed class WindowsProcSystemMetricsProvider : IProcSystemMetricsProvider {
	/// <inheritdoc />
	public ProcSystemCapabilities Capabilities => ProcSystemCapabilities.Memory
		| ProcSystemCapabilities.Swap
		| ProcSystemCapabilities.CpuActivity
		| ProcSystemCapabilities.Uptime
		| ProcSystemCapabilities.UserSessions;

	/// <inheritdoc />
	public Task<ProcObservedValue<ProcMemoryInfo>> GetMemoryAsync( CancellationToken cancellationToken = default ) {
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult( ObserveMemory() );
	}

	/// <inheritdoc />
	public Task<ProcObservedValue<ProcUptimeInfo>> GetUptimeAsync( bool containerMode, CancellationToken cancellationToken = default ) {
		cancellationToken.ThrowIfCancellationRequested();
		if ( containerMode ) {
			return Task.FromResult( ProcObservedValue<ProcUptimeInfo>.Missing(
				ProcObservationAvailability.Unsupported,
				"Windows does not expose procps-ng container uptime semantics."
			) );
		}
		return Task.FromResult( ObserveUptime() );
	}

	/// <inheritdoc />
	public Task<ProcSystemSnapshot> GetSnapshotAsync( CancellationToken cancellationToken = default ) {
		cancellationToken.ThrowIfCancellationRequested();
		if ( !OperatingSystem.IsWindows() ) {
			var unsupported = "The Windows system-metric provider is available only on Windows.";
			return Task.FromResult( UnsupportedSnapshot( unsupported ) );
		}
		return Task.FromResult( new ProcSystemSnapshot {
			Cpu = ProcObservedValue<ProcCpuTimes>.Missing( ProcObservationAvailability.Unsupported, "Windows does not expose Linux /proc/stat CPU categories." ),
			CpuActivity = ObserveCpuActivity(),
			Memory = ObserveMemory(),
			LoadAverage = ProcObservedValue<ProcLoadAverage>.Missing( ProcObservationAvailability.Unsupported, "Windows has no native Unix load-average metric." ),
			LoadAverages = ProcObservedValue<ProcLoadAverages>.Missing( ProcObservationAvailability.Unsupported, "Windows has no native Unix load-average metric." ),
			Uptime = ObserveUptime(),
			VirtualMemory = ProcObservedValue<IReadOnlyDictionary<string, ulong>>.Missing( ProcObservationAvailability.Unsupported, "Linux /proc/vmstat counters do not have a one-to-one Windows representation." ),
			Slab = ProcObservedValue<IReadOnlyList<ProcSlabEntry>>.Missing( ProcObservationAvailability.Unsupported, "Linux slab allocator metrics are not available on Windows." ),
			HugePages = ProcObservedValue<ProcHugePageInfo>.Missing( ProcObservationAvailability.Unsupported, "Linux huge-page accounting is not available through this provider on Windows." ),
			UserSessions = ObserveUserSessions()
		} );
	}

	private static ProcObservedValue<ProcMemoryInfo> ObserveMemory() {
		if ( !OperatingSystem.IsWindows() ) return ProcObservedValue<ProcMemoryInfo>.Missing( ProcObservationAvailability.Unsupported, "Windows memory APIs are available only on Windows." );
		try {
			var performance = new WindowsNative.PerformanceInformation { Cb = (uint)Marshal.SizeOf<WindowsNative.PerformanceInformation>() };
			if ( !WindowsNative.GetPerformanceInfo( ref performance, performance.Cb ) ) {
				return ProcObservedValue<ProcMemoryInfo>.Missing( ProcObservationAvailability.Unavailable, $"GetPerformanceInfo failed with Win32 error {Marshal.GetLastWin32Error()}." );
			}
			var pageSize = checked( (ulong)performance.PageSize );
			var swap = ObservePageFiles( pageSize );
			var memory = new ProcMemoryInfo(
				totalBytes: MultiplyPages( performance.PhysicalTotal, pageSize ),
				freeBytes: null,
				availableBytes: MultiplyPages( performance.PhysicalAvailable, pageSize ),
				cacheBytes: MultiplyPages( performance.SystemCache, pageSize ),
				swapTotalBytes: swap.TotalBytes,
				swapFreeBytes: swap.FreeBytes,
				commitLimitBytes: MultiplyPages( performance.CommitLimit, pageSize ),
				committedBytes: MultiplyPages( performance.CommitTotal, pageSize )
			);
			return ProcObservedValue<ProcMemoryInfo>.Available( memory, ProcObservationSource.WindowsNativeApi, ObservationFidelity.Approximated );
		} catch ( DllNotFoundException exception ) {
			return ProcObservedValue<ProcMemoryInfo>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		} catch ( EntryPointNotFoundException exception ) {
			return ProcObservedValue<ProcMemoryInfo>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		} catch ( OverflowException exception ) {
			return ProcObservedValue<ProcMemoryInfo>.Missing( ProcObservationAvailability.Malformed, exception.Message );
		}
	}

	private static ProcObservedValue<ProcCpuActivity> ObserveCpuActivity() {
		if ( !OperatingSystem.IsWindows() ) return ProcObservedValue<ProcCpuActivity>.Missing( ProcObservationAvailability.Unsupported, "Windows CPU APIs are available only on Windows." );
		try {
			if ( !WindowsNative.GetSystemTimes( out var idle, out var kernel, out var user ) ) {
				return ProcObservedValue<ProcCpuActivity>.Missing( ProcObservationAvailability.Unavailable, $"GetSystemTimes failed with Win32 error {Marshal.GetLastWin32Error()}." );
			}
			var idleTicks = idle.ToUInt64();
			var kernelTicks = kernel.ToUInt64();
			var systemTicks = kernelTicks >= idleTicks ? kernelTicks - idleTicks : 0UL;
			var activeProcessorCount = WindowsNative.GetActiveProcessorCount( WindowsNative.AllProcessorGroups );
			var fidelity = 0U == activeProcessorCount || 64U < activeProcessorCount ? ObservationFidelity.Approximated : ObservationFidelity.Equivalent;
			return ProcObservedValue<ProcCpuActivity>.Available(
				new ProcCpuActivity( user.ToUInt64(), systemTicks, idleTicks ),
				ProcObservationSource.WindowsNativeApi,
				fidelity
			);
		} catch ( DllNotFoundException exception ) {
			return ProcObservedValue<ProcCpuActivity>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		} catch ( EntryPointNotFoundException exception ) {
			return ProcObservedValue<ProcCpuActivity>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		}
	}

	private static ProcObservedValue<ProcUptimeInfo> ObserveUptime() {
		if ( !OperatingSystem.IsWindows() ) return ProcObservedValue<ProcUptimeInfo>.Missing( ProcObservationAvailability.Unsupported, "Windows uptime APIs are available only on Windows." );
		try {
			return ProcObservedValue<ProcUptimeInfo>.Available(
				new ProcUptimeInfo( TimeSpan.FromMilliseconds( WindowsNative.GetTickCount64() ), null ),
				ProcObservationSource.WindowsNativeApi,
				ObservationFidelity.Equivalent
			);
		} catch ( DllNotFoundException exception ) {
			return ProcObservedValue<ProcUptimeInfo>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		} catch ( EntryPointNotFoundException exception ) {
			return ProcObservedValue<ProcUptimeInfo>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		}
	}

	private static ProcObservedValue<ProcUserSessionInfo> ObserveUserSessions() {
		if ( !OperatingSystem.IsWindows() ) return ProcObservedValue<ProcUserSessionInfo>.Missing( ProcObservationAvailability.Unsupported, "Windows Terminal Services APIs are available only on Windows." );
		IntPtr sessions = IntPtr.Zero;
		try {
			if ( !WindowsNative.WTSEnumerateSessionsW( IntPtr.Zero, 0, 1, out sessions, out var count ) ) {
				return ProcObservedValue<ProcUserSessionInfo>.Missing( ProcObservationAvailability.Unavailable, $"WTSEnumerateSessionsW failed with Win32 error {Marshal.GetLastWin32Error()}." );
			}
			var entrySize = Marshal.SizeOf<WindowsNative.WtsSessionInfo>();
			var users = 0;
			for ( var index = 0; index < count; index++ ) {
				var current = IntPtr.Add( sessions, checked( index * entrySize ) );
				var session = Marshal.PtrToStructure<WindowsNative.WtsSessionInfo>( current );
				if ( TryReadSessionUserName( session.SessionId, out var userName ) && !string.IsNullOrWhiteSpace( userName ) ) users++;
			}
			return ProcObservedValue<ProcUserSessionInfo>.Available( new ProcUserSessionInfo( users ), ProcObservationSource.WindowsNativeApi, ObservationFidelity.Approximated );
		} catch ( DllNotFoundException exception ) {
			return ProcObservedValue<ProcUserSessionInfo>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		} catch ( EntryPointNotFoundException exception ) {
			return ProcObservedValue<ProcUserSessionInfo>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		} finally {
			if ( IntPtr.Zero != sessions ) WindowsNative.WTSFreeMemory( sessions );
		}
	}

	private static bool TryReadSessionUserName( int sessionId, out string? userName ) {
		userName = null;
		IntPtr buffer = IntPtr.Zero;
		try {
			if ( !WindowsNative.WTSQuerySessionInformationW( IntPtr.Zero, sessionId, WindowsNative.WtsInfoClass.UserName, out buffer, out var bytes ) || IntPtr.Zero == buffer || 2 >= bytes ) return false;
			userName = Marshal.PtrToStringUni( buffer );
			return true;
		} finally {
			if ( IntPtr.Zero != buffer ) WindowsNative.WTSFreeMemory( buffer );
		}
	}

	private static (ulong? TotalBytes, ulong? FreeBytes) ObservePageFiles( ulong pageSize ) {
		ulong totalPages = 0;
		ulong usedPages = 0;
		WindowsNative.EnumPageFileCallback callback = ( IntPtr context, ref WindowsNative.EnumPageFileInformation information, string fileName ) => {
			totalPages = SaturatingAdd( totalPages, checked( (ulong)information.TotalSize ) );
			usedPages = SaturatingAdd( usedPages, checked( (ulong)information.TotalInUse ) );
			return true;
		};
		if ( !WindowsNative.EnumPageFilesW( callback, IntPtr.Zero ) ) return ( null, null );
		var freePages = totalPages >= usedPages ? totalPages - usedPages : 0UL;
		return ( MultiplyPages( totalPages, pageSize ), MultiplyPages( freePages, pageSize ) );
	}

	private static ulong MultiplyPages( nuint pages, ulong pageSize ) => MultiplyPages( checked( (ulong)pages ), pageSize );
	private static ulong MultiplyPages( ulong pages, ulong pageSize ) => 0UL == pages || 0UL == pageSize ? 0UL : ulong.MaxValue / pages < pageSize ? ulong.MaxValue : pages * pageSize;
	private static ulong SaturatingAdd( ulong left, ulong right ) => ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
	private static ProcSystemSnapshot UnsupportedSnapshot( string diagnostic ) => new() {
		Cpu = ProcObservedValue<ProcCpuTimes>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		CpuActivity = ProcObservedValue<ProcCpuActivity>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		Memory = ProcObservedValue<ProcMemoryInfo>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		LoadAverage = ProcObservedValue<ProcLoadAverage>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		LoadAverages = ProcObservedValue<ProcLoadAverages>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		Uptime = ProcObservedValue<ProcUptimeInfo>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		VirtualMemory = ProcObservedValue<IReadOnlyDictionary<string, ulong>>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		Slab = ProcObservedValue<IReadOnlyList<ProcSlabEntry>>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		HugePages = ProcObservedValue<ProcHugePageInfo>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		UserSessions = ProcObservedValue<ProcUserSessionInfo>.Missing( ProcObservationAvailability.Unsupported, diagnostic )
	};

	private static class WindowsNative {
		public const ushort AllProcessorGroups = 0xffff;
		[StructLayout( LayoutKind.Sequential )]
		public struct PerformanceInformation {
			public uint Cb;
			public nuint CommitTotal;
			public nuint CommitLimit;
			public nuint CommitPeak;
			public nuint PhysicalTotal;
			public nuint PhysicalAvailable;
			public nuint SystemCache;
			public nuint KernelTotal;
			public nuint KernelPaged;
			public nuint KernelNonPaged;
			public nuint PageSize;
			public uint HandleCount;
			public uint ProcessCount;
			public uint ThreadCount;
		}

		[StructLayout( LayoutKind.Sequential )]
		public struct FileTime {
			public uint LowDateTime;
			public uint HighDateTime;
			public readonly ulong ToUInt64() => ( (ulong)this.HighDateTime << 32 ) | this.LowDateTime;
		}

		[StructLayout( LayoutKind.Sequential )]
		public struct EnumPageFileInformation {
			public uint Cb;
			public uint Reserved;
			public nuint TotalSize;
			public nuint TotalInUse;
			public nuint PeakUsage;
		}

		[StructLayout( LayoutKind.Sequential, CharSet = CharSet.Unicode )]
		public struct WtsSessionInfo {
			public int SessionId;
			public IntPtr WinStationName;
			public int State;
		}

		public enum WtsInfoClass { UserName = 5 }

		[UnmanagedFunctionPointer( CallingConvention.Winapi, CharSet = CharSet.Unicode )]
		[return: MarshalAs( UnmanagedType.Bool )]
		public delegate bool EnumPageFileCallback( IntPtr context, ref EnumPageFileInformation information, [MarshalAs( UnmanagedType.LPWStr )] string fileName );

		[DllImport( "psapi.dll", SetLastError = true )]
		[return: MarshalAs( UnmanagedType.Bool )]
		public static extern bool GetPerformanceInfo( ref PerformanceInformation performanceInformation, uint size );

		[DllImport( "kernel32.dll", SetLastError = true )]
		public static extern uint GetActiveProcessorCount( ushort groupNumber );

		[DllImport( "kernel32.dll", SetLastError = true )]
		[return: MarshalAs( UnmanagedType.Bool )]
		public static extern bool GetSystemTimes( out FileTime idleTime, out FileTime kernelTime, out FileTime userTime );

		[DllImport( "kernel32.dll" )]
		public static extern ulong GetTickCount64();

		[DllImport( "psapi.dll", EntryPoint = "EnumPageFilesW", CharSet = CharSet.Unicode, SetLastError = true )]
		[return: MarshalAs( UnmanagedType.Bool )]
		public static extern bool EnumPageFilesW( EnumPageFileCallback callback, IntPtr context );

		[DllImport( "wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true )]
		[return: MarshalAs( UnmanagedType.Bool )]
		public static extern bool WTSEnumerateSessionsW( IntPtr serverHandle, int reserved, int version, out IntPtr sessionInfo, out int count );

		[DllImport( "wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true )]
		[return: MarshalAs( UnmanagedType.Bool )]
		public static extern bool WTSQuerySessionInformationW( IntPtr serverHandle, int sessionId, WtsInfoClass infoClass, out IntPtr buffer, out int bytesReturned );

		[DllImport( "wtsapi32.dll" )]
		public static extern void WTSFreeMemory( IntPtr memory );
	}
}

/// <summary>Observes macOS system metrics through Mach and POSIX/Darwin APIs.</summary>
public sealed class MacOsProcSystemMetricsProvider : IProcSystemMetricsProvider {
	/// <inheritdoc />
	public ProcSystemCapabilities Capabilities => ProcSystemCapabilities.Memory
		| ProcSystemCapabilities.Swap
		| ProcSystemCapabilities.CpuActivity
		| ProcSystemCapabilities.LoadAverage
		| ProcSystemCapabilities.Uptime
		| ProcSystemCapabilities.UserSessions;

	/// <inheritdoc />
	public Task<ProcObservedValue<ProcMemoryInfo>> GetMemoryAsync( CancellationToken cancellationToken = default ) {
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult( ObserveMemory() );
	}

	/// <inheritdoc />
	public Task<ProcObservedValue<ProcUptimeInfo>> GetUptimeAsync( bool containerMode, CancellationToken cancellationToken = default ) {
		cancellationToken.ThrowIfCancellationRequested();
		if ( containerMode ) {
			return Task.FromResult( ProcObservedValue<ProcUptimeInfo>.Missing(
				ProcObservationAvailability.Unsupported,
				"macOS does not expose procps-ng container uptime semantics."
			) );
		}
		return Task.FromResult( ObserveUptime() );
	}

	/// <inheritdoc />
	public Task<ProcSystemSnapshot> GetSnapshotAsync( CancellationToken cancellationToken = default ) {
		cancellationToken.ThrowIfCancellationRequested();
		if ( !OperatingSystem.IsMacOS() ) {
			var unsupported = "The macOS system-metric provider is available only on macOS.";
			return Task.FromResult( UnsupportedSnapshot( unsupported ) );
		}
		return Task.FromResult( new ProcSystemSnapshot {
			Cpu = ProcObservedValue<ProcCpuTimes>.Missing( ProcObservationAvailability.Unsupported, "Darwin does not expose Linux /proc/stat CPU categories." ),
			CpuActivity = ObserveCpuActivity(),
			Memory = ObserveMemory(),
			LoadAverage = ProcObservedValue<ProcLoadAverage>.Missing( ProcObservationAvailability.Unsupported, "Darwin load averages do not include Linux /proc/loadavg runnable/entity/latest-PID fields." ),
			LoadAverages = ObserveLoadAverages(),
			Uptime = ObserveUptime(),
			VirtualMemory = ProcObservedValue<IReadOnlyDictionary<string, ulong>>.Missing( ProcObservationAvailability.Unsupported, "Darwin VM statistics do not use Linux /proc/vmstat field semantics." ),
			Slab = ProcObservedValue<IReadOnlyList<ProcSlabEntry>>.Missing( ProcObservationAvailability.Unsupported, "Linux slab allocator metrics are not available on macOS." ),
			HugePages = ProcObservedValue<ProcHugePageInfo>.Missing( ProcObservationAvailability.Unsupported, "Linux huge-page accounting is not available through this provider on macOS." ),
			UserSessions = ObserveUserSessions()
		} );
	}

	private static ProcObservedValue<ProcMemoryInfo> ObserveMemory() {
		if ( !OperatingSystem.IsMacOS() ) return ProcObservedValue<ProcMemoryInfo>.Missing( ProcObservationAvailability.Unsupported, "Darwin memory APIs are available only on macOS." );
		try {
			if ( !DarwinNative.TryReadUInt64Sysctl( "hw.memsize", out var totalBytes ) ) {
				return ProcObservedValue<ProcMemoryInfo>.Missing( ProcObservationAvailability.Unavailable, "sysctlbyname(hw.memsize) failed." );
			}
			var host = DarwinNative.MachHostSelf();
			if ( 0 != DarwinNative.HostPageSize( host, out var pageSize ) ) {
				return ProcObservedValue<ProcMemoryInfo>.Missing( ProcObservationAvailability.Unavailable, "host_page_size failed." );
			}
			var statistics = new DarwinNative.VmStatistics64();
			var count = checked( (uint)( Marshal.SizeOf<DarwinNative.VmStatistics64>() / sizeof( int ) ) );
			if ( 0 != DarwinNative.HostStatistics64( host, DarwinNative.HostVmInfo64, ref statistics, ref count ) ) {
				return ProcObservedValue<ProcMemoryInfo>.Missing( ProcObservationAvailability.Unavailable, "host_statistics64(HOST_VM_INFO64) failed." );
			}
			var pageBytes = (ulong)pageSize;
			var freeBytes = MultiplyPages( statistics.FreeCount, pageBytes );
			var availablePages = SaturatingAdd( (ulong)statistics.FreeCount, SaturatingAdd( (ulong)statistics.InactiveCount, statistics.PurgeableCount ) );
			var cachePages = SaturatingAdd( (ulong)statistics.InactiveCount, statistics.PurgeableCount );
			ulong? swapTotal = null;
			ulong? swapFree = null;
			if ( DarwinNative.TryReadSwapUsage( out var swap ) ) {
				swapTotal = swap.Total;
				swapFree = swap.Available;
			}
			var fields = new Dictionary<string, ulong>( StringComparer.Ordinal ) {
				[ "DarwinActive" ] = MultiplyPages( statistics.ActiveCount, pageBytes ),
				[ "DarwinInactive" ] = MultiplyPages( statistics.InactiveCount, pageBytes ),
				[ "DarwinPageIns" ] = statistics.PageIns,
				[ "DarwinPageOuts" ] = statistics.PageOuts,
				[ "DarwinSwapIns" ] = statistics.SwapIns,
				[ "DarwinSwapOuts" ] = statistics.SwapOuts,
				[ "DarwinPageSize" ] = pageBytes
			};
			var memory = new ProcMemoryInfo(
				totalBytes: totalBytes,
				freeBytes: freeBytes,
				availableBytes: MultiplyPages( availablePages, pageBytes ),
				cacheBytes: MultiplyPages( cachePages, pageBytes ),
				swapTotalBytes: swapTotal,
				swapFreeBytes: swapFree,
				fields: fields
			);
			return ProcObservedValue<ProcMemoryInfo>.Available( memory, ProcObservationSource.DarwinMach, ObservationFidelity.Approximated );
		} catch ( DllNotFoundException exception ) {
			return ProcObservedValue<ProcMemoryInfo>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		} catch ( EntryPointNotFoundException exception ) {
			return ProcObservedValue<ProcMemoryInfo>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		}
	}

	private static ProcObservedValue<ProcCpuActivity> ObserveCpuActivity() {
		if ( !OperatingSystem.IsMacOS() ) return ProcObservedValue<ProcCpuActivity>.Missing( ProcObservationAvailability.Unsupported, "Darwin CPU APIs are available only on macOS." );
		try {
			var host = DarwinNative.MachHostSelf();
			var info = new DarwinNative.HostCpuLoadInfo();
			var count = 4U;
			if ( 0 != DarwinNative.HostStatistics( host, DarwinNative.HostCpuLoadInfoFlavor, ref info, ref count ) ) {
				return ProcObservedValue<ProcCpuActivity>.Missing( ProcObservationAvailability.Unavailable, "host_statistics(HOST_CPU_LOAD_INFO) failed." );
			}
			return ProcObservedValue<ProcCpuActivity>.Available(
				new ProcCpuActivity( info.User, info.System, info.Idle, info.Nice, counterBitWidth: 32 ),
				ProcObservationSource.DarwinMach,
				ObservationFidelity.Equivalent
			);
		} catch ( DllNotFoundException exception ) {
			return ProcObservedValue<ProcCpuActivity>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		} catch ( EntryPointNotFoundException exception ) {
			return ProcObservedValue<ProcCpuActivity>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		}
	}

	private static ProcObservedValue<ProcLoadAverages> ObserveLoadAverages() {
		if ( !OperatingSystem.IsMacOS() ) return ProcObservedValue<ProcLoadAverages>.Missing( ProcObservationAvailability.Unsupported, "POSIX load averages are available here only on macOS." );
		try {
			var values = new double[ 3 ];
			var count = DarwinNative.GetLoadAverage( values, values.Length );
			if ( 3 > count ) return ProcObservedValue<ProcLoadAverages>.Missing( ProcObservationAvailability.Unavailable, "getloadavg did not return all three load averages." );
			return ProcObservedValue<ProcLoadAverages>.Available( new ProcLoadAverages( values[ 0 ], values[ 1 ], values[ 2 ] ), ProcObservationSource.PosixLibc, ObservationFidelity.Equivalent );
		} catch ( DllNotFoundException exception ) {
			return ProcObservedValue<ProcLoadAverages>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		} catch ( EntryPointNotFoundException exception ) {
			return ProcObservedValue<ProcLoadAverages>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		}
	}

	private static readonly object UserSessionSync = new();

	private static ProcObservedValue<ProcUserSessionInfo> ObserveUserSessions() {
		if ( !OperatingSystem.IsMacOS() ) return ProcObservedValue<ProcUserSessionInfo>.Missing( ProcObservationAvailability.Unsupported, "Darwin utmpx is available only on macOS." );
		lock ( UserSessionSync ) {
			try {
				DarwinNative.SetUtmpxEnt();
				var count = 0;
				while ( true ) {
					var pointer = DarwinNative.GetUtmpxEnt();
					if ( IntPtr.Zero == pointer ) break;
					var entry = Marshal.PtrToStructure<DarwinNative.Utmpx>( pointer );
					if ( DarwinNative.UserProcess == entry.Type ) count++;
				}
				return ProcObservedValue<ProcUserSessionInfo>.Available( new ProcUserSessionInfo( count ), ProcObservationSource.PosixLibc, ObservationFidelity.Equivalent );
			} catch ( DllNotFoundException exception ) {
				return ProcObservedValue<ProcUserSessionInfo>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
			} catch ( EntryPointNotFoundException exception ) {
				return ProcObservedValue<ProcUserSessionInfo>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
			} finally {
				try { DarwinNative.EndUtmpxEnt(); } catch ( DllNotFoundException ) { } catch ( EntryPointNotFoundException ) { }
			}
		}
	}

	private static ProcObservedValue<ProcUptimeInfo> ObserveUptime() {
		if ( !OperatingSystem.IsMacOS() ) return ProcObservedValue<ProcUptimeInfo>.Missing( ProcObservationAvailability.Unsupported, "Darwin uptime APIs are available only on macOS." );
		try {
			if ( !DarwinNative.TryReadBootTime( out var boot ) ) return ProcObservedValue<ProcUptimeInfo>.Missing( ProcObservationAvailability.Unavailable, "sysctlbyname(kern.boottime) failed." );
			var bootUtc = DateTimeOffset.FromUnixTimeSeconds( boot.Seconds ).AddTicks( boot.Microseconds * 10L );
			var uptime = DateTimeOffset.UtcNow - bootUtc;
			if ( TimeSpan.Zero > uptime ) uptime = TimeSpan.Zero;
			return ProcObservedValue<ProcUptimeInfo>.Available( new ProcUptimeInfo( uptime, null ), ProcObservationSource.DarwinSysctl, ObservationFidelity.Equivalent );
		} catch ( ArgumentOutOfRangeException exception ) {
			return ProcObservedValue<ProcUptimeInfo>.Missing( ProcObservationAvailability.Malformed, exception.Message );
		} catch ( DllNotFoundException exception ) {
			return ProcObservedValue<ProcUptimeInfo>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		} catch ( EntryPointNotFoundException exception ) {
			return ProcObservedValue<ProcUptimeInfo>.Missing( ProcObservationAvailability.Unsupported, exception.Message );
		}
	}

	private static ulong MultiplyPages( uint pages, ulong pageSize ) => MultiplyPages( (ulong)pages, pageSize );
	private static ulong MultiplyPages( ulong pages, ulong pageSize ) => 0UL == pages || 0UL == pageSize ? 0UL : ulong.MaxValue / pages < pageSize ? ulong.MaxValue : pages * pageSize;
	private static ulong SaturatingAdd( ulong left, ulong right ) => ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
	private static ProcSystemSnapshot UnsupportedSnapshot( string diagnostic ) => new() {
		Cpu = ProcObservedValue<ProcCpuTimes>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		CpuActivity = ProcObservedValue<ProcCpuActivity>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		Memory = ProcObservedValue<ProcMemoryInfo>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		LoadAverage = ProcObservedValue<ProcLoadAverage>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		LoadAverages = ProcObservedValue<ProcLoadAverages>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		Uptime = ProcObservedValue<ProcUptimeInfo>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		VirtualMemory = ProcObservedValue<IReadOnlyDictionary<string, ulong>>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		Slab = ProcObservedValue<IReadOnlyList<ProcSlabEntry>>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		HugePages = ProcObservedValue<ProcHugePageInfo>.Missing( ProcObservationAvailability.Unsupported, diagnostic ),
		UserSessions = ProcObservedValue<ProcUserSessionInfo>.Missing( ProcObservationAvailability.Unsupported, diagnostic )
	};

	private static class DarwinNative {
		private const string SystemLibrary = "/usr/lib/libSystem.B.dylib";
		public const int HostCpuLoadInfoFlavor = 3;
		public const int HostVmInfo64 = 4;

		[StructLayout( LayoutKind.Sequential )]
		public struct HostCpuLoadInfo {
			public uint User;
			public uint System;
			public uint Idle;
			public uint Nice;
		}

		[StructLayout( LayoutKind.Sequential )]
		public struct VmStatistics64 {
			public uint FreeCount;
			public uint ActiveCount;
			public uint InactiveCount;
			public uint WireCount;
			public ulong ZeroFillCount;
			public ulong Reactivations;
			public ulong PageIns;
			public ulong PageOuts;
			public ulong Faults;
			public ulong CowFaults;
			public ulong Lookups;
			public ulong Hits;
			public ulong Purges;
			public uint PurgeableCount;
			public uint SpeculativeCount;
			public ulong Decompressions;
			public ulong Compressions;
			public ulong SwapIns;
			public ulong SwapOuts;
			public uint CompressorPageCount;
			public uint ThrottledCount;
			public uint ExternalPageCount;
			public uint InternalPageCount;
			public ulong TotalUncompressedPagesInCompressor;
		}

		[StructLayout( LayoutKind.Sequential )]
		public struct TimeValue {
			public long Seconds;
			public int Microseconds;
		}

		[StructLayout( LayoutKind.Sequential )]
		public struct SwapUsage {
			public ulong Total;
			public ulong Available;
			public ulong Used;
			public uint PageSize;
			public int Encrypted;
		}

		public const short UserProcess = 7;

		[StructLayout( LayoutKind.Sequential )]
		public struct UtmpxTimeValue {
			public long Seconds;
			public int Microseconds;
		}

		[StructLayout( LayoutKind.Sequential, CharSet = CharSet.Ansi )]
		public struct Utmpx {
			[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 256 )]
			public string? User;
			[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 4 )]
			public string? Id;
			[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 32 )]
			public string? Line;
			public int ProcessId;
			public short Type;
			public UtmpxTimeValue Time;
			[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 256 )]
			public string? Host;
			[MarshalAs( UnmanagedType.ByValArray, SizeConst = 16 )]
			public uint[]? Padding;
		}

		[DllImport( SystemLibrary, EntryPoint = "mach_host_self" )]
		public static extern uint MachHostSelf();

		[DllImport( SystemLibrary, EntryPoint = "host_page_size" )]
		public static extern int HostPageSize( uint host, out uint pageSize );

		[DllImport( SystemLibrary, EntryPoint = "host_statistics" )]
		public static extern int HostStatistics( uint host, int flavor, ref HostCpuLoadInfo info, ref uint count );

		[DllImport( SystemLibrary, EntryPoint = "host_statistics64" )]
		public static extern int HostStatistics64( uint host, int flavor, ref VmStatistics64 info, ref uint count );

		[DllImport( SystemLibrary, EntryPoint = "getloadavg" )]
		public static extern int GetLoadAverage( [Out] double[] loadAverage, int count );

		[DllImport( SystemLibrary, EntryPoint = "setutxent" )]
		public static extern void SetUtmpxEnt();

		[DllImport( SystemLibrary, EntryPoint = "getutxent" )]
		public static extern IntPtr GetUtmpxEnt();

		[DllImport( SystemLibrary, EntryPoint = "endutxent" )]
		public static extern void EndUtmpxEnt();

		[DllImport( SystemLibrary, EntryPoint = "sysctlbyname" )]
		private static extern int SysCtlUInt64( [MarshalAs( UnmanagedType.LPStr )] string name, out ulong value, ref nuint valueSize, IntPtr newValue, nuint newValueSize );

		[DllImport( SystemLibrary, EntryPoint = "sysctlbyname" )]
		private static extern int SysCtlTimeValue( [MarshalAs( UnmanagedType.LPStr )] string name, out TimeValue value, ref nuint valueSize, IntPtr newValue, nuint newValueSize );

		[DllImport( SystemLibrary, EntryPoint = "sysctlbyname" )]
		private static extern int SysCtlSwapUsage( [MarshalAs( UnmanagedType.LPStr )] string name, out SwapUsage value, ref nuint valueSize, IntPtr newValue, nuint newValueSize );

		public static bool TryReadUInt64Sysctl( string name, out ulong value ) {
			var size = checked( (nuint)sizeof( ulong ) );
			return 0 == SysCtlUInt64( name, out value, ref size, IntPtr.Zero, 0 ) && size >= (nuint)sizeof( ulong );
		}

		public static bool TryReadBootTime( out TimeValue value ) {
			var size = checked( (nuint)Marshal.SizeOf<TimeValue>() );
			return 0 == SysCtlTimeValue( "kern.boottime", out value, ref size, IntPtr.Zero, 0 ) && size >= (nuint)Marshal.SizeOf<TimeValue>();
		}

		public static bool TryReadSwapUsage( out SwapUsage value ) {
			var size = checked( (nuint)Marshal.SizeOf<SwapUsage>() );
			return 0 == SysCtlSwapUsage( "vm.swapusage", out value, ref size, IntPtr.Zero, 0 ) && size >= (nuint)Marshal.SizeOf<SwapUsage>();
		}
	}
}
