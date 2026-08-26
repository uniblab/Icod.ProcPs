namespace Icod.ProcPs.HugeTop;

/// <summary>Provides the procps-ng compatible <c>hugetop [options]</c> entry point.</summary>
public static class Program {
	/// <summary>Runs the <c>hugetop</c> command.</summary>
	public static Task<int> Main( string[] args ) {
		ArgumentNullException.ThrowIfNull( args );
		return Command.RunAsync(
			args,
			stdout: Console.OpenStandardOutput(),
			stderr: Console.OpenStandardError()
		);
	}
}
