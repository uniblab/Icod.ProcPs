/*
	Icod.ProcPs tests
	Provide router-pinned version expectations for command tests.
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

namespace Icod.ProcPs.Tests;

using System.Reflection;

/// <summary>Provides version expectations pinned to the built ProcPs router.</summary>
internal static class ProcPsTestVersion {
	private const string ProcpsNgCompatibilityVersion = "4.0.6";

	/// <summary>Gets the version reported by the built ProcPs router assembly.</summary>
	internal static string RouterVersion { get; } = GetVersionText(
		typeof( global::Icod.ProcPs.Router.Command ).Assembly
	);

	/// <summary>Formats the expected standalone-command version text using the router version.</summary>
	/// <param name="productName">The standalone command product name.</param>
	/// <returns>The expected command version text without a trailing newline.</returns>
	internal static string FormatCommand(
		string productName
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( productName );
		return $"{productName} ({RouterVersion}) inspired by procps-ng {ProcpsNgCompatibilityVersion}";
	}

	private static string GetVersionText(
		Assembly assembly
	) {
		ArgumentNullException.ThrowIfNull( assembly );
		string? informationalVersion = assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
			?.InformationalVersion;
		if ( !string.IsNullOrWhiteSpace( informationalVersion ) ) {
			int metadataSeparator = informationalVersion.IndexOf( '+' );
			if ( 0 <= metadataSeparator ) {
				return informationalVersion[ ..metadataSeparator ];
			}
			return informationalVersion;
		}

		Version? assemblyVersion = assembly.GetName().Version;
		if ( assemblyVersion is null ) {
			return "unknown";
		}
		return assemblyVersion.ToString( 3 );
	}
}
