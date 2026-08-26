// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.Ps;

/// <summary>
/// Provides the executable entry point for the procps-ng-compatible <c>ps</c> command for reporting a snapshot of current processes.
/// </summary>
public static class Program {
	/// <summary>
	/// Runs the <c>ps</c> command using the process console and converts a console interrupt into a cancellation request.
	/// </summary>
	/// <param name="args">The command-line arguments supplied to <c>ps</c>.</param>
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
