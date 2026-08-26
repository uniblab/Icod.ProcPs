/*
	pidof
	Find process identifiers for running programs.
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

namespace Icod.ProcPs.PidOf;

using System.Text;
using Icod.ProcPs.Shared;

/// <summary>Implements procps-ng 4.0.6 <c>pidof</c> over the shared process-lookup engine.</summary>
public static class Command {
	private static readonly Encoding Utf8 = new UTF8Encoding( false );
	private static readonly string VersionText = global::Icod.ProcPs.ProcCommandVersion.Format(
		"Icod.ProcPs.PidOf",
		typeof( Command ).Assembly
	);

	/// <summary>Runs <c>pidof</c> synchronously.</summary>
	public static int Run( string[] args, TextWriter? stdout = null, TextWriter? stderr = null ) {
		ArgumentNullException.ThrowIfNull( args );
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		var status = RunAsync( args, stdout: output, stderr: error ).GetAwaiter().GetResult();
		( stdout ?? Console.Out ).Write( Utf8.GetString( output.ToArray() ) );
		( stderr ?? Console.Error ).Write( Utf8.GetString( error.ToArray() ) );
		return status;
	}

	/// <summary>Runs <c>pidof</c> asynchronously with injectable process and path providers.</summary>
	public static Task<int> RunAsync(
		string[] args,
		Stream? stdout = null,
		Stream? stderr = null,
		IProcProcessProvider? processProvider = null,
		IProcProcessPathProvider? pathProvider = null,
		IProcMatchSupplementProvider? supplements = null,
		Func<bool>? privilegedRootCheckProvider = null,
		Func<int?>? currentParentProcessIdProvider = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		return ProcProcessLookupCommand.RunPidOfAsync(
			args,
			stdout ?? Console.OpenStandardOutput(),
			stderr ?? Console.OpenStandardError(),
			processProvider,
			pathProvider,
			supplements,
			privilegedRootCheckProvider,
			currentParentProcessIdProvider,
			cancellationToken,
			versionText: VersionText
		);
	}
}
