namespace Icod.ProcPs.Router;

using System.Reflection;
using FreeCommand = Icod.ProcPs.Free.Command;
using PgrepCommand = Icod.ProcPs.Pgrep.Command;
using PidOfCommand = Icod.ProcPs.PidOf.Command;
using PidWaitCommand = Icod.ProcPs.PidWait.Command;
using PkillCommand = Icod.ProcPs.Pkill.Command;
using PmapCommand = Icod.ProcPs.Pmap.Command;
using PsCommand = Icod.ProcPs.Ps.Command;
using PwdxCommand = Icod.ProcPs.Pwdx.Command;
using SysctlCommand = Icod.ProcPs.Sysctl.Command;
using UptimeCommand = Icod.ProcPs.Uptime.Command;
using VmstatCommand = Icod.ProcPs.Vmstat.Command;
using WCommand = Icod.ProcPs.W.Command;

/// <summary>Routes <c>procps COMMAND [args...]</c> to the managed ProcPs commands.</summary>
public static class Command {
	private const int UsageError = 2;
	private static readonly string VersionText = $"procps (Icod.ProcPs) {GetVersionText()}";
	private const string HelpText = """
Usage:
 procps COMMAND [OPTION]... [ARG]...

Commands:
 free      display physical-memory and swap usage
 pgrep     find processes matching selection criteria
 pidof     find process identifiers for running programs
 pidwait   wait for selected processes
 pkill     signal processes matching selection criteria
 pmap      report process memory maps
 ps        report a snapshot of current processes
 pwdx      report process working directories
 sysctl    read or write Linux runtime kernel parameters
 uptime    report system uptime, user count, and load averages
 vmstat    report virtual-memory and system activity
 w         show logged-in users and what they are doing

Router options:
 -h, --help       display this help and exit
 -v, --version    output version information and exit

Run 'procps COMMAND --help' for command-specific help.
""";

	/// <summary>Runs the multi-command router.</summary>
	/// <param name="arguments">Router arguments.</param>
	/// <returns>A task whose result is the selected command exit status.</returns>
	public static async Task<int> RunAsync( string[] arguments ) {
		ArgumentNullException.ThrowIfNull( arguments );

		if ( 0 == arguments.Length ) {
			await Console.Error.WriteLineAsync(
				"procps: missing command; use --help to list supported commands"
			).ConfigureAwait( false );
			await Console.Error.WriteAsync( HelpText ).ConfigureAwait( false );
			return UsageError;
		}

		var commandName = arguments[ 0 ];
		if ( "--help" == commandName || "-h" == commandName ) {
			await Console.Out.WriteAsync( HelpText ).ConfigureAwait( false );
			return 0;
		}
		if ( "--version" == commandName || "-v" == commandName ) {
			await Console.Out.WriteLineAsync( VersionText ).ConfigureAwait( false );
			return 0;
		}

		if ( !IsKnownCommand( commandName ) ) {
			await Console.Error.WriteLineAsync(
				$"procps: unknown command '{commandName}'; use --help to list supported commands"
			).ConfigureAwait( false );
			return UsageError;
		}

		var commandArguments = CopyCommandArguments( arguments );
		return commandName switch {
			"free" => await FreeCommand.RunAsync( commandArguments ).ConfigureAwait( false ),
			"pgrep" => await PgrepCommand.RunAsync( commandArguments ).ConfigureAwait( false ),
			"pidof" => await PidOfCommand.RunAsync( commandArguments ).ConfigureAwait( false ),
			"pidwait" => await PidWaitCommand.RunAsync( commandArguments ).ConfigureAwait( false ),
			"pkill" => await PkillCommand.RunAsync( commandArguments ).ConfigureAwait( false ),
			"pmap" => await PmapCommand.RunAsync( commandArguments ).ConfigureAwait( false ),
			"ps" => await PsCommand.RunAsync( commandArguments ).ConfigureAwait( false ),
			"pwdx" => await PwdxCommand.RunAsync( commandArguments ).ConfigureAwait( false ),
			"sysctl" => await SysctlCommand.RunAsync( commandArguments ).ConfigureAwait( false ),
			"uptime" => await UptimeCommand.RunAsync( commandArguments ).ConfigureAwait( false ),
			"vmstat" => await VmstatCommand.RunAsync( commandArguments ).ConfigureAwait( false ),
			"w" => await WCommand.RunAsync( commandArguments ).ConfigureAwait( false ),
			_ => throw new InvalidOperationException( "Known command dispatch was incomplete." )
		};
	}

	private static string GetVersionText() {
		var assembly = typeof( Command ).Assembly;
		var informationalVersion = assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
			?.InformationalVersion;
		if ( !string.IsNullOrWhiteSpace( informationalVersion ) ) {
			var metadataSeparator = informationalVersion.IndexOf( '+' );
			if ( 0 <= metadataSeparator ) {
				return informationalVersion[ ..metadataSeparator ];
			}
			return informationalVersion;
		}

		var assemblyVersion = assembly.GetName().Version;
		if ( assemblyVersion is null ) {
			return "unknown";
		}
		return assemblyVersion.ToString( 3 );
	}

	private static bool IsKnownCommand( string commandName ) {
		ArgumentException.ThrowIfNullOrWhiteSpace( commandName );
		return commandName is
			"free" or
			"pgrep" or
			"pidof" or
			"pidwait" or
			"pkill" or
			"pmap" or
			"ps" or
			"pwdx" or
			"sysctl" or
			"uptime" or
			"vmstat" or
			"w";
	}

	private static string[] CopyCommandArguments( IReadOnlyList<string> arguments ) {
		ArgumentNullException.ThrowIfNull( arguments );
		var commandArguments = new string[ arguments.Count - 1 ];
		for ( var index = 1; index < arguments.Count; index++ ) {
			commandArguments[ index - 1 ] = arguments[ index ];
		}
		return commandArguments;
	}
}
