/*
	watch
	Visible-screen screenshot serialization.
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

namespace Icod.ProcPs.Watch;

using System.Globalization;
using System.Text;

/// <summary>Writes procps-compatible text screenshots of the visible watch frame.</summary>
internal static class WatchScreenshot {
	private const int MaximumCollisionSuffix = 999;

	internal static async Task<string> WriteAsync(
		WatchRenderFrame frame,
		string? directory,
		DateTimeOffset now,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( frame );
		cancellationToken.ThrowIfCancellationRequested();

		string targetDirectory = string.IsNullOrEmpty( directory )
			? Directory.GetCurrentDirectory()
			: directory;
		string stem = string.Concat(
			"watch_",
			now.ToString(
				"yyyyMMdd-HHmmss",
				CultureInfo.InvariantCulture
			)
		);
		byte[] content = Encoding.UTF8.GetBytes(
			BuildText(
				frame
			)
		);

		for ( int suffix = -1; suffix <= MaximumCollisionSuffix; suffix++ ) {
			string fileName = 0 > suffix
				? stem
				: string.Concat(
					stem,
					"-",
					suffix.ToString(
						"D3",
						CultureInfo.InvariantCulture
					)
				);
			string path = Path.Combine(
				targetDirectory,
				fileName
			);
			try {
				await using FileStream stream = new(
					path,
					FileMode.CreateNew,
					FileAccess.Write,
					FileShare.Read,
					bufferSize: 4096,
					options: FileOptions.Asynchronous
				);
				await stream.WriteAsync(
					content,
					cancellationToken
				).ConfigureAwait( false );
				await stream.FlushAsync(
					cancellationToken
				).ConfigureAwait( false );
				return path;
			} catch ( IOException ) when ( File.Exists( path ) ) {
			}
		}

		throw new IOException(
			"Unable to allocate a unique watch screenshot filename."
		);
	}

	private static string BuildText(
		WatchRenderFrame frame
	) {
		ArgumentNullException.ThrowIfNull( frame );

		StringBuilder builder = new();
		int width = frame.Screen.Width;
		foreach ( string headerLine in frame.HeaderLines ) {
			AppendLine(
				builder,
				headerLine,
				width
			);
		}

		WatchScreen screen = frame.Screen;
		for ( int row = 0; row < screen.Height; row++ ) {
			for ( int column = 0; column < screen.Width; column++ ) {
				WatchCell cell = screen.GetCell(
					row,
					column
				);
				if ( cell.IsContinuation ) {
					continue;
				}
				builder.Append(
					0 == cell.Content.Length
						? " "
						: cell.Content
				);
			}
			builder.Append( '\n' );
		}
		return builder.ToString();
	}

	private static void AppendLine(
		StringBuilder builder,
		string text,
		int width
	) {
		ArgumentNullException.ThrowIfNull( builder );
		ArgumentNullException.ThrowIfNull( text );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );

		string clipped = WatchTextLayout.ClipToWidth(
			text,
			width
		);
		builder.Append( clipped );
		int displayWidth = WatchTextLayout.GetWidth(
			clipped
		);
		if ( displayWidth < width ) {
			builder.Append(
				' ',
				width - displayWidth
			);
		}
		builder.Append( '\n' );
	}
}
