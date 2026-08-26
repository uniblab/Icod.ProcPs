namespace Icod.ProcPs.Uptime;

/// <summary>
/// Provides the executable entry point for the procps-ng-compatible <c>uptime</c> command for reporting system uptime, user count, and load averages.
/// </summary>
public static class Program {
	/// <summary>
	/// Runs the <c>uptime</c> command using the process console and converts a console interrupt into a cancellation request.
	/// </summary>
	/// <param name="args">The command-line arguments supplied to <c>uptime</c>.</param>
	/// <returns>A task whose result is the command exit status.</returns>
	public static async Task<int> Main( string[] args ) {
		ArgumentNullException.ThrowIfNull( args );
		using var cancellation = new CancellationTokenSource();
		ConsoleCancelEventHandler handler = ( _, eventArgs ) => {
			eventArgs.Cancel = true;
			cancellation.Cancel();
		};
		Console.CancelKeyPress += handler;
		try {
			var stdout = Console.OpenStandardOutput();
			var stderr = Console.OpenStandardError();
			return await Command.RunAsync(
				args,
				stdout: stdout,
				stderr: stderr,
				cancellationToken: cancellation.Token
			).ConfigureAwait( false );
		} finally {
			Console.CancelKeyPress -= handler;
		}
	}
}
