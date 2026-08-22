// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.Ps;

public static class Program {
	public static Task<int> Main( string[] args ) {
		ArgumentNullException.ThrowIfNull( args );
		return Command.RunAsync( args, stdout: Console.OpenStandardOutput(), stderr: Console.OpenStandardError() );
	}
}
