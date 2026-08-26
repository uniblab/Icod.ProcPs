/*
	pgrep / pkill / pidwait
	Provide GNU regular-expression matching for process-selection commands.
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

namespace Icod.ProcPs;

using Icod.CommandFramework.RegularExpressions;
using Icod.ProcPs.Shared;

internal sealed class GnuProcMatchPatternCompiler : IProcMatchPatternCompiler {
	internal static GnuProcMatchPatternCompiler Instance { get; } = new();

	private GnuProcMatchPatternCompiler() { }

	public IProcMatchPattern Compile(
		string pattern,
		bool ignoreCase
	) {
		ArgumentNullException.ThrowIfNull( pattern );

		var compiled = GnuExtendedRegularExpressionProvider.Default.Compile(
			pattern,
			RegularExpressionOptions.GnuExtendedCompatibility with {
				IgnoreCase = ignoreCase
			}
		);
		if ( !compiled.IsSuccess ) {
			throw new FormatException(
				compiled.Diagnostic?.Message ?? "invalid regular expression"
			);
		}

		return new GnuProcMatchPattern( compiled.Expression! );
	}

	private sealed class GnuProcMatchPattern : IProcMatchPattern {
		private readonly ICompiledRegularExpression expression;

		public GnuProcMatchPattern( ICompiledRegularExpression expression ) {
			ArgumentNullException.ThrowIfNull( expression );
			this.expression = expression;
		}

		public ProcMatchPatternResult? Match(
			string input,
			CancellationToken cancellationToken = default
		) {
			ArgumentNullException.ThrowIfNull( input );
			var result = this.expression.Match(
				input,
				cancellationToken: cancellationToken
			);
			if ( !result.IsSuccess || !result.IsMatch ) {
				return null;
			}

			return new ProcMatchPatternResult(
				result.Match!.Index,
				result.Match.Length
			);
		}
	}
}
