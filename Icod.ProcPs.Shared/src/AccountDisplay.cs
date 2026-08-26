namespace Icod.ProcPs.Shared;

using System.Globalization;

/// <summary>Resolves ProcPs account operands in both name-to-id and id-to-name directions.</summary>
public interface IProcAccountDisplayResolver : IProcAccountResolver {
	/// <summary>Resolves a user identifier to a display name.</summary>
	bool TryGetUserName( uint id, out string name );
	/// <summary>Resolves a group identifier to a display name.</summary>
	bool TryGetGroupName( uint id, out string name );
}

/// <summary>Provides host account resolution for ProcPs presentation engines.</summary>
public sealed class SystemProcAccountDisplayResolver : IProcAccountDisplayResolver {
	/// <summary>Gets the shared host account display resolver.</summary>
	public static SystemProcAccountDisplayResolver Instance { get; } = new();

	private SystemProcAccountDisplayResolver() {
	}

	/// <inheritdoc />
	public bool TryResolveUser( string text, out uint id ) {
		ArgumentNullException.ThrowIfNull( text );
		return SystemProcAccountResolver.Instance.TryResolveUser( text, out id );
	}

	/// <inheritdoc />
	public bool TryResolveGroup( string text, out uint id ) {
		ArgumentNullException.ThrowIfNull( text );
		return SystemProcAccountResolver.Instance.TryResolveGroup( text, out id );
	}

	/// <inheritdoc />
	public bool TryGetUserName( uint id, out string name ) => TryResolveUnixName(
		"/etc/passwd",
		id,
		out name
	);

	/// <inheritdoc />
	public bool TryGetGroupName( uint id, out string name ) => TryResolveUnixName(
		"/etc/group",
		id,
		out name
	);

	private static bool TryResolveUnixName(
		string path,
		uint id,
		out string name
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		if ( OperatingSystem.IsWindows() ) {
			name = string.Empty;
			return false;
		}

		try {
			foreach ( string line in File.ReadLines( path ) ) {
				if ( string.IsNullOrEmpty( line ) || '#' == line[ 0 ] ) {
					continue;
				}
				string[] fields = line.Split( ':' );
				if ( 3 > fields.Length ) {
					continue;
				}
				if ( uint.TryParse(
					fields[ 2 ],
					NumberStyles.None,
					CultureInfo.InvariantCulture,
					out uint candidate
				) && id == candidate ) {
					name = fields[ 0 ];
					return true;
				}
			}
		} catch ( IOException ) {
		} catch ( UnauthorizedAccessException ) {
		}

		name = string.Empty;
		return false;
	}
}
