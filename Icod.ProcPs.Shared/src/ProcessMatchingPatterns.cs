namespace Icod.ProcPs.Shared;

/// <summary>Represents a compiled process-selection pattern.</summary>
public interface IProcMatchPattern {
	/// <summary>Searches an input string for a process-selection match.</summary>
	/// <param name="input">The input string.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The selected match, or <see langword="null"/> when no match is available.</returns>
	ProcMatchPatternResult? Match(
		string input,
		CancellationToken cancellationToken = default
	);
}

/// <summary>Compiles process-selection patterns for the shared matching engine.</summary>
public interface IProcMatchPatternCompiler {
	/// <summary>Compiles a process-selection pattern.</summary>
	/// <param name="pattern">The pattern text.</param>
	/// <param name="ignoreCase">Whether matching is case-insensitive.</param>
	/// <returns>The compiled pattern.</returns>
	/// <exception cref="FormatException">The pattern is invalid.</exception>
	IProcMatchPattern Compile(
		string pattern,
		bool ignoreCase
	);
}

/// <summary>Describes the location of a successful process-selection pattern match.</summary>
/// <param name="Index">The zero-based input index.</param>
/// <param name="Length">The matched input length.</param>
public readonly record struct ProcMatchPatternResult(
	int Index,
	int Length
);
