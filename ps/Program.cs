// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.Ps;

/// <summary>Provides the <c>ps</c> executable entry point.</summary>
public static class Program {
	/// <summary>Runs the <c>ps</c> executable entry point.</summary>
	public static Task<int> Main( string[] args ) {
		ArgumentNullException.ThrowIfNull( args );
		return Command.RunAsync( args, stdout: Console.OpenStandardOutput(), stderr: Console.OpenStandardError() );
	}
}
