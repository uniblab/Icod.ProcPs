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
