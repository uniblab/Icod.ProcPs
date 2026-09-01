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

/// <summary>Contains the five native procps color selections for one top window.</summary>
internal readonly record struct TopColorPalette {
	internal TopColorPalette(
		int summary,
		int messages,
		int header,
		int tasks,
		int taskAccent
	) {
		this.Summary = Validate( summary, nameof( summary ) );
		this.Messages = Validate( messages, nameof( messages ) );
		this.Header = Validate( header, nameof( header ) );
		this.Tasks = Validate( tasks, nameof( tasks ) );
		this.TaskAccent = Validate( taskAccent, nameof( taskAccent ) );
	}

	internal int Summary { get; }
	internal int Messages { get; }
	internal int Header { get; }
	internal int Tasks { get; }
	internal int TaskAccent { get; }

	internal static TopColorPalette ForWindow(
		int windowIndex
	) {
		return windowIndex switch {
			0 => new TopColorPalette( 1, 1, 3, -1, 1 ),
			1 => new TopColorPalette( 6, 6, 7, -1, 6 ),
			2 => new TopColorPalette( 5, 5, 4, -1, 5 ),
			3 => new TopColorPalette( 3, 3, 2, -1, 3 ),
			_ => throw new ArgumentOutOfRangeException(
				nameof( windowIndex )
			)
		};
	}

	private static int Validate(
		int value,
		string parameterName
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( parameterName );
		if ( value is < -1 or > 255 ) {
			throw new ArgumentOutOfRangeException(
				parameterName,
				value,
				"A procps top color must be -1 through 255."
			);
		}
		return value;
	}
}
