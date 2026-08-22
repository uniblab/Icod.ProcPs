// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.Pgrep;

using System.Text;
using Icod.ProcPs.Shared;

/// <summary>Implements procps-ng 4.0.6 <c>pgrep</c> over the shared process-matching engine.</summary>
public static class Command {
	private static readonly Encoding Utf8 = new UTF8Encoding( false );
	/// <summary>Runs <c>pgrep</c> synchronously.</summary>
	public static int Run( string[] args, TextWriter? stdout = null, TextWriter? stderr = null ) {
		ArgumentNullException.ThrowIfNull( args );
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		var status = RunAsync( args, stdout: output, stderr: error ).GetAwaiter().GetResult();
		( stdout ?? Console.Out ).Write( Utf8.GetString( output.ToArray() ) );
		( stderr ?? Console.Error ).Write( Utf8.GetString( error.ToArray() ) );
		return status;
	}
	/// <summary>Runs <c>pgrep</c> asynchronously with injectable process observation and control providers.</summary>
	public static Task<int> RunAsync(
		string[] args,
		Stream? stdin = null,
		Stream? stdout = null,
		Stream? stderr = null,
		IProcProcessProvider? processProvider = null,
		IProcMatchControl? control = null,
		IProcMatchSupplementProvider? supplements = null,
		IProcAccountResolver? accountResolver = null,
		Func<int>? currentProcessIdProvider = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		return ProcMatchCommand.RunAsync(
			ProcMatchCommandMode.Pgrep,
			args,
			stdin ?? Console.OpenStandardInput(),
			stdout ?? Console.OpenStandardOutput(),
			stderr ?? Console.OpenStandardError(),
			processProvider,
			control,
			supplements,
			accountResolver,
			currentProcessIdProvider,
			cancellationToken
		);
	}
}
