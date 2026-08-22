// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.W;

/// <summary>Provides the procps-ng compatible <c>w</c> executable entry point.</summary>
public static class Program {
	/// <summary>Runs the <c>w</c> command.</summary>
	/// <param name="args">Command-line arguments.</param>
	/// <returns>A task whose result is the process exit status.</returns>
	public static Task<int> Main( string[] args ) {
		ArgumentNullException.ThrowIfNull( args );
		return Command.RunAsync(
			args,
			stdout: Console.OpenStandardOutput(),
			stderr: Console.OpenStandardError()
		);
	}
}
