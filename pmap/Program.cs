// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.Pmap;

/// <summary>Program entry point.</summary>
public static class Program {
	/// <summary>Runs the command.</summary>
	public static async Task<int> Main( string[] args ) {
		ArgumentNullException.ThrowIfNull( args );
		await using var stdout = Console.OpenStandardOutput();
		await using var stderr = Console.OpenStandardError();
		return await Command.RunAsync( args, stdout: stdout, stderr: stderr ).ConfigureAwait( false );
	}
}
