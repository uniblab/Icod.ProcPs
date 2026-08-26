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
