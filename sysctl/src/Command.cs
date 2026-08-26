/*
	sysctl
	Read and write kernel runtime parameters.
	Copyright (C) 2026  Timothy J. Bruce <uniblab@hotmail.com>
*/

/*
	This program is free software: you can redistribute it and/or modify
	it under the terms of the GNU General Public License as published by
	the Free Software Foundation, either version 3 of the License, or
	(at your option) any later version.

	This program is distributed in the hope that it will be useful,
	but WITHOUT ANY WARRANTY; without even the implied warranty of
	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
	GNU General Public License for more details.

	You should have received a copy of the GNU General Public License
	along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

namespace Icod.ProcPs.Sysctl;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>Implements the procps-ng 4.0.6 <c>sysctl</c> kernel-parameter command.</summary>
public static class Command {
	private const int Success = 0;
	private const int Failure = 1;
	private const int Cancelled = 130;
	private const string DefaultPreload = "/etc/sysctl.conf";
	private const string ProcDisplayRoot = "/proc/sys/";
	private static readonly string VersionText = global::Icod.ProcPs.ProcCommandVersion.Format(
		"Icod.ProcPs.Sysctl",
		typeof( Command ).Assembly
	);
	private static readonly string[] SystemDirectories = [
		"/etc/sysctl.d",
		"/run/sysctl.d",
		"/usr/local/lib/sysctl.d",
		"/usr/lib/sysctl.d",
		"/lib/sysctl.d"
	];
	private static readonly HashSet<string> DeprecatedLeafNames = new( StringComparer.Ordinal ) {
		"base_reachable_time",
		"retrans_time"
	};
	private static readonly HashSet<string> VerbotenLeafNames = new( StringComparer.Ordinal ) {
		"stat_refresh"
	};
	private static readonly Encoding Utf8 = new UTF8Encoding( false );
	private const string HelpText = """
Usage:
 sysctl [options] [variable[=value] ...]

Options:
  -a, --all            display all variables
  -A                   alias of -a
  -X                   alias of -a
      --deprecated     include deprecated parameters to listing
      --dry-run        print the key and values but do not write
  -b, --binary         print value without new line
  -e, --ignore         ignore unknown variables errors
  -N, --names          print variable names without values
  -n, --values         print only values of the given variable(s)
  -p, --load[=<file>]  read values from file
  -f                   alias of -p
      --system         read values from all system directories
  -r, --pattern <expression>
                       select setting that match expression
  -q, --quiet          do not echo variable set
  -w, --write          enable writing a value to variable
  -o                   does nothing
  -x                   does nothing
  -d                   alias of -h
  -h, --help           display this help and exit
  -V, --version        output version information and exit

For more details see sysctl(8).
""";

	/// <summary>Runs <c>sysctl</c> synchronously.</summary>
	/// <param name="args">Command-line arguments.</param>
	/// <param name="stdout">Optional output writer.</param>
	/// <param name="stderr">Optional diagnostic writer.</param>
	/// <returns>The process exit status.</returns>
	public static int Run( string[] args, TextWriter? stdout = null, TextWriter? stderr = null ) {
		ArgumentNullException.ThrowIfNull( args );
		using var output = new MemoryStream();
		using var error = new MemoryStream();
		var status = RunAsync( args, stdout: output, stderr: error ).GetAwaiter().GetResult();
		( stdout ?? Console.Out ).Write( Utf8.GetString( output.ToArray() ) );
		( stderr ?? Console.Error ).Write( Utf8.GetString( error.ToArray() ) );
		return status;
	}

	/// <summary>Runs procps-ng <c>sysctl</c> asynchronously with an injectable Linux kernel-parameter backend.</summary>
	/// <param name="args">Command-line arguments.</param>
	/// <param name="stdin">Optional standard-input stream used by <c>-p -</c>.</param>
	/// <param name="stdout">Optional standard-output stream.</param>
	/// <param name="stderr">Optional standard-error stream.</param>
	/// <param name="backend">Optional sysctl backend.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>A task whose result is the procps-compatible exit status.</returns>
	public static async Task<int> RunAsync(
		string[] args,
		Stream? stdin = null,
		Stream? stdout = null,
		Stream? stderr = null,
		ISysctlBackend? backend = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		var parsed = ParseArguments( args );
		if ( null != parsed.Error ) {
			await WriteDiagnosticAsync( stderr, parsed.Error, cancellationToken ).ConfigureAwait( false );
			return Failure;
		}
		if ( parsed.ShowHelp ) {
			await WriteTextAsync( stdout, NormalizeLineEndings( HelpText ), cancellationToken ).ConfigureAwait( false );
			return Success;
		}
		if ( parsed.ShowVersion ) {
			await WriteLineAsync( stdout, VersionText, cancellationToken ).ConfigureAwait( false );
			return Success;
		}
		if ( 0 == args.Length ) {
			var errorTarget = stderr ?? Console.OpenStandardError();
			await WriteTextAsync( errorTarget, NormalizeLineEndings( HelpText ), cancellationToken ).ConfigureAwait( false );
			return Failure;
		}
		var activeBackend = backend ?? SystemSysctlBackend.Instance;
		if ( !activeBackend.IsSupported ) {
			await WriteDiagnosticAsync( stderr, $"sysctl: {activeBackend.UnsupportedReason}", cancellationToken ).ConfigureAwait( false );
			return Failure;
		}
		try {
			if ( parsed.DisplayAll ) {
				return await DisplayAllAsync( activeBackend, parsed, stdout, stderr, cancellationToken ).ConfigureAwait( false );
			}
			if ( parsed.PreloadMode ) {
				return await RunPreloadAsync( activeBackend, parsed, stdin, stdout, stderr, cancellationToken ).ConfigureAwait( false );
			}
			var status = Success;
			if ( parsed.SystemMode ) {
				status |= await RunSystemAsync( activeBackend, parsed, stdout, stderr, cancellationToken ).ConfigureAwait( false );
				if ( 0 == parsed.Operands.Count ) {
					return status;
				}
			}
			if ( parsed.NameOnly && parsed.Quiet ) {
				await WriteDiagnosticAsync( stderr, "sysctl: options -N and -q cannot coexist\nTry 'sysctl --help' for more information.", cancellationToken ).ConfigureAwait( false );
				return Failure;
			}
			if ( 0 == parsed.Operands.Count ) {
				await WriteDiagnosticAsync( stderr, "sysctl: no variables specified\nTry 'sysctl --help' for more information.", cancellationToken ).ConfigureAwait( false );
				return Failure;
			}
			foreach ( var operand in parsed.Operands ) {
				cancellationToken.ThrowIfCancellationRequested();
				if ( parsed.WriteMode || operand.Contains( '=' ) ) {
					var setting = ParseCommandLineSetting( operand );
					if ( null == setting ) {
						await WriteDiagnosticAsync( stderr, "sysctl: command line(0): invalid syntax, continuing...", cancellationToken ).ConfigureAwait( false );
						status |= Failure;
						continue;
					}
					if ( !MatchesPattern( operand.TrimStart(), parsed.Pattern ) ) {
						status |= Failure;
						continue;
					}
					status |= await WriteSettingAsync( activeBackend, setting, parsed, stdout, stderr, cancellationToken ).ConfigureAwait( false );
					continue;
				}
				status += await ReadSettingAsync( activeBackend, operand, parsed, stdout, stderr, cancellationToken ).ConfigureAwait( false );
			}
			return status;
		} catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested ) {
			return Cancelled;
		}
	}

	private static ParsedArguments ParseArguments( IReadOnlyList<string> args ) {
		ArgumentNullException.ThrowIfNull( args );
		var parsed = new ParsedArguments();
		var operandsOnly = false;
		for ( var index = 0; index < args.Count; index++ ) {
			var argument = args[ index ];
			if ( operandsOnly || "-" == argument || !argument.StartsWith( '-' ) ) {
				parsed.Operands.Add( argument );
				continue;
			}
			if ( "--" == argument ) {
				operandsOnly = true;
				continue;
			}
			if ( argument.StartsWith( "--", StringComparison.Ordinal ) ) {
				var error = ParseLongOption( argument, args, ref index, parsed );
				if ( null != error ) {
					parsed.Error = error;
					return parsed;
				}
				continue;
			}
			var shortError = ParseShortOptions( argument, args, ref index, parsed );
			if ( null != shortError ) {
				parsed.Error = shortError;
				return parsed;
			}
		}
		return parsed;
	}

	private static string? ParseLongOption( string argument, IReadOnlyList<string> args, ref int index, ParsedArguments parsed ) {
		ArgumentNullException.ThrowIfNull( argument );
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( parsed );
		var equals = argument.IndexOf( '=' );
		var option = argument;
		string? attached = null;
		if ( 0 <= equals ) {
			option = argument[ ..equals ];
			attached = argument[ ( equals + 1 ).. ];
		}
		switch ( option ) {
			case "--all":
				parsed.DisplayAll = true;
				return null;
			case "--deprecated":
				parsed.IncludeDeprecated = true;
				return null;
			case "--dry-run":
				parsed.DryRun = true;
				return null;
			case "--binary":
				parsed.PrintName = false;
				parsed.PrintNewline = false;
				return null;
			case "--ignore":
				parsed.IgnoreUnknown = true;
				return null;
			case "--names":
				parsed.NameOnly = true;
				return null;
			case "--values":
				parsed.PrintName = false;
				return null;
			case "--load":
				parsed.PreloadMode = true;
				if ( null != attached ) {
					parsed.AttachedPreloadFile = attached;
				}
				return null;
			case "--quiet":
				parsed.Quiet = true;
				return null;
			case "--write":
				parsed.WriteMode = true;
				return null;
			case "--system":
				parsed.SystemMode = true;
				parsed.IgnoreUnknown = true;
				return null;
			case "--pattern":
				if ( null == attached ) {
					if ( index + 1 >= args.Count ) {
						return "sysctl: option '--pattern' requires an argument";
					}
					index++;
					attached = args[ index ];
				}
				parsed.Pattern = attached;
				return null;
			case "--help":
				parsed.ShowHelp = true;
				return null;
			case "--version":
				parsed.ShowVersion = true;
				return null;
			default:
				return $"sysctl: unrecognized option '{argument}'";
		}
	}

	private static string? ParseShortOptions( string argument, IReadOnlyList<string> args, ref int index, ParsedArguments parsed ) {
		ArgumentNullException.ThrowIfNull( argument );
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( parsed );
		for ( var position = 1; position < argument.Length; position++ ) {
			var option = argument[ position ];
			switch ( option ) {
				case 'a':
				case 'A':
				case 'X':
					parsed.DisplayAll = true;
					break;
				case 'b':
					parsed.PrintName = false;
					parsed.PrintNewline = false;
					break;
				case 'e':
					parsed.IgnoreUnknown = true;
					break;
				case 'N':
					parsed.NameOnly = true;
					break;
				case 'n':
					parsed.PrintName = false;
					break;
				case 'w':
					parsed.WriteMode = true;
					break;
				case 'f':
					parsed.PreloadMode = true;
					break;
				case 'p':
					parsed.PreloadMode = true;
					if ( position + 1 < argument.Length ) {
						parsed.AttachedPreloadFile = argument[ ( position + 1 ).. ];
						return null;
					}
					break;
				case 'q':
					parsed.Quiet = true;
					break;
				case 'o':
				case 'x':
					break;
				case 'r':
					if ( position + 1 < argument.Length ) {
						parsed.Pattern = argument[ ( position + 1 ).. ];
						return null;
					}
					if ( index + 1 >= args.Count ) {
						return "sysctl: option requires an argument -- 'r'";
					}
					index++;
					parsed.Pattern = args[ index ];
					return null;
				case 'V':
					parsed.ShowVersion = true;
					break;
				case 'd':
				case 'h':
					parsed.ShowHelp = true;
					break;
				default:
					return $"sysctl: invalid option -- '{option}'";
			}
		}
		return null;
	}

	private static async Task<int> DisplayAllAsync( ISysctlBackend backend, ParsedArguments options, Stream? stdout, Stream? stderr, CancellationToken cancellationToken ) {
		ArgumentNullException.ThrowIfNull( backend );
		ArgumentNullException.ThrowIfNull( options );
		IReadOnlyList<string> keys;
		try {
			keys = await backend.EnumerateKeysAsync( cancellationToken ).ConfigureAwait( false );
		} catch ( SysctlBackendException exception ) { await WriteDiagnosticAsync( stderr, $"sysctl: unable to open directory \"/proc/sys/\": {exception.Message}", cancellationToken ).ConfigureAwait( false ); return Failure; }
		var status = Success;
		foreach ( var key in keys.OrderBy( static value => value, StringComparer.Ordinal ) ) {
			cancellationToken.ThrowIfCancellationRequested();
			if ( IsVerboten( key ) ) {
				continue;
			}
			if ( !options.IncludeDeprecated && IsDeprecated( key ) ) {
				continue;
			}
			if ( !MatchesPattern( ToDisplayKey( key ), options.Pattern ) ) {
				continue;
			}
			if ( options.NameOnly ) {
				await WriteLineAsync( stdout, ToDisplayKey( key ), cancellationToken ).ConfigureAwait( false );
				continue;
			}
			status |= await ReadSettingAsync( backend, key, options, stdout, stderr, cancellationToken, keyIsNormalized: true ).ConfigureAwait( false );
		}
		return status;
	}

	private static async Task<int> RunPreloadAsync( ISysctlBackend backend, ParsedArguments options, Stream? stdin, Stream? stdout, Stream? stderr, CancellationToken cancellationToken ) {
		ArgumentNullException.ThrowIfNull( backend );
		ArgumentNullException.ThrowIfNull( options );
		var files = new List<string>();
		if ( null != options.AttachedPreloadFile ) {
			files.Add( options.AttachedPreloadFile );
		}
		if ( 0 == options.Operands.Count && null == options.AttachedPreloadFile ) {
			files.Add( DefaultPreload );
		} else {
			files.AddRange( options.Operands );
		}
		var settings = new List<SysctlSetting>();
		var status = Success;
		foreach ( var filePattern in files ) {
			cancellationToken.ThrowIfCancellationRequested();
			if ( "-" == filePattern ) {
				status |= await PreloadStandardInputAsync( stdin, settings, options, stderr, cancellationToken ).ConfigureAwait( false );
				continue;
			}
			IReadOnlyList<string> expanded;
			try {
				expanded = await backend.ExpandConfigurationFilesAsync( filePattern, cancellationToken ).ConfigureAwait( false );
			} catch ( SysctlBackendException exception ) { await WriteDiagnosticAsync( stderr, $"sysctl: glob failed: {exception.Message}", cancellationToken ).ConfigureAwait( false ); status |= Failure; continue; }
			foreach ( var file in expanded ) {
				status |= await PreloadFileAsync( backend, file, settings, options, stderr, cancellationToken ).ConfigureAwait( false );
			}
		}
		status |= await WriteSettingListAsync( backend, settings, options, stdout, stderr, cancellationToken ).ConfigureAwait( false );
		return status;
	}

	private static async Task<int> RunSystemAsync( ISysctlBackend backend, ParsedArguments options, Stream? stdout, Stream? stderr, CancellationToken cancellationToken ) {
		ArgumentNullException.ThrowIfNull( backend );
		ArgumentNullException.ThrowIfNull( options );
		var files = await ResolveSystemConfigurationFilesAsync( backend, cancellationToken ).ConfigureAwait( false );
		var settings = new List<SysctlSetting>();
		var status = Success;
		foreach ( var file in files ) {
			cancellationToken.ThrowIfCancellationRequested();
			if ( !options.Quiet ) {
				await WriteLineAsync( stdout, $"* Applying {file} ...", cancellationToken ).ConfigureAwait( false );
			}
			status |= await PreloadFileAsync( backend, file, settings, options, stderr, cancellationToken ).ConfigureAwait( false );
		}
		status |= await WriteSettingListAsync( backend, settings, options, stdout, stderr, cancellationToken ).ConfigureAwait( false );
		return status;
	}

	private static async Task<IReadOnlyList<string>> ResolveSystemConfigurationFilesAsync( ISysctlBackend backend, CancellationToken cancellationToken ) {
		ArgumentNullException.ThrowIfNull( backend );
		var byName = new Dictionary<string, string>( StringComparer.Ordinal );
		foreach ( var directory in SystemDirectories ) {
			cancellationToken.ThrowIfCancellationRequested();
			var files = await backend.EnumerateConfigurationFilesAsync( directory, cancellationToken ).ConfigureAwait( false );
			foreach ( var file in files ) {
				var name = GetPortableFileName( file );
				if ( !name.EndsWith( ".conf", StringComparison.Ordinal ) || byName.ContainsKey( name ) ) {
					continue;
				}
				byName.Add( name, file );
			}
		}
		var ordered = byName.OrderBy( static pair => pair.Key, StringComparer.Ordinal ).Select( static pair => pair.Value ).ToList();
		if ( await backend.ConfigurationFileExistsAsync( DefaultPreload, cancellationToken ).ConfigureAwait( false ) ) {
			ordered.Add( DefaultPreload );
		}
		return ordered;
	}

	private static async Task<int> PreloadFileAsync( ISysctlBackend backend, string path, ICollection<SysctlSetting> settings, ParsedArguments options, Stream? stderr, CancellationToken cancellationToken ) {
		ArgumentNullException.ThrowIfNull( backend );
		ArgumentNullException.ThrowIfNull( path );
		ArgumentNullException.ThrowIfNull( settings );
		ArgumentNullException.ThrowIfNull( options );
		IReadOnlyList<string> lines;
		try {
			lines = await backend.ReadConfigurationFileAsync( path, cancellationToken ).ConfigureAwait( false );
		} catch ( SysctlBackendException exception ) { await WriteDiagnosticAsync( stderr, $"sysctl: cannot open \"{path}\": {exception.Message}", cancellationToken ).ConfigureAwait( false ); return Failure; }
		return await ParseConfigurationLinesAsync( path, lines, settings, options, stderr, cancellationToken ).ConfigureAwait( false );
	}

	private static async Task<int> PreloadStandardInputAsync( Stream? stdin, ICollection<SysctlSetting> settings, ParsedArguments options, Stream? stderr, CancellationToken cancellationToken ) {
		ArgumentNullException.ThrowIfNull( settings );
		ArgumentNullException.ThrowIfNull( options );
		var source = stdin ?? Console.OpenStandardInput();
		using var reader = new StreamReader( source, Encoding.UTF8, true, 4096, leaveOpen: true );
		var lines = new List<string>();
		while ( true ) {
			cancellationToken.ThrowIfCancellationRequested();
			var line = await reader.ReadLineAsync( cancellationToken ).ConfigureAwait( false );
			if ( null == line ) {
				break;
			}
			lines.Add( line );
		}
		return await ParseConfigurationLinesAsync( "-", lines, settings, options, stderr, cancellationToken ).ConfigureAwait( false );
	}

	private static async Task<int> ParseConfigurationLinesAsync( string path, IReadOnlyList<string> lines, ICollection<SysctlSetting> settings, ParsedArguments options, Stream? stderr, CancellationToken cancellationToken ) {
		ArgumentNullException.ThrowIfNull( path );
		ArgumentNullException.ThrowIfNull( lines );
		ArgumentNullException.ThrowIfNull( settings );
		ArgumentNullException.ThrowIfNull( options );
		for ( var index = 0; index < lines.Count; index++ ) {
			cancellationToken.ThrowIfCancellationRequested();
			var line = lines[ index ].Trim();
			if ( string.IsNullOrEmpty( line ) || line.StartsWith( "#", StringComparison.Ordinal ) || line.StartsWith( ";", StringComparison.Ordinal ) ) {
				continue;
			}
			if ( !MatchesPattern( line, options.Pattern ) ) {
				continue;
			}
			var setting = ParseConfigurationSetting( line );
			if ( null == setting ) {
				await WriteDiagnosticAsync( stderr, $"sysctl: {path}({index + 1}): invalid syntax, continuing...", cancellationToken ).ConfigureAwait( false );
				continue;
			}
			settings.Add( setting );
		}
		return Success;
	}

	private static SysctlSetting? ParseConfigurationSetting( string line ) {
		ArgumentNullException.ThrowIfNull( line );
		var text = line.Trim();
		if ( string.IsNullOrEmpty( text ) ) {
			return null;
		}
		var equals = text.IndexOf( '=' );
		if ( 0 > equals ) {
			if ( !text.StartsWith( '-' ) ) {
				return null;
			}
			var excludedKey = text[ 1.. ].Trim();
			if ( string.IsNullOrEmpty( excludedKey ) ) {
				return null;
			}
			return CreateSetting( excludedKey, null, ignoreFailure: false, globExclude: true );
		}
		var ignoreFailure = false;
		var keyText = text[ ..equals ].Trim();
		if ( keyText.StartsWith( '-' ) ) {
			ignoreFailure = true;
			keyText = keyText[ 1.. ].Trim();
		}
		if ( string.IsNullOrEmpty( keyText ) ) {
			return null;
		}
		var value = text[ ( equals + 1 ).. ].Trim();
		return CreateSetting( keyText, value, ignoreFailure, globExclude: false );
	}

	private static SysctlSetting? ParseCommandLineSetting( string operand ) {
		ArgumentNullException.ThrowIfNull( operand );
		var equals = operand.IndexOf( '=' );
		if ( 0 > equals ) {
			return null;
		}
		var key = operand[ ..equals ].Trim();
		var ignoreFailure = false;
		if ( key.StartsWith( '-' ) ) {
			ignoreFailure = true;
			key = key[ 1.. ].Trim();
		}
		if ( string.IsNullOrEmpty( key ) ) {
			return null;
		}
		var value = operand[ ( equals + 1 ).. ];
		return CreateSetting( key, value, ignoreFailure, globExclude: false );
	}
	private static SysctlSetting CreateSetting( string key, string? value, bool ignoreFailure, bool globExclude ) {
		ArgumentNullException.ThrowIfNull( key );
		var normalized = NormalizeKeyToProcPath( key );
		return new SysctlSetting( normalized, ToDisplayKey( normalized ), value, ignoreFailure, globExclude );
	}

	private static async Task<int> WriteSettingListAsync( ISysctlBackend backend, IReadOnlyList<SysctlSetting> settings, ParsedArguments options, Stream? stdout, Stream? stderr, CancellationToken cancellationToken ) {
		ArgumentNullException.ThrowIfNull( backend );
		ArgumentNullException.ThrowIfNull( settings );
		ArgumentNullException.ThrowIfNull( options );
		var status = Success;
		IReadOnlyList<string>? allKeys = null;
		foreach ( var setting in settings ) {
			cancellationToken.ThrowIfCancellationRequested();
			if ( setting.GlobExclude ) {
				continue;
			}
			if ( IsGlob( setting.PathKey ) ) {
				allKeys ??= await backend.EnumerateKeysAsync( cancellationToken ).ConfigureAwait( false );
				foreach ( var key in allKeys.OrderBy( static value => value, StringComparer.Ordinal ) ) {
					if ( !GlobMatch( setting.PathKey, key ) ) {
						continue;
					}
					if ( IsOverriddenOrExcluded( settings, key ) ) {
						continue;
					}
					var expanded = new SysctlSetting( key, ToDisplayKey( key ), setting.Value, setting.IgnoreFailure, GlobExclude: false );
					status |= await WriteSettingAsync( backend, expanded, options, stdout, stderr, cancellationToken ).ConfigureAwait( false );
				}
				continue;
			}
			status |= await WriteSettingAsync( backend, setting, options, stdout, stderr, cancellationToken ).ConfigureAwait( false );
		}
		return status;
	}

	private static bool IsOverriddenOrExcluded( IReadOnlyList<SysctlSetting> settings, string key ) {
		ArgumentNullException.ThrowIfNull( settings );
		ArgumentNullException.ThrowIfNull( key );
		foreach ( var candidate in settings ) {
			if ( candidate.GlobExclude && GlobMatch( candidate.PathKey, key ) ) {
				return true;
			}
			if ( !candidate.GlobExclude && !IsGlob( candidate.PathKey ) && string.Equals( candidate.PathKey, key, StringComparison.Ordinal ) ) {
				return true;
			}
		}
		return false;
	}

	private static async Task<int> ReadSettingAsync( ISysctlBackend backend, string requestedKey, ParsedArguments options, Stream? stdout, Stream? stderr, CancellationToken cancellationToken, bool keyIsNormalized = false ) {
		ArgumentNullException.ThrowIfNull( backend );
		ArgumentNullException.ThrowIfNull( requestedKey );
		ArgumentNullException.ThrowIfNull( options );
		var key = requestedKey;
		if ( !keyIsNormalized ) {
			key = NormalizeKeyToProcPath( requestedKey );
		}
		var displayKey = ToDisplayKey( key );
		if ( !MatchesPattern( displayKey, options.Pattern ) ) {
			return Success;
		}
		if ( options.NameOnly ) {
			try {
				var keys = await backend.EnumerateKeysAsync( cancellationToken ).ConfigureAwait( false );
				if ( keys.Contains( key, StringComparer.Ordinal ) ) {
					await WriteLineAsync( stdout, displayKey, cancellationToken ).ConfigureAwait( false );
					return Success;
				}
				var prefix = string.Concat( key.TrimEnd( '/' ), "/" );
				if ( keys.Any( candidate => candidate.StartsWith( prefix, StringComparison.Ordinal ) ) ) {
					return await DisplaySubtreeAsync( backend, key, options, stdout, stderr, cancellationToken ).ConfigureAwait( false );
				}
				var missing = new SysctlBackendException( SysctlBackendFailureKind.NotFound, "No such file or directory" );
				return await ReportReadFailureAsync( key, displayKey, missing, options, stderr, cancellationToken ).ConfigureAwait( false );
			} catch ( SysctlBackendException exception ) { return await ReportReadFailureAsync( key, displayKey, exception, options, stderr, cancellationToken ).ConfigureAwait( false ); }
		}
		try {
			var value = await backend.ReadValueAsync( key, cancellationToken ).ConfigureAwait( false );
			await WriteValueAsync( stdout, displayKey, value, options, cancellationToken ).ConfigureAwait( false );
			return Success;
		} catch ( SysctlBackendException exception ) when ( SysctlBackendFailureKind.IsDirectory == exception.Kind ) { return await DisplaySubtreeAsync( backend, key, options, stdout, stderr, cancellationToken ).ConfigureAwait( false ); } catch ( SysctlBackendException exception ) { return await ReportReadFailureAsync( key, displayKey, exception, options, stderr, cancellationToken ).ConfigureAwait( false ); }
	}

	private static async Task<int> DisplaySubtreeAsync( ISysctlBackend backend, string prefix, ParsedArguments options, Stream? stdout, Stream? stderr, CancellationToken cancellationToken ) {
		ArgumentNullException.ThrowIfNull( backend );
		ArgumentNullException.ThrowIfNull( prefix );
		ArgumentNullException.ThrowIfNull( options );
		var keys = await backend.EnumerateKeysAsync( cancellationToken ).ConfigureAwait( false );
		var normalizedPrefix = prefix.TrimEnd( '/' );
		var prefixWithSlash = string.Concat( normalizedPrefix, "/" );
		var status = Success;
		foreach ( var key in keys.OrderBy( static value => value, StringComparer.Ordinal ) ) {
			if ( !key.StartsWith( prefixWithSlash, StringComparison.Ordinal ) ) {
				continue;
			}
			if ( IsVerboten( key ) ) {
				continue;
			}
			if ( !options.IncludeDeprecated && IsDeprecated( key ) ) {
				continue;
			}
			if ( !MatchesPattern( ToDisplayKey( key ), options.Pattern ) ) {
				continue;
			}
			if ( options.NameOnly ) {
				await WriteLineAsync( stdout, ToDisplayKey( key ), cancellationToken ).ConfigureAwait( false );
				continue;
			}
			status |= await ReadSettingAsync( backend, key, options, stdout, stderr, cancellationToken, keyIsNormalized: true ).ConfigureAwait( false );
		}
		return status;
	}

	private static async Task<int> ReportReadFailureAsync( string key, string displayKey, SysctlBackendException exception, ParsedArguments options, Stream? stderr, CancellationToken cancellationToken ) {
		ArgumentNullException.ThrowIfNull( key );
		ArgumentNullException.ThrowIfNull( displayKey );
		ArgumentNullException.ThrowIfNull( exception );
		ArgumentNullException.ThrowIfNull( options );
		if ( SysctlBackendFailureKind.NotFound == exception.Kind && options.IgnoreUnknown ) {
			return Success;
		}
		if ( SysctlBackendFailureKind.NotFound == exception.Kind ) {
			await WriteDiagnosticAsync( stderr, $"sysctl: cannot stat {ProcDisplayRoot}{key}: {exception.Message}", cancellationToken ).ConfigureAwait( false );
			return Failure;
		}
		if ( SysctlBackendFailureKind.PermissionDenied == exception.Kind ) {
			await WriteDiagnosticAsync( stderr, $"sysctl: permission denied on key '{displayKey}'", cancellationToken ).ConfigureAwait( false );
			return Failure;
		}
		await WriteDiagnosticAsync( stderr, $"sysctl: reading key \"{displayKey}\": {exception.Message}", cancellationToken ).ConfigureAwait( false );
		return Failure;
	}

	private static async Task<int> WriteSettingAsync( ISysctlBackend backend, SysctlSetting setting, ParsedArguments options, Stream? stdout, Stream? stderr, CancellationToken cancellationToken ) {
		ArgumentNullException.ThrowIfNull( backend );
		ArgumentNullException.ThrowIfNull( setting );
		ArgumentNullException.ThrowIfNull( options );
		if ( null == setting.Value ) {
			await WriteDiagnosticAsync( stderr, "sysctl: command line(0): invalid syntax, continuing...", cancellationToken ).ConfigureAwait( false );
			return Failure;
		}
		try {
			await backend.WriteValueAsync( setting.PathKey, setting.Value, options.DryRun, cancellationToken ).ConfigureAwait( false );
			if ( ( !options.Quiet ) || options.DryRun ) {
				await WriteValueAsync( stdout, setting.DisplayKey, setting.Value, options, cancellationToken ).ConfigureAwait( false );
			}
			return Success;
		} catch ( SysctlBackendException exception ) {
			if ( SysctlBackendFailureKind.NotFound == exception.Kind && options.IgnoreUnknown ) {
				return Success;
			}
			var ignoring = setting.IgnoreFailure && SysctlBackendFailureKind.NotWritable != exception.Kind;
			var suffix = ignoring ? ", ignoring" : string.Empty;
			if ( SysctlBackendFailureKind.NotFound == exception.Kind ) {
				await WriteDiagnosticAsync( stderr, $"sysctl: cannot stat {ProcDisplayRoot}{setting.PathKey}: {exception.Message}{suffix}", cancellationToken ).ConfigureAwait( false );
			} else if ( SysctlBackendFailureKind.NotWritable == exception.Kind ) {
				await WriteDiagnosticAsync( stderr, $"sysctl: setting key \"{setting.DisplayKey}\": {exception.Message}", cancellationToken ).ConfigureAwait( false );
			} else if ( SysctlBackendFailureKind.PermissionDenied == exception.Kind ) {
				await WriteDiagnosticAsync( stderr, $"sysctl: permission denied on key \"{setting.DisplayKey}\"{suffix}", cancellationToken ).ConfigureAwait( false );
			} else if ( SysctlBackendFailureKind.IsDirectory == exception.Kind ) {
				await WriteDiagnosticAsync( stderr, $"sysctl: setting key \"{setting.DisplayKey}\": Is a directory{suffix}", cancellationToken ).ConfigureAwait( false );
			} else {
				await WriteDiagnosticAsync( stderr, $"sysctl: setting key \"{setting.DisplayKey}\": {exception.Message}{suffix}", cancellationToken ).ConfigureAwait( false );
			}
			return ignoring ? Success : Failure;
		}
	}

	private static async Task WriteValueAsync( Stream? stdout, string displayKey, string value, ParsedArguments options, CancellationToken cancellationToken ) {
		ArgumentNullException.ThrowIfNull( displayKey );
		ArgumentNullException.ThrowIfNull( value );
		ArgumentNullException.ThrowIfNull( options );
		var normalizedValue = NormalizeMultilineValue( value );
		if ( options.NameOnly ) {
			await WriteLineAsync( stdout, displayKey, cancellationToken ).ConfigureAwait( false );
			return;
		}
		if ( options.PrintName ) {
			var text = string.Concat( displayKey, " = ", normalizedValue );
			await WriteLineAsync( stdout, text, cancellationToken ).ConfigureAwait( false );
			return;
		}
		if ( options.PrintNewline ) {
			await WriteLineAsync( stdout, normalizedValue, cancellationToken ).ConfigureAwait( false );
			return;
		}
		var binaryValue = normalizedValue.Replace( Environment.NewLine, string.Empty, StringComparison.Ordinal );
		await WriteTextAsync( stdout, binaryValue, cancellationToken ).ConfigureAwait( false );
	}

	private static string NormalizeKeyToProcPath( string key ) {
		ArgumentNullException.ThrowIfNull( key );
		var trimmed = key.Trim();
		var firstDot = trimmed.IndexOf( '.' );
		var firstSlash = trimmed.IndexOf( '/' );
		var firstSeparator = -1;
		if ( 0 <= firstDot && 0 <= firstSlash ) {
			firstSeparator = Math.Min( firstDot, firstSlash );
		} else if ( 0 <= firstDot ) {
			firstSeparator = firstDot;
		} else if ( 0 <= firstSlash ) {
			firstSeparator = firstSlash;
		}
		if ( 0 > firstSeparator || '/' == trimmed[ firstSeparator ] ) {
			return trimmed.Trim( '/' );
		}
		var builder = new StringBuilder( trimmed.Length );
		foreach ( var character in trimmed ) {
			if ( '.' == character ) {
				builder.Append( '/' );
			} else if ( '/' == character ) {
				builder.Append( '.' );
			} else {
				builder.Append( character );
			}
		}
		return builder.ToString().Trim( '/' );
	}
	private static string ToDisplayKey( string normalizedKey ) {
		ArgumentNullException.ThrowIfNull( normalizedKey );
		var firstDot = normalizedKey.IndexOf( '.' );
		var firstSlash = normalizedKey.IndexOf( '/' );
		if ( 0 <= firstDot && ( 0 > firstSlash || firstDot < firstSlash ) ) {
			return normalizedKey;
		}
		var builder = new StringBuilder( normalizedKey.Length );
		foreach ( var character in normalizedKey ) {
			if ( '/' == character ) {
				builder.Append( '.' );
			} else if ( '.' == character ) {
				builder.Append( '/' );
			} else {
				builder.Append( character );
			}
		}
		return builder.ToString();
	}
	private static bool MatchesPattern( string displayKey, string? pattern ) {
		ArgumentNullException.ThrowIfNull( displayKey );
		if ( string.IsNullOrEmpty( pattern ) ) {
			return true;
		}
		try {
			return Regex.IsMatch( displayKey, pattern, RegexOptions.CultureInvariant );
		} catch ( ArgumentException ) { return false; }
	}
	private static bool IsDeprecated( string key ) {
		ArgumentNullException.ThrowIfNull( key );
		return DeprecatedLeafNames.Contains( GetPortableFileName( key ) );
	}
	private static bool IsVerboten( string key ) {
		ArgumentNullException.ThrowIfNull( key );
		return VerbotenLeafNames.Contains( GetPortableFileName( key ) );
	}
	private static bool IsGlob( string key ) {
		ArgumentNullException.ThrowIfNull( key );
		return 0 <= key.IndexOfAny( [ '*', '?', '[' ] );
	}
	private static bool GlobMatch( string pattern, string value ) {
		ArgumentNullException.ThrowIfNull( pattern );
		ArgumentNullException.ThrowIfNull( value );
		var expression = new StringBuilder( "^" );
		for ( var index = 0; index < pattern.Length; index++ ) {
			var current = pattern[ index ];
			if ( '*' == current ) {
				expression.Append( "[^/]*" );
				continue;
			}
			if ( '?' == current ) {
				expression.Append( "[^/]" );
				continue;
			}
			if ( '[' == current ) {
				var close = pattern.IndexOf( ']', index + 1 );
				if ( 0 < close ) {
					expression.Append( pattern.AsSpan( index, close - index + 1 ) );
					index = close;
					continue;
				}
			}
			expression.Append( Regex.Escape( current.ToString() ) );
		}
		expression.Append( '$' );
		return Regex.IsMatch( value, expression.ToString(), RegexOptions.CultureInvariant );
	}
	private static string GetPortableFileName( string path ) {
		ArgumentNullException.ThrowIfNull( path );
		var slash = Math.Max( path.LastIndexOf( '/' ), path.LastIndexOf( '\\' ) );
		return 0 > slash ? path : path[ ( slash + 1 ).. ];
	}
	private static string NormalizeMultilineValue( string value ) {
		ArgumentNullException.ThrowIfNull( value );
		return value.Replace( "\r\n", "\n", StringComparison.Ordinal ).Replace( '\r', '\n' ).Replace( "\n", Environment.NewLine, StringComparison.Ordinal );
	}
	private static string NormalizeLineEndings( string value ) {
		ArgumentNullException.ThrowIfNull( value );
		return NormalizeMultilineValue( value.TrimEnd( '\r', '\n' ) ) + Environment.NewLine;
	}
	private static async Task WriteDiagnosticAsync( Stream? stream, string message, CancellationToken cancellationToken ) {
		ArgumentNullException.ThrowIfNull( message );
		var target = stream ?? Console.OpenStandardError();
		var normalized = NormalizeMultilineValue( message.TrimEnd( '\r', '\n' ) );
		await WriteLineAsync( target, normalized, cancellationToken ).ConfigureAwait( false );
	}
	private static Task WriteLineAsync( Stream? stream, string text, CancellationToken cancellationToken ) {
		ArgumentNullException.ThrowIfNull( text );
		return WriteTextAsync( stream, string.Concat( text, Environment.NewLine ), cancellationToken );
	}
	private static async Task WriteTextAsync( Stream? stream, string text, CancellationToken cancellationToken ) {
		ArgumentNullException.ThrowIfNull( text );
		var target = stream ?? Console.OpenStandardOutput();
		var bytes = Utf8.GetBytes( text );
		await target.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false );
	}

	private sealed class ParsedArguments {
		public bool DisplayAll {
			get; set;
		}
		public bool IncludeDeprecated {
			get; set;
		}
		public bool DryRun {
			get; set;
		}
		public bool IgnoreUnknown {
			get; set;
		}
		public bool NameOnly {
			get; set;
		}
		public bool PrintName { get; set; } = true;
		public bool PrintNewline { get; set; } = true;
		public bool PreloadMode {
			get; set;
		}
		public string? AttachedPreloadFile {
			get; set;
		}
		public bool Quiet {
			get; set;
		}
		public bool WriteMode {
			get; set;
		}
		public bool SystemMode {
			get; set;
		}
		public string? Pattern {
			get; set;
		}
		public bool ShowHelp {
			get; set;
		}
		public bool ShowVersion {
			get; set;
		}
		public string? Error {
			get; set;
		}
		public List<string> Operands { get; } = [];
	}
	private sealed record SysctlSetting( string PathKey, string DisplayKey, string? Value, bool IgnoreFailure, bool GlobExclude );
}
