/*
	Icod.ProcPs.Shared
	Provides shared process and system observation infrastructure for Icod.ProcPs.
	Copyright (C) 2026  Timothy J. Bruce <uniblab@hotmail.com>
*/

/*
	This program is free software: you can redistribute it and/or modify
	it under the terms of the GNU Lesser General Public License as published by
	the Free Software Foundation, either version 3 of the License, or
	(at your option) any later version.

	This program is distributed in the hope that it will be useful,
	but WITHOUT ANY WARRANTY; without even the implied warranty of
	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
	GNU Lesser General Public License for more details.

	You should have received a copy of the GNU Lesser General Public License
	along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

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
