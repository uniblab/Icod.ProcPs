namespace Icod.ProcPs.Shared;

using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Icod.CommandFramework.Host;

/// <summary>Describes one logged-in interactive user session for ProcPs reporting.</summary>
public sealed class ProcLoginSession {
	/// <summary>Gets the login name.</summary>
	public string UserName { get; }
	/// <summary>Gets the terminal or platform session name when available.</summary>
	public string? TerminalName { get; }
	/// <summary>Gets the remote host or client name when available.</summary>
	public string? RemoteHost { get; }
	/// <summary>Gets the numeric remote address when the native accounting source exposes it.</summary>
	public string? RemoteAddress { get; }
	/// <summary>Gets the login time in UTC when available.</summary>
	public DateTimeOffset? LoginTimeUtc { get; }
	/// <summary>Gets the last observed terminal/session activity time in UTC when available.</summary>
	public DateTimeOffset? LastActivityTimeUtc { get; }
	/// <summary>Gets the login process identifier when the accounting source exposes one.</summary>
	public int? LoginProcessId { get; }
	/// <summary>Gets the native login/session identifier used by platforms such as Windows Terminal Services.</summary>
	public int? PlatformSessionId { get; }

	/// <summary>Initializes a login-session observation.</summary>
	public ProcLoginSession(
		string userName,
		string? terminalName,
		string? remoteHost,
		DateTimeOffset? loginTimeUtc,
		DateTimeOffset? lastActivityTimeUtc,
		int? loginProcessId,
		int? platformSessionId,
		string? remoteAddress = null
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( userName );
		if ( loginProcessId.HasValue && 0 >= loginProcessId.Value ) {
			throw new ArgumentOutOfRangeException( nameof( loginProcessId ) );
		}
		if ( platformSessionId.HasValue && 0 > platformSessionId.Value ) {
			throw new ArgumentOutOfRangeException( nameof( platformSessionId ) );
		}
		this.UserName = userName;
		if ( string.IsNullOrWhiteSpace( terminalName ) ) {
			this.TerminalName = null;
		} else {
			this.TerminalName = terminalName;
		}
		if ( string.IsNullOrWhiteSpace( remoteHost ) ) {
			this.RemoteHost = null;
		} else {
			this.RemoteHost = remoteHost;
		}
		if ( string.IsNullOrWhiteSpace( remoteAddress ) ) {
			this.RemoteAddress = null;
		} else {
			this.RemoteAddress = remoteAddress;
		}
		this.LoginTimeUtc = loginTimeUtc;
		this.LastActivityTimeUtc = lastActivityTimeUtc;
		this.LoginProcessId = loginProcessId;
		this.PlatformSessionId = platformSessionId;
	}
}

/// <summary>Observes detailed logged-in sessions for commands such as <c>w</c>.</summary>
public interface IProcLoginSessionProvider {
	/// <summary>Gets the currently observable logged-in sessions.</summary>
	Task<ProcObservedValue<IReadOnlyList<ProcLoginSession>>> GetSessionsAsync( CancellationToken cancellationToken = default );
}

/// <summary>Selects the strongest detailed login-session provider available on the current platform.</summary>
public sealed class SystemProcLoginSessionProvider : IProcLoginSessionProvider {
	private readonly IProcLoginSessionProvider inner;
	/// <summary>Gets the shared system login-session provider.</summary>
	public static SystemProcLoginSessionProvider Instance { get; } = new();

	/// <summary>Initializes a provider for the current operating system.</summary>
	public SystemProcLoginSessionProvider() {
		if ( OperatingSystem.IsLinux() ) {
			this.inner = new LinuxProcLoginSessionProvider();
		} else if ( OperatingSystem.IsWindows() ) {
			this.inner = new WindowsProcLoginSessionProvider();
		} else if ( OperatingSystem.IsMacOS() ) {
			this.inner = new MacOsProcLoginSessionProvider();
		} else {
			this.inner = new UnsupportedProcLoginSessionProvider();
		}
	}

	/// <inheritdoc />
	public Task<ProcObservedValue<IReadOnlyList<ProcLoginSession>>> GetSessionsAsync( CancellationToken cancellationToken = default ) {
		return this.inner.GetSessionsAsync( cancellationToken );
	}
}

/// <summary>Reads Linux login accounting through libc <c>utmpx</c>.</summary>
public sealed class LinuxProcLoginSessionProvider : IProcLoginSessionProvider {
	private const short UserProcess = 7;
	private static readonly object Sync = new();
	private readonly string deviceRoot;

	/// <summary>Initializes the Linux provider.</summary>
	public LinuxProcLoginSessionProvider( string deviceRoot = "/dev" ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( deviceRoot );
		this.deviceRoot = deviceRoot;
	}

	/// <inheritdoc />
	public Task<ProcObservedValue<IReadOnlyList<ProcLoginSession>>> GetSessionsAsync( CancellationToken cancellationToken = default ) {
		cancellationToken.ThrowIfCancellationRequested();
		if ( !OperatingSystem.IsLinux() ) {
			return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcLoginSession>>.Missing(
				ProcObservationAvailability.Unsupported,
				"Linux utmpx login accounting is available only on Linux."
			) );
		}
		lock ( Sync ) {
			var opened = false;
			try {
				Native.SetUtmpxEnt();
				opened = true;
				var sessions = new List<ProcLoginSession>();
				while ( true ) {
					cancellationToken.ThrowIfCancellationRequested();
					var pointer = Native.GetUtmpxEnt();
					if ( IntPtr.Zero == pointer ) {
						break;
					}
					var entry = Marshal.PtrToStructure<Native.Utmpx>( pointer );
					if ( UserProcess != entry.Type || string.IsNullOrWhiteSpace( entry.User ) ) {
						continue;
					}
					var terminal = Clean( entry.Line );
					sessions.Add( new ProcLoginSession(
						entry.User!.TrimEnd( '\0' ),
						terminal,
						Clean( entry.Host ),
						TryUnixTime( entry.Seconds ),
						ObserveTerminalActivity( this.deviceRoot, terminal ),
						PositiveOrNull( entry.ProcessId ),
						null,
						FormatAddress( entry.Address )
					) );
				}
				return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcLoginSession>>.Available(
					sessions,
					ProcObservationSource.PosixLibc,
					ObservationFidelity.Exact
				) );
			} catch ( DllNotFoundException exception ) {
				return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcLoginSession>>.Missing( ProcObservationAvailability.Unsupported, exception.Message ) );
			} catch ( EntryPointNotFoundException exception ) {
				return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcLoginSession>>.Missing( ProcObservationAvailability.Unsupported, exception.Message ) );
			} catch ( MarshalDirectiveException exception ) {
				return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcLoginSession>>.Missing( ProcObservationAvailability.Malformed, exception.Message ) );
			} finally {
				if ( opened ) {
					try {
						Native.EndUtmpxEnt();
					} catch ( DllNotFoundException ) {
					} catch ( EntryPointNotFoundException ) {
					}
				}
			}
		}
	}

	private static string? Clean( string? value ) {
		var result = value?.TrimEnd( '\0' ).Trim();
		if ( string.IsNullOrEmpty( result ) ) {
			return null;
		}
		return result;
	}

	private static int? PositiveOrNull( int value ) {
		if ( 0 < value ) {
			return value;
		}
		return null;
	}

	private static DateTimeOffset? TryUnixTime( int seconds ) {
		if ( 0 >= seconds ) {
			return null;
		}
		try {
			return DateTimeOffset.FromUnixTimeSeconds( seconds );
		} catch ( ArgumentOutOfRangeException ) {
			return null;
		}
	}

	private static string? FormatAddress( int[]? address ) {
		if ( null == address || 4 > address.Length ) {
			return null;
		}
		var bytes = new byte[ 16 ];
		Buffer.BlockCopy( address, 0, bytes, 0, bytes.Length );
		var ipv4 = true;
		for ( var index = 4; index < bytes.Length; index++ ) {
			if ( 0 != bytes[ index ] ) {
				ipv4 = false;
				break;
			}
		}
		if ( ipv4 ) {
			if ( 0 == bytes[ 0 ] && 0 == bytes[ 1 ] && 0 == bytes[ 2 ] && 0 == bytes[ 3 ] ) {
				return null;
			}
			return new IPAddress( bytes[ ..4 ] ).ToString();
		}
		var result = new IPAddress( bytes );
		if ( result.IsIPv4MappedToIPv6 ) {
			result = result.MapToIPv4();
		}
		return result.ToString();
	}

	private static DateTimeOffset? ObserveTerminalActivity( string deviceRoot, string? terminal ) {
		if ( string.IsNullOrWhiteSpace( terminal ) || terminal.StartsWith( ":", StringComparison.Ordinal ) ) {
			return null;
		}
		try {
			return new DateTimeOffset( File.GetLastAccessTimeUtc( System.IO.Path.Combine( deviceRoot, terminal ) ), TimeSpan.Zero );
		} catch ( IOException ) {
			return null;
		} catch ( UnauthorizedAccessException ) {
			return null;
		}
	}

	private static class Native {
		[StructLayout( LayoutKind.Sequential, CharSet = CharSet.Ansi )]
		public struct Utmpx {
			public short Type;
			public int ProcessId;
			[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 32 )]
			public string? Line;
			[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 4 )]
			public string? Id;
			[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 32 )]
			public string? User;
			[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 256 )]
			public string? Host;
			public short ExitTermination;
			public short ExitCode;
			public int Session;
			public int Seconds;
			public int Microseconds;
			[MarshalAs( UnmanagedType.ByValArray, SizeConst = 4 )]
			public int[]? Address;
			[MarshalAs( UnmanagedType.ByValArray, SizeConst = 20 )]
			public byte[]? Reserved;
		}

		[DllImport( "libc", EntryPoint = "setutxent", ExactSpelling = true )]
		public static extern void SetUtmpxEnt();
		[DllImport( "libc", EntryPoint = "getutxent", ExactSpelling = true )]
		public static extern IntPtr GetUtmpxEnt();
		[DllImport( "libc", EntryPoint = "endutxent", ExactSpelling = true )]
		public static extern void EndUtmpxEnt();
	}
}

/// <summary>Reads macOS login accounting through Darwin libc <c>utmpx</c>.</summary>
public sealed class MacOsProcLoginSessionProvider : IProcLoginSessionProvider {
	private const short UserProcess = 7;
	private static readonly object Sync = new();
	private readonly string deviceRoot;

	/// <summary>Initializes the macOS provider.</summary>
	public MacOsProcLoginSessionProvider( string deviceRoot = "/dev" ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( deviceRoot );
		this.deviceRoot = deviceRoot;
	}

	/// <inheritdoc />
	public Task<ProcObservedValue<IReadOnlyList<ProcLoginSession>>> GetSessionsAsync( CancellationToken cancellationToken = default ) {
		cancellationToken.ThrowIfCancellationRequested();
		if ( !OperatingSystem.IsMacOS() ) {
			return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcLoginSession>>.Missing(
				ProcObservationAvailability.Unsupported,
				"Darwin utmpx login accounting is available only on macOS."
			) );
		}
		lock ( Sync ) {
			var opened = false;
			try {
				Native.SetUtmpxEnt();
				opened = true;
				var sessions = new List<ProcLoginSession>();
				while ( true ) {
					cancellationToken.ThrowIfCancellationRequested();
					var pointer = Native.GetUtmpxEnt();
					if ( IntPtr.Zero == pointer ) {
						break;
					}
					var entry = Marshal.PtrToStructure<Native.Utmpx>( pointer );
					if ( UserProcess != entry.Type || string.IsNullOrWhiteSpace( entry.User ) ) {
						continue;
					}
					var terminal = Clean( entry.Line );
					sessions.Add( new ProcLoginSession(
						entry.User!.TrimEnd( '\0' ),
						terminal,
						Clean( entry.Host ),
						TryUnixTime( entry.Time.Seconds ),
						ObserveTerminalActivity( this.deviceRoot, terminal ),
						PositiveOrNull( entry.ProcessId ),
						null
					) );
				}
				return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcLoginSession>>.Available(
					sessions,
					ProcObservationSource.PosixLibc,
					ObservationFidelity.Equivalent
				) );
			} catch ( DllNotFoundException exception ) {
				return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcLoginSession>>.Missing( ProcObservationAvailability.Unsupported, exception.Message ) );
			} catch ( EntryPointNotFoundException exception ) {
				return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcLoginSession>>.Missing( ProcObservationAvailability.Unsupported, exception.Message ) );
			} finally {
				if ( opened ) {
					try {
						Native.EndUtmpxEnt();
					} catch ( DllNotFoundException ) {
					} catch ( EntryPointNotFoundException ) {
					}
				}
			}
		}
	}

	private static string? Clean( string? value ) {
		var result = value?.TrimEnd( '\0' ).Trim();
		if ( string.IsNullOrEmpty( result ) ) {
			return null;
		}
		return result;
	}

	private static int? PositiveOrNull( int value ) {
		if ( 0 < value ) {
			return value;
		}
		return null;
	}

	private static DateTimeOffset? TryUnixTime( long seconds ) {
		if ( 0 >= seconds ) {
			return null;
		}
		try {
			return DateTimeOffset.FromUnixTimeSeconds( seconds );
		} catch ( ArgumentOutOfRangeException ) {
			return null;
		}
	}

	private static DateTimeOffset? ObserveTerminalActivity( string deviceRoot, string? terminal ) {
		if ( string.IsNullOrWhiteSpace( terminal ) || terminal.StartsWith( ":", StringComparison.Ordinal ) ) {
			return null;
		}
		try {
			return new DateTimeOffset( File.GetLastAccessTimeUtc( System.IO.Path.Combine( deviceRoot, terminal ) ), TimeSpan.Zero );
		} catch ( IOException ) {
			return null;
		} catch ( UnauthorizedAccessException ) {
			return null;
		}
	}

	private static class Native {
		private const string SystemLibrary = "/usr/lib/libSystem.B.dylib";
		[StructLayout( LayoutKind.Sequential )]
		public struct TimeValue {
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
			public TimeValue Time;
			[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 256 )]
			public string? Host;
			[MarshalAs( UnmanagedType.ByValArray, SizeConst = 16 )]
			public uint[]? Padding;
		}
		[DllImport( SystemLibrary, EntryPoint = "setutxent" )]
		public static extern void SetUtmpxEnt();
		[DllImport( SystemLibrary, EntryPoint = "getutxent" )]
		public static extern IntPtr GetUtmpxEnt();
		[DllImport( SystemLibrary, EntryPoint = "endutxent" )]
		public static extern void EndUtmpxEnt();
	}
}

/// <summary>Reads logged-in Windows sessions through Remote Desktop Services APIs.</summary>
public sealed class WindowsProcLoginSessionProvider : IProcLoginSessionProvider {
	/// <inheritdoc />
	public Task<ProcObservedValue<IReadOnlyList<ProcLoginSession>>> GetSessionsAsync( CancellationToken cancellationToken = default ) {
		cancellationToken.ThrowIfCancellationRequested();
		if ( !OperatingSystem.IsWindows() ) {
			return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcLoginSession>>.Missing(
				ProcObservationAvailability.Unsupported,
				"Windows Terminal Services session APIs are available only on Windows."
			) );
		}
		IntPtr buffer = IntPtr.Zero;
		try {
			if ( !Native.WTSEnumerateSessionsW( IntPtr.Zero, 0, 1, out buffer, out var count ) ) {
				return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcLoginSession>>.Missing(
					ProcObservationAvailability.Unavailable,
					$"WTSEnumerateSessionsW failed with Win32 error {Marshal.GetLastWin32Error()}."
				) );
			}
			var sessions = new List<ProcLoginSession>();
			var size = Marshal.SizeOf<Native.SessionInfo>();
			for ( var index = 0; index < count; index++ ) {
				cancellationToken.ThrowIfCancellationRequested();
				var pointer = IntPtr.Add( buffer, checked( index * size ) );
				var summary = Marshal.PtrToStructure<Native.SessionInfo>( pointer );
				if ( !TryReadInfo( summary.SessionId, out var info ) || string.IsNullOrWhiteSpace( info.UserName ) ) {
					continue;
				}
				var remoteHost = TryReadString( summary.SessionId, Native.InfoClass.ClientName );
				var terminalName = info.WinStationName;
				if ( string.IsNullOrWhiteSpace( terminalName ) ) {
					terminalName = Marshal.PtrToStringUni( summary.WinStationName );
				}
				sessions.Add( new ProcLoginSession(
					info.UserName!,
					terminalName,
					remoteHost,
					FromFileTime( info.LogonTime ),
					FromFileTime( info.LastInputTime ),
					null,
					summary.SessionId,
					TryReadAddress( summary.SessionId )
				) );
			}
			return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcLoginSession>>.Available(
				sessions,
				ProcObservationSource.WindowsNativeApi,
				ObservationFidelity.Equivalent
			) );
		} catch ( DllNotFoundException exception ) {
			return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcLoginSession>>.Missing( ProcObservationAvailability.Unsupported, exception.Message ) );
		} catch ( EntryPointNotFoundException exception ) {
			return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcLoginSession>>.Missing( ProcObservationAvailability.Unsupported, exception.Message ) );
		} finally {
			if ( IntPtr.Zero != buffer ) {
				Native.WTSFreeMemory( buffer );
			}
		}
	}

	private static bool TryReadInfo( int sessionId, out Native.WtsInfo info ) {
		IntPtr buffer = IntPtr.Zero;
		try {
			if ( !Native.WTSQuerySessionInformationW( IntPtr.Zero, sessionId, Native.InfoClass.SessionInfo, out buffer, out var bytes )
				|| IntPtr.Zero == buffer || Marshal.SizeOf<Native.WtsInfo>() > bytes ) {
				info = default;
				return false;
			}
			info = Marshal.PtrToStructure<Native.WtsInfo>( buffer );
			return true;
		} finally {
			if ( IntPtr.Zero != buffer ) {
				Native.WTSFreeMemory( buffer );
			}
		}
	}

	private static string? TryReadString( int sessionId, Native.InfoClass infoClass ) {
		IntPtr buffer = IntPtr.Zero;
		try {
			if ( !Native.WTSQuerySessionInformationW( IntPtr.Zero, sessionId, infoClass, out buffer, out var bytes )
				|| IntPtr.Zero == buffer || 2 >= bytes ) {
				return null;
			}
			var value = Marshal.PtrToStringUni( buffer );
			if ( string.IsNullOrWhiteSpace( value ) ) {
				return null;
			}
			return value;
		} finally {
			if ( IntPtr.Zero != buffer ) {
				Native.WTSFreeMemory( buffer );
			}
		}
	}

	private static string? TryReadAddress( int sessionId ) {
		IntPtr buffer = IntPtr.Zero;
		try {
			if ( !Native.WTSQuerySessionInformationW( IntPtr.Zero, sessionId, Native.InfoClass.ClientAddress, out buffer, out var bytes )
				|| IntPtr.Zero == buffer || Marshal.SizeOf<Native.ClientAddress>() > bytes ) {
				return null;
			}
			var info = Marshal.PtrToStructure<Native.ClientAddress>( buffer );
			if ( null == info.Address ) {
				return null;
			}
			if ( AddressFamily.InterNetwork == (AddressFamily)info.AddressFamily && 6 <= info.Address.Length ) {
				var address = new byte[ 4 ];
				Array.Copy( info.Address, 2, address, 0, address.Length );
				if ( address.All( static value => 0 == value ) ) {
					return null;
				}
				return new IPAddress( address ).ToString();
			}
			if ( AddressFamily.InterNetworkV6 == (AddressFamily)info.AddressFamily && 16 <= info.Address.Length ) {
				var address = new byte[ 16 ];
				Array.Copy( info.Address, address, address.Length );
				if ( address.All( static value => 0 == value ) ) {
					return null;
				}
				return new IPAddress( address ).ToString();
			}
			return null;
		} finally {
			if ( IntPtr.Zero != buffer ) {
				Native.WTSFreeMemory( buffer );
			}
		}
	}

	private static DateTimeOffset? FromFileTime( long value ) {
		if ( 0 >= value ) {
			return null;
		}
		try {
			return DateTimeOffset.FromFileTime( value ).ToUniversalTime();
		} catch ( ArgumentOutOfRangeException ) {
			return null;
		}
	}

	private static class Native {
		[StructLayout( LayoutKind.Sequential, CharSet = CharSet.Unicode )]
		public struct SessionInfo {
			public int SessionId;
			public IntPtr WinStationName;
			public int State;
		}

		[StructLayout( LayoutKind.Sequential )]
		public struct ClientAddress {
			public int AddressFamily;
			[MarshalAs( UnmanagedType.ByValArray, SizeConst = 20 )]
			public byte[]? Address;
		}

		[StructLayout( LayoutKind.Sequential, CharSet = CharSet.Unicode )]
		public struct WtsInfo {
			public int State;
			public uint SessionId;
			public uint IncomingBytes;
			public uint OutgoingBytes;
			public uint IncomingFrames;
			public uint OutgoingFrames;
			public uint IncomingCompressedBytes;
			public uint OutgoingCompressedBytes;
			[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 32 )]
			public string? WinStationName;
			[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 17 )]
			public string? Domain;
			[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 21 )]
			public string? UserName;
			public long ConnectTime;
			public long DisconnectTime;
			public long LastInputTime;
			public long LogonTime;
			public long CurrentTime;
		}

		public enum InfoClass {
			UserName = 5,
			ClientName = 10,
			ClientAddress = 14,
			SessionInfo = 24
		}

		[DllImport( "wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true )]
		[return: MarshalAs( UnmanagedType.Bool )]
		public static extern bool WTSEnumerateSessionsW( IntPtr serverHandle, int reserved, int version, out IntPtr sessionInfo, out int count );
		[DllImport( "wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true )]
		[return: MarshalAs( UnmanagedType.Bool )]
		public static extern bool WTSQuerySessionInformationW( IntPtr serverHandle, int sessionId, InfoClass infoClass, out IntPtr buffer, out int bytesReturned );
		[DllImport( "wtsapi32.dll" )]
		public static extern void WTSFreeMemory( IntPtr memory );
	}
}

internal sealed class UnsupportedProcLoginSessionProvider : IProcLoginSessionProvider {
	public Task<ProcObservedValue<IReadOnlyList<ProcLoginSession>>> GetSessionsAsync( CancellationToken cancellationToken = default ) {
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult( ProcObservedValue<IReadOnlyList<ProcLoginSession>>.Missing(
			ProcObservationAvailability.Unsupported,
			"Detailed login-session accounting is not implemented for this platform."
		) );
	}
}
