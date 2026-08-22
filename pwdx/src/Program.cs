// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.Pwdx;

/// <summary>Program entry point.</summary>
public static class Program {
	/// <summary>Runs the command.</summary>
	public static Task<int> Main( string[] args ) => Command.RunAsync( args );
}
