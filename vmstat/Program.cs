// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

/// <summary>Entry point for <c>vmstat [options] [delay [count]]</c>.</summary>
internal static class Program {
	/// <summary>Runs the procps-ng-compatible vmstat command.</summary>
	/// <param name="args">Command-line arguments.</param>
	/// <returns>The process exit status.</returns>
	private static async Task<int> Main( string[] args ) => await Icod.ProcPs.Vmstat.Command.RunAsync( args ).ConfigureAwait( false );
}
