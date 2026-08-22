// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.Sysctl;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>Identifies a failure reported by an <see cref="ISysctlBackend"/> operation.</summary>
public enum SysctlBackendFailureKind {
	/// <summary>The requested kernel parameter or configuration file was not found.</summary>
	NotFound,
	/// <summary>The operation was denied by the operating system.</summary>
	PermissionDenied,
	/// <summary>The kernel parameter is not owner-writable and procps therefore refuses the write.</summary>
	NotWritable,
	/// <summary>The requested object is a directory where a value file was required.</summary>
	IsDirectory,
	/// <summary>The operating system reported an input/output failure.</summary>
	IoFailure,
	/// <summary>The requested operation is not supported by the active platform.</summary>
	Unsupported
}

/// <summary>Represents a classified backend failure that the command can translate into procps-compatible diagnostics.</summary>
public sealed class SysctlBackendException : IOException {
	/// <summary>Initializes a new instance of the <see cref="SysctlBackendException"/> class.</summary>
	/// <param name="kind">The classified backend failure.</param>
	/// <param name="message">The diagnostic detail supplied by the backend.</param>
	/// <param name="innerException">The optional operating-system exception that caused the failure.</param>
	public SysctlBackendException( SysctlBackendFailureKind kind, string message, Exception? innerException = null )
		: base( message ?? throw new ArgumentNullException( nameof( message ) ), innerException ) {
		this.Kind = kind;
	}

	/// <summary>Gets the classified failure.</summary>
	public SysctlBackendFailureKind Kind { get; }
}

/// <summary>Defines the Linux sysctl value and configuration-file boundary consumed by the command.</summary>
public interface ISysctlBackend {
	/// <summary>Gets whether Linux-style <c>/proc/sys</c> kernel parameter access is available.</summary>
	bool IsSupported { get; }

	/// <summary>Gets a diagnostic explaining why kernel parameter access is unavailable.</summary>
	string UnsupportedReason { get; }

	/// <summary>Reads the value for a normalized slash-separated kernel parameter key.</summary>
	Task<string> ReadValueAsync( string key, CancellationToken cancellationToken = default );
	/// <summary>Writes a value to a normalized slash-separated kernel parameter key.</summary>
	Task WriteValueAsync( string key, string value, bool dryRun, CancellationToken cancellationToken = default );
	/// <summary>Enumerates normalized slash-separated keys below <c>/proc/sys</c>.</summary>
	Task<IReadOnlyList<string>> EnumerateKeysAsync( CancellationToken cancellationToken = default );
	/// <summary>Expands a configuration-file glob while retaining an unmatched literal path.</summary>
	Task<IReadOnlyList<string>> ExpandConfigurationFilesAsync( string pattern, CancellationToken cancellationToken = default );
	/// <summary>Enumerates <c>*.conf</c> files in one system configuration directory.</summary>
	Task<IReadOnlyList<string>> EnumerateConfigurationFilesAsync( string directory, CancellationToken cancellationToken = default );
	/// <summary>Determines whether a configuration file exists.</summary>
	Task<bool> ConfigurationFileExistsAsync( string path, CancellationToken cancellationToken = default );
	/// <summary>Reads all lines from a configuration file.</summary>
	Task<IReadOnlyList<string>> ReadConfigurationFileAsync( string path, CancellationToken cancellationToken = default );
}

/// <summary>Provides the production Linux <c>/proc/sys</c> and sysctl configuration-file backend.</summary>
internal sealed class SystemSysctlBackend : ISysctlBackend {
	private const string ProcRoot = "/proc/sys";
	private static readonly char[] GlobCharacters = [ '*', '?', '[' ];
	/// <summary>Gets the shared system instance.</summary>
	internal static SystemSysctlBackend Instance { get; } = new();
	private SystemSysctlBackend() { }
	/// <summary>Gets whether Linux <c>/proc/sys</c> access is supported on this host.</summary>
	public bool IsSupported => OperatingSystem.IsLinux() && Directory.Exists( ProcRoot );
	/// <summary>Gets the diagnostic used when Linux <c>/proc/sys</c> access is unavailable.</summary>
	public string UnsupportedReason {
		get {
			if ( !OperatingSystem.IsLinux() ) {
				return "Linux /proc/sys kernel parameter access is not supported on this platform";
			}
			return "Linux procfs /proc/sys is unavailable";
		}
	}
	/// <summary>Reads a kernel parameter value asynchronously.</summary>
	public async Task<string> ReadValueAsync( string key, CancellationToken cancellationToken = default ) {
		ArgumentNullException.ThrowIfNull( key ); EnsureSupported(); var path = ResolveProcPath( key );
		if ( Directory.Exists( path ) ) { throw new SysctlBackendException( SysctlBackendFailureKind.IsDirectory, "Is a directory" ); }
		if ( !File.Exists( path ) ) { throw new SysctlBackendException( SysctlBackendFailureKind.NotFound, "No such file or directory" ); }
		try {
			await using var stream = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan );
			using var reader = new StreamReader( stream, Encoding.UTF8, true, 4096, leaveOpen: false );
			var value = await reader.ReadToEndAsync( cancellationToken ).ConfigureAwait( false ); return TrimTerminalLineSeparators( value );
		} catch ( UnauthorizedAccessException exception ) { throw new SysctlBackendException( SysctlBackendFailureKind.PermissionDenied, "Permission denied", exception ); }
		catch ( IOException exception ) { throw ClassifyIoException( exception ); }
	}
	/// <summary>Writes a kernel parameter value asynchronously, or validates it in dry-run mode.</summary>
	public async Task WriteValueAsync( string key, string value, bool dryRun, CancellationToken cancellationToken = default ) {
		ArgumentNullException.ThrowIfNull( key ); ArgumentNullException.ThrowIfNull( value ); EnsureSupported(); var path = ResolveProcPath( key );
		if ( Directory.Exists( path ) ) { throw new SysctlBackendException( SysctlBackendFailureKind.IsDirectory, "Is a directory" ); }
		if ( !File.Exists( path ) ) { throw new SysctlBackendException( SysctlBackendFailureKind.NotFound, "No such file or directory" ); }
		EnsureOwnerWritable( path ); if ( dryRun ) { return; }
		try {
			await using var stream = new FileStream( path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous );
			var bytes = new UTF8Encoding( false ).GetBytes( string.Concat( value, "\n" ) ); await stream.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false ); await stream.FlushAsync( cancellationToken ).ConfigureAwait( false );
		} catch ( UnauthorizedAccessException exception ) { throw new SysctlBackendException( SysctlBackendFailureKind.PermissionDenied, "Permission denied", exception ); }
		catch ( IOException exception ) { throw ClassifyIoException( exception ); }
	}
	/// <summary>Enumerates readable kernel parameter keys asynchronously.</summary>
	public Task<IReadOnlyList<string>> EnumerateKeysAsync( CancellationToken cancellationToken = default ) {
		EnsureSupported(); cancellationToken.ThrowIfCancellationRequested(); var keys = new List<string>(); var pending = new Stack<string>(); pending.Push( ProcRoot );
		while ( 0 < pending.Count ) {
			cancellationToken.ThrowIfCancellationRequested(); var directory = pending.Pop(); IEnumerable<string> entries;
			try { entries = Directory.EnumerateFileSystemEntries( directory ); } catch ( UnauthorizedAccessException ) { continue; } catch ( IOException ) { continue; }
			foreach ( var entry in entries ) {
				cancellationToken.ThrowIfCancellationRequested();
				if ( Directory.Exists( entry ) ) { if ( IsReparsePoint( entry ) ) { continue; } pending.Push( entry ); continue; }
				if ( File.Exists( entry ) ) { var relative = System.IO.Path.GetRelativePath( ProcRoot, entry ).Replace( System.IO.Path.DirectorySeparatorChar, '/' ); keys.Add( relative ); }
			}
		}
		keys.Sort( StringComparer.Ordinal ); return Task.FromResult<IReadOnlyList<string>>( keys );
	}
	/// <summary>Expands a configuration-file glob asynchronously.</summary>
	public Task<IReadOnlyList<string>> ExpandConfigurationFilesAsync( string pattern, CancellationToken cancellationToken = default ) {
		ArgumentNullException.ThrowIfNull( pattern ); cancellationToken.ThrowIfCancellationRequested();
		if ( 0 > pattern.IndexOfAny( GlobCharacters ) ) { return Task.FromResult<IReadOnlyList<string>>( [ pattern ] ); }
		var root = ResolveGlobRoot( pattern ); if ( !Directory.Exists( root ) ) { return Task.FromResult<IReadOnlyList<string>>( [ pattern ] ); }
		var matches = new List<string>();
		try {
			var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, ReturnSpecialDirectories = false };
			foreach ( var file in Directory.EnumerateFiles( root, "*", options ) ) { cancellationToken.ThrowIfCancellationRequested(); if ( GlobMatch( pattern, file ) ) { matches.Add( file ); } }
		} catch ( UnauthorizedAccessException ) { }
		matches.Sort( StringComparer.Ordinal ); if ( 0 == matches.Count ) { matches.Add( pattern ); } return Task.FromResult<IReadOnlyList<string>>( matches );
	}
	/// <summary>Enumerates <c>.conf</c> files in a configuration directory asynchronously.</summary>
	public Task<IReadOnlyList<string>> EnumerateConfigurationFilesAsync( string directory, CancellationToken cancellationToken = default ) {
		ArgumentNullException.ThrowIfNull( directory ); cancellationToken.ThrowIfCancellationRequested(); if ( !Directory.Exists( directory ) ) { return Task.FromResult<IReadOnlyList<string>>( [] ); }
		try { var files = Directory.EnumerateFiles( directory, "*.conf", SearchOption.TopDirectoryOnly ).OrderBy( static path => GetPortableFileName( path ), StringComparer.Ordinal ).ToArray(); return Task.FromResult<IReadOnlyList<string>>( files ); }
		catch ( UnauthorizedAccessException ) { return Task.FromResult<IReadOnlyList<string>>( [] ); } catch ( IOException ) { return Task.FromResult<IReadOnlyList<string>>( [] ); }
	}
	/// <summary>Determines asynchronously whether a configuration file exists.</summary>
	public Task<bool> ConfigurationFileExistsAsync( string path, CancellationToken cancellationToken = default ) { ArgumentNullException.ThrowIfNull( path ); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult( File.Exists( path ) ); }
	/// <summary>Reads all lines from a sysctl configuration file asynchronously.</summary>
	public async Task<IReadOnlyList<string>> ReadConfigurationFileAsync( string path, CancellationToken cancellationToken = default ) {
		ArgumentNullException.ThrowIfNull( path ); cancellationToken.ThrowIfCancellationRequested();
		try { return await File.ReadAllLinesAsync( path, cancellationToken ).ConfigureAwait( false ); }
		catch ( FileNotFoundException exception ) { throw new SysctlBackendException( SysctlBackendFailureKind.NotFound, "No such file or directory", exception ); }
		catch ( DirectoryNotFoundException exception ) { throw new SysctlBackendException( SysctlBackendFailureKind.NotFound, "No such file or directory", exception ); }
		catch ( UnauthorizedAccessException exception ) { throw new SysctlBackendException( SysctlBackendFailureKind.PermissionDenied, "Permission denied", exception ); }
		catch ( IOException exception ) { throw new SysctlBackendException( SysctlBackendFailureKind.IoFailure, exception.Message, exception ); }
	}
	private static void EnsureSupported() { if ( !Instance.IsSupported ) { throw new SysctlBackendException( SysctlBackendFailureKind.Unsupported, Instance.UnsupportedReason ); } }
	private static string ResolveProcPath( string key ) {
		if ( string.IsNullOrWhiteSpace( key ) || key.StartsWith( "/", StringComparison.Ordinal ) || key.Contains( '\0' ) ) { throw new SysctlBackendException( SysctlBackendFailureKind.NotFound, "No such file or directory" ); }
		foreach ( var segment in key.Split( '/', StringSplitOptions.None ) ) { if ( ".." == segment ) { throw new SysctlBackendException( SysctlBackendFailureKind.NotFound, "No such file or directory" ); } }
		var root = System.IO.Path.GetFullPath( ProcRoot ); var candidate = System.IO.Path.GetFullPath( System.IO.Path.Combine( root, key.Replace( '/', System.IO.Path.DirectorySeparatorChar ) ) ); var rootPrefix = string.Concat( root.TrimEnd( System.IO.Path.DirectorySeparatorChar ), System.IO.Path.DirectorySeparatorChar );
		if ( !candidate.StartsWith( rootPrefix, StringComparison.Ordinal ) ) { throw new SysctlBackendException( SysctlBackendFailureKind.NotFound, "No such file or directory" ); }
		return candidate;
	}
	private static void EnsureOwnerWritable( string path ) {
		ArgumentNullException.ThrowIfNull( path ); if ( !OperatingSystem.IsLinux() ) { return; }
		try { var mode = File.GetUnixFileMode( path ); if ( 0 == ( mode & UnixFileMode.UserWrite ) ) { throw new SysctlBackendException( SysctlBackendFailureKind.NotWritable, "Operation not permitted" ); } }
		catch ( PlatformNotSupportedException ) { } catch ( UnauthorizedAccessException exception ) { throw new SysctlBackendException( SysctlBackendFailureKind.PermissionDenied, "Permission denied", exception ); } catch ( IOException exception ) { throw ClassifyIoException( exception ); }
	}
	private static SysctlBackendException ClassifyIoException( IOException exception ) { ArgumentNullException.ThrowIfNull( exception ); if ( exception is FileNotFoundException or DirectoryNotFoundException ) { return new SysctlBackendException( SysctlBackendFailureKind.NotFound, "No such file or directory", exception ); } return new SysctlBackendException( SysctlBackendFailureKind.IoFailure, exception.Message, exception ); }
	private static bool IsReparsePoint( string path ) { ArgumentNullException.ThrowIfNull( path ); try { return 0 != ( File.GetAttributes( path ) & FileAttributes.ReparsePoint ); } catch ( IOException ) { return true; } catch ( UnauthorizedAccessException ) { return true; } }
	private static string TrimTerminalLineSeparators( string value ) { ArgumentNullException.ThrowIfNull( value ); return value.TrimEnd( '\r', '\n' ); }
	private static string ResolveGlobRoot( string pattern ) {
		ArgumentNullException.ThrowIfNull( pattern ); var wildcard = pattern.IndexOfAny( GlobCharacters );
		if ( 0 > wildcard ) { var literalDirectory = System.IO.Path.GetDirectoryName( pattern ); return string.IsNullOrEmpty( literalDirectory ) ? Directory.GetCurrentDirectory() : literalDirectory; }
		var separator = pattern.LastIndexOfAny( [ '/', '\\' ], wildcard ); if ( 0 > separator ) { return Directory.GetCurrentDirectory(); } if ( 0 == separator ) { return System.IO.Path.GetPathRoot( pattern ) ?? System.IO.Path.DirectorySeparatorChar.ToString(); } return pattern[..separator];
	}
	private static bool GlobMatch( string pattern, string value ) {
		ArgumentNullException.ThrowIfNull( pattern ); ArgumentNullException.ThrowIfNull( value ); var expression = new StringBuilder( "^" );
		for ( var index = 0; index < pattern.Length; index++ ) { var current = pattern[index]; if ( '*' == current ) { expression.Append( @"[^/\\]*" ); continue; } if ( '?' == current ) { expression.Append( @"[^/\\]" ); continue; } if ( '[' == current ) { var close = pattern.IndexOf( ']', index + 1 ); if ( 0 < close ) { expression.Append( pattern.AsSpan( index, close - index + 1 ) ); index = close; continue; } } expression.Append( Regex.Escape( current.ToString() ) ); }
		expression.Append( '$' ); return Regex.IsMatch( value, expression.ToString(), RegexOptions.CultureInvariant );
	}
	private static string GetPortableFileName( string path ) { ArgumentNullException.ThrowIfNull( path ); var slash = Math.Max( path.LastIndexOf( '/' ), path.LastIndexOf( '\\' ) ); return 0 > slash ? path : path[( slash + 1 )..]; }
}
