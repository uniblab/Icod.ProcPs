/*
	top
	Procps-compatible fixed-width field expansion support.
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

/// <summary>Applies procps <c>Fixed_widest</c> semantics to supported fixed-width fields.</summary>
internal static class TopFixedWidth {
	internal const int Automatic = -1;
	internal const int MaximumExtra = 512;

	internal static bool IsValid(
		int value
	) {
		return Automatic <= value
			&& value <= MaximumExtra;
	}

	internal static void Configure(
		TopRuntimeState state,
		int value
	) {
		ArgumentNullException.ThrowIfNull( state );
		if ( !IsValid( value ) ) {
			throw new ArgumentOutOfRangeException(
				nameof( value ),
				value,
				$"The extra fixed width must be between -1 and {MaximumExtra}."
			);
		}

		bool clearAutomaticWidths = Automatic != value
			|| Automatic != state.FixedWidthExtra;
		state.FixedWidthExtra = value;
		if ( clearAutomaticWidths ) {
			state.AutomaticFixedWidths.Clear();
		}
		state.HorizontalOffset = 0;
	}

	internal static int Width(
		TopRuntimeState state,
		TopFieldDefinition definition
	) {
		ArgumentNullException.ThrowIfNull( state );
		ArgumentNullException.ThrowIfNull( definition );
		if ( !IsEligible( definition.Id ) ) {
			return definition.Width;
		}
		if ( Automatic == state.FixedWidthExtra ) {
			if (
				state.AutomaticFixedWidths.TryGetValue(
					definition.Id,
					out int automaticWidth
				)
			) {
				return Math.Max(
					definition.Width,
					automaticWidth
				);
			}
			return definition.Width;
		}
		return checked(
			definition.Width + state.FixedWidthExtra
		);
	}

	internal static void Observe(
		IReadOnlyList<TopTaskRow> tasks,
		TopRuntimeState state
	) {
		ArgumentNullException.ThrowIfNull( tasks );
		ArgumentNullException.ThrowIfNull( state );
		if (
			Automatic != state.FixedWidthExtra
			|| !state.VisibleFields.Contains( TopFieldId.User )
		) {
			return;
		}

		TopFieldDefinition definition = TopFieldCatalog.Get(
			TopFieldId.User
		);
		int widest = definition.Width;
		if (
			state.AutomaticFixedWidths.TryGetValue(
				TopFieldId.User,
				out int previousWidth
			)
			&& widest < previousWidth
		) {
			widest = previousWidth;
		}
		foreach ( TopTaskRow task in tasks ) {
			int width = CountRunes(
				task.User
			);
			if ( widest < width ) {
				widest = width;
			}
		}
		state.AutomaticFixedWidths[
			TopFieldId.User
		] = widest;
	}

	internal static string Format(
		string text,
		TopRuntimeState state,
		TopFieldId field
	) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentNullException.ThrowIfNull( state );
		TopFieldDefinition definition = TopFieldCatalog.Get(
			field
		);
		if ( !IsEligible( field ) ) {
			return text;
		}
		return TruncateWithMarker(
			text,
			Width(
				state,
				definition
			)
		);
	}

	private static bool IsEligible(
		TopFieldId field
	) {
		return TopFieldId.User == field;
	}

	private static string TruncateWithMarker(
		string text,
		int width
	) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );
		if ( CountRunes( text ) <= width ) {
			return text;
		}

		var builder = new StringBuilder();
		int remaining = width - 1;
		foreach ( Rune rune in text.EnumerateRunes() ) {
			if ( 0 == remaining ) {
				break;
			}
			builder.Append(
				rune.ToString()
			);
			remaining--;
		}
		builder.Append( '+' );
		return builder.ToString();
	}

	private static int CountRunes(
		string text
	) {
		ArgumentNullException.ThrowIfNull( text );
		int result = 0;
		foreach ( Rune rune in text.EnumerateRunes() ) {
			_ = rune;
			result++;
		}
		return result;
	}
}
