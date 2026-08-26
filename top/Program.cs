// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.Top;

/// <summary>Provides the procps-ng compatible <c>top [options]</c> entry point.</summary>
public static class Program {
	/// <summary>Runs the <c>top</c> command.</summary>
	public static Task<int> Main( string[] args ) {
		ArgumentNullException.ThrowIfNull( args );
		return Command.RunAsync(
			args,
			stdout: Console.OpenStandardOutput(),
			stderr: Console.OpenStandardError()
		);
	}
}
