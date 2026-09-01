/*
	top
	Interactively display processes and system activity.
	Copyright (C) 2026  Timothy J. Bruce <uniblab@hotmail.com>
*/

/*
	This program is free software: you can redistribute it and/or modify
	it under the terms of the GNU General Public License as published by
	the Free Software Foundation, either version 3 of the License, or
	(at your option) any later version.

	This program is distributed in the hope that it will be useful,
	but WITHOUT ANY WARRANTY; without even the implied warranty of
	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
	GNU General Public License for more details.

	You should have received a copy of the GNU General Public License
	along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

namespace Icod.ProcPs.Top;

using System.Text;

/// <summary>Maintains the guarded native procps mirror written by the W command.</summary>
internal sealed partial class SystemTopConfigurationStore {
	private async ValueTask SaveNativeMirrorAsync(
		TopRuntimeState state,
		CancellationToken cancellationToken
	) {
		ArgumentNullException.ThrowIfNull( state );
		cancellationToken.ThrowIfCancellationRequested();
		if ( !this.nativeConfigurationEnabled ) {
			return;
		}

		string? path = await this.ResolveNativeWritePathAsync(
			cancellationToken
		).ConfigureAwait( false );
		if ( path is null ) {
			return;
		}

		string? directory = Path.GetDirectoryName(
			path
		);
		if ( string.IsNullOrEmpty( directory ) ) {
			throw new IOException(
				"unable to establish the native top configuration directory"
			);
		}
		Directory.CreateDirectory(
			directory
		);

		string temporaryPath = Path.Combine(
			directory,
			$".{Path.GetFileName( path )}.{Guid.NewGuid():N}.tmp"
		);
		string nativeConfiguration;
		try {
			nativeConfiguration = TopProcpsConfigurationCodec.Serialize(
				state
			);
		} catch ( Exception exception ) when (
			exception is FormatException
				or InvalidOperationException
				or ArgumentException
		) {
			throw new IOException(
				$"unable to serialize the native top configuration: {exception.Message}",
				exception
			);
		}
		try {
			await File.WriteAllTextAsync(
				temporaryPath,
				nativeConfiguration,
				Utf8,
				cancellationToken
			).ConfigureAwait( false );
			File.Move(
				temporaryPath,
				path,
				overwrite: true
			);
		} finally {
			TryDeleteTemporaryFile(
				temporaryPath
			);
		}
	}

	private async ValueTask<string?> ResolveNativeWritePathAsync(
		CancellationToken cancellationToken
	) {
		cancellationToken.ThrowIfCancellationRequested();

		string? legacyPath = this.paths.NativeLegacyPath;
		if (
			legacyPath is not null
			&& File.Exists( legacyPath )
		) {
			return await IsIcodOwnedNativeConfigurationAsync(
				legacyPath,
				cancellationToken
			).ConfigureAwait( false )
				? legacyPath
				: null
			;
		}

		string? personalPath = this.paths.NativePersonalPath;
		if ( personalPath is null ) {
			return null;
		}
		if (
			File.Exists( personalPath )
			&& !await IsIcodOwnedNativeConfigurationAsync(
				personalPath,
				cancellationToken
			).ConfigureAwait( false )
		) {
			return null;
		}
		return personalPath;
	}

	private static async ValueTask<bool> IsIcodOwnedNativeConfigurationAsync(
		string path,
		CancellationToken cancellationToken
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		cancellationToken.ThrowIfCancellationRequested();

		try {
			await using FileStream stream = File.Open(
				path,
				FileMode.Open,
				FileAccess.Read,
				FileShare.ReadWrite
					| FileShare.Delete
			);
			using var reader = new StreamReader(
				stream,
				Encoding.Latin1,
				detectEncodingFromByteOrderMarks: false
			);
			string? firstLine = await reader.ReadLineAsync(
				cancellationToken
			).ConfigureAwait( false );
			return TopProcpsConfigurationCodec.IsIcodOwned(
				firstLine
			);
		} catch ( Exception exception ) when (
			exception is IOException
				or UnauthorizedAccessException
		) {
			_ = exception;
			return false;
		}
	}
}
