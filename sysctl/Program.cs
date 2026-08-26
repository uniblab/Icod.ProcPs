namespace Icod.ProcPs.Sysctl;

/// <summary>
/// Provides the executable entry point for the procps-ng-compatible <c>sysctl</c> command for reading and writing Linux runtime kernel parameters.
/// </summary>
public static class Program {
	/// <summary>
	/// Runs the <c>sysctl</c> command using the process console and converts a console interrupt into a cancellation request.
	/// </summary>
	/// <param name="args">The command-line arguments supplied to <c>sysctl</c>.</param>
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
			var stdin = Console.OpenStandardInput();
			var stdout = Console.OpenStandardOutput();
			var stderr = Console.OpenStandardError();
			return await Command.RunAsync(
				args,
				stdin: stdin,
				stdout: stdout,
				stderr: stderr,
				cancellationToken: cancellation.Token
			).ConfigureAwait( false );
		} finally {
			Console.CancelKeyPress -= handler;
		}
	}
}
