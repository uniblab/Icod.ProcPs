// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.Pwdx;

using System.Text;
using Icod.ProcPs.Shared;

/// <summary>Implements procps-ng 4.0.6 <c>pwdx</c> over the shared process-path provider.</summary>
public static class Command {
	private static readonly Encoding Utf8 = new UTF8Encoding( false );

	/// <summary>Runs <c>pwdx</c> synchronously.</summary>
	public static int Run( string[] args, TextWriter? stdout = null, TextWriter? stderr = null ) {
		ArgumentNullException.ThrowIfNull( args );
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		var status = RunAsync( args, stdout: output, stderr: error ).GetAwaiter().GetResult();
		( stdout ?? Console.Out ).Write( Utf8.GetString( output.ToArray() ) );
		( stderr ?? Console.Error ).Write( Utf8.GetString( error.ToArray() ) );
		return status;
	}

	/// <summary>Runs <c>pwdx</c> asynchronously with injectable process and path providers.</summary>
	public static Task<int> RunAsync(
		string[] args,
		Stream? stdout = null,
		Stream? stderr = null,
		IProcProcessProvider? processProvider = null,
		IProcProcessPathProvider? pathProvider = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		return ProcProcessLookupCommand.RunPwdxAsync(
			args,
			stdout ?? Console.OpenStandardOutput(),
			stderr ?? Console.OpenStandardError(),
			processProvider,
			pathProvider,
			cancellationToken
		);
	}
}
