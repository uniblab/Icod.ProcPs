// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

namespace Icod.ProcPs.Ps;

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Icod.ProcPs.Shared;

/// <summary>Resolves ProcPs account operands in both name-to-id and id-to-name directions.</summary>
public interface IProcPsAccountResolver : IProcAccountResolver {
	/// <summary>Resolves a user identifier to a display name.</summary>
	bool TryGetUserName( uint id, out string name );
	/// <summary>Resolves a group identifier to a display name.</summary>
	bool TryGetGroupName( uint id, out string name );
}

/// <summary>Provides host account resolution for the <c>ps</c> presentation engine.</summary>
public sealed class SystemProcPsAccountResolver : IProcPsAccountResolver {
	/// <summary>Gets the singleton system resolver.</summary>
	public static SystemProcPsAccountResolver Instance { get; } = new();
	/// <summary>Resolves a user name or numeric identifier to its user identifier.</summary>
	public bool TryResolveUser( string text, out uint id ) {
		ArgumentNullException.ThrowIfNull( text );
		return SystemProcAccountResolver.Instance.TryResolveUser( text, out id );
	}
	/// <summary>Resolves a group name or numeric identifier to its group identifier.</summary>
	public bool TryResolveGroup( string text, out uint id ) {
		ArgumentNullException.ThrowIfNull( text );
		return SystemProcAccountResolver.Instance.TryResolveGroup( text, out id );
	}
	/// <summary>Resolves a user identifier to a display name.</summary>
	public bool TryGetUserName( uint id, out string name ) => TryResolveUnixName( "/etc/passwd", id, out name );
	/// <summary>Resolves a group identifier to a display name.</summary>
	public bool TryGetGroupName( uint id, out string name ) => TryResolveUnixName( "/etc/group", id, out name );
	private static bool TryResolveUnixName( string path, uint id, out string name ) {
		if ( OperatingSystem.IsWindows() ) {
			name = string.Empty;
			return false;
		}
		try {
			foreach ( var line in File.ReadLines( path ) ) {
				if ( string.IsNullOrEmpty( line ) || '#' == line[ 0 ] )
					continue;
				var fields = line.Split( ':' );
				if ( 3 > fields.Length )
					continue;
				if ( uint.TryParse( fields[ 2 ], NumberStyles.None, CultureInfo.InvariantCulture, out var candidate ) && id == candidate ) {
					name = fields[ 0 ];
					return true;
				}
			}
		} catch ( IOException ) { } catch ( UnauthorizedAccessException ) { }
		name = string.Empty;
		return false;
	}
}

/// <summary>Implements the procps-ng 4.0.6 <c>ps</c> command.</summary>
public static class Command {
	private const int Success = 0; private const int Failure = 1; private const int Cancelled = 130; private const int DefaultWidth = 80;
	private static readonly Encoding Utf8 = new UTF8Encoding( false );
	private static readonly IReadOnlyDictionary<string, ProcReportFieldDefinition> FieldCatalog = ProcReportFieldCatalog.Aliases;
	private static readonly string[] DefaultFields = [ "pid", "tty", "time", "comm" ];
	private static readonly string[] BsdDefaultFields = [ "pid", "tty", "stat", "time", "command" ];
	private static readonly string[] ThreadFields = [ "pid", "lwp", "tgid", "nlwp", "tty", "time", "comm" ];
	private static readonly string[] FullFields = [ "user", "pid", "ppid", "c", "stime", "tty", "time", "cmd" ];
	private static readonly string[] FullExtraFields = [ "user", "pid", "ppid", "c", "sz", "rss", "psr", "stime", "tty", "time", "cmd" ];
	private static readonly string[] LongFields = [ "f", "state", "uid", "pid", "ppid", "c", "pri", "ni", "addr", "sz", "wchan", "tty", "time", "cmd" ];
	private static readonly string[] JobsFields = [ "pid", "pgid", "sid", "tty", "time", "cmd" ];
	private static readonly string[] UserFields = [ "user", "pid", "pcpu", "pmem", "vsz", "rss", "tty", "stat", "start", "time", "command" ];
	private static readonly string[] MemoryFields = [ "pid", "tty", "stat", "time", "vsz", "rss", "pmem", "command" ];
	private const string Usage = """

Usage:
 ps [options]
Selection:
 -A, -e                    select all processes
 -a                        select processes with a terminal, except session leaders
 a                         lift the current-user restriction
 x                         include processes without a controlling terminal
 -d                        select all processes except session leaders
 -N, --deselect            invert the selection
 -p, --pid PIDLIST         select process IDs
 -q, --quick-pid PIDLIST   select process IDs and preserve the supplied order
 --ppid PIDLIST            select parent process IDs
 -g GROUPLIST            select sessions (numeric) or effective groups (named)
 --pgroup PIDLIST          select process-group IDs
 --group GROUPLIST         select effective groups
 -s, --sid PIDLIST         select session IDs
 -t, --tty TTYLIST         select terminals
 -u, --user USERLIST       select effective users
 -U, --User USERLIST       select real users
 -G, --Group GROUPLIST     select real groups
 -C, --command LIST        select short command names
 r                         restrict selection to running tasks
Output:
 L                         list format specifiers
 -o, --format FORMAT       user-defined output format
 -f, -F, -l                full, extra-full, or long format
 j, l, u, v                BSD jobs, long, user, or virtual-memory format
 --sort SPEC               sort by comma-separated [+|-]field keys
 --forest, -H, f           show the process hierarchy
 -L, -T, -m, H, m          show threads where the provider can enumerate them
 e                         append the environment to the command
 c                         show the short command name instead of arguments
 --headers, --no-headers   force or suppress headings
 --cols N, --columns N     set output width
 --width N                 set output width
 w                         widen output; repeat for unlimited width
 --personality NAME        select linux, posix, bsd, sunos4, digital, hp, or aix
 --help                    display this help and exit
 --version                 output version information and exit
""";
	/// <summary>Runs <c>ps</c> synchronously.</summary>
	public static int Run( string[] args, TextWriter? stdout = null, TextWriter? stderr = null ) {
		ArgumentNullException.ThrowIfNull( args );
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		var status = RunAsync( args, stdout: output, stderr: error ).GetAwaiter().GetResult();
		( stdout ?? Console.Out ).Write( Utf8.GetString( output.ToArray() ) );
		( stderr ?? Console.Error ).Write( Utf8.GetString( error.ToArray() ) );
		return status;
	}
	/// <summary>Runs <c>ps</c> asynchronously with injectable ProcPs providers.</summary>
	public static async Task<int> RunAsync( string[] args, Stream? stdout = null, Stream? stderr = null, IProcProcessProvider? processProvider = null, IProcSystemMetricsProvider? metricsProvider = null, IProcMatchSupplementProvider? supplementProvider = null, IProcPsAccountResolver? accountResolver = null, Func<int>? currentProcessIdProvider = null, IReadOnlyDictionary<string, string?>? environment = null, Func<DateTimeOffset>? nowProvider = null, CancellationToken cancellationToken = default ) {
		ArgumentNullException.ThrowIfNull( args );
		var hostEnvironment = environment ?? ReadPersonalityEnvironment();
		var parsed = ParseArguments( args, hostEnvironment, accountResolver ?? SystemProcPsAccountResolver.Instance );
		if ( null != parsed.Error ) {
			await WriteLineAsync( stderr, $"ps: {parsed.Error}", cancellationToken ).ConfigureAwait( false );
			return Failure;
		}
		if ( parsed.ShowHelp ) {
			await WriteAsync( stdout, NormalizeLineEndings( Usage ), cancellationToken ).ConfigureAwait( false );
			return Success;
		}
		if ( parsed.ShowVersion ) {
			await WriteLineAsync( stdout, "ps from procps-ng 4.0.6", cancellationToken ).ConfigureAwait( false );
			return Success;
		}
		if ( parsed.ShowFieldList ) {
			foreach ( var field in FieldCatalog.Values.GroupBy( value => value.Name, StringComparer.Ordinal ).Select( group => group.First() ).OrderBy( value => value.Name, StringComparer.Ordinal ) )
				await WriteLineAsync( stdout, $"{field.Name,-16} {field.Header}", cancellationToken ).ConfigureAwait( false );
			return Success;
		}
		try {
			cancellationToken.ThrowIfCancellationRequested();
			var processes = processProvider ?? SystemProcProcessProvider.Instance;
			var collection = await processes.GetProcessesAsync( cancellationToken ).ConfigureAwait( false );
			var currentProcessId = currentProcessIdProvider?.Invoke() ?? Environment.ProcessId;
			var current = collection.Processes.FirstOrDefault( process => process.ProcessId == currentProcessId );
			var selected = SelectProcesses( collection.Processes, parsed, current );
			var needSupplements = parsed.ShowThreads || parsed.IncludeEnvironment || FieldsNeedSupplements( parsed.Fields );
			var candidates = await BuildCandidatesAsync( selected, needSupplements, parsed.ShowThreads, supplementProvider, cancellationToken ).ConfigureAwait( false );
			if ( parsed.QuickProcessIds.Count > 0 )
				candidates = OrderQuick( candidates, parsed.QuickProcessIds );
			else if ( parsed.Forest )
				candidates = OrderForest( candidates );
			else if ( 0 < parsed.SortKeys.Count )
				candidates = SortCandidates( candidates, parsed.SortKeys );
			ProcSystemSnapshot? system = null;
			if ( FieldsNeedMetrics( parsed.Fields ) )
				system = await ( metricsProvider ?? SystemProcSystemMetricsProvider.Instance ).GetSnapshotAsync( cancellationToken ).ConfigureAwait( false );
			var now = nowProvider?.Invoke() ?? DateTimeOffset.Now;
			await RenderAsync( candidates, parsed, system, accountResolver ?? SystemProcPsAccountResolver.Instance, now, stdout, cancellationToken ).ConfigureAwait( false );
			return Success;
		} catch ( OperationCanceledException ) { return Cancelled; } catch ( PlatformNotSupportedException exception ) { await WriteLineAsync( stderr, $"ps: {exception.Message}", CancellationToken.None ).ConfigureAwait( false ); return Failure; } catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or InvalidOperationException ) { await WriteLineAsync( stderr, $"ps: {exception.Message}", CancellationToken.None ).ConfigureAwait( false ); return Failure; }
	}
	private static ParsedArguments ParseArguments( string[] args, IReadOnlyDictionary<string, string?> environment, IProcPsAccountResolver accountResolver ) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( environment );
		ArgumentNullException.ThrowIfNull( accountResolver );
		var result = new ParsedArguments { Personality = ProcPersonalityResolver.ResolveEnvironment( environment ) };
		for ( var index = 0; index < args.Length; index++ ) {
			var argument = args[ index ];
			if ( int.TryParse( argument, NumberStyles.None, CultureInfo.InvariantCulture, out var operandPid ) && 0 < operandPid ) {
				result.ProcessIds.Add( operandPid );
				result.HasExplicitSelection = true;
				continue;
			}
			if ( 1 < argument.Length && '-' == argument[ 0 ] && int.TryParse( argument[ 1.. ], NumberStyles.None, CultureInfo.InvariantCulture, out var negativeFormPid ) && 0 < negativeFormPid ) {
				result.ProcessIds.Add( negativeFormPid );
				result.HasExplicitSelection = true;
				continue;
			}
			if ( TryParseBsdOperandOption( args, ref index, argument, result, accountResolver ) )
				continue;
			if ( "--" == argument ) {
				if ( index + 1 < args.Length )
					result.Fail( $"garbage option: {args[ index + 1 ]}" );
				break;
			}
			if ( argument.StartsWith( "--", StringComparison.Ordinal ) ) {
				ParseLongOption( args, ref index, argument, result, accountResolver );
				continue;
			}
			if ( argument.StartsWith( '-' ) && 1 < argument.Length ) {
				ParseUnixOptions( args, ref index, argument, result, accountResolver );
				continue;
			}
			ParseBsdOptions( argument, result );
		}
		if ( null != result.Error || result.ShowHelp || result.ShowVersion || result.ShowFieldList )
			return result;
		if ( 0 < result.QuickProcessIds.Count && ( 0 < result.SortKeys.Count || result.Forest || result.HasNonQuickSelection() ) ) {
			result.Fail( "-q/--quick-pid is incompatible with other selection options, sorting, and forest output" );
			return result;
		}
		if ( !result.CustomFormat )
			ApplyPresetFormat( result );
		if ( 0 == result.Fields.Count )
			result.AddFields( DefaultFields );
		if ( result.SecurityFormat && !result.Fields.Any( field => ProcReportFieldKind.SecurityLabel == field.Definition.Kind ) )
			result.PrependField( "label" );
		return result;
	}
	private static void ParseLongOption( string[] args, ref int index, string argument, ParsedArguments result, IProcPsAccountResolver accounts ) {
		var argumentIndex = index;
		var equals = argument.IndexOf( '=' );
		var name = 0 <= equals ? argument[ ..equals ] : argument;
		var attached = 0 <= equals ? argument[ ( equals + 1 ).. ] : null;
		string? Value() {
			if ( null != attached )
				return attached;
			return argumentIndex + 1 < args.Length ? args[ ++argumentIndex ] : null;
		}
		switch ( name ) {
			case "--help":
				result.ShowHelp = true;
				break;
			case "--version":
				result.ShowVersion = true;
				break;
			case "--all":
				result.SelectAll = true;
				result.HasExplicitSelection = true;
				break;
			case "--deselect":
				result.Invert = true;
				break;
			case "--pid":
				AddIds( Value(), result.ProcessIds, result );
				result.HasExplicitSelection = true;
				break;
			case "--quick-pid":
				AddOrderedIds( Value(), result.QuickProcessIds, result );
				result.HasExplicitSelection = true;
				break;
			case "--ppid":
				AddIds( Value(), result.ParentIds, result );
				result.HasExplicitSelection = true;
				break;
			case "--pgroup":
				AddIds( Value(), result.ProcessGroupIds, result );
				result.HasExplicitSelection = true;
				break;
			case "--sid":
				AddIds( Value(), result.SessionIds, result );
				result.HasExplicitSelection = true;
				break;
			case "--group":
				AddAccounts( Value(), result.EffectiveGroupIds, result, accounts, false );
				result.HasExplicitSelection = true;
				break;
			case "--user":
				AddAccounts( Value(), result.EffectiveUserIds, result, accounts, true );
				result.HasExplicitSelection = true;
				break;
			case "--User":
				AddAccounts( Value(), result.RealUserIds, result, accounts, true );
				result.HasExplicitSelection = true;
				break;
			case "--Group":
				AddAccounts( Value(), result.RealGroupIds, result, accounts, false );
				result.HasExplicitSelection = true;
				break;
			case "--tty":
				AddStrings( Value(), result.Terminals );
				result.HasExplicitSelection = true;
				break;
			case "--command":
				AddStrings( Value(), result.CommandNames );
				result.HasExplicitSelection = true;
				break;
			case "--format":
				ParseFormat( Value(), result );
				break;
			case "--sort":
				ParseSort( Value(), result );
				break;
			case "--forest":
				result.Forest = true;
				break;
			case "--context":
				result.SecurityFormat = true;
				break;
			case "--headers":
				result.HeaderMode = HeaderMode.Show;
				break;
			case "--no-heading":
			case "--no-headers":
				result.HeaderMode = HeaderMode.Hide;
				break;
			case "--cols":
			case "--columns":
			case "--width":
				ParseWidth( Value(), result );
				break;
			case "--personality": {
					var value = Value();
					if ( !ProcPersonalityResolver.TryParse( value, out var personality ) )
						result.Fail( $"unknown personality '{value}'" );
					else
						result.Personality = personality;
					break;
				}
			default:
				result.Fail( $"unknown option {name}" );
				break;
		}
		index = argumentIndex;
	}
	private static void ParseUnixOptions( string[] args, ref int index, string argument, ParsedArguments result, IProcPsAccountResolver accounts ) {
		var argumentIndex = index;
		for ( var i = 1; i < argument.Length; i++ ) {
			var option = argument[ i ];
			string? Value() {
				if ( i + 1 < argument.Length ) {
					var v = argument[ ( i + 1 ).. ];
					i = argument.Length;
					return v;
				}
				if ( argumentIndex + 1 < args.Length ) {
					i = argument.Length;
					return args[ ++argumentIndex ];
				}
				return null;
			}
			switch ( option ) {
				case 'A':
				case 'e':
					result.SelectAll = true;
					result.HasExplicitSelection = true;
					break;
				case 'a':
					result.SelectTerminalProcesses = true;
					result.HasExplicitSelection = true;
					break;
				case 'd':
					result.SelectExceptSessionLeaders = true;
					result.HasExplicitSelection = true;
					break;
				case 'N':
					result.Invert = true;
					break;
				case 'p':
					AddIds( Value(), result.ProcessIds, result );
					result.HasExplicitSelection = true;
					break;
				case 'q':
					AddOrderedIds( Value(), result.QuickProcessIds, result );
					result.HasExplicitSelection = true;
					break;
				case 'g': {
						var v = Value();
						if ( v != null && SplitList( v ).All( x => uint.TryParse( x, out _ ) ) )
							AddIds( v, result.SessionIds, result );
						else
							AddAccounts( v, result.EffectiveGroupIds, result, accounts, false );
						result.HasExplicitSelection = true;
						break;
					}
				case 's':
					AddIds( Value(), result.SessionIds, result );
					result.HasExplicitSelection = true;
					break;
				case 't':
					AddStrings( Value(), result.Terminals );
					result.HasExplicitSelection = true;
					break;
				case 'u':
					AddAccounts( Value(), result.EffectiveUserIds, result, accounts, true );
					result.HasExplicitSelection = true;
					break;
				case 'U':
					AddAccounts( Value(), result.RealUserIds, result, accounts, true );
					result.HasExplicitSelection = true;
					break;
				case 'G':
					AddAccounts( Value(), result.RealGroupIds, result, accounts, false );
					result.HasExplicitSelection = true;
					break;
				case 'C':
					AddStrings( Value(), result.CommandNames );
					result.HasExplicitSelection = true;
					break;
				case 'o':
					ParseFormat( Value(), result );
					break;
				case 'f':
					result.FullFormat = true;
					break;
				case 'F':
					result.FullExtraFormat = true;
					break;
				case 'l':
					result.LongFormat = true;
					break;
				case 'j':
					result.JobsFormat = true;
					break;
				case 'M':
				case 'Z':
					result.SecurityFormat = true;
					break;
				case 'V':
					result.ShowVersion = true;
					break;
				case 'H':
					result.Forest = true;
					break;
				case 'L':
				case 'T':
				case 'm':
					result.ShowThreads = true;
					break;
				case 'w':
					result.Widen();
					break;
				case 'h':
					result.HeaderMode = HeaderMode.Hide;
					break;
				default:
					result.Fail( $"unknown option -{option}" );
					return;
			}
		}
		index = argumentIndex;
	}
	private static void ParseBsdOptions( string argument, ParsedArguments result ) {
		foreach ( var option in argument ) {
			switch ( option ) {
				case 'a':
					result.BsdAllUsers = true;
					result.Personality = ProcPersonality.Bsd;
					break;
				case 'x':
					result.BsdIncludeNoTerminal = true;
					result.Personality = ProcPersonality.Bsd;
					break;
				case 'u':
					result.UserFormat = true;
					result.Personality = ProcPersonality.Bsd;
					break;
				case 'l':
					result.LongFormat = true;
					result.Personality = ProcPersonality.Bsd;
					break;
				case 'j':
					result.JobsFormat = true;
					result.Personality = ProcPersonality.Bsd;
					break;
				case 'L':
					result.ShowFieldList = true;
					result.Personality = ProcPersonality.Bsd;
					break;
				case 'v':
					result.MemoryFormat = true;
					result.Personality = ProcPersonality.Bsd;
					break;
				case 'r':
					result.RunningOnly = true;
					break;
				case 'f':
					result.Forest = true;
					break;
				case 'e':
					result.IncludeEnvironment = true;
					break;
				case 'c':
					result.CommandNameOnly = true;
					break;
				case 'H':
				case 'm':
					result.ShowThreads = true;
					break;
				case 'w':
					result.Widen();
					break;
				case 'T':
					result.CurrentTerminalOnly = true;
					result.HasExplicitSelection = true;
					break;
				case 'Z':
					result.SecurityFormat = true;
					break;
				case 'V':
					result.ShowVersion = true;
					break;
				case 'h':
					result.HeaderMode = HeaderMode.Hide;
					break;
				default:
					result.Fail( $"unknown BSD option {option}" );
					return;
			}
		}
	}
	private static bool TryParseBsdOperandOption( string[] args, ref int index, string argument, ParsedArguments result, IProcPsAccountResolver accounts ) {
		if ( "t" == argument ) {
			result.CurrentTerminalOnly = true;
			result.HasExplicitSelection = true;
			return true;
		}
		if ( argument is not ( "p" or "q" or "U" or "o" or "k" ) )
			return false;
		if ( index + 1 >= args.Length ) {
			result.Fail( $"option '{argument}' requires an argument" );
			return true;
		}
		var value = args[ ++index ];
		switch ( argument ) {
			case "p":
				AddIds( value, result.ProcessIds, result );
				result.HasExplicitSelection = true;
				break;
			case "q":
				AddOrderedIds( value, result.QuickProcessIds, result );
				result.HasExplicitSelection = true;
				break;
			case "U":
				AddAccounts( value, result.EffectiveUserIds, result, accounts, true );
				result.HasExplicitSelection = true;
				break;
			case "o":
				ParseFormat( value, result );
				break;
			case "k":
				ParseSort( value, result );
				break;
		}
		result.Personality = ProcPersonality.Bsd;
		return true;
	}
	private static void ApplyPresetFormat( ParsedArguments result ) {
		IEnumerable<string> fields = result.FullExtraFormat ? FullExtraFields : result.FullFormat ? FullFields : result.UserFormat ? UserFields : result.MemoryFormat ? MemoryFields : result.JobsFormat ? JobsFields : result.LongFormat ? LongFields : result.ShowThreads ? ThreadFields : ProcPersonality.Bsd == result.Personality ? BsdDefaultFields : DefaultFields;
		result.AddFields( fields );
	}
	private static void ParseFormat( string? text, ParsedArguments result ) {
		if ( string.IsNullOrWhiteSpace( text ) ) {
			result.Fail( "format requires an argument" );
			return;
		}
		if ( !result.CustomFormat ) {
			result.Fields.Clear();
			result.CustomFormat = true;
		}
		foreach ( var token in SplitFormat( text ) ) {
			var equals = token.IndexOf( '=' );
			var nameAndWidth = 0 <= equals ? token[ ..equals ] : token;
			var header = 0 <= equals ? token[ ( equals + 1 ).. ] : null;
			var name = nameAndWidth;
			int? width = null;
			var colon = nameAndWidth.LastIndexOf( ':' );
			if ( 0 < colon ) {
				name = nameAndWidth[ ..colon ];
				if ( !int.TryParse( nameAndWidth[ ( colon + 1 ).. ], out var parsedWidth ) || 0 >= parsedWidth ) {
					result.Fail( $"invalid width in format specifier '{token}'" );
					return;
				}
				width = parsedWidth;
			}
			if ( !FieldCatalog.TryGetValue( name, out var definition ) ) {
				result.Fail( $"unknown user-defined format specifier '{name}'" );
				return;
			}
			result.Fields.Add( new SelectedField( definition, header, width ) );
		}
	}
	private static void ParseSort( string? text, ParsedArguments result ) {
		if ( string.IsNullOrWhiteSpace( text ) ) {
			result.Fail( "sort requires an argument" );
			return;
		}
		foreach ( var raw in text.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) ) {
			var descending = '-' == raw[ 0 ];
			var key = ( '+' == raw[ 0 ] || '-' == raw[ 0 ] ) ? raw[ 1.. ] : raw;
			if ( !FieldCatalog.TryGetValue( key, out var definition ) ) {
				result.Fail( $"unknown sort specifier '{key}'" );
				return;
			}
			result.SortKeys.Add( new SortKey( definition.Kind, descending ) );
		}
	}
	private static void ParseWidth( string? text, ParsedArguments result ) {
		if ( !int.TryParse( text, NumberStyles.None, CultureInfo.InvariantCulture, out var value ) || 1 > value )
			result.Fail( $"invalid width: {text}" );
		else
			result.Width = value;
	}
	private static IEnumerable<string> SplitList( string text ) => text.Split( new[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ); private static IEnumerable<string> SplitFormat( string text ) => 0 <= text.IndexOf( ',' ) ? text.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) : text.Split( new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
	private static void AddIds( string? text, ISet<int> destination, ParsedArguments result ) {
		if ( string.IsNullOrWhiteSpace( text ) ) {
			result.Fail( "identifier list requires an argument" );
			return;
		}
		foreach ( var token in SplitList( text ) ) {
			if ( !int.TryParse( token, NumberStyles.None, CultureInfo.InvariantCulture, out var value ) || 0 > value ) {
				result.Fail( $"invalid process ID list: {text}" );
				return;
			}
			destination.Add( value );
		}
	}
	private static void AddOrderedIds( string? text, ICollection<int> destination, ParsedArguments result ) {
		if ( string.IsNullOrWhiteSpace( text ) ) {
			result.Fail( "identifier list requires an argument" );
			return;
		}
		foreach ( var token in SplitList( text ) ) {
			if ( !int.TryParse( token, NumberStyles.None, CultureInfo.InvariantCulture, out var value ) || 0 > value ) {
				result.Fail( $"invalid process ID list: {text}" );
				return;
			}
			destination.Add( value );
		}
	}
	private static void AddStrings( string? text, ISet<string> destination ) {
		if ( null == text )
			return;
		foreach ( var token in SplitList( text ) )
			destination.Add( token );
	}
	private static void AddAccounts( string? text, ISet<uint> destination, ParsedArguments result, IProcPsAccountResolver resolver, bool user ) {
		if ( string.IsNullOrWhiteSpace( text ) ) {
			result.Fail( "account list requires an argument" );
			return;
		}
		foreach ( var token in SplitList( text ) ) {
			uint id;
			var ok = user ? resolver.TryResolveUser( token, out id ) : resolver.TryResolveGroup( token, out id );
			if ( !ok ) {
				result.Fail( $"user/group name does not exist: {token}" );
				return;
			}
			destination.Add( id );
		}
	}
	private static IReadOnlyList<ProcProcessSnapshot> SelectProcesses( IReadOnlyList<ProcProcessSnapshot> processes, ParsedArguments options, ProcProcessSnapshot? current ) {
		IEnumerable<ProcProcessSnapshot> selected;
		if ( 0 < options.QuickProcessIds.Count ) {
			var set = options.QuickProcessIds.ToHashSet();
			selected = processes.Where( p => set.Contains( p.ProcessId ) );
		} else if ( options.SelectAll )
			selected = processes;
		else if ( options.HasExplicitSelection )
			selected = processes.Where( p => MatchesExplicit( p, options, current ) );
		else
			selected = processes.Where( p => MatchesDefault( p, options, current ) );
		if ( options.RunningOnly )
			selected = selected.Where( p => p.State.HasValue && ProcProcessState.Running == p.State.Value );
		if ( options.Invert ) {
			var ids = selected.Select( p => p.ProcessId ).ToHashSet();
			selected = processes.Where( p => !ids.Contains( p.ProcessId ) );
		}
		return selected.ToArray();
	}
	private static bool MatchesDefault( ProcProcessSnapshot p, ParsedArguments o, ProcProcessSnapshot? current ) {
		if ( o.BsdAllUsers )
			return o.BsdIncludeNoTerminal || p.Terminal.HasValue;
		var userMatches = null == current || !current.EffectiveUserId.HasValue || ( p.EffectiveUserId.HasValue && p.EffectiveUserId.Value == current.EffectiveUserId.Value );
		if ( !userMatches )
			return false;
		if ( o.BsdIncludeNoTerminal )
			return true;
		if ( null == current || !current.Terminal.HasValue )
			return p.Terminal.HasValue;
		return SameTerminal( p.Terminal, current.Terminal );
	}
	private static bool MatchesExplicit( ProcProcessSnapshot p, ParsedArguments o, ProcProcessSnapshot? current ) {
		var any = false;
		var match = false;
		void C( bool criterion, bool value ) {
			if ( criterion ) {
				any = true;
				match |= value;
			}
		}
		C( 0 < o.ProcessIds.Count, o.ProcessIds.Contains( p.ProcessId ) );
		C( 0 < o.ParentIds.Count, p.ParentProcessId.HasValue && o.ParentIds.Contains( p.ParentProcessId.Value ) );
		C( 0 < o.ProcessGroupIds.Count, p.ProcessGroupId.HasValue && o.ProcessGroupIds.Contains( p.ProcessGroupId.Value ) );
		C( 0 < o.SessionIds.Count, p.SessionId.HasValue && o.SessionIds.Contains( p.SessionId.Value ) );
		C( 0 < o.EffectiveUserIds.Count, p.EffectiveUserId.HasValue && o.EffectiveUserIds.Contains( p.EffectiveUserId.Value ) );
		C( 0 < o.RealUserIds.Count, p.RealUserId.HasValue && o.RealUserIds.Contains( p.RealUserId.Value ) );
		C( 0 < o.EffectiveGroupIds.Count, p.EffectiveGroupId.HasValue && o.EffectiveGroupIds.Contains( p.EffectiveGroupId.Value ) );
		C( 0 < o.RealGroupIds.Count, p.RealGroupId.HasValue && o.RealGroupIds.Contains( p.RealGroupId.Value ) );
		C( 0 < o.Terminals.Count, MatchesTerminal( p.Terminal, o.Terminals ) );
		C( 0 < o.CommandNames.Count, p.CommandName.HasValue && o.CommandNames.Contains( p.CommandName.Value ) );
		C( o.SelectTerminalProcesses, p.Terminal.HasValue && !( p.SessionId.HasValue && p.SessionId.Value == p.ProcessId ) );
		C( o.CurrentTerminalOnly, null != current && SameTerminal( p.Terminal, current.Terminal ) );
		C( o.SelectExceptSessionLeaders, !( p.SessionId.HasValue && p.SessionId.Value == p.ProcessId ) );
		return any && match;
	}
	private static bool MatchesTerminal( ProcObservedValue<ProcTerminalInfo> terminal, IReadOnlySet<string> selectors ) {
		if ( !terminal.HasValue )
			return selectors.Contains( "?" ) || selectors.Contains( "-" );
		var value = NormalizeTerminal( terminal.Value );
		return selectors.Contains( value ) || selectors.Contains( $"/dev/{value}" );
	}
	private static bool SameTerminal( ProcObservedValue<ProcTerminalInfo> left, ProcObservedValue<ProcTerminalInfo> right ) => left.HasValue && right.HasValue && string.Equals( NormalizeTerminal( left.Value ), NormalizeTerminal( right.Value ), StringComparison.Ordinal ); private static string NormalizeTerminal( ProcTerminalInfo terminal ) => string.IsNullOrWhiteSpace( terminal.Name ) ? "?" : terminal.Name.StartsWith( "/dev/", StringComparison.Ordinal ) ? terminal.Name[ 5.. ] : terminal.Name;
	private static async Task<IReadOnlyList<ProcMatchCandidate>> BuildCandidatesAsync( IReadOnlyList<ProcProcessSnapshot> processes, bool needSupplements, bool includeThreads, IProcMatchSupplementProvider? provider, CancellationToken token ) {
		if ( needSupplements )
			return await ( provider ?? SystemProcMatchSupplementProvider.Instance ).GetCandidatesAsync( processes, includeThreads, token ).ConfigureAwait( false );
		return processes.Select( p => new ProcMatchCandidate( p, new ProcMatchSupplement { ThreadGroupId = p.ProcessId } ) ).ToArray();
	}
	private static IReadOnlyList<ProcMatchCandidate> OrderQuick( IReadOnlyList<ProcMatchCandidate> candidates, IReadOnlyList<int> ids ) {
		var byId = candidates.GroupBy( c => c.Process.ProcessId ).ToDictionary( g => g.Key, g => g.ToArray() );
		var result = new List<ProcMatchCandidate>();
		foreach ( var id in ids )
			if ( byId.TryGetValue( id, out var values ) )
				result.AddRange( values );
		return result;
	}
	private static IReadOnlyList<ProcMatchCandidate> SortCandidates( IReadOnlyList<ProcMatchCandidate> candidates, IReadOnlyList<SortKey> keys ) {
		var result = candidates.ToArray();
		Array.Sort( result, ( l, r ) => { foreach ( var key in keys ) { var c = CompareField( l.Process, r.Process, key.Kind ); if ( 0 != c ) return key.Descending ? -c : c; } return l.Process.ProcessId.CompareTo( r.Process.ProcessId ); } );
		return result;
	}
	private static int CompareField( ProcProcessSnapshot l, ProcProcessSnapshot r, ProcReportFieldKind kind ) => kind switch { ProcReportFieldKind.Pid => l.ProcessId.CompareTo( r.ProcessId ), ProcReportFieldKind.ParentPid => CompareObserved( l.ParentProcessId, r.ParentProcessId ), ProcReportFieldKind.ProcessGroup => CompareObserved( l.ProcessGroupId, r.ProcessGroupId ), ProcReportFieldKind.Session => CompareObserved( l.SessionId, r.SessionId ), ProcReportFieldKind.EffectiveUserId => CompareObserved( l.EffectiveUserId, r.EffectiveUserId ), ProcReportFieldKind.RealUserId => CompareObserved( l.RealUserId, r.RealUserId ), ProcReportFieldKind.EffectiveGroupId => CompareObserved( l.EffectiveGroupId, r.EffectiveGroupId ), ProcReportFieldKind.RealGroupId => CompareObserved( l.RealGroupId, r.RealGroupId ), ProcReportFieldKind.Nice => CompareObserved( l.NiceValue, r.NiceValue ), ProcReportFieldKind.Threads => CompareObserved( l.ThreadCount, r.ThreadCount ), ProcReportFieldKind.ResidentMemory => CompareObserved( l.ResidentMemoryBytes, r.ResidentMemoryBytes ), ProcReportFieldKind.VirtualMemory => CompareObserved( l.VirtualMemoryBytes, r.VirtualMemoryBytes ), ProcReportFieldKind.Command => CompareObservedString( l.CommandName, r.CommandName ), _ => l.ProcessId.CompareTo( r.ProcessId ) };
	private static int CompareObserved<T>( ProcObservedValue<T> l, ProcObservedValue<T> r ) where T : IComparable<T> {
		if ( l.HasValue && r.HasValue )
			return l.Value.CompareTo( r.Value );
		if ( l.HasValue )
			return -1;
		return r.HasValue ? 1 : 0;
	}
	private static int CompareObservedString( ProcObservedValue<string> l, ProcObservedValue<string> r ) {
		if ( l.HasValue && r.HasValue )
			return string.Compare( l.Value, r.Value, StringComparison.Ordinal );
		if ( l.HasValue )
			return -1;
		return r.HasValue ? 1 : 0;
	}
	private static IReadOnlyList<ProcMatchCandidate> OrderForest( IReadOnlyList<ProcMatchCandidate> candidates ) {
		var byParent = candidates.GroupBy( c => c.Process.ParentProcessId.HasValue ? c.Process.ParentProcessId.Value : int.MinValue ).ToDictionary( g => g.Key, g => g.OrderBy( c => c.Process.ProcessId ).ToArray() );
		var ids = candidates.Select( c => c.Process.ProcessId ).ToHashSet();
		var roots = candidates.Where( c => !c.Process.ParentProcessId.HasValue || !ids.Contains( c.Process.ParentProcessId.Value ) ).OrderBy( c => c.Process.ProcessId );
		var result = new List<ProcMatchCandidate>();
		var visited = new HashSet<int>();
		void Add( ProcMatchCandidate c ) {
			if ( !visited.Add( c.Process.ProcessId ) )
				return;
			result.Add( c );
			if ( byParent.TryGetValue( c.Process.ProcessId, out var children ) )
				foreach ( var child in children )
					Add( child );
		}
		foreach ( var root in roots )
			Add( root );
		foreach ( var c in candidates.OrderBy( c => c.Process.ProcessId ) )
			Add( c );
		return result;
	}
	private static int GetForestDepth( ProcProcessSnapshot process, IReadOnlyList<ProcMatchCandidate> candidates ) {
		var byId = candidates.ToDictionary( c => c.Process.ProcessId, c => c.Process );
		var depth = 0;
		var current = process;
		var visited = new HashSet<int> { current.ProcessId };
		while ( current.ParentProcessId.HasValue && byId.TryGetValue( current.ParentProcessId.Value, out var parent ) && visited.Add( parent.ProcessId ) ) {
			depth++;
			current = parent;
		}
		return depth;
	}
	private static bool FieldsNeedSupplements( IReadOnlyList<SelectedField> fields ) => fields.Any( f => f.Definition.Kind is ProcReportFieldKind.Elapsed or ProcReportFieldKind.ElapsedSeconds or ProcReportFieldKind.CpuPercent or ProcReportFieldKind.Start or ProcReportFieldKind.StartLong or ProcReportFieldKind.Environment or ProcReportFieldKind.SecurityLabel or ProcReportFieldKind.SignalBlocked or ProcReportFieldKind.SignalCaught or ProcReportFieldKind.SignalIgnored or ProcReportFieldKind.SignalPending or ProcReportFieldKind.CapabilityInheritable or ProcReportFieldKind.CapabilityPermitted or ProcReportFieldKind.CapabilityEffective or ProcReportFieldKind.CapabilityBounding or ProcReportFieldKind.CapabilityAmbient );
	private static bool FieldsNeedMetrics( IReadOnlyList<SelectedField> fields ) => fields.Any( f => f.Definition.Kind is ProcReportFieldKind.CpuTime or ProcReportFieldKind.CpuPercent or ProcReportFieldKind.MemoryPercent );
	private static async Task RenderAsync( IReadOnlyList<ProcMatchCandidate> candidates, ParsedArguments options, ProcSystemSnapshot? system, IProcPsAccountResolver accounts, DateTimeOffset now, Stream? stdout, CancellationToken token ) {
		var showHeader = HeaderMode.Show == options.HeaderMode || ( HeaderMode.Default == options.HeaderMode && options.Fields.Any( f => null == f.HeaderOverride || 0 < f.HeaderOverride.Length ) );
		if ( showHeader ) {
			var header = string.Join( " ", options.Fields.Select( f => PadField( f.Header, f.Width, f.Definition.RightAligned ) ) ).TrimEnd();
			await WriteLineAsync( stdout, LimitWidth( header, options.Width ), token ).ConfigureAwait( false );
		}
		foreach ( var candidate in candidates ) {
			token.ThrowIfCancellationRequested();
			var context = new RenderContext( candidate, system, accounts, now, options, options.Forest ? GetForestDepth( candidate.Process, candidates ) : 0 );
			var values = new string[ options.Fields.Count ];
			for ( var i = 0; i < options.Fields.Count; i++ ) {
				var field = options.Fields[ i ];
				var value = FormatField( field.Definition.Kind, context );
				values[ i ] = i + 1 == options.Fields.Count ? value : PadField( value, field.Width, field.Definition.RightAligned );
			}
			await WriteLineAsync( stdout, LimitWidth( string.Join( " ", values ).TrimEnd(), options.Width ), token ).ConfigureAwait( false );
		}
	}
	private static string FormatField( ProcReportFieldKind kind, RenderContext c ) {
		var p = c.Candidate.Process;
		return kind switch {
			ProcReportFieldKind.Pid => p.ProcessId.ToString( CultureInfo.InvariantCulture ),
			ProcReportFieldKind.ThreadId => p.ProcessId.ToString( CultureInfo.InvariantCulture ),
			ProcReportFieldKind.ThreadGroupId => c.Candidate.Supplement.ThreadGroupId.ToString( CultureInfo.InvariantCulture ),
			ProcReportFieldKind.ParentPid => FormatObserved( p.ParentProcessId ),
			ProcReportFieldKind.ProcessGroup => FormatObserved( p.ProcessGroupId ),
			ProcReportFieldKind.Session => FormatObserved( p.SessionId ),
			ProcReportFieldKind.EffectiveUserId => FormatObserved( p.EffectiveUserId ),
			ProcReportFieldKind.RealUserId => FormatObserved( p.RealUserId ),
			ProcReportFieldKind.EffectiveGroupId => FormatObserved( p.EffectiveGroupId ),
			ProcReportFieldKind.RealGroupId => FormatObserved( p.RealGroupId ),
			ProcReportFieldKind.EffectiveUserName => FormatAccount( p.EffectiveUserId, c.AccountResolver, true ),
			ProcReportFieldKind.RealUserName => FormatAccount( p.RealUserId, c.AccountResolver, true ),
			ProcReportFieldKind.EffectiveGroupName => FormatAccount( p.EffectiveGroupId, c.AccountResolver, false ),
			ProcReportFieldKind.RealGroupName => FormatAccount( p.RealGroupId, c.AccountResolver, false ),
			ProcReportFieldKind.Terminal => p.Terminal.HasValue ? NormalizeTerminal( p.Terminal.Value ) : "?",
			ProcReportFieldKind.State => p.State.HasValue ? StateCode( p.State.Value ) : "?",
			ProcReportFieldKind.Stat => FormatStat( p ),
			ProcReportFieldKind.Nice => FormatObserved( p.NiceValue ),
			ProcReportFieldKind.Priority => p.NiceValue.HasValue ? ( 20 + p.NiceValue.Value ).ToString( CultureInfo.InvariantCulture ) : "-",
			ProcReportFieldKind.Threads => FormatObserved( p.ThreadCount ),
			ProcReportFieldKind.ResidentMemory => FormatKb( p.ResidentMemoryBytes ),
			ProcReportFieldKind.VirtualMemory => FormatKb( p.VirtualMemoryBytes ),
			ProcReportFieldKind.SizePages => p.VirtualMemoryBytes.HasValue ? ( ( p.VirtualMemoryBytes.Value + (ulong)Math.Max( Environment.SystemPageSize, 1 ) - 1 ) / (ulong)Math.Max( Environment.SystemPageSize, 1 ) ).ToString( CultureInfo.InvariantCulture ) : "-",
			ProcReportFieldKind.CommandName => p.CommandName.HasValue ? p.CommandName.Value : "-",
			ProcReportFieldKind.Command => FormatCommand( c ),
			ProcReportFieldKind.Environment => c.Candidate.Supplement.Environment.HasValue ? string.Join( " ", c.Candidate.Supplement.Environment.Value ) : "-",
			ProcReportFieldKind.Elapsed => FormatElapsed( c.Candidate.Supplement.Elapsed, false ),
			ProcReportFieldKind.ElapsedSeconds => FormatElapsed( c.Candidate.Supplement.Elapsed, true ),
			ProcReportFieldKind.CpuTime => FormatCpuTime( p, c.System ),
			ProcReportFieldKind.CpuPercent => FormatCpuPercent( p, c.System, c.Candidate.Supplement.Elapsed ),
			ProcReportFieldKind.MemoryPercent => FormatMemPercent( p, c.System ),
			ProcReportFieldKind.Start => FormatStart( c.Candidate.Supplement.Elapsed, c.Now, false ),
			ProcReportFieldKind.StartLong => FormatStart( c.Candidate.Supplement.Elapsed, c.Now, true ),
			ProcReportFieldKind.Cgroup => p.Container.HasValue ? p.Container.Value.CgroupPath : "-",
			ProcReportFieldKind.Container => p.Container.HasValue ? p.Container.Value.ContainerId ?? p.Container.Value.Runtime ?? p.Container.Value.CgroupPath : "-",
			ProcReportFieldKind.NamespacePid => p.NamespaceProcessIds.HasValue ? string.Join( ",", p.NamespaceProcessIds.Value ) : "-",
			ProcReportFieldKind.IpcNamespace => FormatNamespace( p.Namespaces, "ipc" ),
			ProcReportFieldKind.MountNamespace => FormatNamespace( p.Namespaces, "mnt" ),
			ProcReportFieldKind.NetNamespace => FormatNamespace( p.Namespaces, "net" ),
			ProcReportFieldKind.PidNamespace => FormatNamespace( p.Namespaces, "pid" ),
			ProcReportFieldKind.UserNamespace => FormatNamespace( p.Namespaces, "user" ),
			ProcReportFieldKind.UtsNamespace => FormatNamespace( p.Namespaces, "uts" ),
			ProcReportFieldKind.SecurityLabel => FormatLabel( c.Candidate.Supplement.SecurityLabel ),
			ProcReportFieldKind.SignalBlocked => Status( c.Candidate.Supplement.LinuxStatusFields, "SigBlk" ),
			ProcReportFieldKind.SignalCaught => Status( c.Candidate.Supplement.LinuxStatusFields, "SigCgt" ),
			ProcReportFieldKind.SignalIgnored => Status( c.Candidate.Supplement.LinuxStatusFields, "SigIgn" ),
			ProcReportFieldKind.SignalPending => Status( c.Candidate.Supplement.LinuxStatusFields, "SigPnd" ),
			ProcReportFieldKind.CapabilityInheritable => Status( c.Candidate.Supplement.LinuxStatusFields, "CapInh" ),
			ProcReportFieldKind.CapabilityPermitted => Status( c.Candidate.Supplement.LinuxStatusFields, "CapPrm" ),
			ProcReportFieldKind.CapabilityEffective => Status( c.Candidate.Supplement.LinuxStatusFields, "CapEff" ),
			ProcReportFieldKind.CapabilityBounding => Status( c.Candidate.Supplement.LinuxStatusFields, "CapBnd" ),
			ProcReportFieldKind.CapabilityAmbient => Status( c.Candidate.Supplement.LinuxStatusFields, "CapAmb" ),
			_ => "-"
		};
	}
	private static string FormatObserved<T>( ProcObservedValue<T> v ) where T : IFormattable => v.HasValue ? v.Value.ToString( null, CultureInfo.InvariantCulture ) : "-";
	private static string FormatAccount( ProcObservedValue<uint> id, IProcPsAccountResolver r, bool user ) {
		if ( !id.HasValue )
			return "-";
		string name;
		var ok = user ? r.TryGetUserName( id.Value, out name ) : r.TryGetGroupName( id.Value, out name );
		return ok ? name : id.Value.ToString( CultureInfo.InvariantCulture );
	}
	private static string FormatCommand( RenderContext c ) {
		var p = c.Candidate.Process;
		var command = c.Options.CommandNameOnly ? ( p.CommandName.HasValue ? p.CommandName.Value : "-" ) : p.CommandLineArguments.HasValue && 0 < p.CommandLineArguments.Value.Count ? string.Join( " ", p.CommandLineArguments.Value ) : ( p.CommandName.HasValue ? p.CommandName.Value : "-" );
		if ( c.Options.Forest && 0 < c.ForestDepth )
			command = new string( ' ', c.ForestDepth * 2 ) + "\\_ " + command;
		if ( c.Options.IncludeEnvironment && c.Candidate.Supplement.Environment.HasValue && 0 < c.Candidate.Supplement.Environment.Value.Count )
			command += " " + string.Join( " ", c.Candidate.Supplement.Environment.Value );
		return command;
	}
	private static string FormatStat( ProcProcessSnapshot p ) {
		var state = p.State.HasValue ? StateCode( p.State.Value ) : "?";
		if ( p.NiceValue.HasValue )
			state += 0 < p.NiceValue.Value ? "N" : 0 > p.NiceValue.Value ? "<" : "";
		if ( p.ThreadCount.HasValue && 1 < p.ThreadCount.Value )
			state += "l";
		if ( p.SessionId.HasValue && p.SessionId.Value == p.ProcessId )
			state += "s";
		return state;
	}
	private static string StateCode( ProcProcessState state ) => state switch { ProcProcessState.Running => "R", ProcProcessState.Sleeping => "S", ProcProcessState.DiskSleep => "D", ProcProcessState.Stopped => "T", ProcProcessState.TracingStop => "t", ProcProcessState.Zombie => "Z", ProcProcessState.Dead => "X", ProcProcessState.Idle => "I", ProcProcessState.Waking => "W", ProcProcessState.Parked => "P", _ => "?" };
	private static string FormatKb( ProcObservedValue<ulong> bytes ) => bytes.HasValue ? ( bytes.Value / 1024UL ).ToString( CultureInfo.InvariantCulture ) : "-"; private static string FormatElapsed( ProcObservedValue<TimeSpan> elapsed, bool seconds ) => !elapsed.HasValue ? "-" : seconds ? Math.Max( 0L, (long)elapsed.Value.TotalSeconds ).ToString( CultureInfo.InvariantCulture ) : FormatDuration( elapsed.Value );
	private static double? CpuSeconds( ProcProcessSnapshot p, ProcSystemSnapshot? system ) {
		if ( !p.UserCpuTicks.HasValue || !p.SystemCpuTicks.HasValue )
			return null;
		var total = p.UserCpuTicks.Value + p.SystemCpuTicks.Value;
		if ( ProcObservationSource.DotNetProcessApi == p.UserCpuTicks.Source )
			return total / (double)TimeSpan.TicksPerSecond;
		if ( null == system || !system.Cpu.HasValue || !system.Uptime.HasValue || 0 >= system.Uptime.Value.Uptime.TotalSeconds )
			return null;
		var hz = system.Cpu.Value.Total / system.Uptime.Value.Uptime.TotalSeconds / Math.Max( Environment.ProcessorCount, 1 );
		return 0 < hz ? total / Math.Max( 1.0, Math.Round( hz ) ) : null;
	}
	private static string FormatCpuTime( ProcProcessSnapshot p, ProcSystemSnapshot? s ) {
		var sec = CpuSeconds( p, s );
		return sec.HasValue ? FormatDuration( TimeSpan.FromSeconds( sec.Value ) ) : "-";
	}
	private static string FormatCpuPercent( ProcProcessSnapshot p, ProcSystemSnapshot? s, ProcObservedValue<TimeSpan> elapsed ) {
		var sec = CpuSeconds( p, s );
		return sec.HasValue && elapsed.HasValue && 0 < elapsed.Value.TotalSeconds ? ( 100.0 * sec.Value / elapsed.Value.TotalSeconds ).ToString( "0.0", CultureInfo.InvariantCulture ) : "0.0";
	}
	private static string FormatMemPercent( ProcProcessSnapshot p, ProcSystemSnapshot? s ) => p.ResidentMemoryBytes.HasValue && null != s && s.Memory.HasValue && s.Memory.Value.TotalBytes.HasValue && 0 < s.Memory.Value.TotalBytes.Value ? ( 100.0 * p.ResidentMemoryBytes.Value / s.Memory.Value.TotalBytes.Value ).ToString( "0.0", CultureInfo.InvariantCulture ) : "0.0";
	private static string FormatStart( ProcObservedValue<TimeSpan> elapsed, DateTimeOffset now, bool longForm ) {
		if ( !elapsed.HasValue )
			return "-";
		var start = now - elapsed.Value;
		return longForm ? start.ToString( "ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture ) : 24.0 > elapsed.Value.TotalHours ? start.ToString( "HH:mm", CultureInfo.InvariantCulture ) : start.ToString( "MMMdd", CultureInfo.InvariantCulture );
	}
	private static string FormatNamespace( ProcObservedValue<IReadOnlyDictionary<string, ProcNamespaceInfo>> namespaces, string kind ) {
		if ( !namespaces.HasValue || !namespaces.Value.TryGetValue( kind, out var value ) )
			return "-";
		return value.Identifier.HasValue ? value.Identifier.Value.ToString( CultureInfo.InvariantCulture ) : value.LinkTarget;
	}
	private static string FormatLabel( ProcObservedValue<string> label ) => label.HasValue && !string.IsNullOrWhiteSpace( label.Value ) ? label.Value : "-"; private static string Status( ProcObservedValue<IReadOnlyDictionary<string, string>> status, string field ) => status.HasValue && status.Value.TryGetValue( field, out var value ) && !string.IsNullOrWhiteSpace( value ) ? value : "-";
	private static string FormatDuration( TimeSpan value ) {
		if ( TimeSpan.Zero > value )
			value = TimeSpan.Zero;
		var hours = checked((long)value.TotalHours);
		return 0 < value.Days ? string.Format( CultureInfo.InvariantCulture, "{0}-{1:00}:{2:00}:{3:00}", value.Days, value.Hours, value.Minutes, value.Seconds ) : string.Format( CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", hours, value.Minutes, value.Seconds );
	}
	private static string PadField( string value, int width, bool right ) => value.Length >= width ? value : right ? value.PadLeft( width ) : value.PadRight( width ); private static string LimitWidth( string value, int? width ) => !width.HasValue || value.Length <= width.Value ? value : value[ ..width.Value ];
	private static IReadOnlyDictionary<string, string?> ReadPersonalityEnvironment() => new Dictionary<string, string?>( StringComparer.Ordinal ) { { "PS_PERSONALITY", Environment.GetEnvironmentVariable( "PS_PERSONALITY" ) }, { "CMD_ENV", Environment.GetEnvironmentVariable( "CMD_ENV" ) } };
	private static string NormalizeLineEndings( string value ) {
		var normalized = value.Replace( "\r\n", "\n", StringComparison.Ordinal ).Replace( "\r", "\n", StringComparison.Ordinal );
		return "\n" == Environment.NewLine ? normalized : normalized.Replace( "\n", Environment.NewLine, StringComparison.Ordinal );
	}
	private static async Task WriteLineAsync( Stream? stream, string text, CancellationToken token ) => await WriteAsync( stream, text + Environment.NewLine, token ).ConfigureAwait( false ); private static async Task WriteAsync( Stream? stream, string text, CancellationToken token ) {
		if ( null == stream )
			return;
		var bytes = Utf8.GetBytes( text );
		await stream.WriteAsync( bytes.AsMemory(), token ).ConfigureAwait( false );
	}
	private enum HeaderMode {
		Default, Show, Hide
	}
	private sealed class SelectedField {
		public ProcReportFieldDefinition Definition {
			get;
		}
		public string? HeaderOverride {
			get;
		}
		public int? ExplicitWidth {
			get;
		}
		public string Header => this.HeaderOverride ?? this.Definition.Header; public int Width => Math.Max( this.ExplicitWidth ?? this.Definition.Width, this.Header.Length ); public SelectedField( ProcReportFieldDefinition definition, string? headerOverride = null, int? explicitWidth = null ) {
			this.Definition = definition;
			this.HeaderOverride = headerOverride;
			this.ExplicitWidth = explicitWidth;
		}
	}
	private sealed class SortKey {
		public ProcReportFieldKind Kind {
			get;
		}
		public bool Descending {
			get;
		}
		public SortKey( ProcReportFieldKind kind, bool descending ) {
			this.Kind = kind;
			this.Descending = descending;
		}
	}
	private sealed class RenderContext {
		public ProcMatchCandidate Candidate {
			get;
		}
		public ProcSystemSnapshot? System {
			get;
		}
		public IProcPsAccountResolver AccountResolver {
			get;
		}
		public DateTimeOffset Now {
			get;
		}
		public ParsedArguments Options {
			get;
		}
		public int ForestDepth {
			get;
		}
		public RenderContext( ProcMatchCandidate candidate, ProcSystemSnapshot? system, IProcPsAccountResolver accountResolver, DateTimeOffset now, ParsedArguments options, int forestDepth ) {
			this.Candidate = candidate;
			this.System = system;
			this.AccountResolver = accountResolver;
			this.Now = now;
			this.Options = options;
			this.ForestDepth = forestDepth;
		}
	}
	private sealed class ParsedArguments {
		public string? Error {
			get; private set;
		}
		public bool ShowHelp; public bool ShowVersion; public bool ShowFieldList; public bool SelectAll; public bool SelectTerminalProcesses; public bool SelectExceptSessionLeaders; public bool HasExplicitSelection; public bool Invert; public bool BsdAllUsers; public bool BsdIncludeNoTerminal; public bool RunningOnly; public bool CurrentTerminalOnly; public bool ShowThreads; public bool Forest; public bool IncludeEnvironment; public bool CommandNameOnly; public bool FullFormat; public bool FullExtraFormat; public bool LongFormat; public bool JobsFormat; public bool UserFormat; public bool MemoryFormat; public bool SecurityFormat; public bool CustomFormat; public ProcPersonality Personality; public HeaderMode HeaderMode; public int? Width = DefaultWidth; public HashSet<int> ProcessIds { get; } = []; public List<int> QuickProcessIds { get; } = []; public HashSet<int> ParentIds { get; } = []; public HashSet<int> ProcessGroupIds { get; } = []; public HashSet<int> SessionIds { get; } = []; public HashSet<uint> EffectiveUserIds { get; } = []; public HashSet<uint> RealUserIds { get; } = []; public HashSet<uint> EffectiveGroupIds { get; } = []; public HashSet<uint> RealGroupIds { get; } = []; public HashSet<string> Terminals { get; } = new( StringComparer.Ordinal ); public HashSet<string> CommandNames { get; } = new( StringComparer.Ordinal ); public List<SelectedField> Fields { get; } = []; public List<SortKey> SortKeys { get; } = []; private int WidenCount;
		public void AddFields( IEnumerable<string> names ) {
			foreach ( var name in names ) {
				if ( !FieldCatalog.TryGetValue( name, out var definition ) )
					throw new InvalidOperationException( $"Internal ps field '{name}' is not registered." );
				this.Fields.Add( new SelectedField( definition ) );
			}
		}
		public void PrependField( string name ) {
			if ( !FieldCatalog.TryGetValue( name, out var definition ) )
				throw new InvalidOperationException( $"Internal ps field '{name}' is not registered." );
			this.Fields.Insert( 0, new SelectedField( definition ) );
		}
		public bool HasNonQuickSelection() => this.SelectAll || this.SelectTerminalProcesses || this.BsdAllUsers || this.BsdIncludeNoTerminal || this.RunningOnly || this.SelectExceptSessionLeaders || this.CurrentTerminalOnly || 0 < this.ProcessIds.Count || 0 < this.ParentIds.Count || 0 < this.ProcessGroupIds.Count || 0 < this.SessionIds.Count || 0 < this.EffectiveUserIds.Count || 0 < this.RealUserIds.Count || 0 < this.EffectiveGroupIds.Count || 0 < this.RealGroupIds.Count || 0 < this.Terminals.Count || 0 < this.CommandNames.Count; public void Widen() {
			this.WidenCount++;
			this.Width = 1 == this.WidenCount ? 132 : null;
		}
		public void Fail( string error ) {
			this.Error ??= error;
		}
	}
}
