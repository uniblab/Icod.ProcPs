// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.SlabTop;

/// <summary>Provides the procps-ng compatible <c>slabtop [options]</c> entry point.</summary>
public static class Program {
	/// <summary>Runs the <c>slabtop</c> command.</summary>
	public static Task<int> Main( string[] args ) {
		ArgumentNullException.ThrowIfNull( args );
		return Command.RunAsync(
			args,
			stdout: Console.OpenStandardOutput(),
			stderr: Console.OpenStandardError()
		);
	}
}
