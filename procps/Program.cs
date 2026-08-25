namespace Icod.ProcPs.Router;

/// <summary>Provides the process entry point for <c>procps</c>.</summary>
public static class Program {
	/// <summary>Runs the multi-command ProcPs router.</summary>
	/// <param name="args">Command-line arguments.</param>
	/// <returns>A task whose result is the process exit status.</returns>
	public static Task<int> Main( string[] args ) {
		ArgumentNullException.ThrowIfNull( args );
		return Command.RunAsync( args );
	}
}
