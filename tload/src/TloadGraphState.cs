/*
	tload
	Graphically display the current system load average.
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

namespace Icod.ProcPs.Tload;

using System.Globalization;
using Icod.ProcPs.Shared;

/// <summary>Maintains the procps-style scrolling load graph independently from terminal I/O.</summary>
internal sealed class TloadGraphState {
	private readonly double configuredScale;
	private readonly List<GraphPoint> history = [];
	private double scaleFactor;

	/// <summary>Initializes graph state for the requested vertical scale.</summary>
	/// <param name="configuredScale">Configured vertical scale, or zero for automatic scaling.</param>
	internal TloadGraphState( double configuredScale ) {
		if ( 0d > configuredScale || !double.IsFinite( configuredScale ) ) {
			throw new ArgumentOutOfRangeException( nameof( configuredScale ) );
		}
		this.configuredScale = configuredScale;
	}

	/// <summary>Clears scrolling history after a terminal geometry change.</summary>
	internal void Reset() {
		this.history.Clear();
		this.scaleFactor = 0d;
	}

	/// <summary>Renders one complete terminal frame for the next load observation.</summary>
	/// <param name="load">Current one-, five-, and fifteen-minute load averages.</param>
	/// <param name="dimensions">Current terminal dimensions.</param>
	/// <returns>The complete frame payload excluding the terminal home sequence.</returns>
	internal string Render(
		ProcLoadAverages load,
		TloadTerminalDimensions dimensions
	) {
		ArgumentNullException.ThrowIfNull( load );
		if ( 2 > dimensions.Columns || 2 > dimensions.Rows ) {
			throw new ArgumentOutOfRangeException( nameof( dimensions ) );
		}

		double maximumScale = ( 0d < this.configuredScale )
			? this.configuredScale
			: dimensions.Rows
		;
		if ( 0d >= this.scaleFactor ) {
			this.scaleFactor = maximumScale;
		} else if ( this.scaleFactor < maximumScale ) {
			this.scaleFactor *= 2d;
		}
		while ( dimensions.Rows <= load.OneMinute * this.scaleFactor ) {
			this.scaleFactor /= 2d;
			if ( double.Epsilon >= this.scaleFactor ) {
				break;
			}
		}

		this.history.Add( new GraphPoint( load, this.scaleFactor ) );
		if ( dimensions.Columns < this.history.Count ) {
			this.history.RemoveAt( 0 );
		}

		int size = checked( dimensions.Columns * dimensions.Rows );
		var buffer = new char[ size ];
		Array.Fill( buffer, ' ' );
		for ( int column = 0; column < this.history.Count; column++ ) {
			DrawColumn(
				buffer,
				dimensions,
				column,
				this.history[ column ]
			);
		}

		string label = string.Concat(
			" ",
			load.OneMinute.ToString( "F2", CultureInfo.InvariantCulture ),
			", ",
			load.FiveMinutes.ToString( "F2", CultureInfo.InvariantCulture ),
			", ",
			load.FifteenMinutes.ToString( "F2", CultureInfo.InvariantCulture )
		);
		int labelLength = Math.Min(
			label.Length,
			Math.Max( 0, size - 1 )
		);
		label.AsSpan( 0, labelLength ).CopyTo( buffer );
		if ( labelLength < size - 1 ) {
			buffer[ labelLength ] = ' ';
		}

		return new string( buffer, 0, size - 1 );
	}

	private static void DrawColumn(
		char[] buffer,
		TloadTerminalDimensions dimensions,
		int column,
		GraphPoint point
	) {
		ArgumentNullException.ThrowIfNull( buffer );
		ArgumentNullException.ThrowIfNull( point );

		int lines = (int)( point.Load.OneMinute * point.Scale );
		int row = dimensions.Rows - 1;
		while ( 0 < lines && 0 <= row ) {
			buffer[ ( row * dimensions.Columns ) + column ] = '*';
			lines--;
			row--;
		}

		if ( 1d > point.Scale ) {
			for ( row = dimensions.Rows - 1; 0 <= row; row-- ) {
				DrawScaleMark( buffer, dimensions, column, row );
			}
			return;
		}

		for ( int tick = 1; ; tick++ ) {
			double rowValue = dimensions.Rows - ( tick * point.Scale );
			if ( !double.IsFinite( rowValue ) || -1d >= rowValue ) {
				break;
			}
			row = (int)rowValue;
			if ( dimensions.Rows <= row ) {
				continue;
			}
			DrawScaleMark( buffer, dimensions, column, row );
		}
	}

	private static void DrawScaleMark(
		char[] buffer,
		TloadTerminalDimensions dimensions,
		int column,
		int row
	) {
		ArgumentNullException.ThrowIfNull( buffer );
		int offset = ( row * dimensions.Columns ) + column;
		buffer[ offset ] = ( ' ' == buffer[ offset ] )
			? '-'
			: '='
		;
	}

	private sealed record GraphPoint(
		ProcLoadAverages Load,
		double Scale
	);
}
