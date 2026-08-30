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

/// <summary>Identifies the comparison operation used by one top Other Filter.</summary>
internal enum TopOtherFilterOperator {
	Equality,
	LessThan,
	GreaterThan
}

/// <summary>Contains one parsed top Other Filter criterion.</summary>
internal sealed class TopOtherFilter {
	internal TopOtherFilter(
		string rawText,
		TopFieldId field,
		TopOtherFilterOperator comparisonOperator,
		string selectionValue,
		bool caseSensitive,
		bool include
	) {
		ArgumentException.ThrowIfNullOrEmpty( rawText );
		ArgumentNullException.ThrowIfNull( selectionValue );

		this.RawText = rawText;
		this.Field = field;
		this.Operator = comparisonOperator;
		this.SelectionValue = selectionValue;
		this.CaseSensitive = caseSensitive;
		this.Include = include;
	}

	internal string RawText { get; }
	internal TopFieldId Field { get; }
	internal TopOtherFilterOperator Operator { get; }
	internal string SelectionValue { get; }
	internal bool CaseSensitive { get; }
	internal bool Include { get; }
}

/// <summary>Parses procps-compatible top Other Filter input.</summary>
internal static class TopOtherFilterParser {
	internal static bool TryParse(
		string rawText,
		bool caseSensitive,
		TopRuntimeState state,
		out TopOtherFilter? filter,
		out string? error
	) {
		ArgumentNullException.ThrowIfNull( rawText );
		ArgumentNullException.ThrowIfNull( state );

		filter = null;
		error = null;
		if ( 0 == rawText.Length ) {
			error = "other filter requires a field, operator, and selection value";
			return false;
		}
		foreach ( TopOtherFilter existing in state.OtherFilters ) {
			if ( string.Equals(
				existing.RawText,
				rawText,
				StringComparison.Ordinal
			) ) {
				error = "duplicate other filter";
				return false;
			}
		}

		int fieldStart = 0;
		bool include = true;
		if ( '!' == rawText[ 0 ] ) {
			include = false;
			fieldStart = 1;
		}

		int delimiterIndex = -1;
		for ( int index = fieldStart; index < rawText.Length; index++ ) {
			if ( rawText[ index ] is '<' or '=' or '>' ) {
				delimiterIndex = index;
				break;
			}
		}
		if ( 0 > delimiterIndex ) {
			error = "other filter requires one of =, <, or >";
			return false;
		}

		string fieldName = rawText[
			fieldStart..delimiterIndex
		];
		if (
			!TopFieldCatalog.TryParseHeaderName(
				fieldName,
				out TopFieldId field
			)
		) {
			error = $"unknown filter field '{fieldName}'";
			return false;
		}

		int valueIndex = delimiterIndex + 1;
		if ( rawText.Length <= valueIndex ) {
			error = "other filter requires a selection value";
			return false;
		}
		string selectionValue = rawText[ valueIndex.. ];

		TopOtherFilterOperator comparisonOperator = rawText[ delimiterIndex ] switch {
			'=' => TopOtherFilterOperator.Equality,
			'<' => TopOtherFilterOperator.LessThan,
			'>' => TopOtherFilterOperator.GreaterThan,
			_ => throw new InvalidOperationException(
				"The Other Filter delimiter was not recognized."
			)
		};

		if ( TopOtherFilterOperator.Equality != comparisonOperator ) {
			TopFieldDefinition definition = TopFieldCatalog.Get(
				field
			);
			bool leftJustified;
			if ( definition.Numeric ) {
				leftJustified = state.NumericLeftJustified;
			} else {
				leftJustified = !state.CharacterRightJustified;
			}
			selectionValue = TopRenderer.FieldAlign(
				selectionValue,
				definition.Width,
				leftJustified
			);
		}

		filter = new TopOtherFilter(
			rawText,
			field,
			comparisonOperator,
			selectionValue,
			caseSensitive,
			include
		);
		return true;
	}
}
